using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class StatusEffectManager : MonoBehaviour
    {
        public static StatusEffectManager Instance { get; private set; }

        // Track all active status effects
        private Dictionary<Unit, List<StatusEffectInstance>> activeEffects = new Dictionary<Unit, List<StatusEffectInstance>>();

        // Events
        public static event System.Action<Unit, StatusEffectInstance> OnStatusEffectApplied;
        public static event System.Action<Unit, StatusEffectInstance> OnStatusEffectRemoved;
        public static event System.Action<Unit, StatusEffectInstance> OnStatusEffectTriggered;

        // Power modifier hook - passives register here to modify effectPower before application.
        // Signature: (source, target, effectData, originalPower) -> modifiedPower.
        // Multiple passives chain by each adding their delta to the incoming value.
        public static event System.Func<Unit, Unit, StatusEffectData, float, float> OnModifyEffectPower;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Subscribe to turn events
            TurnManager.OnTurnStarted += HandleTurnStarted;
            TurnManager.OnTurnEnded += HandleTurnEnded;
            UnitManager.OnUnitDied += HandleUnitDied;
        }

        void OnDestroy()
        {
            TurnManager.OnTurnStarted -= HandleTurnStarted;
            TurnManager.OnTurnEnded -= HandleTurnEnded;
            UnitManager.OnUnitDied -= HandleUnitDied;
        }

        public StatusEffectInstance ApplyStatusEffect(Unit target, StatusEffectData effectData, Unit source, int duration, float power = 0)
        {
            if (target == null || effectData == null) return null;

            if (!activeEffects.ContainsKey(target))
                activeEffects[target] = new List<StatusEffectInstance>();

            // Unique effects always create a fresh independent instance with their own timer.
            // Skip FindExistingEffect so each application stacks individually and expires
            // independently (e.g. hit on turn 1 expires turn 4, hit on turn 2 expires turn 5).
            bool isUnique = effectData.stackingBehavior == StatusEffectData.StackingBehavior.Unique;

            var existingEffect = isUnique ? null : FindExistingEffect(target, effectData);

            if (existingEffect != null)
            {
                // Handle stacking
                return HandleStackingBehavior(existingEffect, effectData, source, duration, power);
            }
            else
            {
                // Allow passives to modify the power before the instance is created.
                if (OnModifyEffectPower != null)
                {
                    foreach (System.Func<Unit, Unit, StatusEffectData, float, float> modifier
                             in OnModifyEffectPower.GetInvocationList())
                    {
                        power = modifier(source, target, effectData, power);
                    }
                }

                // Create new effect instance
                var newEffect = new StatusEffectInstance(effectData, source, target, duration, power);
                activeEffects[target].Add(newEffect);

                // Apply immediate effects
                ApplyImmediateEffects(newEffect);

                OnStatusEffectApplied?.Invoke(target, newEffect);
                return newEffect;
            }
        }

        public void RemoveStatusEffect(Unit target, StatusEffectInstance effect)
        {
            if (!activeEffects.ContainsKey(target)) return;

            if (activeEffects[target].Remove(effect))
            {
                RemoveEffectModifiers(effect);
                OnStatusEffectRemoved?.Invoke(target, effect);

                if (activeEffects[target].Count == 0)
                    activeEffects.Remove(target);
            }
        }

        public bool HasStatusEffect(Unit unit, StatusEffectType effectType)
        {
            if (!activeEffects.ContainsKey(unit)) return false;
            return activeEffects[unit].Any(e => e.effectData.effectType == effectType);
        }

        public StatusEffectInstance GetStatusEffect(Unit unit, StatusEffectType effectType)
        {
            if (!activeEffects.ContainsKey(unit)) return null;
            return activeEffects[unit].FirstOrDefault(e => e.effectData.effectType == effectType);
        }

        public List<StatusEffectInstance> GetAllStatusEffects(Unit unit)
        {
            if (!activeEffects.ContainsKey(unit)) return new List<StatusEffectInstance>();
            return new List<StatusEffectInstance>(activeEffects[unit]);
        }

        private void HandleTurnStarted(Unit unit)
        {
            ProcessEffectsForTiming(unit, StatusEffectData.EffectTriggerTiming.StartOfTurn);
        }

        private void HandleTurnEnded(Unit unit)
        {
            ProcessEffectsForTiming(unit, StatusEffectData.EffectTriggerTiming.EndOfTurn);
            UpdateEffectDurations(unit);
        }

        private void HandleUnitDied(Unit unit)
        {
            if (activeEffects.ContainsKey(unit))
            {
                activeEffects[unit].Clear();
                activeEffects.Remove(unit);
            }
        }

        private void ProcessEffectsForTiming(Unit unit, StatusEffectData.EffectTriggerTiming timing)
        {
            if (!activeEffects.ContainsKey(unit)) return;

            var effects = activeEffects[unit].ToList(); // Copy to avoid modification during iteration

            foreach (var effect in effects)
            {
                if (effect.effectData.triggerTiming == timing && !effect.hasTriggeredThisTurn)
                {
                    TriggerEffect(effect);
                    effect.hasTriggeredThisTurn = true;
                }
            }
        }

        private void TriggerEffect(StatusEffectInstance effect)
        {
            // This will be expanded with specific effect implementations
            switch (effect.effectData.effectType)
            {
                case StatusEffectType.Bleeding:
                    effect.target.ReceiveDamage(Mathf.RoundToInt(effect.effectPower * effect.stackCount));
                    effect.stackCount = Mathf.Max(0, effect.stackCount - 1);
                    if (effect.stackCount == 0)
                        RemoveStatusEffect(effect.target, effect);
                    break;

                case StatusEffectType.Poison:
                    int poisonDamage = Mathf.RoundToInt(effect.effectPower * (effect.effectData.effectName.Length - effect.remainingDuration + 1));
                    effect.target.ReceiveDamage(poisonDamage);
                    break;

                case StatusEffectType.Healthy:
                    effect.target.currentHealth = Mathf.Min(effect.target.currentHealth + Mathf.RoundToInt(effect.effectPower),
                                                            effect.target.characterData.maxHealth);
                    break;

                case StatusEffectType.Fire:
                    effect.target.ReceiveDamage(Mathf.RoundToInt(effect.effectPower));
                    break;

                    // Add more cases as needed
            }

            OnStatusEffectTriggered?.Invoke(effect.target, effect);
        }

        private void UpdateEffectDurations(Unit unit)
        {
            if (!activeEffects.ContainsKey(unit)) return;

            var effectsToRemove = new List<StatusEffectInstance>();

            foreach (var effect in activeEffects[unit])
            {
                // Duration -1 means indefinite (e.g. passive-managed effects).
                // The owning system is responsible for removal; skip ticking.
                if (effect.remainingDuration == -1)
                {
                    effect.hasTriggeredThisTurn = false;
                    continue;
                }

                effect.remainingDuration--;
                effect.hasTriggeredThisTurn = false;

                if (effect.IsExpired)
                    effectsToRemove.Add(effect);
            }

            foreach (var effect in effectsToRemove)
            {
                RemoveStatusEffect(unit, effect);
            }
        }

        private StatusEffectInstance HandleStackingBehavior(StatusEffectInstance existing, StatusEffectData effectData,
                                                           Unit source, int duration, float power)
        {
            switch (effectData.stackingBehavior)
            {
                case StatusEffectData.StackingBehavior.None:
                    return existing; // Do nothing

                case StatusEffectData.StackingBehavior.RefreshDuration:
                    existing.RefreshDuration(duration);
                    return existing;

                case StatusEffectData.StackingBehavior.AddDuration:
                    existing.remainingDuration += duration;
                    return existing;

                case StatusEffectData.StackingBehavior.AddStacks:
                    existing.stackCount = Mathf.Min(existing.stackCount + 1, effectData.maxStacks);
                    existing.RefreshDuration(duration);
                    return existing;

                case StatusEffectData.StackingBehavior.Replace:
                    RemoveStatusEffect(existing.target, existing);
                    return ApplyStatusEffect(existing.target, effectData, source, duration, power);

                default:
                    return existing;
            }
        }

        private StatusEffectInstance FindExistingEffect(Unit target, StatusEffectData effectData)
        {
            if (!activeEffects.ContainsKey(target)) return null;
            return activeEffects[target].FirstOrDefault(e => e.effectData == effectData);
        }

        // **UPDATE** - Replace existing ApplyImmediateEffects method
        private void ApplyImmediateEffects(StatusEffectInstance effect)
        {
            // Apply stat modifiers
            switch (effect.effectData.effectType)
            {
                case StatusEffectType.AttackUp:
                    effect.target.currentAttack += Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.AttackDown:
                    effect.target.currentAttack -= Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.DefenseUp:
                    effect.target.currentDefense += Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.DefenseDown:
                    effect.target.currentDefense -= Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.SpeedUp:
                    effect.target.currentSpeed += Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.SpeedDown:
                    effect.target.currentSpeed -= Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.Intimidated:
                    // Behavioural effect: no stat change on application.
                    // Unit.CanTarget enforces the restriction at selection time.
                    break;
                case StatusEffectType.Taunting:
                    // Register this unit as a likely target on all enemy AI units.
                    // effectPower is the scoring bonus (configured on the StatusEffectData SO).
                    foreach (var unit in UnitManager.AllUnits)
                    {
                        var ai = unit.GetComponent<DDD.TNFY.BRAWL.UnitAI>();
                        if (ai != null)
                            ai.AddTargetLikelyUnit(effect.target, effect.remainingDuration);
                    }
                    break;
            }

            // Spawn VFX if configured
            if (effect.effectData.applicationVFX != null)
            {
                var vfx = Instantiate(effect.effectData.applicationVFX,
                                    effect.target.transform.position,
                                    Quaternion.identity);
                vfx.transform.SetParent(effect.target.transform);
                Destroy(vfx, 3f);
            }
        }

        // **UPDATE** - Replace existing RemoveEffectModifiers method
        private void RemoveEffectModifiers(StatusEffectInstance effect)
        {
            // Reverse stat modifiers when effect ends.
            switch (effect.effectData.effectType)
            {
                case StatusEffectType.AttackUp:
                    effect.target.currentAttack -= Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.AttackDown:
                    effect.target.currentAttack += Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.DefenseUp:
                    effect.target.currentDefense -= Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.DefenseDown:
                    effect.target.currentDefense += Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.SpeedUp:
                    effect.target.currentSpeed -= Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.SpeedDown:
                    effect.target.currentSpeed += Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.Intimidated:
                    // Nothing to reverse: restriction is checked live via Unit.CanTarget.
                    break;
                case StatusEffectType.Taunting:
                    // Nothing to reverse: AddTargetLikelyUnit tracks its own duration
                    // and clears itself via UpdateTargetingDurations in UnitAI.
                    break;
            }

            // Spawn removal VFX if configured
            if (effect.effectData.removalVFX != null)
            {
                var vfx = Instantiate(effect.effectData.removalVFX,
                                    effect.target.transform.position,
                                    Quaternion.identity);
                Destroy(vfx, 2f);
            }
        }
    }
}