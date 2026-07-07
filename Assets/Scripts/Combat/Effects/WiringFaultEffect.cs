using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Wiring Fault Effect")]
    public class WiringFaultEffect : AbilityEffect
    {
        [Header("Status")]
        public StatusEffectData shockedStatusData;
        
        public int shockedDuration = 2;

        public int shockedDamage = 5;

        [Header("Recoil Distance")]
        public int recoilDistance = 2;

        [Header("Animation Timing")]
        public float pullDurationPerTile = 0.15f;
        
        public float knockbackStartDelay = 0.1f;
        
        public float pullKnockbackEndDuration = 0.1f;

        public float pauseAfterPull = 0.0f;

        public float recoilDurationPerTile = 0.18f;

        public float knockbackEndDuration = 0.4f;
        
        public float wallRecoilAnimDuration = 0.3f;

        public float missAnimDuration = 0.5f;
        
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.Displacement;
        
        public override string TargetAnimationHint => "Knockback";
        
        public override float ExpectedAnimationDuration => 3f;

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx?.caster == null) return;
            
            Unit hookedUnit = (targets != null && targets.Count > 0) ? targets[0] : null;
            if (hookedUnit == null || hookedUnit.IsDead) return;
            
            ctx.HookedUnit = hookedUnit;
            
            ctx.caster.StartCoroutine(RunSequence(ctx, hookedUnit));
        }

        private IEnumerator RunSequence(AbilityContext ctx, Unit hookedUnit)
        {
            Tile casterTile = ctx.caster.currentTile;
            if (casterTile == null) yield break;

            Vector2Int recoilDir = new Vector2Int(-ctx.aimDir.x, -ctx.aimDir.y);
            List<Tile> recoilPath = CalculatePath(ctx.caster, recoilDir, recoilDistance);

            bool wallCase = recoilPath.Count == 0;
            
            Vector2Int pullDir = recoilDir; 
            Tile pullDestination = CalculatePullDestination(
                hookedUnit.currentTile, casterTile, pullDir, includeCasterTile: !wallCase);
            
            yield return ctx.caster.StartCoroutine(PullToCasterTile(ctx, hookedUnit, pullDestination));
            
            if (!hookedUnit.IsDead)
            {
                int baseDamage = ctx.ability.damage + (ctx.caster != null ? ctx.caster.currentAttack : 0);
                int finalDamage = Mathf.Max(1, baseDamage - hookedUnit.currentDefense);
                hookedUnit.ReceiveDamage(finalDamage, ctx.caster);
                UnitManager.NotifyUnitDamaged(hookedUnit, ctx.caster);
                ctx.LastResolvedDamage += finalDamage;
                Debug.Log($"[WiringFault] {hookedUnit.name} took {finalDamage} damage on arrival.");
            }
            
            if (wallCase)
            {
                Debug.Log("[WiringFault] Caster is against a wall — recoil and Shocked skipped.");
                var casterAnimator = ctx.caster.GetComponent<UnitAnimator>();
                casterAnimator?.PlayKnockbackStart();
                yield return new WaitForSeconds(wallRecoilAnimDuration);
                casterAnimator?.PlayKnockbackEnd();
                yield return new WaitForSeconds(knockbackEndDuration);
                yield break;
            }
            
            yield return new WaitForSeconds(pauseAfterPull);
            
            bool isPlayerCaster = ctx.caster is PlayerUnit;
            CombatManager combatManager = null;
            if (isPlayerCaster)
            {
                combatManager = Object.FindAnyObjectByType<CombatManager>();
                if (combatManager != null && combatManager.CurrentActiveUnit == ctx.caster)
                    combatManager.BlockAnimationForEffect();
            }

            yield return ctx.caster.StartCoroutine(
                ApplyRecoilWithAnimation(ctx.caster, recoilDir, recoilPath));
            
            bool collisionHit = false;

            Tile landingTile = recoilPath[recoilPath.Count - 1];
            Tile beyondTile  = GridManager.Instance.GetTileInDirection(landingTile, recoilDir);

            if (beyondTile != null &&
                beyondTile.currentUnit != null &&
                beyondTile.currentUnit != ctx.caster &&
                !beyondTile.currentUnit.IsDead)
            {
                collisionHit = true;
                Unit victim = beyondTile.currentUnit;

                int baseDamage = ctx.ability.damage + (ctx.caster != null ? ctx.caster.currentAttack : 0);
                int doubleDamage = Mathf.Max(1, (baseDamage - victim.currentDefense) * 2);
                victim.ReceiveDamage(doubleDamage, ctx.caster);
                UnitManager.NotifyUnitDamaged(victim, ctx.caster);
                ctx.LastResolvedDamage += doubleDamage;
                Debug.Log($"[WiringFault] Recoil slammed into {victim.name} — {doubleDamage} damage (double).");

                victim.GetComponent<UnitAnimator>()?.PlayHurt();
                ApplyShocked(victim, ctx.caster);
            }

            if (!collisionHit)
            {
                Debug.Log("[WiringFault] Recoil missed — caster takes Shocked.");
                ApplyShocked(ctx.caster, ctx.caster);

                var casterAnimator = ctx.caster.GetComponent<UnitAnimator>();
                if (casterAnimator != null && casterAnimator.HasState("Debuff"))
                    casterAnimator.PlayAnimation("Debuff");
                else
                    casterAnimator?.PlayHurt();

                yield return new WaitForSeconds(missAnimDuration);
            }
            
            if (isPlayerCaster && combatManager != null &&
                combatManager.CurrentActiveUnit == ctx.caster)
            {
                yield return ctx.caster.StartCoroutine(
                    DelayedInputRestore(combatManager, ctx.caster));
            }
        }
        
        private Tile CalculatePullDestination(Tile from, Tile casterTile, Vector2Int pullDir, bool includeCasterTile)
        {
            Tile bestReachable = from;
            Tile current = from;

            int maxSteps = 20;
            while (maxSteps-- > 0)
            {
                Tile next = GridManager.Instance.GetTileInDirection(current, pullDir);

                if (next == null || !next.passableTerrain) break;
                
                if (next == casterTile)
                {
                    if (includeCasterTile) bestReachable = casterTile;
                    break;
                }
                
                if (next.occupied) break;

                bestReachable = next;
                current = next;
            }

            return bestReachable;
        }
        
        private IEnumerator PullToCasterTile(AbilityContext ctx, Unit hookedUnit, Tile destination)
        {
            if (hookedUnit.currentTile == destination) yield break;

            int distance = GridManager.Instance.GetGridDistance(hookedUnit.currentTile, destination);
            if (distance == 0) yield break;

            var hookedAnimator = hookedUnit.GetComponent<UnitAnimator>();
            
            hookedUnit.FaceDirection(new Vector2Int(-ctx.aimDir.x, -ctx.aimDir.y));

            hookedAnimator?.PlayKnockbackStart();
            yield return new WaitForSeconds(knockbackStartDelay);

            float totalDuration = distance * pullDurationPerTile;
            bool pullComplete = false;
            hookedUnit.AnimateToTile(destination, totalDuration, () => pullComplete = true);
            while (!pullComplete)
                yield return null;

            hookedAnimator?.PlayKnockbackEnd();
            yield return new WaitForSeconds(pullKnockbackEndDuration);
        }
        
        private IEnumerator ApplyRecoilWithAnimation(Unit caster, Vector2Int recoilDir, List<Tile> recoilPath)
        {
            var casterAnimator = caster.GetComponent<UnitAnimator>();

            casterAnimator?.PlayKnockbackStart();
            yield return new WaitForSeconds(knockbackStartDelay);

            Tile landingTile = recoilPath[recoilPath.Count - 1];
            float totalDuration = recoilPath.Count * recoilDurationPerTile;
            bool recoilComplete = false;
            caster.AnimateToTile(landingTile, totalDuration, () => recoilComplete = true);
            while (!recoilComplete)
                yield return null;

            casterAnimator?.PlayKnockbackEnd();
            yield return new WaitForSeconds(knockbackEndDuration);
        }
        
        private List<Tile> CalculatePath(Unit unit, Vector2Int dir, int distance)
        {
            var path  = new List<Tile>();
            Tile current = unit.currentTile;
            if (current == null) return path;

            for (int i = 0; i < distance; i++)
            {
                Tile next = GridManager.Instance.GetTileInDirection(current, dir);
                if (next == null || !next.passableTerrain) break;
                if (next.currentUnit != null && next.currentUnit != unit) break;

                path.Add(next);
                current = next;
            }

            return path;
        }

        private void ApplyShocked(Unit target, Unit source)
        {
            if (shockedStatusData == null)
            {
                Debug.LogWarning("[WiringFault] shockedStatusData is not assigned on the " +
                                 "WiringFaultEffect asset — Shocked was not applied.");
                return;
            }

            StatusEffectManager.Instance?.ApplyStatusEffect(
                target, shockedStatusData, source, shockedDuration, shockedDamage);

            Debug.Log($"[WiringFault] {target.name} is Shocked for {shockedDuration} turns.");
        }

        private IEnumerator DelayedInputRestore(CombatManager combatManager, Unit caster)
        {
            yield return null;
            combatManager.ReleaseAnimationBlock();
            
            if (combatManager.CanMove)
            {
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.Movement, caster);
            }
        }
    }
}