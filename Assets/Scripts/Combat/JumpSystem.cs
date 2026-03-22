using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class JumpSystem : MonoBehaviour
    {
        [Header("Jump Settings")]
        [SerializeField] private int jumpRange = 2;
        [SerializeField] private float facingReturnDelay = 1.0f;

        private CombatManager combatManager;
        private CameraController cameraController;
        private bool isTargetingJump = false;
        private Tile hoveredTile;

        void Start()
        {
            combatManager = FindObjectOfType<CombatManager>();
            cameraController = FindObjectOfType<CameraController>();
        }

        void Update()
        {
            if (isTargetingJump)
            {
                UpdateJumpTargeting();
            }
        }

        public bool CanUseJump(Unit unit)
        {
            if (unit == null || combatManager == null) return false;
            if (!(unit is PlayerUnit)) return false;

            // Check if it's the unit's turn and they can act
            if (combatManager.CurrentActiveUnit != unit) return false;
            if (!combatManager.CanUseAbility) return false; // Need to be able to use abilities

            // Player can jump as long as they haven't used ALL their movement
            // We need to check if they still have tiles they can reach with remaining movement
            return HasMovementRemaining(unit);
        }

        private bool HasMovementRemaining(Unit unit)
        {
            // Player must have at least jumpRange movement points remaining to jump
            return combatManager.HasEnoughMovementForJump(jumpRange);
        }

        public void StartJumpTargeting()
        {
            if (!CanUseJump(combatManager.CurrentActiveUnit)) return;

            isTargetingJump = true;

            // Clear current highlights and show jump range
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            ShowJumpRange();
        }

        public void CancelJumpTargeting()
        {
            isTargetingJump = false;
            hoveredTile = null;

            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            // Restore movement highlights if player can still move
            if (combatManager.HasEnoughMovementForJump(jumpRange) && combatManager.CurrentActiveUnit is PlayerUnit)
            {
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.Movement, combatManager.CurrentActiveUnit);
            }
        }

        void UpdateJumpTargeting()
        {
            // Get mouse world position
            Vector3 mouseWorld = GetMouseWorldPosition();
            if (mouseWorld == Vector3.zero) return;

            Tile targetTile = GridManager.Instance.GetClosestTile(mouseWorld, MapManager.Instance.CurrentConfiguration.tileSpacing);

            if (targetTile != hoveredTile)
            {
                hoveredTile = targetTile;
                ShowJumpRange();
            }

            // Handle input
            if (Input.GetMouseButtonDown(0) && !IsMouseOverUI())
            {
                ExecuteJump(targetTile);
            }
            else if (Input.GetMouseButtonDown(1))
            {
                CancelJumpTargeting();
            }
        }

        void ShowJumpRange()
        {
            Unit currentUnit = combatManager.CurrentActiveUnit;
            if (currentUnit == null) return;

            // Clear previous highlights
            GridManager.Instance.ClearAllHighlights();

            // Get all valid jump tiles (includes Y level considerations)
            var jumpTiles = GetJumpableTiles(currentUnit);

            // Highlight all jumpable tiles
            foreach (var tile in jumpTiles)
            {
                tile.Highlight(TileHighlightType.Danger); // Orange for jump range
            }

            // If hovering over a specific tile, check if it's valid
            if (hoveredTile != null && jumpTiles.Contains(hoveredTile))
            {
                if (!hoveredTile.occupied && hoveredTile.passableTerrain)
                {
                    hoveredTile.Highlight(TileHighlightType.AttackRange); // Green for valid destination
                }
            }
        }

        private List<Tile> GetJumpableTiles(Unit unit)
        {
            var jumpableTiles = new List<Tile>();
            if (unit?.currentTile == null) return jumpableTiles;

            Tile startTile = unit.currentTile;

            // Get all tiles at exactly jump range distance (including Y level differences)
            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (tile == null || tile == startTile) continue;

                // FIXED: Use GetGridDistance3D for consistent 3D distance calculation
                int distance = GridManager.Instance.GetGridDistance(startTile, tile, true);

                // Only tiles at exactly jump range distance
                if (distance == jumpRange && tile.passableTerrain && !tile.occupied)
                {
                    // Check for wall blocking before adding to jumpable tiles
                    if (!IsJumpBlockedByWalls(startTile, tile))
                    {
                        jumpableTiles.Add(tile);
                    }
                }
            }

            return jumpableTiles;
        }

        void ExecuteJump(Tile targetTile)
        {
            Unit currentUnit = combatManager.CurrentActiveUnit;
            if (currentUnit == null || targetTile == null) return;

            // FIXED: Use GetGridDistance3D for consistent validation
            int distance = GridManager.Instance.GetGridDistance(currentUnit.currentTile, targetTile, true);
            if (distance != jumpRange)
            {
                Debug.Log($"Invalid jump distance: {distance}. Must be exactly {jumpRange} tiles away (including height differences).");
                return; // Don't cancel targeting, let them try again
            }

            if (targetTile.occupied || !targetTile.passableTerrain)
            {
                Debug.Log("Cannot jump to occupied or impassable tile!");
                return; // Don't cancel targeting, let them try again
            }

            // Check for wall obstacles between start and destination
            if (IsJumpBlockedByWalls(currentUnit.currentTile, targetTile))
            {
                Debug.Log("Jump is blocked by walls!");
                return; // Don't cancel targeting, let them try again
            }

            // Execute the jump
            Vector3 startPos = currentUnit.transform.position;
            Vector3 endPos = targetTile.transform.position;

            // Update tile reference immediately but animate the movement
            currentUnit.SetCurrentTileLogical(targetTile);

            // Start jump animation with camera coordination
            if (cameraController != null)
            {
                StartCoroutine(JumpAnimation(currentUnit, startPos, endPos));
            }

            // Mark that player has used their movement - jumping consumes all remaining movement
            SetMovementUsed();

            // Clear targeting
            isTargetingJump = false;
            hoveredTile = null;
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
        }

        private bool IsJumpBlockedByWalls(Tile startTile, Tile targetTile)
        {
            if (startTile == null || targetTile == null) return true;

            Vector3 startPos = startTile.transform.position;
            Vector3 endPos = targetTile.transform.position;

            // Calculate the actual jump distance to limit raycast
            float jumpDistance = Vector3.Distance(startPos, endPos);

            // Raycast slightly above ground level to avoid hitting floor colliders
            Vector3 rayStart = startPos + Vector3.up * 0.5f;
            Vector3 rayEnd = endPos + Vector3.up * 0.5f;
            Vector3 rayDirection = (rayEnd - rayStart).normalized;

            // Only check up to 90% of the jump distance to avoid detecting walls at/past the destination
            float checkDistance = jumpDistance * 0.5f;

            // Check for walls on the "Walls" layer
            int wallsLayerMask = LayerMask.GetMask("Walls");

            if (Physics.Raycast(rayStart, rayDirection, out RaycastHit hit, checkDistance, wallsLayerMask))
            {
                Debug.Log($"Jump blocked by wall: {hit.collider.name} at distance {hit.distance}");
                return true;
            }

            return false;
        }

        public IEnumerator JumpAnimation(Unit unit, Vector3 startPos, Vector3 endPos)
        {
            float duration = 0.5f;
            float elapsed = 0f;
            float jumpHeight = 2f;

            // Calculate jump direction for sprite facing
            Vector3 jumpDirection = (endPos - startPos).normalized;
            Vector2Int jumpDir = GetJumpDirection(jumpDirection);

            // Face the jump direction
            unit.FaceDirection(jumpDir);

            // Get camera controller reference
            var cameraController = FindObjectOfType<CameraController>();

            Vector3 cameraStartPos = Vector3.zero;
            Vector3 cameraTargetPos = Vector3.zero;
            bool shouldMoveCamera = cameraController != null;

            if (shouldMoveCamera)
            {
                cameraStartPos = cameraController.transform.position;
                cameraTargetPos = new Vector3(
                    endPos.x,
                    endPos.y + 2f,
                    endPos.z - 3.5f   // Maintain offset behind player
                );

                // Apply bounds checking
                cameraTargetPos = cameraController.ClampToBounds(cameraTargetPos);
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Create arc motion for unit
                Vector3 currentPos = Vector3.Lerp(startPos, endPos, t);
                currentPos.y += Mathf.Sin(t * Mathf.PI) * jumpHeight;
                unit.transform.position = currentPos;

                // Move camera smoothly to follow
                if (shouldMoveCamera)
                {
                    float smoothT = Mathf.SmoothStep(0f, 1f, t);
                    cameraController.transform.position = Vector3.Lerp(cameraStartPos, cameraTargetPos, smoothT);
                }

                yield return null;
            }

            // Ensure exact final positions
            unit.transform.position = endPos;
            if (shouldMoveCamera)
            {
                cameraController.transform.position = cameraTargetPos;
            }

            // Wait for the delay before returning to natural facing
            yield return new WaitForSeconds(facingReturnDelay);

            // Return to natural facing after delay
            unit.ReturnToNaturalFacing();
        }


        private Vector2Int GetJumpDirection(Vector3 worldDirection)
        {
            // For diagonal movement, we need to consider both axes
            // Normalize the direction first to get clean values
            Vector3 normalized = worldDirection.normalized;

            // Check if this is primarily diagonal movement
            float xAbs = Mathf.Abs(normalized.x);
            float zAbs = Mathf.Abs(normalized.z);

            // If both axes are significant (diagonal movement), pick the direction that makes most sense
            if (Mathf.Abs(xAbs - zAbs) < 0.3f) // Both axes are roughly equal - diagonal movement
            {
                // For diagonal movement, prioritize the axis that would be most natural for facing
                // You can adjust this logic based on your game's needs
                if (normalized.x > 0.3f) return Vector2Int.right;
                if (normalized.x < -0.3f) return Vector2Int.left;
                if (normalized.z > 0.3f) return Vector2Int.up;
                if (normalized.z < -0.3f) return Vector2Int.down;
            }

            // For non-diagonal movement, use the dominant axis as before
            if (xAbs > zAbs)
            {
                return normalized.x > 0 ? Vector2Int.right : Vector2Int.left;
            }
            else
            {
                return normalized.z > 0 ? Vector2Int.up : Vector2Int.down;
            }
        }

        void SetMovementUsed()
        {
            if (combatManager != null)
            {
                combatManager.SetMovementUsed();
            }
        }

        Vector3 GetMouseWorldPosition()
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            // First try to hit actual tile colliders
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, GridManager.Instance.tileLayer))
            {
                return hit.point;
            }

            return Vector3.zero;
        }

        bool IsMouseOverUI()
        {
            return UnityEngine.EventSystems.EventSystem.current != null &&
                   UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        }

        // Public properties for UI
        public bool IsTargetingJump => isTargetingJump;
        public Unit CurrentActiveUnit => combatManager?.CurrentActiveUnit;
    }
}