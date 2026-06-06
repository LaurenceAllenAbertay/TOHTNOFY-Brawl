using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public abstract class Unit : MonoBehaviour
    {
        public CharacterData characterData;

        [Header("Turn Order")]
        [Tooltip("If true, this unit is always placed at the front of the turn order regardless of initiative.")]
        public bool goesFirst = false;

        [Header("Runtime Stats")]
        public int currentHealth;
        public int currentAttack;
        public int currentDefense;
        public Tile currentTile;
        public int currentSpeed;
        public bool canMove;

        // Flat bonus added to all ability ranges at targeting time.
        // Set by passives (e.g. Cannoneer). Does not affect movement range.
        public int RangeModifier { get; set; }

        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugLogging = false;

        // Forwarding property — polled by CombatManager and UnitAI to detect sequence completion
        public AbilityContext currentAbilityContext => abilitySequencer?.CurrentAbilityContext;

        private UnitAnimator unitAnimator;
        private SpriteRenderer unitSpriteRenderer;
        private AbilitySequencer abilitySequencer;

        #region Unity Lifecycle

        private void Awake()
        {
            unitAnimator = GetComponent<UnitAnimator>();
            unitSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
            abilitySequencer = GetComponent<AbilitySequencer>();

            if (characterData)
            {
                currentHealth = characterData.maxHealth;
                currentAttack = characterData.attack;
                currentDefense = characterData.defense;
                currentSpeed = characterData.speed;
            }

            if (currentTile == null)
            {
                Tile tileBelow = GridManager.Instance?.GetTileAtPosition(transform.position);
                if (tileBelow != null)
                    SetCurrentTile(tileBelow);
            }
            else
            {
                SetCurrentTile(currentTile);
            }
        }

        void Start()
        {
            if (!UnitManager.AllUnits.Contains(this))
                UnitManager.RegisterUnit(this);
        }

        void OnDestroy()
        {
            UnitManager.UnregisterUnit(this);
        }

        #endregion

        #region Ability Execution

        /// <summary>
        /// Entry point called by Ability.cs. Starts the sequence on AbilitySequencer
        /// and polls until it finishes so callers that yield on this coroutine block correctly.
        /// </summary>
        public IEnumerator ExecuteAbilityAnimationSequence(AbilityContext ctx, List<Unit> targets)
        {
            if (abilitySequencer == null)
            {
                Debug.LogError($"[Unit:{name}] No AbilitySequencer component found. Add AbilitySequencer to this prefab.");
                yield break;
            }

            abilitySequencer.BeginSequence(ctx, targets);

            // Poll until the sequence finishes (CombatManager and UnitAI do the same check)
            while (abilitySequencer.CurrentAbilityContext != null)
                yield return null;
        }

        #endregion

        #region Turn Management

        public virtual void StartTurn()
        {
            canMove = true;
            ReturnToNaturalFacing();

            if (unitAnimator != null)
                unitAnimator.SetActiveTurn();
        }

        public virtual void EndTurn()
        {
            StartCoroutine(ReturnToNaturalFacingDelayed(0.1f));

            if (unitAnimator != null)
                unitAnimator.SetInactiveTurn();
        }

        private IEnumerator ReturnToNaturalFacingDelayed(float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnToNaturalFacing();
        }

        #endregion

        #region Movement and Facing

        public void FaceDirection(Vector2Int direction)
        {
            if (unitSpriteRenderer == null) return;

            if (direction == Vector2Int.left)
                unitSpriteRenderer.flipX = true;
            else if (direction == Vector2Int.right)
                unitSpriteRenderer.flipX = false;
        }

        public void ReturnToNaturalFacing()
        {
            Vector2Int naturalFacing = MapManager.GetNaturalFacing(this);
            FaceDirection(naturalFacing);
        }

        public void SetCurrentTile(Tile newTile)
        {
            if (currentTile != null && currentTile.currentUnit == this)
                currentTile.currentUnit = null;

            currentTile = newTile;
            if (currentTile != null)
            {
                currentTile.currentUnit = this;
                transform.position = currentTile.transform.position;
            }

            UnitManager.NotifyUnitMoved(this);
        }

        /// <summary>Sets tile reference without moving the transform (for animated movement).</summary>
        public void SetCurrentTileLogical(Tile newTile)
        {
            if (currentTile != null && currentTile.currentUnit == this)
                currentTile.currentUnit = null;

            currentTile = newTile;
            if (currentTile != null)
                currentTile.currentUnit = this;

            UnitManager.NotifyUnitMoved(this);
        }

        /// <summary>Smoothly animates the unit to a target tile using knockback-style easing.</summary>
        public void AnimateToTile(Tile targetTile, float duration, System.Action onComplete = null)
        {
            if (targetTile == null) return;
            StartCoroutine(SmoothKnockbackMovement(targetTile, duration, onComplete));
        }

        private IEnumerator SmoothKnockbackMovement(Tile targetTile, float duration, System.Action onComplete)
        {
            Vector3 startPos = transform.position;
            Vector3 endPos = targetTile.transform.position;

            SetCurrentTileLogical(targetTile);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float easedT = 1f - Mathf.Pow(1f - elapsed / duration, 3f);
                transform.position = Vector3.Lerp(startPos, endPos, easedT);
                yield return null;
            }

            transform.position = endPos;
            onComplete?.Invoke();
        }

        #endregion

        #region Combat and Status Effects

        public virtual void ReceiveDamage(int amount)
        {
            if (StatusEffectManager.Instance != null)
            {
                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Shielded))
                {
                    Debug.Log($"{name} is shielded - no damage taken!");
                    return;
                }

                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Guarded))
                {
                    var guardEffect = StatusEffectManager.Instance.GetStatusEffect(this, StatusEffectType.Guarded);
                    guardEffect.effectPower -= amount;
                    if (guardEffect.effectPower <= 0)
                    {
                        StatusEffectManager.Instance.RemoveStatusEffect(this, guardEffect);
                        Debug.Log($"{name}'s guard was broken!");
                    }
                    else
                    {
                        Debug.Log($"{name}'s guard absorbed {amount} damage!");
                    }
                    return;
                }

                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Alerted))
                {
                    var alertEffect = StatusEffectManager.Instance.GetStatusEffect(this, StatusEffectType.Alerted);
                    if (Random.Range(0f, 1f) < alertEffect.effectPower)
                    {
                        Debug.Log($"{name} dodged the attack!");
                        StatusEffectManager.Instance.RemoveStatusEffect(this, alertEffect);
                        return;
                    }
                }
            }

            currentHealth -= amount;
            Debug.Log($"{name} took {amount} damage. HP now {currentHealth}");

            if (currentHealth <= 0)
            {
                Debug.Log($"{name} was defeated.");
                UnitManager.NotifyUnitDied(this);
            }
        }

        public virtual bool CanMove()
        {
            if (!canMove) return false;
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Ensnared))
                return false;
            return true;
        }

        public virtual int GetEffectiveMovementRange()
        {
            int baseSpeed = currentSpeed;
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Encumbered))
            {
                var enc = StatusEffectManager.Instance.GetStatusEffect(this, StatusEffectType.Encumbered);
                baseSpeed = Mathf.Max(1, baseSpeed - Mathf.RoundToInt(enc.effectPower));
            }
            return baseSpeed;
        }

        public virtual bool CanTarget(Unit unit)
        {
            if (unit == null || unit == this) return false;
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(unit, StatusEffectType.Untargetable))
                return false;

            // Intimidated: this unit cannot target the source of the effect on it.
            // Only the intimidated unit is blocked, not every unit in the game.
            if (StatusEffectManager.Instance != null)
            {
                var intimidated = StatusEffectManager.Instance.GetStatusEffect(this, StatusEffectType.Intimidated);
                if (intimidated != null && intimidated.source == unit)
                    return false;
            }

            return !IsAllyOf(unit);
        }

        public bool IsAllyOf(Unit other)
        {
            if (other == null) return false;
            bool thisIsEnemy = this is EnemyUnit;
            bool otherIsEnemy = other is EnemyUnit;
            return thisIsEnemy == otherIsEnemy;
        }

        #endregion

        #region Editor Support

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void EnableDebugLogging(bool enable)
        {
            enableDebugLogging = enable;
        }

        #endregion
    }
}