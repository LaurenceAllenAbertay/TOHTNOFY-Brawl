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

        [Tooltip("If true, the caster's turn ends immediately after this ability executes.")]
        public bool endTurnOnCast = false;

        [Header("Animation & Visual Effects")]
        [SerializeField] private string animationState = "Attack_Melee_1";

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
    }
}