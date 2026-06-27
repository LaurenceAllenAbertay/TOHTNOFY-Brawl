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
        [SerializeField] private float jumpHeight = 2f;

        private CombatManager combatManager;
        private bool isTargetingJump = false;
        private Tile hoveredTile;

        void Start()
        {
            combatManager = FindAnyObjectByType<CombatManager>();
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
            if (combatManager.IsMovementLocked) return false;
            if (!unit.CanMove()) return false;
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

            // Capture the origin tile before we update the logical position — used
            // by ExecuteStomp to derive knockback direction via the grid.
            Tile originTile = currentUnit.currentTile;

            Vector3 startPos = currentUnit.transform.position;
            Vector3 endPos = targetTile.transform.position;

            // Update tile reference immediately but animate the movement.
            currentUnit.SetCurrentTileLogical(targetTile);

            StartCoroutine(JumpAnimation(currentUnit, startPos, endPos, stompTarget, originTile));

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

        public IEnumerator JumpAnimation(Unit unit, Vector3 startPos, Vector3 endPos, Unit stompTarget = null, Tile originTile = null)
        {
            // Face the jump direction before the wind-up so the animation plays correctly.
            Vector3 jumpDirection = (endPos - startPos).normalized;
            unit.FaceDirection(GetJumpDirection(jumpDirection));

            // Wind-up: play the tier-appropriate jump animation and wait for AnimEvent_JumpLaunch
            // before starting the arc. This gives the animator full control over launch timing —
            // move the event keyframe in the clip and the code timing follows automatically.
            // Units without a Jump animation state skip straight to the arc with no delay.
            var unitAnimator = unit.GetComponent<UnitAnimator>();
            float arcDuration = 0.5f; // fallback for units with no jump animation clip

            if (unitAnimator != null && unitAnimator.HasJumpAnimation)
            {
                bool launched = false;
                System.Action onLaunch = () => launched = true;
                unitAnimator.OnJumpLaunchEvent += onLaunch;
                unitAnimator.PlayJump();

                const float launchMaxWait = 3f;
                float launchElapsed = 0f;
                while (!launched && launchElapsed < launchMaxWait)
                {
                    launchElapsed += Time.deltaTime;
                    yield return null;
                }

                if (!launched)
                    Debug.LogWarning($"[JumpSystem] AnimEvent_JumpLaunch never fired on {unit.name}. " +
                                     "Ensure every Jump clip (Jump, Jump_Bad, Jump_Hurt) has the " +
                                     "AnimEvent_JumpLaunch event placed at the launch frame.");

                unitAnimator.OnJumpLaunchEvent -= onLaunch;

                // Read remaining clip time now that we're exactly at the launch frame.
                // This becomes the arc duration so the unit arrives at the destination
                // precisely as the animation ends — no hardcoded timing required.
                var anim = unit.GetComponent<Animator>();
                if (anim != null)
                {
                    var info = anim.GetCurrentAnimatorStateInfo(0);
                    float remaining = (1f - Mathf.Clamp01(info.normalizedTime)) * info.length;
                    if (remaining > 0.05f)
                        arcDuration = remaining;
                }
            }

            // Arc movement.
            float elapsed = 0f;

            while (elapsed < arcDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / arcDuration;

                Vector3 currentPos = Vector3.Lerp(startPos, endPos, t);
                currentPos.y += Mathf.Sin(t * Mathf.PI) * jumpHeight;
                unit.transform.position = currentPos;

                yield return null;
            }

            // Snap to exact final position.
            unit.transform.position = endPos;

            // Apply stomp to any unit that was on the landing tile.
            if (stompTarget != null)
                ExecuteStomp(unit, stompTarget, originTile);

            // Return to idle now that the jump is complete.
            unitAnimator?.PlayIdle();
        }


        /// <summary>
        /// Called on landing when CanStompOccupiedTiles is true.
        /// Deals damage to the stomped unit, then knocks them back using the agreed
        /// fallback chain. Direction is derived from the origin tile to the landing tile
        /// via the grid — no world-space positions involved.
        ///
        /// If ResolveKnockbackDestination returns null the victim has nowhere to go.
        /// Because two units cannot share a tile the victim is killed by the impact.
        /// </summary>
        private void ExecuteStomp(Unit stomper, Unit victim, Tile originTile)
        {
            if (victim == null || stomper == null) return;

            int damage = Mathf.Max(1, stomper.currentAttack - victim.currentDefense);
            victim.ReceiveDamage(damage);
            UnitManager.NotifyUnitDamaged(victim, stomper);

            if (victim.currentHealth <= 0) return;

            // Derive knockback direction from origin tile → landing tile using the grid.
            // stomper.currentTile is the landing tile (set by SetCurrentTileLogical before animation).
            // FromTiles is used rather than CardinalFromTiles so that a diagonal jump (e.g. NE)
            // produces a diagonal push (NE) — CardinalFromTiles would arbitrarily pick N or E
            // because on a tile grid the axes are always exactly equal for diagonal positions.
            Vector2Int knockbackDir = Vector2Int.right; // safe fallback — should never be reached
            if (originTile != null && stomper.currentTile != null)
            {
                Vector2Int dir = GridDirectionUtility.FromTiles(originTile, stomper.currentTile);
                if (dir != Vector2Int.zero) knockbackDir = dir;
            }

            // Resolve destination using the same full fallback chain as all other knockback.
            Tile knockbackTile = GridDirectionUtility.ResolveKnockbackDestination(victim.currentTile, knockbackDir);

            if (knockbackTile == null)
            {
                // No valid tile in any direction — victim cannot share the tile so they are killed.
                Debug.Log($"[Stomp] {victim.name} has no valid knockback tile — killed by impact.");
                victim.ReceiveDamage(victim.currentHealth);
                return;
            }

            var victimAnimator = victim.GetComponent<UnitAnimator>();
            if (victimAnimator != null)
                StartCoroutine(StompKnockbackAnimation(victim, knockbackTile, victimAnimator));
            else
                victim.SetCurrentTile(knockbackTile);
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