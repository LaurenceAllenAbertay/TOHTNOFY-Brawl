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

        // ── Unity lifecycle ───────────────────────────────────────────────────

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
            // Reveal the whole bar group on first damage taken.
            if (!healthBarRoot.activeSelf)
                healthBarRoot.SetActive(true);

            if (_unit.characterData == null || _unit.characterData.maxHealth <= 0)
            {
                healthBarImage.fillAmount = 1f;
                return;
            }

            float fraction = (float)_unit.currentHealth / _unit.characterData.maxHealth;
            healthBarImage.fillAmount = Mathf.Clamp01(fraction);
        }
    }
}