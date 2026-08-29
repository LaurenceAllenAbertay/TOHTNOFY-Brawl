using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace DDD.TNFY.BRAWL
{
    public abstract class Unit : MonoBehaviour
    {
        public CharacterData characterData;

        [Header("Team")]
        public int team = -1;

        [Header("Turn Order")]
        public bool goesFirst = false;

        [Header("Runtime Stats")]
        public int currentHealth;
        public int maxHealth;
        public Tile currentTile;
        public int currentSpeed;
        public bool canMove;

        public Vector2Int currentFacing = Vector2Int.right;
        
        private int _baseAttack;
        private int _baseDefense;

        public int baseAttack => _baseAttack;
        public int baseDefense => _baseDefense;

        public void AdminOverrideBaseStats(int? attack, int? defense)
        {
            if (attack.HasValue)
                _baseAttack = attack.Value;
            if (defense.HasValue)
                _baseDefense = defense.Value;
        }

        public void ApplyCharacterData(CharacterData data)
        {
            characterData = data;

            if (data == null) return;

            currentHealth = data.maxHealth;
            maxHealth     = data.maxHealth;
            _baseAttack   = data.attack;
            _baseDefense  = data.defense;
            currentSpeed  = data.speed;
        }

        public void EnsureTeamResolved()
        {
            if (team < 0)
                team = this is PlayerUnit ? 0 : 1;
        }
        
        public int currentAttack
            => Mathf.Max(0, Mathf.RoundToInt(_baseAttack * StatusEffectManager.GetAttackMultiplier(this)));

        public int currentDefense
            => Mathf.Max(0, Mathf.RoundToInt(_baseDefense * StatusEffectManager.GetDefenseMultiplier(this)));
        
        public int RangeModifier { get; set; }
        
        public int JumpRange { get; set; } = 2;

        public bool CanStompOccupiedTiles { get; set; } = false;
        
        public PendingAction? pendingAction = null;

        private readonly int[] _abilityCooldownsRemaining = new int[3];

        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugLogging = false;

        public bool IsDead { get; private set; } = false;

        public bool IsBody { get; private set; } = false;
        
        public virtual bool IsNeutral => IsBody;

        private bool? _forcedAIControl = null;

        public bool IsAIControlled => _forcedAIControl ?? !(this is PlayerUnit);

        public void SetControlOverride(bool? forceAIControlled)
        {
            _forcedAIControl = forceAIControlled;
        }

        public void SetTeam(int newTeam)
        {
            if (team == newTeam) return;

            var ai = GetComponent<UnitAI>();
            ai?.UnregisterFromTeam();

            team = newTeam;

            ai?.RegisterWithTeam();
        }

        public AbilityContext currentAbilityContext => abilitySequencer?.CurrentAbilityContext;

        private UnitAnimator unitAnimator;
        private SpriteRenderer[] unitSpriteRenderers;
        private AbilitySequencer abilitySequencer;
        
        private void Awake()
        {
            EnsureTeamResolved();

            unitAnimator = GetComponent<UnitAnimator>();
            unitSpriteRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            abilitySequencer = GetComponent<AbilitySequencer>();

            if (characterData)
            {
                currentHealth  = characterData.maxHealth;
                maxHealth      = characterData.maxHealth;
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
            
            FaceDirection(this is PlayerUnit ? Vector2Int.right : Vector2Int.left);
        }

        void Start()
        {
            if (!UnitManager.AllUnits.Contains(this))
                UnitManager.RegisterUnit(this);
            
            foreach (var canvas in GetComponentsInChildren<Canvas>())
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                    canvas.worldCamera = Camera.main;
            }
        }

        void OnDestroy()
        {
            UnitManager.UnregisterUnit(this);
            Addressables.ReleaseInstance(gameObject);
        }
        
        public IEnumerator ExecuteAbilityAnimationSequence(AbilityContext ctx, List<Unit> targets)
        {
            if (abilitySequencer == null)
            {
                Debug.LogError($"[Unit:{name}] No AbilitySequencer component found. Add AbilitySequencer to this prefab.");
                yield break;
            }

            abilitySequencer.BeginSequence(ctx, targets);
            
            while (abilitySequencer.CurrentAbilityContext != null)
                yield return null;
        }
        
        public IEnumerator ExecuteAbilityCoroutine(AbilityContext ctx)
        {
            if (ctx?.ability == null || ctx.ability.targeting == null) yield break;

            var targets = ctx.ability.targeting.SelectTargets(ctx);
            if (targets.Count == 0 && !ctx.ability.canExecuteWithoutTargets) yield break;
            
            TriggerAbilityCooldown(ctx.ability);

            yield return StartCoroutine(ExecuteAbilityAnimationSequence(ctx, targets));
        }
        
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

        public bool IsAbilityOnCooldown(int slot)
            => slot >= 0 && slot < _abilityCooldownsRemaining.Length && _abilityCooldownsRemaining[slot] > 0;

        public int GetAbilityCooldownRemaining(int slot)
            => (slot >= 0 && slot < _abilityCooldownsRemaining.Length) ? _abilityCooldownsRemaining[slot] : 0;

        public void TriggerAbilityCooldown(Ability ability)
        {
            if (ability == null || ability.cooldown <= 0) return;

            var abilities = UnitLoadoutManager.GetAbilities(this);
            for (int i = 0; i < abilities.Length; i++)
            {
                if (abilities[i] == ability)
                {
                    _abilityCooldownsRemaining[i] = ability.cooldown + 1;
                    return;
                }
            }
        }
        public void TickAbilityCooldowns()
        {
            for (int i = 0; i < _abilityCooldownsRemaining.Length; i++)
            {
                if (_abilityCooldownsRemaining[i] > 0)
                    _abilityCooldownsRemaining[i]--;
            }
        }

        private void FaceAttacker(Unit attacker)
        {
            if (attacker == null || attacker.currentTile == null || currentTile == null) return;

            int dx = attacker.currentTile.gridPosition.x - currentTile.gridPosition.x;
            if (dx > 0) FaceDirection(Vector2Int.right);
            else if (dx < 0) FaceDirection(Vector2Int.left);
        }

        public void FaceDirection(Vector2Int direction)
        {
            if (unitSpriteRenderers == null || unitSpriteRenderers.Length == 0) return;

            currentFacing = direction;

            bool flipX;
            if (direction == Vector2Int.left)
                flipX = false;
            else if (direction == Vector2Int.right)
                flipX = true;
            else
                return;

            foreach (var sr in unitSpriteRenderers)
            {
                if (sr == null) continue;
                sr.flipX = flipX;
            }
        }

        public void SetCurrentTile(Tile newTile)
        {
            if (currentTile != null && currentTile.currentUnit == this)
                currentTile.currentUnit = null;
            
            if (newTile != null && newTile.currentUnit != null && newTile.currentUnit != this)
            {
                Debug.LogError("This is a bug");

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
        
        public void SetCurrentTileLogical(Tile newTile)
        {
            if (currentTile != null && currentTile.currentUnit == this)
                currentTile.currentUnit = null;

            currentTile = newTile;
            if (currentTile != null)
                currentTile.currentUnit = this;

            UnitManager.NotifyUnitMoved(this);
        }
        
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

        public static event System.Func<Unit, Unit, Ability, int, int> OnPreReceiveDamage;

        public static event System.Action<Unit> OnHealthChanged;
        
        public static void NotifyHealthChanged(Unit unit)
        {
            OnHealthChanged?.Invoke(unit);
        }

        public static event System.Action<Unit, int> OnDamageDealt;

        public static void NotifyDamageDealt(Unit victim, int amount)
        {
            OnDamageDealt?.Invoke(victim, amount);
        }

        public virtual void ReceiveDamage(int amount, Unit attacker = null, Ability sourceAbility = null)
        {
            if (IsDead) return;
            
            if (IsBody) return;

            if (StatusEffectManager.Instance != null)
            {
                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Shielded))
                {
                    var shieldEffect = StatusEffectManager.Instance.GetStatusEffect(this, StatusEffectType.Shielded);
                    StatusEffectManager.Instance.RemoveStatusEffect(this, shieldEffect);
                    return;
                }

                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Invulnerable))
                {
                    return;
                }

                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Guarded))
                {
                    var guardEffect = StatusEffectManager.Instance.GetStatusEffect(this, StatusEffectType.Guarded);
                    guardEffect.effectPower -= amount;
                    if (guardEffect.effectPower <= 0)
                    {
                        StatusEffectManager.Instance.RemoveStatusEffect(this, guardEffect);
                    }

                    Unit redirectTarget = guardEffect.source;
                    if (redirectTarget != null && redirectTarget != this && !redirectTarget.IsDead)
                    {
                        redirectTarget.ReceiveDamage(amount, attacker, sourceAbility);
                    }

                    return;
                }
            }

            ApplyDamageAndNotify(amount, attacker, sourceAbility);
        }

        public void AdminApplyTrueDamage(int amount, Unit attacker = null)
        {
            if (IsDead) return;
            if (IsBody) return;

            ApplyDamageAndNotify(amount, attacker, null);
        }

        private void ApplyDamageAndNotify(int amount, Unit attacker, Ability sourceAbility)
        {
            if (OnPreReceiveDamage != null)
            {
                foreach (System.Func<Unit, Unit, Ability, int, int> modifier in OnPreReceiveDamage.GetInvocationList())
                    amount = modifier(this, attacker, sourceAbility, amount);
            }

            if (amount <= 0) return;

            FaceAttacker(attacker);

            currentHealth -= amount;
            Debug.Log($"{name} took {amount} damage. HP now {currentHealth}");

            OnDamageDealt?.Invoke(this, amount);
            OnHealthChanged?.Invoke(this);

            if (currentHealth <= 0)
            {
                Debug.Log($"{name} was defeated.");
                Die(attacker);
            }
        }

        public void AdminDelete()
        {
            if (this == null || !gameObject.activeSelf) return;

            IsDead = true;

            if (currentTile != null && currentTile.currentUnit == this)
                currentTile.currentUnit = null;

            UnitManager.NotifyUnitDied(this);
            UnitManager.UnregisterUnit(this);

            Destroy(gameObject);
        }

        public void Die(Unit killer = null)
        {
            if (IsDead) return;
            IsDead = true;

            if (currentTile != null && currentTile.currentUnit == this)
                currentTile.currentUnit = null;

            if (killer is PlayerUnit && team != killer.team)
                DialogueManager.Trigger(DialogueTrigger.AllyDownsEnemy, instigator: killer);

            UnitManager.NotifyUnitDied(this);

            if (UnitDownedSequencer.Instance != null)
                UnitDownedSequencer.Instance.EnqueueDowned(this, killer);
            else
                gameObject.SetActive(false); 
        }

        public void BecomeBody()
        {
            IsBody = true;

            if (currentTile != null && currentTile.currentUnit == null)
                currentTile.currentUnit = this;

            UnitManager.NotifyBodySpawned(this);
        }

        public virtual bool CanMove()
        {
            if (!canMove) return false;
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Stuck))
                return false;
            return true;
        }
        
        public virtual bool CanUseAbilities()
        {
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Scared))
                return false;
            return true;
        }

        public virtual int GetEffectiveMovementRange()
        {
            return currentSpeed;
        }

        public virtual bool CanTarget(Unit unit)
        {
            if (unit == null || unit == this) return false;
            
            if (unit.IsDead && !unit.IsBody) return false;
            if (unit.IsBody) return false; 

            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(unit, StatusEffectType.Untargetable))
                return false;
            
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
            return team == other.team;
        }
        
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void EnableDebugLogging(bool enable)
        {
            enableDebugLogging = enable;
        }

    #if UNITY_EDITOR
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