using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

    public enum CameraMode
    {
        None = 1,
        Targeted = 2,
        PostExecutionZoom = 3,
        Follow = 4,
        PreExecutionZoom = 5
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
        
        [FormerlySerializedAs("suppressCameraTransitions")]
        [SerializeField, HideInInspector] private bool legacySuppressCameraTransitions = false;

        [Tooltip("How the camera behaves during this ability. Must be set explicitly — there is no fallback.")]
        public CameraMode cameraMode = CameraMode.None;
        
        [SerializeField] private string animationState = "";

        [Tooltip("If set, the caster holds on this state after animationState finishes, instead of returning to idle (e.g. Bunker_Down_Idle).")]
        [SerializeField] private string animationHoldState = "";

        [Tooltip("Played when the status effect that caused animationHoldState expires or is removed (e.g. Bunker_Down_End). Only used if Animation Hold State is set.")]
        [SerializeField] private string animationHoldReleaseState = "";
        
        [SerializeField] private GameObject castEffectPrefab;
        [SerializeField] private Vector3 castEffectOffset = Vector3.zero;
        [SerializeField] private bool parentCastEffectToCaster = false;

        [SerializeField] private GameObject hitEffectPrefab;
        [SerializeField] private Vector3 hitEffectOffset = Vector3.zero;
        [SerializeField] private bool parentHitEffectToTarget = false;

        public AbilityTargeting targeting;
        public List<AbilityEffect> effects;
        
        public string AnimationState => animationState;

        public string AnimationStateHurt => string.IsNullOrEmpty(animationState) ? "" : animationState + "_Hurt";
        public string AnimationHoldState => animationHoldState;
        public string AnimationHoldReleaseState => animationHoldReleaseState;
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

            MigrateLegacyCameraSuppression();

            if (!System.Enum.IsDefined(typeof(CameraMode), cameraMode))
            {
                Debug.LogWarning(
                    $"[Ability] '{abilityName}' has no Camera Mode set. " +
                    $"There is no fallback anymore — pick None, Targeted, or PostExecutionZoom explicitly. ({name})",
                    this);
            }
        }

        private void MigrateLegacyCameraSuppression()
        {
            if (!legacySuppressCameraTransitions) return;

            cameraMode = CameraMode.None;
            legacySuppressCameraTransitions = false;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [UnityEditor.MenuItem("Tools/DDD/Migrate Ability Camera Modes")]
        private static void MigrateAllAbilityCameraModes()
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:Ability");
            int migrated = 0;

            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var ability = UnityEditor.AssetDatabase.LoadAssetAtPath<Ability>(path);
                if (ability == null || !ability.legacySuppressCameraTransitions) continue;

                ability.MigrateLegacyCameraSuppression();
                migrated++;
            }

            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log($"[Ability] Migrated camera mode on {migrated} ability asset(s).");
        }

        [UnityEditor.MenuItem("Tools/DDD/List Abilities With Unset Camera Mode")]
        private static void ListAbilitiesWithUnsetCameraMode()
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:Ability");
            var unset = new List<string>();

            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var ability = UnityEditor.AssetDatabase.LoadAssetAtPath<Ability>(path);
                if (ability == null) continue;

                if (!System.Enum.IsDefined(typeof(CameraMode), ability.cameraMode))
                    unset.Add($"{ability.abilityName} ({path})");
            }

            if (unset.Count == 0)
            {
                Debug.Log("[Ability] Every ability has an explicit Camera Mode set.");
                return;
            }

            Debug.LogWarning($"[Ability] {unset.Count} ability asset(s) still need a Camera Mode set:\n" +
                              string.Join("\n", unset));
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