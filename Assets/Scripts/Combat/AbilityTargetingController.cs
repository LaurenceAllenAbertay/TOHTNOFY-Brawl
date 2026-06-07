using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Owns the ability targeting state machine: which ability is active, which tile
    /// is hovered, range-preview highlights, and confirmation/cancellation.
    /// Attach to the same GameObject as CombatManager.
    ///
    /// Input is routed via virtual properties on AbilityTargeting (UsesDirectionalInput,
    /// UsesHoverTracking, ConfirmsOnTileClick) so this class never needs to know which
    /// concrete targeting type is active. Adding a new targeting type requires zero
    /// changes here.
    ///
    /// When an ability is confirmed it calls back to CombatManager.StartAbilityExecution
    /// so CombatManager remains the single owner of execution-state flags.
    /// </summary>
    public class AbilityTargetingController : MonoBehaviour
    {
        #region Public State

        public bool IsTargetingAbility => currentAbility != null;
        public Ability CurrentAbility => currentAbility;

        #endregion

        #region Private Fields

        private CombatManager combatManager;
        private Ability currentAbility;
        private int currentAbilitySlot;
        private Tile hoveredTile;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            combatManager = GetComponent<CombatManager>();
        }

        #endregion

        #region Targeting Entry / Exit

        /// <summary>
        /// Enters targeting mode for the given ability slot on activeUnit.
        /// </summary>
        public void EnterAbilityTargeting(int slot, Unit activeUnit)
        {
            if (activeUnit?.characterData?.abilityLoadout == null) return;

            currentAbility = activeUnit.characterData.abilityLoadout[slot];
            if (currentAbility == null || currentAbility.targeting == null)
            {
                currentAbility = null;
                return;
            }

            currentAbilitySlot = slot;
            hoveredTile = null;
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            var ctx = MakeContext(activeUnit);
            currentAbility.targeting.ShowEnterPreview(ctx, hoveredTile);

            UIEvents.OnTargetingStateChanged();
        }

        /// <summary>Cancels targeting and restores default highlights.</summary>
        public void CancelAbilityTargeting(Unit activeUnit)
        {
            // Note: RandomAOETargeting cache is intentionally NOT cleared here.
            // The same random tiles must be shown for the entire turn — cancel/retarget
            // should present the same selection. Cache is only reset at the start of a new turn.

            if (currentAbility != null)
            {
                var ctx = MakeContext(activeUnit);
                currentAbility.targeting.OnCancel(ctx);
            }

            ClearAbilityTargeting();
            RestoreDefaultHighlights(activeUnit);
            UIEvents.OnTargetingStateChanged();
        }

        /// <summary>Clears targeting state without updating highlights or firing events.</summary>
        public void ClearAbilityTargeting()
        {
            currentAbility = null;
            hoveredTile = null;
        }

        #endregion

        #region Mouse Input Handlers

        /// <summary>
        /// Called by CombatManager when a tile's OnMouseEnter fires.
        /// Only active for targeting types that use hover tracking (e.g. SingleTargeting).
        /// </summary>
        public void HandleTileHovered(Tile tile, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;
            if (!currentAbility.targeting.UsesHoverTracking) return;
            if (tile == hoveredTile) return;

            hoveredTile = tile;
            var ctx = MakeContext(activeUnit);
            currentAbility.targeting.ShowHoverPreview(ctx, hoveredTile);
        }

        public void HandleTileHoverExited(Tile tile, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;
            if (!currentAbility.targeting.UsesHoverTracking) return;

            // Only clear if the exited tile is the one we're tracking — OnMouseEnter on the
            // next tile fires before OnMouseExit on the previous one in Unity, so if hoveredTile
            // has already advanced we leave it alone.
            if (tile != hoveredTile) return;

            hoveredTile = null;
            var ctx = MakeContext(activeUnit);
            currentAbility.targeting.ShowHoverPreview(ctx, hoveredTile);
        }

        public void HandleMouseMoved(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;

            var targeting = currentAbility.targeting;

            if (targeting.UsesDirectionalInput)
            {
                // Directional types (Line, AOE, MovementLine) update the preview on mouse move.
                HandleDirectionalMouseMove(mouseWorldPosition, activeUnit);
            }
            else if (!targeting.UsesHoverTracking)
            {
                // Non-directional, non-hover types (e.g. MultiTileSelection) get a generic
                // mouse-moved notification so they can update their own preview state.
                Tile newHover = GetHoveredTile(mouseWorldPosition);
                if (newHover == hoveredTile) return;
                hoveredTile = newHover;

                var ctx = MakeContext(activeUnit);
                targeting.OnMouseMoved(ctx, hoveredTile);
            }
            // UsesHoverTracking types (SingleTargeting) are driven by HandleTileHovered —
            // no polling needed here.
        }

        public void HandleMouseClicked(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;

            // Only directional targeting types confirm via raw mouse position.
            // All others confirm via HandleTileClicked.
            if (!currentAbility.targeting.UsesDirectionalInput) return;

            HandleDirectionalMouseClick(mouseWorldPosition, activeUnit);
        }

        public void HandleTileClicked(Tile clickedTile, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;
            if (!currentAbility.targeting.ConfirmsOnTileClick) return;

            var ctx = MakeContext(activeUnit);
            bool confirmed = currentAbility.targeting.OnTileClicked(clickedTile, ctx, out var outCtx);

            if (confirmed)
            {
                if (!ValidateAbilityExecution(currentAbility, activeUnit, outCtx))
                    return;

                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
                bool isDirectional = false;
                combatManager.StartAbilityExecution(
                    currentAbility, outCtx, isDirectional, Vector2Int.zero);
            }
        }

        #endregion

        #region Directional Targeting

        private void HandleDirectionalMouseMove(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            Vector3 dir = (mouseWorldPosition - activeUnit.transform.position);
            dir.y = 0;

            if (dir.sqrMagnitude > 0.1f)
            {
                Vector2Int aimDir = GetCardinalDirection(dir.normalized);
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.AbilityPreview,
                    activeUnit,
                    currentAbility,
                    aimDir);
            }
        }

        private void HandleDirectionalMouseClick(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            Vector3 dir = (mouseWorldPosition - activeUnit.transform.position);
            dir.y = 0;

            if (dir.sqrMagnitude > 0.1f)
            {
                Vector2Int aimDir = GetCardinalDirection(dir.normalized);
                ConfirmDirectionalAbility(aimDir, activeUnit);
            }
        }

        private void ConfirmDirectionalAbility(Vector2Int aimDir, Unit activeUnit)
        {
            var ctx = MakeContext(activeUnit, aimDir: aimDir);
            if (!ValidateAbilityExecution(currentAbility, activeUnit, ctx)) return;

            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            combatManager.StartAbilityExecution(currentAbility, null, isDirectional: true, aimDir);
        }

        #endregion

        #region Highlight Helpers

        /// <summary>Restores default movement highlights after targeting ends.</summary>
        public void RestoreDefaultHighlights(Unit activeUnit, bool suppressMovement = false)
        {
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            if (suppressMovement) return;
            if (combatManager != null && combatManager.IsExecutingPendingAction) return;

            if (activeUnit is PlayerUnit && combatManager != null && combatManager.CanMove)
            {
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.Movement,
                    activeUnit,
                    movementRangeOverride: combatManager.GetRemainingMovement());
            }
        }

        /// <summary>
        /// Re-shows the enter preview for the active targeting type.
        /// Called after a failed execution attempt so the player can retry.
        /// </summary>
        public void ShowRetryPreview(Unit activeUnit)
        {
            if (!IsTargetingAbility) return;
            var ctx = MakeContext(activeUnit);
            currentAbility.targeting.ShowEnterPreview(ctx, hoveredTile);
        }

        #endregion

        #region Validation

        private bool ValidateAbilityExecution(Ability ability, Unit activeUnit, AbilityContext ctx)
        {
            if (ability == null || ability.targeting == null) return false;

            // Build a temporary context for target resolution if one wasn't provided.
            var evalCtx = ctx ?? MakeContext(activeUnit);

            var targets = ability.targeting.SelectTargets(evalCtx);
            return targets.Count > 0 || ability.canExecuteWithoutTargets;
        }

        #endregion

        #region Utilities

        private AbilityContext MakeContext(Unit activeUnit, Tile targetTile = null, Vector2Int aimDir = default)
        {
            return new AbilityContext
            {
                caster     = activeUnit,
                ability    = currentAbility,
                targetTile = targetTile,
                aimDir     = aimDir
            };
        }

        private Vector2Int GetCardinalDirection(Vector3 dir)
        {
            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.z))
                return dir.x > 0 ? Vector2Int.right : Vector2Int.left;
            else
                return dir.z > 0 ? Vector2Int.up : Vector2Int.down;
        }

        private Tile GetHoveredTile(Vector3 fallbackMouseWorldPosition)
        {
            var camera = Camera.main;
            var grid = GridManager.Instance;
            if (grid == null) return null;

            var tileFromCursor = grid.GetTileAtScreenPosition(camera, Input.mousePosition);
            if (tileFromCursor != null) return tileFromCursor;

            return grid.GetTileAtPosition(fallbackMouseWorldPosition);
        }

        #endregion
    }
}