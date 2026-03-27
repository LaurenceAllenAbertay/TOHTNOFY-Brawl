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
            if (currentAbility?.targeting is RandomAOETargeting randomTargeting)
                randomTargeting.OnTargetingCancelled();

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

        public void HandleMouseMoved(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;

            if (currentAbility.targeting is SingleTargeting)
                HandleSingleTargetingMouseMove(mouseWorldPosition, activeUnit);
            else if (currentAbility.targeting is MultiTileSelectionTargeting)
                HandleMultiTileSelectionMouseMove(mouseWorldPosition, activeUnit);
            else
                HandleDirectionalTargetingMouseMove(mouseWorldPosition, activeUnit);
        }

        public void HandleMouseClicked(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;

            if (currentAbility.targeting is SingleTargeting)
                return; // Single targeting uses tile click events, not raw mouse position

            HandleDirectionalAbilityClick(mouseWorldPosition, activeUnit);
        }

        public void HandleTileClicked(Tile clickedTile, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;

            if (currentAbility.targeting is SingleTargeting)
                ConfirmSingleTargetAbility(clickedTile, activeUnit);
            else if (currentAbility.targeting is MultiTileSelectionTargeting multiTargeting)
                HandleMultiTileSelectionClick(clickedTile, multiTargeting, activeUnit);
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

        private void HandleSingleTargetingMouseMove(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            Tile targetTile = GridManager.Instance.GetClosestTile(
                mouseWorldPosition, MapManager.Instance.CurrentConfiguration.tileSpacing);

            if (targetTile != hoveredTile)
            {
                hoveredTile = targetTile;
                ShowSingleTargetRangePreview(activeUnit);
            }
        }

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

            Tile newHover = GridManager.Instance.GetClosestTile(
                mouseWorldPosition, MapManager.Instance.CurrentConfiguration.tileSpacing);

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

        #region Highlight Helpers

        /// <summary>Restores default movement highlights after targeting ends.</summary>
        public void RestoreDefaultHighlights(Unit activeUnit)
        {
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            if (activeUnit is PlayerUnit && combatManager != null && !combatManager.HasMovedThisTurn)
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.Movement, activeUnit);
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

        #endregion
    }
}