using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
        [FormerlySerializedAs("pullDurationPerTile")]
        public float pullDuration = 0.3f;
        
        public float knockbackStartDelay = 0.1f;
        
        public float pullKnockbackEndDuration = 0.1f;

        public float pauseAfterPull = 0.0f;

        public float recoilDurationPerTile = 0.18f;

        public float missAnimDuration = 0.5f;

        [Header("Recoil Animation State")]
        public string recoilKnockbackState;

        [Header("Wire VFX")]
        public GameObject wirePrefab;

        public string[] wireStateNamesByDistance = { "Wire_1", "Wire_2", "Wire_3", "Wire_4" };

        public Vector3 wireSpawnOffset = Vector3.zero;

        public float wireCueMaxWait = 2f;
        
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

            bool isPlayerCaster = ctx.caster is PlayerUnit;
            CombatManager combatManager = null;
            if (isPlayerCaster)
            {
                combatManager = Object.FindAnyObjectByType<CombatManager>();
                if (combatManager != null && combatManager.CurrentActiveUnit == ctx.caster)
                    combatManager.BlockAnimationForEffect();
            }

            Vector2Int recoilDir = new Vector2Int(-ctx.aimDir.x, -ctx.aimDir.y);
            List<Tile> recoilPath = CalculatePath(ctx.caster, recoilDir, recoilDistance);

            bool wallCase = recoilPath.Count == 0;
            
            Vector2Int pullDir = recoilDir; 
            Tile pullDestination = CalculatePullDestination(
                hookedUnit.currentTile, casterTile, pullDir, includeCasterTile: !wallCase);
            
            yield return ctx.caster.StartCoroutine(PullToCasterTile(ctx, hookedUnit, pullDestination));
            
            if (!hookedUnit.IsDead)
            {
                float attackMultiplier = ctx.caster != null ? StatusEffectManager.GetAttackMultiplier(ctx.caster) : 1f;
                int rawAttack = ctx.caster != null ? ctx.caster.baseAttack : 0;
                int baseDamage = Mathf.RoundToInt((ctx.ability.damage + rawAttack) * attackMultiplier);
                int finalDamage = Mathf.Max(1, baseDamage - hookedUnit.currentDefense);
                hookedUnit.ReceiveDamage(finalDamage, ctx.caster);
                UnitManager.NotifyUnitDamaged(hookedUnit, ctx.caster);
                ctx.LastResolvedDamage += finalDamage;
                Debug.Log($"[WiringFault] {hookedUnit.name} took {finalDamage} damage on arrival.");

                if (BigMomentSequencer.Instance != null)
                    yield return BigMomentSequencer.Instance.DrainQueue();
            }
            
            if (wallCase)
            {
                Debug.Log("[WiringFault] Caster is against a wall — recoil and Shocked skipped.");
                var casterAnimator = ctx.caster.GetComponent<UnitAnimator>();
                casterAnimator?.PlayFullKnockback(recoilKnockbackState);
                while (casterAnimator != null && casterAnimator.IsInKnockbackSequence)
                    yield return null;

                if (isPlayerCaster && combatManager != null &&
                    combatManager.CurrentActiveUnit == ctx.caster)
                {
                    yield return ctx.caster.StartCoroutine(
                        DelayedInputRestore(combatManager, ctx.caster));
                }

                yield break;
            }
            
            yield return new WaitForSeconds(pauseAfterPull);

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

                float attackMultiplier = ctx.caster != null ? StatusEffectManager.GetAttackMultiplier(ctx.caster) : 1f;
                int rawAttack = ctx.caster != null ? ctx.caster.baseAttack : 0;
                int baseDamage = Mathf.RoundToInt((ctx.ability.damage + rawAttack) * attackMultiplier);
                int doubleDamage = Mathf.Max(1, (baseDamage - victim.currentDefense) * 2);
                victim.ReceiveDamage(doubleDamage, ctx.caster);
                UnitManager.NotifyUnitDamaged(victim, ctx.caster);
                ctx.LastResolvedDamage += doubleDamage;
                Debug.Log($"[WiringFault] Recoil slammed into {victim.name} — {doubleDamage} damage (double).");

                if (BigMomentSequencer.Instance != null)
                    yield return BigMomentSequencer.Instance.DrainQueue();

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
            var casterAnimator = ctx.caster.GetComponent<UnitAnimator>();
            
            hookedUnit.FaceDirection(new Vector2Int(-ctx.aimDir.x, -ctx.aimDir.y));

            GameObject wireInstance = SpawnWire(ctx, distance);
            EffectCueRelay cueRelay = wireInstance != null ? wireInstance.GetComponent<EffectCueRelay>() : null;

            bool beginPull = false;
            bool pullSettled = false;
            void OnWireCue(int index)
            {
                if (index == 0) beginPull = true;
                else if (index == 1) pullSettled = true;
            }
            if (cueRelay != null) cueRelay.OnCue += OnWireCue;

            hookedAnimator?.PlayKnockbackStart();

            if (cueRelay != null)
            {
                float waited = 0f;
                while (!beginPull && waited < wireCueMaxWait)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                if (!beginPull)
                    Debug.LogWarning($"[WiringFault] AnimEvent_Cue0 never fired on {wireInstance.name} — " +
                                      $"ensure the {distance}-tile wire clip has the Cue0 event placed at the wrap/grab frame.");
            }
            else
            {
                yield return new WaitForSeconds(knockbackStartDelay);
            }

            float totalDuration = pullDuration;
            bool pullComplete = false;
            hookedUnit.AnimateToTile(destination, totalDuration, () => pullComplete = true);
            while (!pullComplete)
                yield return null;

            if (cueRelay != null)
            {
                float waited = 0f;
                while (!pullSettled && waited < wireCueMaxWait)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                if (!pullSettled)
                    Debug.LogWarning($"[WiringFault] AnimEvent_Cue1 never fired on {wireInstance.name} — " +
                                      $"ensure the {distance}-tile wire clip has the Cue1 event placed at the settle frame.");
            }

            hookedAnimator?.PlayKnockbackEnd();
            casterAnimator?.ReleaseHold();
            yield return new WaitForSeconds(pullKnockbackEndDuration);

            if (cueRelay != null) cueRelay.OnCue -= OnWireCue;
            if (wireInstance != null) Object.Destroy(wireInstance);
        }

        private GameObject SpawnWire(AbilityContext ctx, int distance)
        {
            if (wirePrefab == null || ctx.caster == null) return null;

            Vector3 spawnPos = ctx.caster.transform.position + wireSpawnOffset;
            GameObject instance = Object.Instantiate(wirePrefab, spawnPos, ctx.caster.transform.rotation);

            var wireSpriteRenderers = instance.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            if (wireSpriteRenderers != null && wireSpriteRenderers.Length > 0 && ctx.aimDir.x != 0)
            {
                bool flipX = ctx.aimDir.x > 0;
                foreach (var sr in wireSpriteRenderers)
                {
                    if (sr == null) continue;
                    sr.flipX = flipX;
                }
            }

            string stateName = GetWireStateName(distance);
            var wireAnimator = instance.GetComponent<Animator>();
            if (wireAnimator != null && !string.IsNullOrEmpty(stateName))
            {
                if (wireAnimator.HasState(0, Animator.StringToHash(stateName)))
                    wireAnimator.Play(stateName);
                else
                    Debug.LogWarning($"[WiringFault] Wire prefab has no state named '{stateName}' for a {distance}-tile pull.");
            }

            return instance;
        }

        private string GetWireStateName(int distance)
        {
            if (wireStateNamesByDistance == null || wireStateNamesByDistance.Length == 0) return null;

            int index = Mathf.Clamp(distance - 1, 0, wireStateNamesByDistance.Length - 1);
            return wireStateNamesByDistance[index];
        }
        
        private IEnumerator ApplyRecoilWithAnimation(Unit caster, Vector2Int recoilDir, List<Tile> recoilPath)
        {
            var casterAnimator = caster.GetComponent<UnitAnimator>();

            casterAnimator?.PlayFullKnockback(recoilKnockbackState);
            yield return new WaitForSeconds(knockbackStartDelay);

            Tile landingTile = recoilPath[recoilPath.Count - 1];
            float totalDuration = recoilPath.Count * recoilDurationPerTile;
            bool recoilComplete = false;
            caster.AnimateToTile(landingTile, totalDuration, () => recoilComplete = true);

            while (!recoilComplete || (casterAnimator != null && casterAnimator.IsInKnockbackSequence))
                yield return null;
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