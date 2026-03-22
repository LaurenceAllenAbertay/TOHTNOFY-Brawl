using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public enum AbilityRangeType
    {
        SingleTarget,   // selected tile
        Line,           // straight line until blocked
        AOE,            // radius around a tile
        Self,           // affects the caster
        Custom          // anything unique
    }

    [System.Serializable]
    public class AbilityAnimationTiming
    {
        [Header("Animation Event Timing (as percentage of total animation)")]
        [Range(0f, 1f)] public float castEffectTime = 0.8f;
        [Range(0f, 1f)] public float hitEffectTime = 0.85f;
        [Range(0f, 1f)] public float applyEffectsTime = 0.82f;
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

        [Tooltip("Who can be targeted by this ability")]
        public bool canHitEnemies = true;
        public bool canHitAllies = false;

        [Tooltip("If true, ability keeps travelling through units and can hit more than one.")]
        public bool passThroughUnits = false;

        [Tooltip("Max units to affect.")]
        public int maxTargets = 1;

        [Header("Execution")]
        [Tooltip("If true, ability can execute even when no valid targets are found")]
        public bool canExecuteWithoutTargets = false;

        [Header("Animation & Visual Effects")]
        [SerializeField] private string animationState = "Attack_Melee_1"; // Specific animation to play
        [SerializeField] private AbilityAnimationTiming animationTiming = new AbilityAnimationTiming();

        public AbilityAnimationTiming AnimationTiming => animationTiming;

        [Header("Cast Effect (when ability starts)")]
        [SerializeField] private GameObject castEffectPrefab;              // Effect when ability casts
        [SerializeField] private Vector3 castEffectOffset = Vector3.zero;  // Offset from caster position
        [SerializeField] private bool parentCastEffectToCaster = false;     // Whether to parent the effect to the caster

        [Header("Hit Effect (when ability impacts targets)")]
        [SerializeField] private GameObject hitEffectPrefab;               // Effect when ability hits
        [SerializeField] private Vector3 hitEffectOffset = Vector3.zero;   // Offset from target position
        [SerializeField] private bool parentHitEffectToTarget = false;     // Whether to parent the effect to the target

        [Header("Composition")]
        public AbilityTargeting targeting;
        public List<AbilityEffect> effects;

        // Public properties for external access
        public string AnimationState => animationState;
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

            // Let targeting resolve the units (and/or tiles) to affect
            var targets = targeting.SelectTargets(ctx);

            // Check if we can execute with the current target count
            if (targets.Count == 0 && !canExecuteWithoutTargets)
                return false; // Ability was blocked - no execution

            // Start the full execution sequence with proper timing
            caster.StartCoroutine(ExecuteAbilitySequence(ctx, targets));

            return true; // Ability was successfully executed
        }

        // Handle single-target abilities with context
        public bool ExecuteWithContext(AbilityContext ctx)
        {
            if (ctx == null || targeting == null || effects == null || effects.Count == 0)
                return false;

            // Let targeting resolve the units (and/or tiles) to affect
            var targets = targeting.SelectTargets(ctx);

            // Check if we can execute with the current target count
            if (targets.Count == 0 && !canExecuteWithoutTargets)
                return false; // Ability was blocked - no execution

            // Start the full execution sequence with proper timing
            ctx.caster.StartCoroutine(ExecuteAbilitySequence(ctx, targets));

            return true; // Ability was successfully executed
        }

        private IEnumerator ExecuteAbilitySequence(AbilityContext ctx, List<Unit> targets)
        {
            // Step 0: Face the ability direction
            ctx.caster.FaceDirection(ctx.aimDir);

            // Use the timing-based animation system
            yield return ctx.caster.StartCoroutine(ctx.caster.ExecuteAbilityAnimationSequence(ctx, targets));
        }

        // Validation method to check if ability can be used
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

            // Check if we have valid targets or can execute without targets
            var targets = targeting.SelectTargets(ctx);
            return targets.Count > 0 || canExecuteWithoutTargets;
        }
    }

    // Data passed through targeting/effects during one cast.
    public class AbilityContext
    {
        public Unit caster;
        public Ability ability;
        public Vector2Int aimDir;
        public Tile targetTile;

        public Tile OriginTile => caster?.currentTile;
    }
}