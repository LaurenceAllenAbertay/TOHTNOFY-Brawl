using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Big Moment — Kallper's passive.
    ///
    /// Once per combat: if an incoming hit would reduce Kallper's health to zero or below,
    /// the damage is fully absorbed via OnPreReceiveDamage (returning 0). No HP is actually
    /// removed and Die() is never called, so all gameplay systems remain unaffected.
    ///
    /// The presentation sequence is then orchestrated as a coroutine:
    ///   1. Health bar tweens visually to zero (HP is still at its real value).
    ///   2. The Big_Moment animation plays in full.
    ///   3. currentHealth is set to 50% max, OnHealthChanged is fired, and the bar
    ///      animates up from zero to the new real value.
    ///
    /// The one-per-combat limit is tracked by a bool on this ScriptableObject instance.
    /// Because Unity reuses SO instances across play sessions in the editor, the flag is
    /// reset in both Initialise (combat start) and Cleanup (combat end) so it never
    /// carries over between runs.
    ///
    /// Setup: equip this SO through UnitLoadoutManager for players, or EnemyLoadout for
    /// enemies. Assign attackUpData to the project's AttackUp StatusEffectData asset.
    /// Set attackPower and buffDuration in the Inspector.
    /// Ensure Kallper's Animator has a state named "Big_Moment".
    ///
    /// Create via: Assets > Create > TNFY Brawl > Passives > Big Moment
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Big Moment")]
    public class BigMomentPassive : PassiveAbility
    {
        [Header("Attack Buff")]
        [Tooltip("AttackUp StatusEffectData asset applied after the Big Moment sequence.")]
        public StatusEffectData attackUpData;

        [Tooltip("Flat attack bonus granted by the buff.")]
        public float attackPower = 3f;

        [Tooltip("How many turns the attack buff lasts.")]
        public int buffDuration = 3;

        private const string BIG_MOMENT_STATE = "Big_Moment";

        // Instance flag — reset on Initialise so it never persists between combat sessions.
        private bool _triggered;

        // Cached references so the event handler and coroutine can access them without
        // closing over the handler (ScriptableObjects are not MonoBehaviours and cannot
        // start coroutines — we delegate that to the handler's MonoBehaviour).
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
            // Only intercept damage aimed at our owner, only once, and only when lethal.
            if (_triggered) return amount;
            if (victim != _owner) return amount;
            if (_owner.currentHealth - amount > 0) return amount;

            _triggered = true;
            Debug.Log($"[BigMoment] {_owner.name} survived a lethal hit! Starting Big Moment sequence.");

            // Absorb the damage entirely — currentHealth stays at its real value and
            // Die() is never called. The visual presentation runs as a separate coroutine.
            _handler.StartCoroutine(BigMomentSequence());

            // Return 0 so ReceiveDamage applies no HP change and does not call Die().
            return 0;
        }

        private IEnumerator BigMomentSequence()
        {
            var unitAnimator = _owner.GetComponent<UnitAnimator>();
            var healthBar    = _owner.GetComponentInChildren<UnitHealthBarDisplay>();
            int maxHealth    = _owner.characterData != null ? _owner.characterData.maxHealth : 1;

            // ── Step 1: Wait for the Hurt animation (already triggered by AbilitySequencer)
            // and let the health bar finish its normal tween before we override it.
            // AbilitySequencer fired PlayHurt() and expects the bar to tween — because we
            // returned 0 from InterceptDamage, currentHealth didn't change and OnHealthChanged
            // was NOT fired, so RefreshBar was never called. The bar is still at its pre-hit
            // fraction. We wait for the hurt animation so the timing feels natural.
            if (unitAnimator != null)
                yield return _handler.StartCoroutine(unitAnimator.WaitForHurtAnimation());

            // ── Step 2: Drive the health bar visually to zero regardless of real HP.
            if (healthBar != null)
            {
                healthBar.TweenToFraction(0f, displayHealthOverride: 0);
                yield return _handler.StartCoroutine(healthBar.WaitForTweenComplete());
            }

            // ── Step 3: Play the Big_Moment animation and wait for it to finish.
            if (unitAnimator != null && unitAnimator.HasState(BIG_MOMENT_STATE))
            {
                unitAnimator.PlayAnimation(BIG_MOMENT_STATE);

                // Give the animator one frame to register the state transition.
                yield return null;

                // Wait for Big_Moment to complete.
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

            // ── Step 4: Restore Kallper to 50% HP and animate the bar back up.
            int restoredHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * 0.5f));
            _owner.currentHealth = restoredHealth;

            // Fire OnHealthChanged so UnitAnimator's idle tier re-evaluates and any other
            // subscriber (e.g. combat AI health checks) sees the updated value.
            // UnitHealthBarDisplay.HandleHealthChanged will call RefreshBar, which starts a
            // new tween from the current fillAmount (0) up to the restored fraction — exactly
            // the visual we want.
            Unit.NotifyHealthChanged(_owner);

            // ── Step 5: Apply the attack buff now that the sequence is complete.
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