using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class AbilityTargetingController : MonoBehaviour
    {
        public bool IsTargetingAbility => currentAbility != null;
        public Ability CurrentAbility => currentAbility;
        
        private CombatManager combatManager;
        private Ability currentAbility;
        private int currentAbilitySlot;
        private Tile hoveredTile;
        

        #region Unity Lifecycle

        void Awake()
        {
            combatManager = GetComponent<CombatManager>();
        }

        #endregion

        #region Targeting Entry / Exit

        public void EnterAbilityTargeting(int slot, Unit activeUnit)
        {
            if (activeUnit == null) return;

            var abilities = UnitLoadoutManager.GetAbilities(activeUnit);
            currentAbility = (slot >= 0 && slot < abilities.Length) ? abilities[slot] : null;
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

            activeUnit.GetComponent<UnitAnimator>()?.PlayTargeting();
            UIEvents.OnTargetingStateChanged();
        }
        
        public void CancelAbilityTargeting(Unit activeUnit)
        {
            if (currentAbility != null)
            {
                var ctx = MakeContext(activeUnit);
                currentAbility.targeting.OnCancel(ctx);
            }

            ClearAbilityTargeting();
            RestoreDefaultHighlights(activeUnit);
            activeUnit?.GetComponent<UnitAnimator>()?.PlayIdle();
            UIEvents.OnTargetingStateChanged();
        }
        
        public void ClearAbilityTargeting()
        {
            currentAbility = null;
            hoveredTile = null;
        }

        #endregion

        #region Mouse Input Handlers
        
        public void HandleTileHovered(Tile tile, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;
            if (!currentAbility.targeting.UsesHoverTracking) return;
            if (tile == hoveredTile) return;

            hoveredTile = tile;
            TryUpdateFacingFromTile(activeUnit, hoveredTile);
            var ctx = MakeContext(activeUnit);
            currentAbility.targeting.ShowHoverPreview(ctx, hoveredTile);
        }

        public void HandleTileHoverExited(Tile tile, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;
            if (!currentAbility.targeting.UsesHoverTracking) return;

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
                HandleDirectionalMouseMove(mouseWorldPosition, activeUnit);
            }
            else if (!targeting.UsesHoverTracking)
            {
                Tile newHover = GetHoveredTile(mouseWorldPosition);
                if (newHover == hoveredTile) return;
                hoveredTile = newHover;
                TryUpdateFacingFromTile(activeUnit, hoveredTile);

                var ctx = MakeContext(activeUnit);
                targeting.OnMouseMoved(ctx, hoveredTile);
            }
        }

        public void HandleMouseClicked(Vector3 mouseWorldPosition, Unit activeUnit)
        {
            if (!IsTargetingAbility) return;
            
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
                Vector2Int aimDir = currentAbility.targeting.ResolveAimDirection(dir.normalized);

                if (!currentAbility.targeting.IsValidAimDirection(aimDir))
                {
                    GridManager.Instance.ClearAllHighlights();
                    return;
                }

                TryUpdateFacingFromDirection(activeUnit, aimDir);

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
                Vector2Int aimDir = currentAbility.targeting.ResolveAimDirection(dir.normalized);

                if (!currentAbility.targeting.IsValidAimDirection(aimDir)) return;

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
        
        public void RestoreDefaultHighlights(Unit activeUnit, bool suppressMovement = false)
        {
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            if (suppressMovement) return;
            if (combatManager != null && combatManager.IsExecutingPendingAction) return;

            if (activeUnit != null && !activeUnit.IsAIControlled && combatManager != null && combatManager.CanMove)
            {
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.Movement,
                    activeUnit,
                    movementRangeOverride: combatManager.GetRemainingMovement());
            }
        }
        
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
                caster = activeUnit,
                ability = currentAbility,
                targetTile = targetTile,
                aimDir = aimDir
            };
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
        
        private void TryUpdateFacingFromDirection(Unit activeUnit, Vector2Int aimDir)
        {
            if (activeUnit == null) return;
            if (aimDir.x > 0)
                activeUnit.FaceDirection(Vector2Int.right);
            else if (aimDir.x < 0)
                activeUnit.FaceDirection(Vector2Int.left);
        }

        private void TryUpdateFacingFromTile(Unit activeUnit, Tile targetTile)
        {
            if (activeUnit == null || targetTile == null) return;
            float deltaX = targetTile.transform.position.x - activeUnit.transform.position.x;
            if (deltaX > 0.01f)
                activeUnit.FaceDirection(Vector2Int.right);
            else if (deltaX < -0.01f)
                activeUnit.FaceDirection(Vector2Int.left);
        }
    }
}
#endregion