using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class JumpSystem : MonoBehaviour
    {
        [Header("Jump Settings")]
        // jumpRange is now read from Unit.JumpRange so passives can modify it per-unit.
        // The minimum jump distance is always 2 (the base value on Unit).

        private CombatManager combatManager;
        private CameraController cameraController;
        private bool isTargetingJump = false;
        private Tile hoveredTile;

        void Start()
        {
            combatManager = FindAnyObjectByType<CombatManager>();
            cameraController = FindAnyObjectByType<CameraController>();
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
            if (combatManager.CurrentActiveUnit != unit) return false;
            if (combatManager.IsExecutingAbility || combatManager.IsMoving) return false;
            if (combatManager.currentState != CombatState.WaitingForInput) return false;
            // Jump costs exactly 2 movement points and can only be performed once per turn.
            return combatManager.HasEnoughMovementForJump(2);
        }

        public void StartJumpTargeting()
        {
            if (!CanUseJump(combatManager.CurrentActiveUnit)) return;

            // If ability targeting is active, cancel it before entering jump targeting.
            // The two modes are mutually exclusive — both own tile highlights and input.
            if (combatManager.IsTargetingAbility)
                combatManager.CancelAbilityTargeting();

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

            // Restore movement highlights if the player can still move
            if (combatManager.CanMove && combatManager.CurrentActiveUnit is PlayerUnit)
            {
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.Movement,
                    combatManager.CurrentActiveUnit,
                    movementRangeOverride: combatManager.GetRemainingMovement());
            }
        }

        void UpdateJumpTargeting()
        {
            // Get mouse world position
            Vector3 mouseWorld = GetMouseWorldPosition();
            if (mouseWorld == Vector3.zero) return;

            Tile targetTile = GridManager.Instance.GetTileAtScreenPosition(Camera.main, Input.mousePosition)
                             ?? GridManager.Instance.GetTileAtPosition(mouseWorld);

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
                // Empty tile — valid landing spot, highlight green.
                // Occupied tile — only valid if stomp is enabled; highlight red to signal attack.
                if (!hoveredTile.occupied && hoveredTile.passableTerrain)
                    hoveredTile.Highlight(TileHighlightType.AttackRange);
                else if (hoveredTile.occupied && currentUnit.CanStompOccupiedTiles)
                    hoveredTile.Highlight(TileHighlightType.AttackRange);
            }
        }

        private List<Tile> GetJumpableTiles(Unit unit)
        {
            var jumpableTiles = new List<Tile>();
            if (unit?.currentTile == null) return jumpableTiles;

            Tile startTile = unit.currentTile;
            int maxRange = unit.JumpRange;
            const int minRange = 2;

            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (tile == null || tile == startTile) continue;
                if (!tile.passableTerrain) continue;

                int distance = GridManager.Instance.GetGridDistance(startTile, tile, true);

                // Valid jump distances: anywhere from minRange up to the unit's JumpRange.
                if (distance < minRange || distance > maxRange) continue;

                // Occupied tiles are only valid landing spots when the unit can stomp.
                if (tile.occupied && !unit.CanStompOccupiedTiles) continue;

                if (!IsJumpBlockedByWalls(startTile, tile))
                    jumpableTiles.Add(tile);
            }

            return jumpableTiles;
        }

        void ExecuteJump(Tile targetTile)
        {
            Unit currentUnit = combatManager.CurrentActiveUnit;
            if (currentUnit == null || targetTile == null) return;

            int distance = GridManager.Instance.GetGridDistance(currentUnit.currentTile, targetTile, true);
            int maxRange = currentUnit.JumpRange;
            const int minRange = 2;

            if (distance < minRange || distance > maxRange)
            {
                Debug.Log($"Invalid jump distance: {distance}. Must be between {minRange} and {maxRange} tiles away.");
                return;
            }

            if (!targetTile.passableTerrain)
            {
                Debug.Log("Cannot jump to impassable tile!");
                return;
            }

            // Stomp check: occupied tiles are only valid when the unit has the stomp ability.
            if (targetTile.occupied && !currentUnit.CanStompOccupiedTiles)
            {
                Debug.Log("Cannot jump to occupied tile!");
                return;
            }

            if (IsJumpBlockedByWalls(currentUnit.currentTile, targetTile))
            {
                Debug.Log("Jump is blocked by walls!");
                return;
            }

            // Cache the occupant before we move (SetCurrentTileLogical will clear the tile).
            Unit stompTarget = targetTile.occupied ? targetTile.currentUnit : null;

            Vector3 startPos = currentUnit.transform.position;
            Vector3 endPos = targetTile.transform.position;

            // Update tile reference immediately but animate the movement.
            currentUnit.SetCurrentTileLogical(targetTile);

            // Start jump animation with camera coordination.
            if (cameraController != null)
            {
                StartCoroutine(JumpAnimation(currentUnit, startPos, endPos, stompTarget));
            }

            // Mark that the player has used their movement — jumping consumes all remaining movement.
            SetMovementUsed();

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

        public IEnumerator JumpAnimation(Unit unit, Vector3 startPos, Vector3 endPos, Unit stompTarget = null)
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
            var cameraController = FindAnyObjectByType<CameraController>();

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

            // If this was a stomp, apply damage and knockback to the occupant now that
            // Brodie has fully landed. The stomp direction is away from Brodie's origin tile.
            if (stompTarget != null)
            {
                ExecuteStomp(unit, stompTarget, startPos, endPos);
            }
        }


        /// <summary>
        /// Called on landing when CanStompOccupiedTiles is true.
        /// Deals damage to the stomped unit equal to the jumping unit's attack stat,
        /// then knocks them back 1 tile away from the landing point.
        /// The knockback direction is derived from the jump vector (start → end projected
        /// to the dominant axis) so the victim is thrown in the direction Brodie jumped.
        /// </summary>
        private void ExecuteStomp(Unit stomper, Unit victim, Vector3 jumpStartPos, Vector3 jumpEndPos)
        {
            if (victim == null || stomper == null) return;

            // Deal damage — base is the stomper's current attack.
            int damage = Mathf.Max(1, stomper.currentAttack - victim.currentDefense);
            victim.ReceiveDamage(damage);
            UnitManager.NotifyUnitDamaged(victim, stomper);

            // If the victim died from the stomp damage, no knockback needed.
            if (victim.currentHealth <= 0) return;

            // Derive primary knockback direction from the horizontal jump vector, dominant axis.
            Vector3 jumpDir = jumpEndPos - jumpStartPos;
            int dx = jumpDir.x > 0.01f ? 1 : (jumpDir.x < -0.01f ? -1 : 0);
            int dz = jumpDir.z > 0.01f ? 1 : (jumpDir.z < -0.01f ? -1 : 0);
            Vector2Int knockbackDir = Mathf.Abs(jumpDir.x) >= Mathf.Abs(jumpDir.z)
                ? new Vector2Int(dx, 0)
                : new Vector2Int(0, dz);

            // Try primary direction first, then the two perpendicular sides — mirrors
            // KnockbackEffect.FindValidKnockbackTile so stomp behaves consistently.
            Tile knockbackTile = FindStompKnockbackTile(victim.currentTile, knockbackDir);

            if (knockbackTile == null)
            {
                // Truly no tile available anywhere — two units cannot share a tile,
                // so the victim is killed by the impact.
                Debug.Log($"[Stomp] {victim.name} has no valid knockback tile in any direction — killed by impact.");
                victim.ReceiveDamage(victim.currentHealth);
                return;
            }

            // Play knockback animation and move the victim.
            var victimAnimator = victim.GetComponent<UnitAnimator>();
            if (victimAnimator != null)
                StartCoroutine(StompKnockbackAnimation(victim, knockbackTile, victimAnimator));
            else
                victim.SetCurrentTile(knockbackTile);
        }

        /// <summary>
        /// Tries the primary knockback direction, then the two perpendicular sides.
        /// Returns the first free, passable, unoccupied tile found, or null if all are blocked.
        /// Mirrors KnockbackEffect.FindValidKnockbackTile / GetKnockbackDirectionsPriority.
        /// </summary>
        private Tile FindStompKnockbackTile(Tile fromTile, Vector2Int primaryDir)
        {
            // Perpendicular directions: clockwise and counter-clockwise of primary.
            Vector2Int cwDir  = new Vector2Int(-primaryDir.y,  primaryDir.x);
            Vector2Int ccwDir = new Vector2Int( primaryDir.y, -primaryDir.x);

            Vector2Int[] directionsToTry = { primaryDir, cwDir, ccwDir };

            foreach (var dir in directionsToTry)
            {
                Tile candidate = GridManager.Instance.GetTileInDirection(fromTile, dir);
                if (candidate != null && candidate.passableTerrain && !candidate.occupied)
                    return candidate;
            }

            return null;
        }

        private IEnumerator StompKnockbackAnimation(Unit victim, Tile destination, UnitAnimator animator)
        {
            animator.PlayKnockbackStart();
            yield return new WaitForSeconds(0.15f);

            bool moveComplete = false;
            victim.AnimateToTile(destination, 0.2f, () => moveComplete = true);
            while (!moveComplete) yield return null;

            animator.PlayKnockbackEnd();
            yield return new WaitForSeconds(0.2f);
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