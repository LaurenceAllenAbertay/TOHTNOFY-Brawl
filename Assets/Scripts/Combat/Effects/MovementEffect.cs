using DDD.TNFY.BRAWL;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Effects/Movement Effect")]
public class MovementEffect : AbilityEffect
{
    [Tooltip("Direction to move relative to aim direction")]
    public Vector2Int moveDirection = Vector2Int.down; // down = backwards from up

    [Tooltip("How far to move")]
    public int moveDistance = 1;

    [Tooltip("If true, applies movement to caster instead of targets")]
    public bool applyToCaster = false;

    [Header("Animation Settings")]
    [Tooltip("How long the movement takes per tile")]
    public float movementDurationPerTile = 0.3f;

    public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
    {
        if (ctx?.caster == null) return;

        if (applyToCaster)
        {
            // Apply to caster only (original SelfMovementEffect behavior)
            if (ctx.caster.currentTile != null)
            {
                ctx.caster.StartCoroutine(ApplyMovementWithAnimation(ctx, ctx.caster));
            }
        }
        else
        {
            // Apply to all targets
            if (targets != null)
            {
                foreach (var target in targets)
                {
                    if (target?.currentTile != null)
                    {
                        ctx.caster.StartCoroutine(ApplyMovementWithAnimation(ctx, target));
                    }
                }
            }
        }
    }

    private IEnumerator ApplyMovementWithAnimation(AbilityContext ctx, Unit unitToMove)
    {
        // Calculate actual movement direction based on aim direction
        Vector2Int actualDirection = CalculateMovementDirection(moveDirection, ctx.aimDir);

        // Calculate the full movement path
        var movementPath = CalculateMovementPath(unitToMove, actualDirection);

        if (movementPath.Count == 0)
        {
            Debug.Log($"{unitToMove.name} couldn't move {GetDirectionName(actualDirection)} - blocked or no valid tile");
            yield break;
        }

        Debug.Log($"{unitToMove.name} starting movement: {movementPath.Count} tiles in direction {GetDirectionName(actualDirection)}");

        // Handle player state during movement if this is the active player
        bool isActivePlayer = IsActivePlayerUnit(unitToMove);
        if (isActivePlayer)
        {
            BlockPlayerInputDuringMovement();
        }

        // Use regular movement animation like CombatManager does
        var unitAnimator = unitToMove.GetComponent<UnitAnimator>();
        var spriteRenderer = unitToMove.GetComponentInChildren<SpriteRenderer>();

        // Start walk animation
        if (unitAnimator != null)
        {
            unitAnimator.PlayMove();
        }

        // Move through each tile in the path
        Vector3 currentPos = unitToMove.transform.position;

        foreach (var tile in movementPath)
        {
            Vector3 segmentStart = currentPos;
            Vector3 segmentEnd = tile.transform.position;
            float segmentDuration = movementDurationPerTile;

            // Update sprite facing for this segment
            if (spriteRenderer != null)
            {
                Vector3 direction = (segmentEnd - segmentStart).normalized;
                if (Mathf.Abs(direction.x) > 0.1f)
                {
                    spriteRenderer.flipX = direction.x < 0;
                }
            }

            // Animate this segment
            float segmentElapsed = 0f;
            while (segmentElapsed < segmentDuration)
            {
                segmentElapsed += Time.deltaTime;
                float segmentT = segmentElapsed / segmentDuration;

                // Smooth interpolation for this segment
                float smoothSegmentT = Mathf.SmoothStep(0f, 1f, segmentT);
                unitToMove.transform.position = Vector3.Lerp(segmentStart, segmentEnd, smoothSegmentT);

                yield return null;
            }

            currentPos = segmentEnd;

            // Update logical tile position
            unitToMove.SetCurrentTileLogical(tile);
        }

        // Ensure exact final position
        if (movementPath.Count > 0)
        {
            unitToMove.transform.position = movementPath[movementPath.Count - 1].transform.position;
            unitToMove.SetCurrentTile(movementPath[movementPath.Count - 1]);
        }

        // Stop walk animation and return to idle
        if (unitAnimator != null)
        {
            unitAnimator.PlayIdle();
        }

        // Return to natural facing after movement
        unitToMove.ReturnToNaturalFacing();

        // Restore player state if this was the active player
        if (isActivePlayer)
        {
            RestorePlayerInputAfterMovement();
        }
    }

