using System.Collections;
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

        // The unit this bar belongs to — resolved at Start via GetComponentInParent.
        private Unit _unit;

        // ── Colour gradient ───────────────────────────────────────────────────
        // Transitions from green (#1FF200) to red (#FF0000) between 20 % and 80 % health.
        // Below 20 % the bar is fully red; above 80 % it is fully green.
        private static readonly Color HealthColourHigh = new Color(0x1F / 255f, 0xF2 / 255f, 0x00 / 255f); // #1FF200
        private static readonly Color HealthColourLow  = new Color(1f,           0f,           0f);           // #FF0000
        private const float ColourThresholdLow  = 0.20f;
        private const float ColourThresholdHigh = 0.80f;

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
            UnitManager.OnUnitDied += HandleUnitDied;
        }

        private void OnDestroy()
        {
            Unit.OnHealthChanged -= HandleHealthChanged;
            UnitManager.OnUnitDied -= HandleUnitDied;
        }

        // ── Event handler ─────────────────────────────────────────────────────

        private void HandleHealthChanged(Unit changedUnit)
        {
            if (changedUnit != _unit) return;
            RefreshBar();
        }

        private void HandleUnitDied(Unit deadUnit)
        {
            if (deadUnit != _unit) return;
            healthBarRoot.SetActive(false);
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

            // First reveal: show the root and snap to the correct value immediately
            // so the bar doesn't slide in from nothing.
            if (!healthBarRoot.activeSelf)
            {
                healthBarRoot.SetActive(true);
                ApplyBarValues(targetFraction);
                return;
            }

            // Subsequent changes: cancel any in-progress tween and slide from current values.
            if (_tweenCoroutine != null)
                StopCoroutine(_tweenCoroutine);

            _tweenCoroutine = StartCoroutine(AnimateBar(targetFraction));
        }

        /// <summary>
        /// Slides fillAmount and color from their current values to the target fraction
        /// over tweenDuration seconds using SmoothStep easing.
        /// </summary>
        private IEnumerator AnimateBar(float targetFraction)
        {
            float startFill   = healthBarImage.fillAmount;
            Color startColour = healthBarImage.color;

            float targetColourT = Mathf.InverseLerp(ColourThresholdHigh, ColourThresholdLow, targetFraction);
            Color targetColour  = Color.Lerp(HealthColourHigh, HealthColourLow, targetColourT);

            float elapsed = 0f;
            while (elapsed < tweenDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / tweenDuration));

                healthBarImage.fillAmount = Mathf.Lerp(startFill, targetFraction, t);
                healthBarImage.color      = Color.Lerp(startColour, targetColour, t);

                yield return null;
            }

            // Snap to exact final values once the tween completes.
            ApplyBarValues(targetFraction);
            _tweenCoroutine = null;
        }

        /// <summary>Immediately sets fill and colour with no animation.</summary>
        private void ApplyBarValues(float fraction)
        {
            healthBarImage.fillAmount = fraction;

            float colourT = Mathf.InverseLerp(ColourThresholdHigh, ColourThresholdLow, fraction);
            healthBarImage.color = Color.Lerp(HealthColourHigh, HealthColourLow, colourT);
        }
    }
}