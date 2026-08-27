using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class JumpSystem : MonoBehaviour
    {
        [SerializeField] private float jumpHeight = 2f;
        [SerializeField] private float fallbackArcDuration = 0.5f;

        private static readonly Dictionary<string, float> measuredArcDurations = new Dictionary<string, float>();

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
            if (unit.IsAIControlled) return false;
            if (combatManager.CurrentActiveUnit != unit) return false;
            if (combatManager.IsExecutingAbility || combatManager.IsMoving) return false;
            if (combatManager.currentState != CombatState.WaitingForInput) return false;
            if (combatManager.IsMovementLocked) return false;
            if (!unit.CanMove()) return false;
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(unit, StatusEffectType.Encumbered)) return false;
            
            return combatManager.HasEnoughMovementForJump(2);
        }

        public void StartJumpTargeting()
        {
            if (!CanUseJump(combatManager.CurrentActiveUnit)) return;
            
            if (combatManager.IsTargetingAbility)
                combatManager.CancelAbilityTargeting();

            isTargetingJump = true;
            
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            ShowJumpRange();
        }

        public void CancelJumpTargeting()
        {
            isTargetingJump = false;
            hoveredTile = null;

            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            
            if (combatManager.CanMove && combatManager.CurrentActiveUnit != null && !combatManager.CurrentActiveUnit.IsAIControlled)
            {
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.Movement,
                    combatManager.CurrentActiveUnit,
                    movementRangeOverride: combatManager.GetRemainingMovement());
            }
        }

        void UpdateJumpTargeting()
        {
            Vector3 mouseWorld = GetMouseWorldPosition();
            if (mouseWorld == Vector3.zero) return;

            Tile targetTile = GridManager.Instance.GetTileAtScreenPosition(Camera.main, Input.mousePosition)
                             ?? GridManager.Instance.GetTileAtPosition(mouseWorld);

            if (targetTile != hoveredTile)
            {
                hoveredTile = targetTile;
                ShowJumpRange();
            }

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
            
            GridManager.Instance.ClearAllHighlights();
            
            var jumpTiles = GetJumpableTiles(currentUnit);
            
            foreach (var tile in jumpTiles)
            {
                tile.Highlight(TileHighlightType.Danger); 
            }
            
            if (hoveredTile != null && jumpTiles.Contains(hoveredTile))
            {
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

                if (distance < minRange || distance > maxRange) continue;
                
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

            Unit stompTarget = targetTile.occupied ? targetTile.currentUnit : null;
            
            Tile originTile = currentUnit.currentTile;

            Vector3 startPos = currentUnit.transform.position;
            Vector3 endPos = targetTile.transform.position;
            
            currentUnit.SetCurrentTileLogical(targetTile);

            StartCoroutine(JumpAnimation(currentUnit, startPos, endPos, stompTarget, originTile));

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
            
            float jumpDistance = Vector3.Distance(startPos, endPos);
            
            Vector3 rayStart = startPos + Vector3.up * 0.5f;
            Vector3 rayEnd = endPos + Vector3.up * 0.5f;
            Vector3 rayDirection = (rayEnd - rayStart).normalized;
            
            float checkDistance = jumpDistance * 0.5f;

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
            UIEvents.OnMovementAnimationStarted();

            Vector3 jumpDirection = (endPos - startPos).normalized;
            unit.FaceDirection(GetJumpDirection(jumpDirection));
            
            var unitAnimator = unit.GetComponent<UnitAnimator>();

            if (unitAnimator != null && unitAnimator.HasJumpAnimation)
            {
                bool launched = false;
                System.Action onLaunch = () => launched = true;
                unitAnimator.OnJumpLaunchEvent += onLaunch;
                unitAnimator.PlayJump();

                string jumpStateName = unitAnimator.CurrentAnimation;

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

                bool landed = false;
                System.Action onLand = () => landed = true;
                unitAnimator.OnJumpLandEvent += onLand;

                float launchTime = Time.time;

                float arcDuration = (jumpStateName != null && measuredArcDurations.TryGetValue(jumpStateName, out float cached))
                    ? cached
                    : fallbackArcDuration;

                const float landMaxWait = 3f;
                float landElapsed = 0f;

                while (!landed && landElapsed < landMaxWait)
                {
                    landElapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(landElapsed / arcDuration);

                    Vector3 currentPos = Vector3.Lerp(startPos, endPos, t);
                    currentPos.y += Mathf.Sin(t * Mathf.PI) * jumpHeight;
                    unit.transform.position = currentPos;

                    yield return null;
                }

                if (!landed)
                    Debug.LogWarning($"[JumpSystem] AnimEvent_JumpLand never fired on {unit.name}. " +
                                     "Ensure every Jump clip (Jump, Jump_Bad, Jump_Hurt) has the " +
                                     "AnimEvent_JumpLand event placed at the landing frame.");
                else if (jumpStateName != null)
                    measuredArcDurations[jumpStateName] = Mathf.Max(0.05f, Time.time - launchTime);

                unitAnimator.OnJumpLandEvent -= onLand;
            }
            else
            {
                float arcDuration = fallbackArcDuration;
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

                unitAnimator?.PlayIdle();
            }

            unit.transform.position = endPos;

            UIEvents.OnMovementAnimationComplete();

            if (stompTarget != null)
                ExecuteStomp(unit, stompTarget, originTile);
        }

        private void ExecuteStomp(Unit stomper, Unit victim, Tile originTile)
        {
            if (victim == null || stomper == null) return;

            int damage = Mathf.Max(1, stomper.currentAttack - victim.currentDefense);
            victim.ReceiveDamage(damage);
            UnitManager.NotifyUnitDamaged(victim, stomper);

            if (victim.currentHealth <= 0) return;
            
            Vector2Int knockbackDir = Vector2Int.right; 
            if (originTile != null && stomper.currentTile != null)
            {
                Vector2Int dir = GridDirectionUtility.FromTiles(originTile, stomper.currentTile);
                if (dir != Vector2Int.zero) knockbackDir = dir;
            }

            Tile knockbackTile = GridDirectionUtility.ResolveKnockbackDestination(victim.currentTile, knockbackDir);

            if (knockbackTile == null)
            {
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
            Vector3 normalized = worldDirection.normalized;
            
            float xAbs = Mathf.Abs(normalized.x);
            float zAbs = Mathf.Abs(normalized.z);

            if (Mathf.Abs(xAbs - zAbs) < 0.3f)
            {
                if (normalized.x > 0.3f) return Vector2Int.right;
                if (normalized.x < -0.3f) return Vector2Int.left;
                if (normalized.z > 0.3f) return Vector2Int.up;
                if (normalized.z < -0.3f) return Vector2Int.down;
            }

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
        
        public bool IsTargetingJump => isTargetingJump;
        public Unit CurrentActiveUnit => combatManager?.CurrentActiveUnit;
    }
}