using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public enum AbilityRangeType
    {
        SingleTarget, 
        Line, 
        AOE, 
        Self, 
        Unique          
    }

    public enum AttackType
    {
        Ranged, 
        Melee, 
    }

    [CreateAssetMenu(menuName = "TNFY Brawl/Ability")]
    public class Ability : ScriptableObject
    {
        [Header("Basic Info")]
        public string abilityName;
        public string description;
        public Sprite image;

        [Header("Targeting")]
        public AbilityRangeType rangeType;
        public int range = 5;

        [Header("Numbers")]
        public int damage = 10;

        [Header("Cooldown")]
        public int cooldown = 0;
        
        public AttackType attackType = AttackType.Ranged;
        
        public bool canHitEnemies = true;
        public bool canHitAllies = false;

        public bool canTargetNeutral = false;
        
        public bool passThroughUnits = false;

        public int maxTargets = 1;
        
        public bool canExecuteWithoutTargets = false;
        
        public bool endTurnOnCast = false;
        
        public bool suppressCameraTransitions = false;
        
        [SerializeField] private string animationState = "";

        [SerializeField] private string animationStateHurt = "";
        
        [SerializeField] private GameObject castEffectPrefab;
        [SerializeField] private Vector3 castEffectOffset = Vector3.zero;
        [SerializeField] private bool parentCastEffectToCaster = false;

        [SerializeField] private GameObject hitEffectPrefab;
        [SerializeField] private Vector3 hitEffectOffset = Vector3.zero;
        [SerializeField] private bool parentHitEffectToTarget = false;

        public AbilityTargeting targeting;
        public List<AbilityEffect> effects;
        
        public string AnimationState => animationState;
        public string AnimationStateHurt => animationStateHurt;
        public GameObject CastEffectPrefab => castEffectPrefab;
        public Vector3 CastEffectOffset => castEffectOffset;
        public bool ParentCastEffectToCaster => parentCastEffectToCaster;
        public GameObject HitEffectPrefab => hitEffectPrefab;
        public Vector3 HitEffectOffset => hitEffectOffset;
        public bool ParentHitEffectToTarget => parentHitEffectToTarget;

        public bool Execute(Unit caster, Vector2Int aimDirection)
        {
            if (caster == null || targeting == null || effects == null || effects.Count == 0)
                return false;

            var ctx = new AbilityContext
            {
                caster = caster,
                ability = this,
                aimDir = aimDirection
            };

            var targets = targeting.SelectTargets(ctx);

            if (targets.Count == 0 && !canExecuteWithoutTargets)
                return false;

            caster.TriggerAbilityCooldown(this);
            caster.StartCoroutine(ExecuteAbilitySequence(ctx, targets));
            return true;
        }

        public bool ExecuteWithContext(AbilityContext ctx)
        {
            if (ctx == null || targeting == null || effects == null || effects.Count == 0)
                return false;

            var targets = targeting.SelectTargets(ctx);

            if (targets.Count == 0 && !canExecuteWithoutTargets)
                return false;

            ctx.caster.TriggerAbilityCooldown(this);
            ctx.caster.StartCoroutine(ExecuteAbilitySequence(ctx, targets));
            return true;
        }

        private IEnumerator ExecuteAbilitySequence(AbilityContext ctx, List<Unit> targets)
        {
            ctx.caster.FaceDirection(ctx.aimDir);
            yield return ctx.caster.StartCoroutine(ctx.caster.ExecuteAbilityAnimationSequence(ctx, targets));
        }

        public bool CanExecute(Unit caster, Vector2Int aimDirection)
        {
            if (caster == null || targeting == null || effects == null || effects.Count == 0)
                return false;

            var ctx = new AbilityContext
            {
                caster = caster,
                ability = this,
                aimDir = aimDirection
            };

            var targets = targeting.SelectTargets(ctx);
            return targets.Count > 0 || canExecuteWithoutTargets;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(animationState))
            {
                Debug.LogWarning(
                    $"[Ability] '{abilityName}' has no Animation State set. " +
                    $"The ability animation will not play and post-animation effects " +
                    $"(camera pan, hit effects) will never trigger. " +
                    $"Set an Animation State in the Inspector. ({name})",
                    this);
            }
        }
#endif
    }
    
    public class AbilityContext
    {
        public Unit caster;
        public Ability ability;
        public Vector2Int aimDir;
        public Tile targetTile;

        public Tile OriginTile => caster?.currentTile;

        public int EffectiveRange => ability.range + (caster?.RangeModifier ?? 0);
        
        public int LastResolvedDamage;
        
        public Unit HookedUnit;
    }
}