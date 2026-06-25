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

    public enum AttackType
    {
        Ranged, // Default — does not trigger Staticy or similar melee-reactive passives.
        Melee,  // Close-contact attack — triggers Staticy and any other melee-reactive passives.
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
        [Tooltip("How many of this unit's turns must pass before this ability can be used again.\n" +
                 "0 = usable every turn (no cooldown).\n" +
                 "1 = locked for the unit's next turn, available the turn after that.\n" +
                 "2 = locked for two turns, and so on.")]
        public int cooldown = 0;

        [Tooltip("Whether this ability counts as melee or ranged for passive interactions (e.g. Staticy).")]
        public AttackType attackType = AttackType.Ranged;

        [Tooltip("Who can be targeted by this ability")]
        public bool canHitEnemies = true;
        public bool canHitAllies = false;

        [Tooltip("If true, this ability can target neutral units — downed bodies and future neutral " +
                 "map objects (barrels, crates, etc.). Use for knockback abilities, AOEs, or any " +
                 "ability that should affect everything in its path regardless of faction. " +
                 "Leave false for buffs, heals, and status effects that should never land on bodies.")]
        public bool canTargetNeutral = false;

        [Tooltip("If true, ability keeps travelling through units and can hit more than one.")]
        public bool passThroughUnits = false;

        [Tooltip("Max units to affect.")]
        public int maxTargets = 1;

        [Header("Execution")]
        [Tooltip("If true, ability can execute even when no valid targets are found")]
        public bool canExecuteWithoutTargets = false;

        [Tooltip("If true, the caster's turn ends immediately after this ability executes.")]
        public bool endTurnOnCast = false;

        [Tooltip("If true, the camera stays on the caster for the full ability sequence. " +
                 "Overrides any per-target camera transitions that the targeting type would normally trigger. " +
                 "Use for abilities like Wiring Fault where the effect drives its own animated sequence.")]
        public bool suppressCameraTransitions = false;

        [Header("Animation & Visual Effects")]
        [SerializeField] private string animationState = "";

        [Tooltip("Animation state to play when the caster is in the hurt idle tier (HP < 25%). " +
                 "Leave empty to fall back to the standard Animation State.")]
        [SerializeField] private string animationStateHurt = "";

        [Header("Cast Effect (when ability starts)")]
        [SerializeField] private GameObject castEffectPrefab;
        [SerializeField] private Vector3 castEffectOffset = Vector3.zero;
        [SerializeField] private bool parentCastEffectToCaster = false;

        [Header("Hit Effect (when ability impacts targets)")]
        [SerializeField] private GameObject hitEffectPrefab;
        [SerializeField] private Vector3 hitEffectOffset = Vector3.zero;
        [SerializeField] private bool parentHitEffectToTarget = false;

        [Header("Composition")]
        public AbilityTargeting targeting;
        public List<AbilityEffect> effects;

        // Public properties for external access
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
            // Face the ability direction, then hand off to Unit's animation pipeline.
            // Unit.ExecuteAbilityAnimationSequence delegates to AbilitySequencer.BeginSequence
            // and polls until the sequence is complete — no direct reference to AbilitySequencer
            // needed here.
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

    // Data passed through targeting/effects during one cast.
    public class AbilityContext
    {
        public Unit caster;
        public Ability ability;
        public Vector2Int aimDir;
        public Tile targetTile;

        public Tile OriginTile => caster?.currentTile;

        // ability.range plus any caster passive modifier (e.g. Cannoneer +1).
        // All targeting scripts read this instead of ability.range directly.
        public int EffectiveRange => ability.range + (caster?.RangeModifier ?? 0);

        // Total damage actually dealt to all targets during this cast (after defence subtraction).
        // Written by DamageEffect.Apply; read by RecoilDamageEffect to calculate recoil.
        // Accumulated across all targets so multi-hit abilities recoil correctly.
        public int LastResolvedDamage;

        // Unit pulled to the caster's tile by WiringFaultEffect (the hooked target).
        // Written during the pull phase; read by downstream conditional logic within the
        // same effect to confirm which unit was displaced before the self-knockback fires.
        public Unit HookedUnit;
    }
}