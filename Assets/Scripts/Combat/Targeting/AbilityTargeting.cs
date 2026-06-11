using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public abstract class AbilityTargeting : ScriptableObject
    {
        [Header("Traversal Settings")]
        [Tooltip("If true, the ability can pass through walls")]
        public bool affectsThroughWalls = false;

        [Tooltip("If true, the ability can pass over gaps and impassable terrain")]
        public bool affectsOverGaps = true;

        [Tooltip("If true, the ability reaches tiles on a higher Y level than the caster. " +
                 "A tile that is 1 step across AND 1 step up costs 2 range, not 1.")]
        public bool affectsUpperLayers = false;

        [Tooltip("If true, the ability reaches tiles on a lower Y level than the caster. " +
                 "A tile that is 1 step across AND 1 step down costs 2 range, not 1.")]
        public bool affectsLowerLayers = false;

        // ── Input behaviour descriptors ───────────────────────────────────────────
        // AbilityTargetingController reads these to route input without type-checking.

        /// <summary>
        /// True when this targeting type is aimed by mouse direction (line, AOE directional).
        /// False for types that confirm via tile click or accumulate tile selections.
        /// When false, HandleMouseMoved and HandleMouseClicked are no-ops for this type.
        /// </summary>
        public virtual bool UsesDirectionalInput => true;

        /// <summary>
        /// True when this targeting type tracks the hovered tile for highlight feedback
        /// (SingleTargeting only — uses Unity OnMouseEnter/Exit rather than polling).
        /// When false, HandleTileHovered and HandleTileHoverExited are no-ops.
        /// </summary>
        public virtual bool UsesHoverTracking => false;

        /// <summary>
        /// True when a tile click should confirm the ability rather than a directional
        /// mouse click. When true, HandleTileClicked calls OnTileClicked; when false,
        /// the directional mouse-click path fires instead.
        /// </summary>
        public virtual bool ConfirmsOnTileClick => false;

        /// <summary>
        /// True when AbilitySequencer should pan the camera to each target individually
        /// before applying effects. Used for abilities where seeing each hit land on its
        /// target matters (SingleTargeting, LineTargeting).
        /// False for AOE and directional types where a single camera position covers all targets.
        /// </summary>
        public virtual bool UsesCameraTransitionsPerTarget => false;

        // ── Lifecycle hooks ───────────────────────────────────────────────────────

        /// <summary>
        /// Called by AbilityTargetingController immediately after entering targeting mode.
        /// Draw range/preview highlights here. hoveredTile is the currently hovered tile
        /// (may be null), passed from the controller which owns that state.
        /// </summary>
        public virtual void ShowEnterPreview(AbilityContext ctx, Tile hoveredTile) { }

        /// <summary>
        /// Called when the player hovers over a tile while this targeting type is active.
        /// Only invoked when UsesHoverTracking is true.
        /// Use to update hover-sensitive highlights (e.g. SingleTargeting selection colour).
        /// </summary>
        public virtual void ShowHoverPreview(AbilityContext ctx, Tile hoveredTile) { }

        /// <summary>
        /// Called when the mouse moves while this targeting type is active and
        /// UsesDirectionalInput is false. Use to update highlight state that depends
        /// on cursor position without directional confirmation (e.g. MultiTileSelection).
        /// </summary>
        public virtual void OnMouseMoved(AbilityContext ctx, Tile hoveredTile) { }

        /// <summary>
        /// Called when a tile is clicked while this targeting type is active and
        /// ConfirmsOnTileClick is true. Return true when the ability should be confirmed
        /// (so the controller can call StartAbilityExecution), false to keep targeting open
        /// (e.g. MultiTileSelection accumulating clicks). outCtx should be set to the
        /// context to pass to StartAbilityExecution, or left null for directional/AOE casts.
        /// </summary>
        public virtual bool OnTileClicked(Tile tile, AbilityContext ctx, out AbilityContext outCtx)
        {
            outCtx = null;
            return false;
        }

        /// <summary>
        /// Called when targeting is cancelled (Escape or ability deselect).
        /// Override to clean up any per-session state (e.g. MultiTileSelection clears
        /// its accumulated selection list).
        /// </summary>
        public virtual void OnCancel(AbilityContext ctx) { }

        /// <summary>
        /// Called at the start of every turn and after the active unit moves.
        /// Override in targeting types that cache per-turn selections (e.g. RandomAOETargeting)
        /// so the roll stays fresh relative to the unit's current position.
        /// </summary>
        public virtual void ResetForNewTurn() { }

        /// <summary>
        /// True when the camera should zoom out to frame the full ability range after execution,
        /// then smoothly return to the caster. Used for AOE abilities where all hit tiles
        /// should be visible simultaneously.
        /// </summary>
        public virtual bool UsesCameraZoomAfterExecution => false;

        // ── Core targeting ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when <paramref name="aimDir"/> is a valid firing direction for
        /// this targeting type. The default implementation allows all directions; override
        /// in types that restrict aim (e.g. horizontalOnly LineTargeting).
        /// AbilityTargetingController calls this before previewing or confirming so invalid
        /// directions are silently ignored rather than firing with an empty traversal.
        /// </summary>
        public virtual bool IsValidAimDirection(Vector2Int aimDir) => true;

        public abstract List<Tile> GetTraversal(AbilityContext ctx);

        // Resolve which units to actually hit, respecting filters / pass-through / maxTargets.
        public virtual List<Unit> SelectTargets(AbilityContext ctx)
        {
            var result = new List<Unit>();
            if (ctx?.ability == null || ctx.caster == null) return result;

            var tiles = GetTraversal(ctx);
            foreach (var t in tiles)
            {
                var u = t.currentUnit;
                if (u == null) continue;

                bool isAlly = IsAlly(ctx.caster, u);
                if ((isAlly && ctx.ability.canHitAllies) || (!isAlly && ctx.ability.canHitEnemies))
                {
                    result.Add(u);

                    if (result.Count >= ctx.ability.maxTargets)
                        break;

                    if (!ctx.ability.passThroughUnits)
                        break;
                }
            }
            return result;
        }

        // ── Shared helpers ────────────────────────────────────────────────────────

        protected bool IsAlly(Unit a, Unit b)
        {
            bool aIsEnemy = a is EnemyUnit;
            bool bIsEnemy = b is EnemyUnit;
            return aIsEnemy == bIsEnemy;
        }

        /// <summary>
        /// Highlights traversal tiles using the standard AOE colour scheme:
        /// hittable units get AttackRange, all other tiles get Danger.
        /// Shared by RandomAOETargeting, SquareAOETargeting, and any future fixed-preview type.
        /// </summary>
        protected void ShowTraversalPreview(AbilityContext ctx)
        {
            var tiles = GetTraversal(ctx);
            GridManager.Instance.ClearAllHighlights();
            foreach (var tile in tiles)
            {
                var u = tile.currentUnit;
                if (u != null)
                {
                    bool isAlly = IsAlly(ctx.caster, u);
                    bool canHit = (isAlly && ctx.ability.canHitAllies) ||
                                  (!isAlly && ctx.ability.canHitEnemies);
                    tile.Highlight(canHit ? TileHighlightType.AttackRange : TileHighlightType.Danger);
                }
                else
                {
                    tile.Highlight(TileHighlightType.Danger);
                }
            }
        }

        protected bool IsBlockedByWall(Tile fromTile, Tile toTile)
        {
            if (fromTile == null || toTile == null) return true;

            Vector3 startPos = fromTile.transform.position + Vector3.up * 0.5f;
            Vector3 endPos   = toTile.transform.position   + Vector3.up * 0.5f;
            Vector3 direction = (endPos - startPos).normalized;
            float distance = Vector3.Distance(startPos, endPos);

            int wallsLayerMask = LayerMask.GetMask("Walls");
            return Physics.Raycast(startPos, direction, out _, distance * 0.9f, wallsLayerMask);
        }

        /// <summary>
        /// Returns true when <paramref name="candidate"/> passes the layer flags on this targeting asset,
        /// and sets <paramref name="dist3D"/> to the full 3D Manhattan distance (X + Z + Y steps).
        /// Follows the same pattern as JumpSystem.GetJumpableTiles:
        ///   - always uses GetGridDistance(includeYLevel: true) for cost
        ///   - rejects tiles above the caster when affectsUpperLayers is false
        ///   - rejects tiles below the caster when affectsLowerLayers is false
        ///   - same-level tiles always pass (neither flag applies)
        /// </summary>
        protected bool IsAllowedByLayerFlags(Tile center, Tile candidate, out int dist3D)
        {
            dist3D = int.MaxValue;
            if (center == null || candidate == null) return false;

            // Use the same Y-level test that GridManager uses internally.
            bool sameLevel = GridManager.Instance.IsSameYLevel(center, candidate);

            if (!sameLevel)
            {
                bool candidateIsAbove = candidate.transform.position.y > center.transform.position.y;

                if (candidateIsAbove && !affectsUpperLayers) return false;
                if (!candidateIsAbove && !affectsLowerLayers) return false;
            }

            // Always measure using 3D Manhattan distance — same as JumpSystem does.
            dist3D = GridManager.Instance.GetGridDistance(center, candidate, includeYLevel: true);
            return true;
        }
    }
}