    private List<Tile> CalculateMovementPath(Unit unit, Vector2Int direction)
    {
        var path = new List<Tile>();
        Tile current = unit.currentTile;

        for (int i = 0; i < moveDistance; i++)
        {
            var next = GridManager.Instance.GetTileInDirection(current, direction);
            if (next == null || !next.moveable || next.occupied)
                break;

            path.Add(next);
            current = next;
        }

        return path;
    }

    private Vector2Int CalculateMovementDirection(Vector2Int relativeDirection, Vector2Int aimDirection)
    {
        // Simple approach: if moveDirection is down (backwards), return opposite of aim direction
        if (relativeDirection == Vector2Int.down)
        {
            return GetOppositeDirection(aimDirection);
        }

        // For other relative directions, use rotation method
        return RotateDirection(relativeDirection, aimDirection);
    }

    private Vector2Int GetOppositeDirection(Vector2Int direction)
    {
        if (direction == Vector2Int.up) return Vector2Int.down;
        if (direction == Vector2Int.down) return Vector2Int.up;
        if (direction == Vector2Int.left) return Vector2Int.right;
        if (direction == Vector2Int.right) return Vector2Int.left;

        return Vector2Int.zero;
    }

    private Vector2Int RotateDirection(Vector2Int dir, Vector2Int aimDir)
    {
        // Convert aim direction to rotation steps (0, 1, 2, 3 = up, right, down, left)
        int rotation = 0;
        if (aimDir == Vector2Int.right) rotation = 1;
        else if (aimDir == Vector2Int.down) rotation = 2;
        else if (aimDir == Vector2Int.left) rotation = 3;

        // Rotate the movement direction
        Vector2Int result = dir;
        for (int i = 0; i < rotation; i++)
        {
            // Rotate 90 degrees clockwise: (x,y) -> (-y,x)
            result = new Vector2Int(-result.y, result.x);
        }

        return result;
    }

    private bool IsActivePlayerUnit(Unit unit)
    {
        var combatManager = Object.FindObjectOfType<CombatManager>();
        return combatManager != null &&
               combatManager.CurrentActiveUnit == unit &&
               unit is PlayerUnit;
    }

    private void BlockPlayerInputDuringMovement()
    {
        var combatManager = Object.FindObjectOfType<CombatManager>();
        if (combatManager != null)
        {
            // Clear tile highlights immediately
            if (GridManager.Instance != null)
            {
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            }

            // Block input during animation
            combatManager.currentState = CombatState.ExecutingAction;

            // Use reflection to set the private isWaitingForAnimation field
            var combatManagerType = typeof(CombatManager);
            var isWaitingForAnimationField = combatManagerType.GetField("isWaitingForAnimation",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (isWaitingForAnimationField != null)
            {
                isWaitingForAnimationField.SetValue(combatManager, true);
            }

            Debug.Log("Blocked player input during movement effect");
        }
    }

    private void RestorePlayerInputAfterMovement()
    {
        var combatManager = Object.FindObjectOfType<CombatManager>();
        if (combatManager != null)
        {
            combatManager.StartCoroutine(DelayedInputRestore(combatManager));
        }
    }

    private IEnumerator DelayedInputRestore(CombatManager combatManager)
    {
        // Wait an extra frame to ensure animation is fully complete
        yield return null;

        // Restore input state
        combatManager.currentState = CombatState.WaitingForInput;

        // Use reflection to clear the private isWaitingForAnimation field
        var combatManagerType = typeof(CombatManager);
        var isWaitingForAnimationField = combatManagerType.GetField("isWaitingForAnimation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (isWaitingForAnimationField != null)
        {
            isWaitingForAnimationField.SetValue(combatManager, false);
        }

        // Update movement highlights from the new position if player can still move
        if (combatManager.CanMove)
        {
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.Movement, combatManager.CurrentActiveUnit);
        }

        // Trigger UI updates
        UIEvents.OnCombatStateChanged();
        UIEvents.OnUnitMoved();

        Debug.Log("Restored player input after movement effect");
    }

    private string GetDirectionName(Vector2Int direction)
    {
        if (direction == Vector2Int.up) return "north";
        if (direction == Vector2Int.down) return "south";
        if (direction == Vector2Int.left) return "west";
        if (direction == Vector2Int.right) return "east";
        return "unknown";
    }
}