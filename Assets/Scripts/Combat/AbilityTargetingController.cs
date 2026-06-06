using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Owns the ability targeting state machine: which ability is active, which tile
    /// is hovered, range-preview highlights, and confirmation/cancellation.
    /// Attach to the same GameObject as CombatManager.
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
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            if (currentAbility.targeting is SingleTargeting)
                ShowSingleTargetRangePreview(activeUnit);

            if (currentAbility.targeting is RandomAOETargeting)
                ShowRandomAOEPreview(activeUnit);

            if (currentAbility.targeting is SquareAOETargeting)
                ShowSquareAOEPreview(activeUnit);

            if (currentAbility.targeting is MultiTileSelectionTargeting multiTargeting)
            {
                var ctx = new AbilityContext { caster = activeUnit, ability = currentAbility };
                multiTargeting.BeginSelection(ctx);
                ShowMultiTileSelectionPreview(multiTargeting, ctx);
            }

            UIEvents.OnTargetingStateChanged();
        }

        /// <summary>Cancels targeting and restores default highlights.</summary>
        public void CancelAbilityTargeting(Unit activeUnit)
        {
            // Note: RandomAOETargeting cache is intentionally NOT cleared here.
            // The same random tiles must be shown for the entire turn — cancel/retarget
            // should present the same selection. Cache is only reset at the start of a new turn.

            if (currentAbility?.targeting is MultiTileSelectionTargeting multiTargeting)
                multiTargeting.CancelSelection();

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
        /// Drives hoveredTile for single targeting via Unity's physics collider system,
        /// which is consistent with OnMouseDown and correct on multi-level maps.
        /// </summary>
        public void HandleTileHovered(Tile tile, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;
            if (!(currentAbility.targeting is SingleTargeting)) return;

            if (tile == hoveredTile) return;
            hoveredTile = tile;
            ShowSingleTargetRangePreview(activeUnit);
        }

        public void HandleTileHoverExited(Tile tile, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;
            if (!(currentAbility.targeting is SingleTargeting)) return;

            // Only clear if the exited tile is the one we're tracking — OnMouseEnter on the
            // next tile fires before OnMouseExit on the previous one in Unity, so if hoveredTile
            // has already advanced we leave it alone.
            if (tile != hoveredTile) return;
            hoveredTile = null;
            ShowSingleTargetRangePreview(activeUnit);
        }

        public void HandleMouseMoved(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;

            if (currentAbility.targeting is SingleTargeting)
                return; // hoveredTile is driven by HandleTileHovered (OnMouseEnter) — no polling needed
            else if (currentAbility.targeting is MultiTileSelectionTargeting)
                HandleMultiTileSelectionMouseMove(mouseWorldPosition, activeUnit);
            else if (currentAbility.targeting is RandomAOETargeting)
                return; // Highlights are fixed for the turn — no update on mouse move
            else if (currentAbility.targeting is SquareAOETargeting)
                return; // Highlights fixed to caster position — no update on mouse move
            else
                HandleDirectionalTargetingMouseMove(mouseWorldPosition, activeUnit);
        }

        public void HandleMouseClicked(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;

            if (currentAbility.targeting is SingleTargeting)
                return; // Single targeting uses tile click events, not raw mouse position

            if (currentAbility.targeting is RandomAOETargeting)
                return; // RandomAOE confirms via tile click — handled in HandleTileClicked

            if (currentAbility.targeting is SquareAOETargeting)
                return; // SquareAOE confirms via tile click — handled in HandleTileClicked

            HandleDirectionalAbilityClick(mouseWorldPosition, activeUnit);
        }

        public void HandleTileClicked(Tile clickedTile, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;

            if (currentAbility.targeting is SingleTargeting)
                ConfirmSingleTargetAbility(clickedTile, activeUnit);
            else if (currentAbility.targeting is MultiTileSelectionTargeting multiTargeting)
                HandleMultiTileSelectionClick(clickedTile, multiTargeting, activeUnit);
            else if (currentAbility.targeting is RandomAOETargeting)
                ConfirmRandomAOEAbility(activeUnit);
            else if (currentAbility.targeting is SquareAOETargeting)
                ConfirmSquareAOEAbility(activeUnit);
        }

        #endregion

        #region Directional Targeting

        private void HandleDirectionalTargetingMouseMove(Vector3 mouseWorldPosition, Unit activeUnit)
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

        private void HandleDirectionalAbilityClick(Vector3 mouseWorldPosition, Unit activeUnit)
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
            if (!ValidateAbilityExecution(currentAbility, activeUnit, null, aimDir)) return;

            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            combatManager.StartAbilityExecution(currentAbility, null, isDirectional: true, aimDir);
        }

        #endregion

        #region Single Targeting

        private void ConfirmSingleTargetAbility(Tile targetTile, Unit activeUnit)
        {
            if (targetTile == null) return;
            if (!(currentAbility.targeting is SingleTargeting singleTargeting)) return;

            var ctx = new AbilityContext
            {
                caster = activeUnit,
                ability = currentAbility,
                targetTile = targetTile
            };

            if (!singleTargeting.IsWithinRange(ctx, targetTile))
            {
                Debug.Log("Target tile is out of range. Try again.");
                return;
            }

            bool isTeleportAbility = currentAbility.effects.Exists(e => e is TeleportEffect);
            if (isTeleportAbility && (targetTile.occupied || !targetTile.passableTerrain))
            {
                Debug.Log("Cannot teleport to occupied or impassable tile. Try again.");
                return;
            }

            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            combatManager.StartAbilityExecution(currentAbility, ctx, isDirectional: false, Vector2Int.zero);
        }

        private void ShowSingleTargetRangePreview(Unit activeUnit)
        {
            if (!(currentAbility.targeting is SingleTargeting singleTargeting)) return;

            var ctx = new AbilityContext { caster = activeUnit, ability = currentAbility };
            var tilesInRange = singleTargeting.GetTilesInRange(ctx);

            GridManager.Instance.ClearAllHighlights();
            foreach (var tile in tilesInRange)
                tile.Highlight(TileHighlightType.Danger);

            if (hoveredTile != null && tilesInRange.Contains(hoveredTile))
            {
                bool isTeleportAbility = currentAbility.effects.Exists(e => e is TeleportEffect);

                if (isTeleportAbility)
                {
                    if (hoveredTile.currentUnit == null && hoveredTile.passableTerrain)
                        hoveredTile.Highlight(TileHighlightType.AttackRange);
                }
                else if (hoveredTile.currentUnit != null)
                {
                    var unit = hoveredTile.currentUnit;
                    bool isAlly = unit is EnemyUnit == activeUnit is EnemyUnit;
                    bool canHit = (isAlly && currentAbility.canHitAllies) || (!isAlly && currentAbility.canHitEnemies);
                    if (canHit) hoveredTile.Highlight(TileHighlightType.AttackRange);
                }
            }
        }

        #endregion

        #region Multi-Tile Selection Targeting

        private void HandleMultiTileSelectionMouseMove(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            if (!(currentAbility.targeting is MultiTileSelectionTargeting multiTargeting)) return;

            Tile newHover = GetHoveredTile(mouseWorldPosition);

            if (newHover == hoveredTile) return;
            hoveredTile = newHover;

            var ctx = new AbilityContext { caster = activeUnit, ability = currentAbility };
            ShowMultiTileSelectionPreview(multiTargeting, ctx);

            if (hoveredTile != null && multiTargeting.IsValidSelection(hoveredTile, ctx))
                hoveredTile.Highlight(TileHighlightType.Occupied);
        }

        private void HandleMultiTileSelectionClick(Tile clickedTile, MultiTileSelectionTargeting targeting, Unit activeUnit)
        {
            var ctx = new AbilityContext { caster = activeUnit, ability = currentAbility };

            if (!targeting.TrySelectTile(clickedTile, ctx)) return;

            if (targeting.IsComplete)
            {
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
                combatManager.StartAbilityExecution(currentAbility, ctx, isDirectional: false, Vector2Int.zero);
            }
            else
            {
                ShowMultiTileSelectionPreview(targeting, ctx);
            }
        }

        private void ShowMultiTileSelectionPreview(MultiTileSelectionTargeting targeting, AbilityContext ctx)
        {
            GridManager.Instance.ClearAllHighlights();

            foreach (var tile in targeting.GetTilesInRange(ctx))
            {
                if (!targeting.SelectedTiles.Contains(tile))
                    tile.Highlight(TileHighlightType.Moveable);
            }

            foreach (var tile in targeting.SelectedTiles)
                tile.Highlight(TileHighlightType.AttackRange);
        }

        #endregion

        #region Random AOE Targeting

        private void ShowRandomAOEPreview(Unit activeUnit)
        {
            var ctx = new AbilityContext { caster = activeUnit, ability = currentAbility };
            var tiles = currentAbility.targeting.GetTraversal(ctx);

            GridManager.Instance.ClearAllHighlights();
            foreach (var tile in tiles)
            {
                var u = tile.currentUnit;
                if (u != null)
                {
                    bool isAlly = u is EnemyUnit == activeUnit is EnemyUnit;
                    bool canHit = (isAlly && currentAbility.canHitAllies) || (!isAlly && currentAbility.canHitEnemies);
                    tile.Highlight(canHit ? TileHighlightType.AttackRange : TileHighlightType.Danger);
                }
                else
                {
                    tile.Highlight(TileHighlightType.Danger);
                }
            }
        }

        private void ShowSquareAOEPreview(Unit activeUnit)
        {
            var ctx = new AbilityContext { caster = activeUnit, ability = currentAbility };
            var tiles = currentAbility.targeting.GetTraversal(ctx);

            GridManager.Instance.ClearAllHighlights();
            foreach (var tile in tiles)
            {
                var u = tile.currentUnit;
                if (u != null)
                {
                    bool isAlly = u is EnemyUnit == activeUnit is EnemyUnit;
                    bool canHit = (isAlly && currentAbility.canHitAllies) || (!isAlly && currentAbility.canHitEnemies);
                    tile.Highlight(canHit ? TileHighlightType.AttackRange : TileHighlightType.Danger);
                }
                else
                {
                    tile.Highlight(TileHighlightType.Danger);
                }
            }
        }

        private void ConfirmRandomAOEAbility(Unit activeUnit)
        {
            if (!ValidateAbilityExecution(currentAbility, activeUnit, null, Vector2Int.zero)) return;
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            combatManager.StartAbilityExecution(currentAbility, null, isDirectional: false, Vector2Int.zero);
        }

        private void ConfirmSquareAOEAbility(Unit activeUnit)
        {
            if (!ValidateAbilityExecution(currentAbility, activeUnit, null, Vector2Int.zero)) return;
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            combatManager.StartAbilityExecution(currentAbility, null, isDirectional: false, Vector2Int.zero);
        }

        #endregion

        #region Highlight Helpers

        /// <summary>Restores default movement highlights after targeting ends.</summary>
        public void RestoreDefaultHighlights(Unit activeUnit, bool suppressMovement = false)
        {
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            // Suppress if caller requests it, or if a pending action is auto-executing.
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

        /// <summary>Re-shows single-target range preview — called on failed execution to allow retry.</summary>
        public void ShowRetryPreview(Unit activeUnit)
        {
            if (currentAbility?.targeting is SingleTargeting)
                ShowSingleTargetRangePreview(activeUnit);
        }

        #endregion

        #region Validation

        private bool ValidateAbilityExecution(Ability ability, Unit activeUnit, Tile targetTile, Vector2Int aimDir)
        {
            if (ability == null || ability.targeting == null) return false;

            var ctx = new AbilityContext
            {
                caster = activeUnit,
                ability = ability,
                aimDir = aimDir,
                targetTile = targetTile
            };

            var targets = ability.targeting.SelectTargets(ctx);
            return targets.Count > 0 || ability.canExecuteWithoutTargets;
        }

        #endregion

        #region Utilities

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