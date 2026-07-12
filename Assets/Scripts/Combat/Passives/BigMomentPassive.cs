using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Big Moment")]
    public class BigMomentPassive : PassiveAbility
    {
        [Header("Attack Buff")]
        public StatusEffectData attackUpData;

        public float attackPower = 3f;

        public int buffDuration = 3;

        private const string BIG_MOMENT_STATE = "Big_Moment";
        
        private bool _triggered;
        
        private Unit _owner;
        private PassiveAbilityHandler _handler;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            _triggered = false;
            _owner     = handler.Owner;
            _handler   = handler;

            System.Func<Unit, Unit, Ability, int, int> onPreDamage = InterceptDamage;
            Unit.OnPreReceiveDamage += onPreDamage;
            RegisterCleanup(() => Unit.OnPreReceiveDamage -= onPreDamage);
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            _triggered = false;
            _owner     = null;
            _handler   = null;
            base.Cleanup(handler);
        }

        private int InterceptDamage(Unit victim, Unit attacker, Ability sourceAbility, int amount)
        {
            if (_triggered) return amount;
            if (victim != _owner) return amount;
            if (_owner.currentHealth - amount > 0) return amount;

            _triggered = true;
            Debug.Log($"[BigMoment] {_owner.name} survived a lethal hit! Starting Big Moment sequence.");
            
            _handler.StartCoroutine(BigMomentSequence());
            
            return 0;
        }

        private IEnumerator BigMomentSequence()
        {
            var unitAnimator = _owner.GetComponent<UnitAnimator>();
            var healthBar    = _owner.GetComponentInChildren<UnitHealthBarDisplay>();
            int maxHealth    = _owner.maxHealth > 0 ? _owner.maxHealth : 1;
            
            if (unitAnimator != null)
                yield return _handler.StartCoroutine(unitAnimator.WaitForHurtAnimation());
            
            if (healthBar != null)
            {
                healthBar.TweenToFraction(0f, displayHealthOverride: 0);
                yield return _handler.StartCoroutine(healthBar.WaitForTweenComplete());
            }
            
            if (unitAnimator != null && unitAnimator.HasState(BIG_MOMENT_STATE))
            {
                unitAnimator.PlayAnimation(BIG_MOMENT_STATE);
                
                yield return null;
                
                float elapsed = 0f;
                const float maxWait = 10f;
                while (elapsed < maxWait)
                {
                    var stateInfo = unitAnimator.GetComponent<Animator>()
                                               .GetCurrentAnimatorStateInfo(0);
                    if (stateInfo.IsName(BIG_MOMENT_STATE) && stateInfo.normalizedTime >= 1f && !stateInfo.loop)
                        break;
                    if (!stateInfo.IsName(BIG_MOMENT_STATE))
                        break;

                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                Debug.LogWarning($"[BigMoment] Animator on {_owner.name} has no '{BIG_MOMENT_STATE}' state. " +
                                 "Add the state to the Animator Controller.");
            }
            
            int restoredHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * 0.5f));
            _owner.currentHealth = restoredHealth;
            
            Unit.NotifyHealthChanged(_owner);
            
            if (attackUpData != null && StatusEffectManager.Instance != null)
            {
                StatusEffectManager.Instance.ApplyStatusEffect(
                    _owner, attackUpData, _owner, buffDuration, attackPower);
                Debug.Log($"[BigMoment] {_owner.name} restored to {restoredHealth} HP " +
                          $"and gains +{attackPower} attack for {buffDuration} turns.");
            }
        }
    }
}