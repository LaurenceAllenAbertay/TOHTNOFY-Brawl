using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Drives a world-space health bar for the unit this component belongs to,
    /// using a filled Image rather than a Slider.
    ///
    /// ── Required Unity hierarchy ──────────────────────────────────────────────
    ///
    ///   [Unit prefab root]
    ///   └── Canvas                     ← Canvas (World Space), this script lives here
    ///       └── HealthBarRoot          ← GameObject, wire to healthBarRoot below
    ///                                     Start this inactive in the Inspector
    ///           ├── Background         ← Image (dark backing bar)
    ///           └── HealthBar          ← Image, wire to healthBarImage below
    ///                                     Image Type  = Filled
    ///                                     Fill Method = Horizontal
    ///                                     Fill Origin = Left
    ///
    /// ── Setup steps ──────────────────────────────────────────────────────────
    ///  1. Add this script to your Unit Canvas GameObject.
    ///  2. Assign the HealthBarRoot GameObject to the healthBarRoot field.
    ///  3. Assign the HealthBar fill Image to the healthBarImage field.
    ///  4. Set HealthBarRoot inactive in the Inspector — this script shows it
    ///     the moment the unit takes any damage.
    ///
    /// ── How it works ─────────────────────────────────────────────────────────
    ///  • On Start it resolves its owner via GetComponentInParent<Unit>(), so it
    ///    works transparently for both PlayerUnit and EnemyUnit with no subclass changes.
    ///  • It subscribes to Unit.OnHealthChanged, which is fired by every health-change
    ///    path: ReceiveDamage (normal damage + tile effects) and RecoilDamageEffect
    ///    (which writes currentHealth directly, bypassing ReceiveDamage).
    ///  • The HealthBarRoot is hidden at full health and shown the moment the unit
    ///    drops below max HP, revealing both the background and the fill together.
    /// </summary>
    public class UnitHealthBarDisplay : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Root GameObject that holds the background and fill Image. " +
                 "Set inactive in the Inspector — shown on first damage taken.")]
        [SerializeField] private GameObject healthBarRoot;

        [Tooltip("The filled Image that represents health. " +
                 "Must have Image Type = Filled, Fill Method = Horizontal, Fill Origin = Left.")]
        [SerializeField] private Image healthBarImage;

        [Tooltip("Optional TMP label that displays 'currentHP / maxHP'. Leave unassigned to skip.")]
        [SerializeField] private TMP_Text healthNumberLabel;

        // The unit this bar belongs to — resolved at Start via GetComponentInParent.
        private Unit _unit;

        // ── Colour gradient ───────────────────────────────────────────────────
        // Shifts hue in HSV space from green (120deg) down to red (0deg) as health drops.
        // This sweeps cleanly through yellow at 50% health — the industry standard used
        // by Pokemon, most JRPGs, and strategy games — avoiding the muddy brown that
        // appears when lerping green->red directly in RGB space.
        // Saturation and Value are held constant so only the hue moves.
        private const float HueGreen     = 120f / 360f; // 0.333...
        private const float HueRed       =   0f / 360f; // 0.0
        private const float BarSaturation = 1.0f;
        private const float BarValue      = 0.9f;

        [Header("Colour Thresholds")]
        [Tooltip("Health fraction at or below which the bar is always fully red.")]
        [SerializeField] [Range(0f, 0.5f)] private float redThreshold    = 0.15f;
        [Tooltip("Health fraction at or above which the bar is always fully green.")]
        [SerializeField] [Range(0.5f, 1f)] private float greenThreshold  = 0.85f;

        // ── Tween ─────────────────────────────────────────────────────────────
        [Header("Animation")]
        [Tooltip("How long the fill and colour slide to their new values, in seconds.")]
        [SerializeField] private float tweenDuration = 0.3f;

        // Tracked so we can cancel and restart mid-tween when health changes rapidly.
        private Coroutine _tweenCoroutine;

        private void Start()
        {
            _unit = GetComponentInParent<Unit>();

            if (_unit == null)
            {
                Debug.LogWarning($"[UnitHealthBarDisplay] No Unit found in parent hierarchy of " +
                                 $"'{gameObject.name}'. Disabling component.");
                enabled = false;
                return;
            }

            if (healthBarRoot == null || healthBarImage == null)
            {
                Debug.LogWarning($"[UnitHealthBarDisplay] healthBarRoot or healthBarImage is not " +
                                 $"assigned on '{gameObject.name}'. Disabling component.");
                enabled = false;
                return;
            }

            // Ensure the bar is hidden at full health regardless of Inspector state.
            healthBarRoot.SetActive(false);

            Unit.OnHealthChanged += HandleHealthChanged;
        }

        private void OnDestroy()
        {
            Unit.OnHealthChanged -= HandleHealthChanged;
        }

        // ── Event handler ─────────────────────────────────────────────────────

        private void HandleHealthChanged(Unit changedUnit)
        {
            if (changedUnit != _unit) return;
            RefreshBar();
        }

        // ── Bar update ────────────────────────────────────────────────────────

        /// <summary>
        /// Shows the root and recalculates the fill fraction from the unit's current
        /// and max health. maxHealth is read from characterData — the same source
        /// Unit.Awake() uses, so there is no separate field to keep in sync.
        /// </summary>
        private void RefreshBar()
        {
            if (_unit.characterData == null || _unit.characterData.maxHealth <= 0)
                return;

            float targetFraction = Mathf.Clamp01((float)_unit.currentHealth / _unit.characterData.maxHealth);

            // First reveal: always snap to FULL first, then tween down to the target value.
            // This ensures the player sees the bar appear at full health and drain to the new
            // value — even on a lethal first hit where targetFraction is 0. Without this,
            // a one-shot kill would snap the bar straight to zero with no visible drain,
            // and WaitForTweenComplete() would return immediately, skipping the death wait.
            if (!healthBarRoot.activeSelf)
            {
                healthBarRoot.SetActive(true);
                ApplyBarValues(1f);

                // If the hit somehow brought health back to full, nothing to tween.
                if (Mathf.Approximately(targetFraction, 1f))
                    return;
            }

            // Subsequent changes (and first-reveal drain): cancel any in-progress tween
            // and slide from the current fill value down to the new target.
            if (_tweenCoroutine != null)
                StopCoroutine(_tweenCoroutine);

            _tweenCoroutine = StartCoroutine(AnimateBar(targetFraction));
        }

        /// <summary>
        /// Slides fillAmount, colour, and the health number label from their current
        /// values to the target over tweenDuration seconds using SmoothStep easing.
        /// The number is lerped between the HP value at the start of this tween and
        /// targetHealthOverride (or the unit's real currentHealth when not overridden),
        /// so it rolls in perfect sync with the bar.
        /// </summary>
        private IEnumerator AnimateBar(float targetFraction, int? targetHealthOverride = null)
        {
            float startFill   = healthBarImage.fillAmount;
            Color startColour = healthBarImage.color;

            Color targetColour = ColourForFraction(targetFraction);

            // Capture the HP the label is currently showing so we can roll from there,
            // not from the unit's already-updated currentHealth.
            int maxHealth    = _unit.characterData.maxHealth;
            int targetHealth = targetHealthOverride ?? _unit.currentHealth;
            int startHealth  = Mathf.RoundToInt(startFill * maxHealth);

            float elapsed = 0f;
            while (elapsed < tweenDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / tweenDuration));

                float currentFraction     = Mathf.Lerp(startFill, targetFraction, t);
                healthBarImage.fillAmount = currentFraction;
                healthBarImage.color      = ColourForFraction(currentFraction);

                SetHealthNumberText(Mathf.RoundToInt(Mathf.Lerp(startHealth, targetHealth, t)), maxHealth);

                yield return null;
            }

            // Snap to exact final values once the tween completes.
            ApplyBarValues(targetFraction, targetHealthOverride);
            _tweenCoroutine = null;
        }

        /// <summary>Immediately sets fill, colour, and number label with no animation.</summary>
        private void ApplyBarValues(float fraction, int? healthOverride = null)
        {
            healthBarImage.fillAmount = fraction;
            healthBarImage.color = ColourForFraction(fraction);
            int displayHealth = healthOverride ?? _unit.currentHealth;
            SetHealthNumberText(displayHealth, _unit.characterData.maxHealth);
        }

        /// <summary>
        /// Returns the bar colour for a given health fraction by shifting the hue in
        /// HSV space from green (120 deg, full health) down to red (0 deg, empty).
        /// InverseLerp remaps the fraction into the active gradient window
        /// [redThreshold, greenThreshold], so the bar locks to full red below
        /// redThreshold and full green above greenThreshold.
        /// </summary>
        private Color ColourForFraction(float fraction)
        {
            float t   = Mathf.InverseLerp(redThreshold, greenThreshold, fraction);
            float hue = Mathf.Lerp(HueRed, HueGreen, t);
            return Color.HSVToRGB(hue, BarSaturation, BarValue);
        }

        /// <summary>
        /// Writes "current / max" to the optional health number label.
        /// Accepts the values directly so it can be called both mid-tween (with a
        /// lerped value) and at snap points (with the real currentHealth).
        /// </summary>
        private void SetHealthNumberText(int current, int max)
        {
            if (healthNumberLabel == null) return;
            healthNumberLabel.text = $"{current}/{max}";
        }

        // ── Sequencer API ─────────────────────────────────────────────────────

        /// <summary>
        /// Yields until the current health bar tween has finished (or immediately if
        /// no tween is running). Called by AbilitySequencer so it can wait for the
        /// bar to reach zero before hiding it and playing the death animation.
        /// </summary>
        public IEnumerator WaitForTweenComplete()
        {
            while (_tweenCoroutine != null)
                yield return null;
        }

        /// <summary>
        /// Animates the bar to an explicit visual fraction, bypassing currentHealth.
        /// Used by BigMomentPassive to fake the bar draining to zero and recovering
        /// to 50% as a pure presentation without touching actual HP values.
        ///
        /// <paramref name="displayHealthOverride"/> controls what the number label shows
        /// at the end of the tween. Pass 0 when draining to zero so the label reads "0/max"
        /// correctly even though currentHealth hasn't changed. Omit (or pass null) to let
        /// the label use currentHealth as normal.
        ///
        /// Ensures the bar root is visible before starting the tween so the animation
        /// is always seen even if the bar was previously hidden (first-hit scenario).
        /// </summary>
        public void TweenToFraction(float targetFraction, int? displayHealthOverride = null)
        {
            if (healthBarRoot == null || healthBarImage == null) return;

            if (!healthBarRoot.activeSelf)
                healthBarRoot.SetActive(true);

            if (_tweenCoroutine != null)
                StopCoroutine(_tweenCoroutine);

            _tweenCoroutine = StartCoroutine(AnimateBar(targetFraction, displayHealthOverride));
        }

        /// <summary>
        /// Instantly hides the health bar root with no animation. Called by
        /// AbilitySequencer after the hurt animation and tween have both completed
        /// on a unit that died, immediately before the death animation plays.
        /// </summary>
        public void HideImmediate()
        {
            if (_tweenCoroutine != null)
            {
                StopCoroutine(_tweenCoroutine);
                _tweenCoroutine = null;
            }
            healthBarRoot.SetActive(false);
        }
    }
}