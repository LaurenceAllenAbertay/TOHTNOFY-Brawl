using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Displays the active status effect icons for the unit this component belongs to.
    ///
    /// ── Required Unity hierarchy ──────────────────────────────────────────────
    ///
    ///   [Unit prefab root]
    ///   └── StatusEffectCanvas          ← Canvas (World Space), Bills Board optional,
    ///                                      this script lives here
    ///       └── IconContainer           ← RectTransform + HorizontalLayoutGroup
    ///                                      + ContentSizeFitter (Horizontal: Preferred Size)
    ///           └── (spawned at runtime) ← Image prefab with a fixed Width/Height RectTransform
    ///
    ///
    /// ── How it works ─────────────────────────────────────────────────────────
    ///  • On Start it looks for a Unit component in its parent hierarchy to know which
    ///    unit it belongs to (works for both PlayerUnit and EnemyUnit transparently).
    ///  • It subscribes to StatusEffectManager.OnStatusEffectApplied and
    ///    OnStatusEffectRemoved, filtering by its own unit, then rebuilds the strip.
    ///  • Each rebuild clears all children and re-stamps one Image per active effect.
    ///    Because effect counts are typically small (1-5), a full rebuild is cheap
    ///    and avoids any stale-icon edge cases from stacking/removal interactions.
    /// </summary>
    public class StatusEffectIconDisplay : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The RectTransform that holds the icon Images. " +
                 "Should have a HorizontalLayoutGroup and ContentSizeFitter.")]
        [SerializeField] private RectTransform iconContainer;

        [Tooltip("Prefab stamped once per active status effect. " +
                 "Must have an Image component on its root.")]
        [SerializeField] private GameObject iconPrefab;

        // The unit this display belongs to — resolved at Start via GetComponentInParent.
        private Unit _unit;

        // Pool of spawned icon instances so we can clear them on rebuild.
        private readonly List<GameObject> _spawnedIcons = new List<GameObject>();

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Start()
        {
            _unit = GetComponentInParent<Unit>();

            if (_unit == null)
            {
                Debug.LogWarning($"[StatusEffectIconDisplay] No Unit found in parent hierarchy of '{gameObject.name}'. " +
                                 "Disabling component.");
                enabled = false;
                return;
            }

            if (iconContainer == null)
            {
                Debug.LogWarning($"[StatusEffectIconDisplay] iconContainer is not assigned on '{gameObject.name}'. " +
                                 "Disabling component.");
                enabled = false;
                return;
            }

            if (iconPrefab == null)
            {
                Debug.LogWarning($"[StatusEffectIconDisplay] iconPrefab is not assigned on '{gameObject.name}'. " +
                                 "Disabling component.");
                enabled = false;
                return;
            }

            StatusEffectManager.OnStatusEffectApplied += HandleEffectApplied;
            StatusEffectManager.OnStatusEffectRemoved += HandleEffectRemoved;

            // Rebuild once at start in case effects were applied before this component woke.
            RebuildIcons();
        }

        private void OnDestroy()
        {
            StatusEffectManager.OnStatusEffectApplied -= HandleEffectApplied;
            StatusEffectManager.OnStatusEffectRemoved -= HandleEffectRemoved;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void HandleEffectApplied(Unit target, StatusEffectInstance effect)
        {
            if (target != _unit) return;
            RebuildIcons();
        }

        private void HandleEffectRemoved(Unit target, StatusEffectInstance effect)
        {
            if (target != _unit) return;
            RebuildIcons();
        }

        // ── Sequencer API ─────────────────────────────────────────────────────

        /// <summary>
        /// Hides this status effect display immediately. Called by AbilitySequencer and
        /// UnitDeathSequencer after the hurt animation and health bar tween have finished
        /// on a lethal hit — immediately before the death animation plays.
        /// Never called in response to OnUnitDied directly; timing is owned by the sequencers.
        /// </summary>
        public void HideImmediate()
        {
            gameObject.SetActive(false);
        }

        // ── Icon rebuild ──────────────────────────────────────────────────────

        /// <summary>
        /// Clears all existing icon images and re-stamps one per active status effect.
        /// Called whenever any effect is applied to or removed from this unit.
        /// </summary>
        private void RebuildIcons()
        {
            // Clear previous icons.
            foreach (var icon in _spawnedIcons)
            {
                if (icon != null)
                    Destroy(icon);
            }
            _spawnedIcons.Clear();

            if (StatusEffectManager.Instance == null) return;

            var activeEffects = StatusEffectManager.Instance.GetAllStatusEffects(_unit);

            foreach (var effect in activeEffects)
            {
                // Skip effects that have no icon configured — silently skip rather than
                // stamping a blank Image, which would create empty gaps in the layout.
                if (effect.effectData == null || effect.effectData.icon == null)
                    continue;

                var iconGO = Instantiate(iconPrefab, iconContainer);
                var img = iconGO.GetComponent<Image>();

                if (img != null)
                    img.sprite = effect.effectData.icon;

                _spawnedIcons.Add(iconGO);
            }
        }
    }
}