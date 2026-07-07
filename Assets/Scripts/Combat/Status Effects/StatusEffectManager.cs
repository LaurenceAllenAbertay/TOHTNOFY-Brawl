using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class StatusEffectManager : MonoBehaviour
    {
        public static StatusEffectManager Instance { get; private set; }
        
        private Dictionary<Unit, List<StatusEffectInstance>> activeEffects = new Dictionary<Unit, List<StatusEffectInstance>>();
        
        private Dictionary<Unit, int> stunnedApplicationCount = new Dictionary<Unit, int>();
        private HashSet<Unit> stunnedThisTurn = new HashSet<Unit>();
        
        private Dictionary<Unit, int> immuneApplicationCount = new Dictionary<Unit, int>();
        private HashSet<Unit> immuneThisTurn = new HashSet<Unit>();

        public static event System.Action<Unit, StatusEffectInstance> OnStatusEffectApplied;
        public static event System.Action<Unit, StatusEffectInstance> OnStatusEffectRemoved;
        public static event System.Action<Unit, StatusEffectInstance> OnStatusEffectTriggered;
        
        public static event System.Func<Unit, Unit, StatusEffectData, float, float> OnModifyEffectPower;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
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
            
            bool isUnique = effectData.stackingBehavior == StatusEffectData.StackingBehavior.Unique;

            var existingEffect = isUnique ? null : FindExistingEffect(target, effectData);

            if (existingEffect != null)
            {
                // Handle stacking
                return HandleStackingBehavior(existingEffect, effectData, source, duration, power);
            }
            else
            {
                if (effectData.effectType == StatusEffectType.Stunned)
                {
                    int count = stunnedApplicationCount.ContainsKey(target) ? stunnedApplicationCount[target] : 0;
                    float failChance = count * 0.75f;
                    if (UnityEngine.Random.value < failChance)
                    {
                        Debug.Log($"[Stunned] Failed to apply to {target.name} (attempt {count + 1}, fail chance {failChance * 100}%)");
                        return null;
                    }
                    stunnedThisTurn.Add(target);
                    stunnedApplicationCount[target] = count + 1;
                }
                
                if (effectData.effectType == StatusEffectType.Immune)
                {
                    int count = immuneApplicationCount.ContainsKey(target) ? immuneApplicationCount[target] : 0;
                    float failChance = count * 0.75f;
                    if (UnityEngine.Random.value < failChance)
                    {
                        Debug.Log($"[Immune] Failed to apply to {target.name} (attempt {count + 1}, fail chance {failChance * 100}%)");
                        return null;
                    }
                    immuneThisTurn.Add(target);
                    immuneApplicationCount[target] = count + 1;
                }
                
                if (OnModifyEffectPower != null)
                {
                    foreach (System.Func<Unit, Unit, StatusEffectData, float, float> modifier
                             in OnModifyEffectPower.GetInvocationList())
                    {
                        power = modifier(source, target, effectData, power);
                    }
                }
                
                var newEffect = new StatusEffectInstance(effectData, source, target, duration, power);
                activeEffects[target].Add(newEffect);

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
            
            if (!stunnedThisTurn.Contains(unit))
                stunnedApplicationCount.Remove(unit);

            stunnedThisTurn.Remove(unit);
            
            if (!immuneThisTurn.Contains(unit))
                immuneApplicationCount.Remove(unit);

            immuneThisTurn.Remove(unit);
        }

        private void HandleUnitDied(Unit unit)
        {
            if (activeEffects.ContainsKey(unit))
            {
                activeEffects[unit].Clear();
                activeEffects.Remove(unit);
            }
            stunnedApplicationCount.Remove(unit);
            stunnedThisTurn.Remove(unit);
            immuneApplicationCount.Remove(unit);
            immuneThisTurn.Remove(unit);
        }

        private void ProcessEffectsForTiming(Unit unit, StatusEffectData.EffectTriggerTiming timing)
        {
            if (!activeEffects.ContainsKey(unit)) return;

            var effects = activeEffects[unit].ToList();

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
            switch (effect.effectData.effectType)
            {
                case StatusEffectType.Bleeding:
                    effect.target.ReceiveDamage(effect.stackCount);
                    effect.stackCount = Mathf.Min(effect.stackCount + 1, effect.effectData.maxStacks);
                    break;

                case StatusEffectType.Poison:
                    effect.target.ReceiveDamage(effect.stackCount);
                    effect.stackCount = Mathf.Max(0, effect.stackCount - 1);
                    if (effect.stackCount == 0)
                        RemoveStatusEffect(effect.target, effect);
                    break;

                case StatusEffectType.Healthy:
                    effect.target.currentHealth = Mathf.Min(effect.target.currentHealth + Mathf.RoundToInt(effect.effectPower),
                                                            effect.target.characterData.maxHealth);
                    break;

                case StatusEffectType.Fire:
                    effect.target.ReceiveDamage(Mathf.RoundToInt(effect.effectPower));
                    break;

                case StatusEffectType.Shocked:
                    int shockDamage = Mathf.RoundToInt(effect.effectPower);
                    effect.target.ReceiveDamage(shockDamage);
                    Debug.Log($"[Shocked] {effect.target.name} takes {shockDamage} shock damage.");
                    
                    if (effect.target.currentTile != null && GridManager.Instance != null)
                    {
                        int splashDamage = Mathf.Max(1, Mathf.RoundToInt(shockDamage / 2f));
                        var adjacentTiles = GridManager.Instance.GetAdjacentTiles(effect.target.currentTile);
                        foreach (var adjTile in adjacentTiles)
                        {
                            var adjUnit = adjTile.currentUnit;
                            if (adjUnit == null || adjUnit.IsBody) continue;
                            if (!effect.target.IsAllyOf(adjUnit)) continue;

                            adjUnit.ReceiveDamage(splashDamage);
                            Debug.Log($"[Shocked] {adjUnit.name} takes {splashDamage} arc splash damage.");
                        }
                    }
                    break;
                
            }

            OnStatusEffectTriggered?.Invoke(effect.target, effect);
        }

        private void UpdateEffectDurations(Unit unit)
        {
            if (!activeEffects.ContainsKey(unit)) return;

            var effectsToRemove = new List<StatusEffectInstance>();

            foreach (var effect in activeEffects[unit])
            {
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
                    existing.stackCount = Mathf.Min(
                        existing.stackCount + Mathf.Max(1, Mathf.RoundToInt(power)),
                        effectData.maxStacks);
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
        
        private void ApplyImmediateEffects(StatusEffectInstance effect)
        {
            // Apply stat modifiers
            switch (effect.effectData.effectType)
            {
                case StatusEffectType.AttackUp:
                case StatusEffectType.AttackDown:
                    // Nothing to apply immediately
                    break;
                case StatusEffectType.DefenseUp:
                case StatusEffectType.DefenseDown:
                    // Nothing to apply immediately
                    break;
                case StatusEffectType.SpeedUp:
                    effect.target.currentSpeed += Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.SpeedDown:
                    effect.target.currentSpeed -= Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.Intimidated:
                    // Nothing to apply immediately
                    break;
                case StatusEffectType.Taunting:
                    // Nothing to apply immediately
                    break;
                case StatusEffectType.Stunned:
                    // Nothing to apply immediately
                    break;
                case StatusEffectType.Immune:
                    // Nothing to apply immediately
                    break;
                case StatusEffectType.Warned:
                    // Nothing to apply immediately
                    break;
                case StatusEffectType.Shocked:
                    // Nothing to apply immediately
                    break;
                case StatusEffectType.Dizzy:
                    // Nothing to apply immediately
                    break;
                case StatusEffectType.Stuck:
                    // Nothing to apply immediately
                    break;
                case StatusEffectType.Scared:
                    // Nothing to apply immediately
                    break;
            }
            
            if (effect.effectData.applicationVFX != null)
            {
                var vfx = Instantiate(effect.effectData.applicationVFX,
                                    effect.target.transform.position,
                                    Quaternion.identity);
                vfx.transform.SetParent(effect.target.transform);
                Destroy(vfx, 3f);
            }
        }
        
        private void RemoveEffectModifiers(StatusEffectInstance effect)
        {
            switch (effect.effectData.effectType)
            {
                case StatusEffectType.AttackUp:
                case StatusEffectType.AttackDown:
                    break;
                case StatusEffectType.DefenseUp:
                case StatusEffectType.DefenseDown:
                    break;
                case StatusEffectType.SpeedUp:
                    effect.target.currentSpeed -= Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.SpeedDown:
                    effect.target.currentSpeed += Mathf.RoundToInt(effect.effectPower);
                    break;
                case StatusEffectType.Intimidated:
                    // Nothing to reverse
                    break;
                case StatusEffectType.Taunting:
                    // Nothing to reverse
                    break;
                case StatusEffectType.Stunned:
                    // Nothing to reverse
                    break;
                case StatusEffectType.Immune:
                    // Nothing to reverse
                    break;
                case StatusEffectType.Warned:
                    if (!effect.wasTriggered && effect.customData is StatusEffectData defenseDownData)
                    {
                        ApplyStatusEffect(effect.target, defenseDownData, effect.source,
                                          duration: 2, power: defenseDownData != null ? 0 : 0);
                        Debug.Log($"[Warned] {effect.target.name} never dodged — applying DefenseDown.");
                    }
                    break;
                case StatusEffectType.Shocked:
                    // Nothing to reverse
                    break;
                case StatusEffectType.Dizzy:
                    // Nothing to reverse
                    break;
                case StatusEffectType.Stuck:
                    // Nothing to reverse
                    break;
                case StatusEffectType.Scared:
                    // Nothing to reverse
                    break;
            }
            
            if (effect.effectData.removalVFX != null)
            {
                var vfx = Instantiate(effect.effectData.removalVFX,
                                    effect.target.transform.position,
                                    Quaternion.identity);
                Destroy(vfx, 2f);
            }
        }

        public static float GetAttackMultiplier(Unit unit)
        {
            if (Instance == null || unit == null) return 1f;
            if (!Instance.activeEffects.TryGetValue(unit, out var effects)) return 1f;

            float multiplier = 1f;
            foreach (var effect in effects)
            {
                if (effect.effectData.effectType == StatusEffectType.AttackUp)
                    multiplier += effect.effectPower * 0.1f;
                else if (effect.effectData.effectType == StatusEffectType.AttackDown)
                    multiplier -= effect.effectPower * 0.1f;
            }
            return Mathf.Max(0f, multiplier);
        }

        public static float GetDefenseMultiplier(Unit unit)
        {
            if (Instance == null || unit == null) return 1f;
            if (!Instance.activeEffects.TryGetValue(unit, out var effects)) return 1f;

            float multiplier = 1f;
            foreach (var effect in effects)
            {
                if (effect.effectData.effectType == StatusEffectType.DefenseUp)
                    multiplier += effect.effectPower * 0.1f;
                else if (effect.effectData.effectType == StatusEffectType.DefenseDown)
                    multiplier -= effect.effectPower * 0.1f;
            }
            return Mathf.Max(0f, multiplier);
        }
    }
}