using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// AbilityEffect subclass that spawns a NeutralUnit prefab onto a selected empty tile.
    ///
    /// Designed for abilities like Oliver's Mega Mug placement — the caster selects an
    /// empty tile via SingleTargeting, and this effect instantiates the prefab there,
    /// snaps it to the tile, and registers it with UnitManager so the environment-turn
    /// pass can find it via AllNeutralUnits.
    ///
    /// If <see cref="allowOnlyOne"/> is true (the default), any existing live instance
    /// spawned by this effect asset is silently removed before the new one appears,
    /// enforcing the "only one mug at a time" rule at the effect level rather than
    /// requiring ability-level gating.
    ///
    /// Ability ScriptableObject setup:
    ///   • Targeting        → SingleTargeting (UsesHoverTracking, ConfirmsOnTileClick)
    ///   • canExecuteWithoutTargets = false
    ///   • This effect's RequiresEmptyTargetTile = true causes SingleTargeting to colour
    ///     the hover highlight correctly — occupied tiles will not highlight green.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Effects > Spawn Neutral Unit
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Spawn Neutral Unit")]
    public class SpawnNeutralUnitEffect : AbilityEffect
    {
        [Header("Prefab")]
        [Tooltip("The NeutralUnit prefab to instantiate. Must have a NeutralUnit component.")]
        [SerializeField] private GameObject neutralUnitPrefab;

        [Header("Placement Rules")]
        [Tooltip("If true, any existing live instance spawned by this same effect asset is " +
                 "removed before the new one appears. Use for 'only one at a time' units " +
                 "such as the Mega Mug.")]
        [SerializeField] private bool allowOnlyOne = true;

        [Tooltip("Vertical offset applied when placing the prefab so it sits at the correct " +
                 "height above the tile surface.")]
        [SerializeField] private Vector3 spawnOffset = Vector3.zero;

        // ── Runtime state ────────────────────────────────────────────────────

        // The currently live instance spawned by this effect asset (if any).
        // Stored on the ScriptableObject — survives scene-to-scene but is intentionally
        // reset to null by OnInstanceDestroyed() when the unit is destroyed.
        private NeutralUnit _activeInstance;

        /// <summary>
        /// True while a live instance spawned by this effect asset exists on the map.
        /// Read externally (e.g. ability button state) to know whether the ability is
        /// currently "locked" due to an existing placement.
        /// </summary>
        public bool HasActiveInstance => _activeInstance != null;

        // ── AbilityEffect overrides ───────────────────────────────────────────

        /// <summary>PostEffect — runs after all other effects in the cast sequence.</summary>
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.PostEffect;

        /// <summary>
        /// Tells SingleTargeting's hover logic that the target tile must be empty and
        /// passable — occupied tiles will not show the green "valid" highlight.
        /// </summary>
        public override bool RequiresEmptyTargetTile => true;

        // ── Apply ─────────────────────────────────────────────────────────────

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (neutralUnitPrefab == null)
            {
                Debug.LogWarning("[SpawnNeutralUnitEffect] No prefab assigned — nothing will spawn.");
                return;
            }

            // Resolve the target tile from the context.
            // SingleTargeting writes the confirmed tile into ctx.targetTile.
            Tile spawnTile = ctx.targetTile ?? ctx.caster?.currentTile;
            if (spawnTile == null)
            {
                Debug.LogWarning("[SpawnNeutralUnitEffect] No valid target tile found in AbilityContext.");
                return;
            }

            // The grid is the source of truth — reject occupied tiles.
            if (spawnTile.currentUnit != null)
            {
                Debug.LogWarning($"[SpawnNeutralUnitEffect] Target tile '{spawnTile.name}' is occupied — spawn aborted.");
                return;
            }

            // Destroy any existing instance if the "only one" rule is active.
            if (allowOnlyOne && _activeInstance != null)
            {
                RemoveActiveInstance();
            }

            // Instantiate the prefab.
            Vector3 spawnPosition = spawnTile.transform.position + spawnOffset;
            GameObject go = Object.Instantiate(
                neutralUnitPrefab, spawnPosition, neutralUnitPrefab.transform.rotation);
            go.name = neutralUnitPrefab.name; // strip "(Clone)"

            NeutralUnit spawned = go.GetComponent<NeutralUnit>();
            if (spawned == null)
            {
                Debug.LogError($"[SpawnNeutralUnitEffect] Prefab '{neutralUnitPrefab.name}' " +
                               $"has no NeutralUnit component — destroying.");
                Object.Destroy(go);
                return;
            }

            // Unit.Awake snaps to the tile via GetTileAtPosition, but set explicitly here
            // so the grid is definitely correct regardless of spawn position precision.
            spawned.SetCurrentTile(spawnTile);

            // Wire the back-reference so the unit can notify us when it is destroyed.
            spawned.spawnSource = this;

            // Register with UnitManager — adds to AllUnits and AllNeutralUnits.
            UnitManager.RegisterUnit(spawned);

            _activeInstance = spawned;

            Debug.Log($"[SpawnNeutralUnitEffect] Spawned '{go.name}' on '{spawnTile.name}'.");
        }

        // ── Lifecycle callback from NeutralUnit ───────────────────────────────

        /// <summary>
        /// Called by NeutralUnit.OnDestroy() so this asset clears its stale reference
        /// and HasActiveInstance returns false, allowing the ability to be cast again.
        /// </summary>
        public void OnInstanceDestroyed(NeutralUnit instance)
        {
            if (_activeInstance == instance)
                _activeInstance = null;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Silently removes the current active instance from the map without triggering
        /// the normal Die() / death-animation path. Used when a new placement replaces
        /// the old one (the mug is "recalled" rather than destroyed in combat).
        /// </summary>
        private void RemoveActiveInstance()
        {
            if (_activeInstance == null) return;

            // Vacate the tile so the grid stays consistent.
            if (_activeInstance.currentTile != null &&
                _activeInstance.currentTile.currentUnit == _activeInstance)
            {
                _activeInstance.currentTile.currentUnit = null;
            }

            UnitManager.UnregisterUnit(_activeInstance);
            Object.Destroy(_activeInstance.gameObject);
            // _activeInstance is cleared by NeutralUnit.OnDestroy → OnInstanceDestroyed.
        }
    }
}