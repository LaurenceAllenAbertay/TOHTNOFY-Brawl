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
        public Tile currentTile;
        public int currentSpeed;
        public bool canMove;

        /// <summary>
        /// The direction this unit is currently facing.
        /// Updated by FaceDirection and persists until the next call.
        /// Used as the default aim direction seed for abilities.
        /// </summary>
        public Vector2Int currentFacing = Vector2Int.right;

        // Base stats set once from CharacterData at initialisation.
        // Never modified after that — status effects are layered on top via multipliers.
        private int _baseAttack;
        private int _baseDefense;

        /// <summary>
        /// The unit's effective attack stat this turn.
        /// Computed from the base stat and any active AttackUp/AttackDown multipliers.
        /// Each point of effectPower on an attack modifier is worth ±10% of the base value.
        /// Reading this value is always safe and always reflects the current game state.
        /// </summary>
        public int currentAttack
            => Mathf.Max(0, Mathf.RoundToInt(_baseAttack * StatusEffectManager.GetAttackMultiplier(this)));

        /// <summary>
        /// The unit's effective defense stat this turn.
        /// Computed from the base stat and any active DefenseUp/DefenseDown multipliers.
        /// Each point of effectPower on a defense modifier is worth ±10% of the base value.
        /// Reading this value is always safe and always reflects the current game state.
        /// </summary>
        public int currentDefense
            => Mathf.Max(0, Mathf.RoundToInt(_baseDefense * StatusEffectManager.GetDefenseMultiplier(this)));

        // Flat bonus added to all ability ranges at targeting time.
        // Set by passives (e.g. Cannoneer). Does not affect movement range.
        public int RangeModifier { get; set; }

        // How far this unit can jump. Default is 2 (the universal minimum).
        // Passives can raise this; units may always jump any distance from 2 up to this value.
        // Read by JumpSystem, AIEvaluator, and AIExecutor — update all three via this field.
        public int JumpRange { get; set; } = 2;

        // When true, this unit may land on occupied tiles while jumping.
        // The occupying unit takes damage and is knocked back 1 tile away from the landing point.
        // Set by BrodieBootsPassive.
        public bool CanStompOccupiedTiles { get; set; } = false;

        // Queued follow-up action set by abilities like Chug.
        // Checked at turn start: if set, auto-executes and ends turn immediately.
        public PendingAction? pendingAction = null;

        // Per-slot ability cooldown tracking.
        // Index matches the unit's loadout slot (0-2). Value is the number of ticks
        // remaining before the slot is usable again; 0 means available.
        // Set via TriggerAbilityCooldown; decremented each turn start via TickAbilityCooldowns.
        private readonly int[] _abilityCooldownsRemaining = new int[3];

        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugLogging = false;

        /// <summary>
        /// Set to true the moment Die() is called so that ReceiveDamage, targeting, and
        /// AI systems treat this unit as gone even before its GameObject is disabled.
        /// Checked by CanTarget and ReceiveDamage to prevent double-.
        /// </summary>
        public bool IsDead { get; private set; } = false;

        /// <summary>
        /// Set to true by UnitDownedSequencer after the downed animation completes, when this
        /// unit transitions into a downed body. The unit remains on its tile, blocking
        /// movement and pathfinding, but takes no damage, has no turn, and is ignored by AI.
        /// </summary>
        public bool IsBody { get; private set; } = false;

        /// <summary>
        /// True when this unit should be treated as a neutral object on the map — neither
        /// player nor enemy. Currently this covers downed bodies (IsBody) and will also
        /// cover future neutral map objects (barrels, crates, etc.) via NeutralUnit.
        /// Abilities opt-in to targeting neutrals via the canTargetNeutral flag.
        /// </summary>
        public virtual bool IsNeutral => IsBody;

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
                currentHealth  = characterData.maxHealth;
                _baseAttack    = characterData.attack;
                _baseDefense   = characterData.defense;
                currentSpeed   = characterData.speed;
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

            // Set default facing based on unit type.
            // Source art faces left, so enemies face left (no flip) and players face right (flipped).
            FaceDirection(this is PlayerUnit ? Vector2Int.right : Vector2Int.left);
        }

        void Start()
        {
            if (!UnitManager.AllUnits.Contains(this))
                UnitManager.RegisterUnit(this);

            // Assign the Event Camera on any World Space Canvas children (e.g. the status
            // effect icon strip). Without this, GraphicRaycaster cannot raycast pointer
            // events — hover tooltips on status icons will not work without it.
            foreach (var canvas in GetComponentsInChildren<Canvas>())
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                    canvas.worldCamera = Camera.main;
            }
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

        /// <summary>
        /// Executes an ability from a pre-built context (used by AI pending action).
        /// Resolves targets, fires the sequence, and waits for completion.
        /// </summary>
        public IEnumerator ExecuteAbilityCoroutine(AbilityContext ctx)
        {
            if (ctx?.ability == null || ctx.ability.targeting == null) yield break;

            var targets = ctx.ability.targeting.SelectTargets(ctx);
            if (targets.Count == 0 && !ctx.ability.canExecuteWithoutTargets) yield break;

            // This path (AI pending actions, Dizzy) bypasses Ability.Execute / ExecuteWithContext,
            // so we trigger the cooldown here once execution is confirmed.
            TriggerAbilityCooldown(ctx.ability);

            yield return StartCoroutine(ExecuteAbilityAnimationSequence(ctx, targets));
        }

        #endregion

        #region Turn Management

        public virtual void StartTurn()
        {
            canMove = true;
            TickAbilityCooldowns();

            if (unitAnimator != null)
                unitAnimator.SetActiveTurn();
        }

        public virtual void EndTurn()
        {
            if (unitAnimator != null)
                unitAnimator.SetInactiveTurn();
        }

        #endregion

        /// <summary>Returns true if the ability in the given loadout slot is currently on cooldown.</summary>
        public bool IsAbilityOnCooldown(int slot)
            => slot >= 0 && slot < _abilityCooldownsRemaining.Length && _abilityCooldownsRemaining[slot] > 0;

        /// <summary>
        /// Returns the number of this unit's turns that must pass before the ability
        /// in the given slot becomes available again. 0 means it is available now.
        /// </summary>
        public int GetAbilityCooldownRemaining(int slot)
            => (slot >= 0 && slot < _abilityCooldownsRemaining.Length) ? _abilityCooldownsRemaining[slot] : 0;

        /// <summary>
        /// Puts the given ability on cooldown. Finds its slot by reference in this unit's
        /// current loadout. No-op if the ability has cooldown 0 (usable every turn) or
        /// if the ability is not found in the loadout.
        /// Called by Ability.Execute / ExecuteWithContext and Unit.ExecuteAbilityCoroutine
        /// immediately after a successful execution is confirmed.
        /// </summary>
        public void TriggerAbilityCooldown(Ability ability)
        {
            if (ability == null || ability.cooldown <= 0) return;

            var abilities = UnitLoadoutManager.GetAbilities(this);
            for (int i = 0; i < abilities.Length; i++)
            {
                if (abilities[i] == ability)
                {
                    // cooldown + 1: the +1 ensures the slot remains locked for exactly
                    // `cooldown` of this unit's turns. The first tick (at the start of
                    // the very next turn) brings it from cooldown+1 down to cooldown,
                    // so the first full turn it is locked it reads exactly `cooldown`.
                    _abilityCooldownsRemaining[i] = ability.cooldown + 1;
                    return;
                }
            }
        }

        /// <summary>
        /// Decrements all active cooldowns by one tick.
        /// Called at the start of every unit turn (including stunned turns via TurnManager).
        /// </summary>
        public void TickAbilityCooldowns()
        {
            for (int i = 0; i < _abilityCooldownsRemaining.Length; i++)
            {
                if (_abilityCooldownsRemaining[i] > 0)
                    _abilityCooldownsRemaining[i]--;
            }
        }

        #region Movement and Facing

        public void FaceDirection(Vector2Int direction)
        {
            if (unitSpriteRenderer == null) return;

            currentFacing = direction;

            // Source art faces left, so no flip is needed for left-facing.
            // Flipping is only required when the unit should face right.
            if (direction == Vector2Int.left)
                unitSpriteRenderer.flipX = false;
            else if (direction == Vector2Int.right)
                unitSpriteRenderer.flipX = true;
        }

        public void SetCurrentTile(Tile newTile)
        {
            if (currentTile != null && currentTile.currentUnit == this)
                currentTile.currentUnit = null;

            // ── Double-occupancy guard (at-rest only) ─────────────────────────────
            // This fires only when a unit fully comes to rest on a tile, never during
            // animated movement (SetCurrentTileLogical handles that path).
            // Two units must never occupy the same tile at rest. If this happens it is
            // a bug in upstream logic — the LogError is the signal to fix the caller.
            if (newTile != null && newTile.currentUnit != null && newTile.currentUnit != this)
            {
                Debug.LogError(
                    $"[Unit] Double-occupancy at rest on '{newTile.name}'! " +
                    $"Existing: '{newTile.currentUnit.name}', incoming: '{name}'. " +
                    $"Teleporting existing unit away. This is a bug — fix the calling code.");

                Tile.TeleportToClosestFreeTile(newTile.currentUnit, newTile);
            }

            currentTile = newTile;
            if (currentTile != null)
            {
                currentTile.currentUnit = this;
                transform.position = currentTile.transform.position;
            }

            UnitManager.NotifyUnitMoved(this);
        }

        /// <summary>Sets tile reference without moving the transform (for animated movement).
        /// Intentionally allows transient overlap (e.g. Wiring Fault pull) — no occupancy
        /// guard here. Use SetCurrentTile when the unit is coming to rest.</summary>
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

        /// <summary>
        /// Fired after status-effect checks (Shielded, Immune, etc.) but before health is
        /// subtracted. Subscribers receive (victim, attacker, sourceAbility, incomingAmount)
        /// and return the final amount to apply — return 0 to fully absorb the hit.
        /// Multiple subscribers chain: each receives the value the previous returned.
        /// attacker and sourceAbility may be null for non-ability damage sources (tile effects,
        /// recoil, etc.) — passives should guard against null before reading ability data.
        /// </summary>
        public static event System.Func<Unit, Unit, Ability, int, int> OnPreReceiveDamage;

        /// <summary>
        /// Fired whenever this unit's currentHealth changes for any reason (damage, recoil,
        /// tile effects, healing). Passes the unit whose health changed.
        /// Subscribe here rather than to UnitManager.OnUnitDamaged — that event is only
        /// fired by DamageEffect and would miss recoil and tile-effect damage paths.
        /// </summary>
        public static event System.Action<Unit> OnHealthChanged;

        /// <summary>
        /// Fires OnHealthChanged for the given unit. Use this from outside the Unit class
        /// (e.g. RecoilDamageEffect) when currentHealth is mutated directly rather than
        /// via ReceiveDamage. C# events can only be invoked from within their declaring class,
        /// so this method acts as the authorised external trigger — matching the pattern
        /// UnitManager uses for NotifyUnitDamaged.
        /// </summary>
        public static void NotifyHealthChanged(Unit unit)
        {
            OnHealthChanged?.Invoke(unit);
        }

        public virtual void ReceiveDamage(int amount, Unit attacker = null, Ability sourceAbility = null)
        {
            if (IsDead) return;

            // Bodies are immortal — they cannot be damaged or killed again.
            // No animation, no numbers, no response. Knockback still works because
            // it bypasses ReceiveDamage entirely (uses AnimateToTile directly).
            if (IsBody) return;

            if (StatusEffectManager.Instance != null)
            {
                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Shielded))
                {
                    Debug.Log($"{name} is shielded - no damage taken!");
                    return;
                }

                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Immune))
                {
                    Debug.Log($"{name} is immune - no damage taken!");
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

            // Allow passives to intercept or modify the final damage amount.
            if (OnPreReceiveDamage != null)
            {
                foreach (System.Func<Unit, Unit, Ability, int, int> modifier in OnPreReceiveDamage.GetInvocationList())
                    amount = modifier(this, attacker, sourceAbility, amount);
            }

            if (amount <= 0) return;

            currentHealth -= amount;
            Debug.Log($"{name} took {amount} damage. HP now {currentHealth}");

            OnHealthChanged?.Invoke(this);

            if (currentHealth <= 0)
            {
                Debug.Log($"{name} was defeated.");
                Die(attacker);
            }
        }

        /// <summary>
        /// Marks this unit as dead, notifies all gameplay systems (UnitManager, TurnManager,
        /// DialogueManager etc. via the OnUnitDied event), then enqueues the unit with
        /// UnitDownedSequencer for the camera-pan + downed-animation presentation.
        ///
        /// Call this instead of UnitManager.NotifyUnitDied directly — it is the single
        /// authoritative down path for both player-controlled and AI-controlled units.
        /// </summary>
        public void Die(Unit killer = null)
        {
            if (IsDead) return;
            IsDead = true;

            // Vacate the tile immediately so pathfinding, knockback destinations, and
            // targeting all see a free tile during the rest of this ability sequence.
            // (UnitDownedSequencer cannot do this early enough — it runs after effects resolve.)
            if (currentTile != null && currentTile.currentUnit == this)
                currentTile.currentUnit = null;

            // Fire AllyDownsEnemy dialogue when a player unit kills an enemy.
            // Per DialogueTrigger.cs, this trigger is not covered by a global event —
            // it must be called manually at the kill site.
            if (this is EnemyUnit && killer is PlayerUnit)
                DialogueManager.Trigger(DialogueTrigger.AllyDownsEnemy, instigator: killer);

            // Notify all gameplay systems synchronously.  TurnManager removes this unit
            // from the turn order; UnitManager unregisters it from targeting lists.
            // This happens immediately so no subsequent turn or ability targets a dead unit.
            UnitManager.NotifyUnitDied(this);

            // Enqueue the visual presentation (camera pan + downed animation) for later.
            // UnitDownedSequencer.DrainDownedQueue() is called by AbilitySequencer and
            // TurnManager after all effects in the current action are resolved, so multiple
            // downs from one ability are sequenced rather than played simultaneously.
            if (UnitDownedSequencer.Instance != null)
                UnitDownedSequencer.Instance.EnqueueDowned(this, killer);
            else
                gameObject.SetActive(false); // fallback: no sequencer in scene
        }

        /// <summary>
        /// Called by UnitDownedSequencer after the downed animation finishes.
        /// Transitions the unit from "dead and being removed" to "downed body on the map".
        /// The tile occupancy is restored here so the body blocks movement and pathfinding.
        /// </summary>
        public void BecomeBody()
        {
            IsBody = true;

            // Re-occupy the tile. Die() cleared it so gameplay systems saw a free tile
            // during the ability sequence — now that the sequence is over, the body
            // takes up space again.
            if (currentTile != null && currentTile.currentUnit == null)
                currentTile.currentUnit = this;

            UnitManager.NotifyBodySpawned(this);
            Debug.Log($"{name} is now a body on {currentTile?.name}.");
        }

        public virtual bool CanMove()
        {
            if (!canMove) return false;
            if (StatusEffectManager.Instance != null &&
                (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Ensnared) ||
                 StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Stuck)))
                return false;
            return true;
        }

        /// <summary>
        /// Returns false when a status effect prevents this unit from using abilities this turn.
        /// Checked by CombatManager (player) and AIPlanner (AI) before any ability is selected.
        /// </summary>
        public virtual bool CanUseAbilities()
        {
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Scared))
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

            // Dead units (mid-down-sequence, before becoming a body) are never targetable.
            // Bodies are handled separately: SelectTargets checks canTargetNeutral so that
            // targeting decisions are ability-specific rather than unit-specific.
            if (unit.IsDead && !unit.IsBody) return false;
            if (unit.IsBody) return false; // Bodies filtered at SelectTargets level, not here.

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

#if UNITY_EDITOR
        /// <summary>
        /// Debug helper: instantly kills this unit via the normal down path so all
        /// downstream systems (TurnManager, DialogueManager, StatusEffectManager) fire
        /// exactly as they would during gameplay.
        /// Right-click the Unit component in the Inspector and select "Debug: Kill Unit".
        /// Only compiled into the Editor — stripped from release builds.
        /// </summary>
        [ContextMenu("Debug: Kill Unit")]
        private void Debug_Die()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"[Unit:{name}] Debug_Die called outside Play Mode — ignored.");
                return;
            }

            Debug.Log($"[Unit:{name}] Debug_Die triggered.");
            Die(killer: null);
        }
#endif

    }
}