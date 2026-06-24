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
    ///  4. Set HealthBarRoot inactive in the Inspector — this script manages
    ///     all show/hide logic at runtime.
    ///
    /// ── Unit hover collider setup ─────────────────────────────────────────────
    ///  Unit hover detection uses InputManager's unit-layer raycast, not tile events.
    ///  For hover to work each unit prefab needs:
    ///   • A Box Collider (Is Trigger = true) sized to cover the unit sprite.
    ///   • The unit's root GameObject set to the "Unit" layer (or whichever layer
    ///     is assigned to InputManager.unitLayer in the Inspector).
    ///
    /// ── When the bar is visible ───────────────────────────────────────────────
    ///  The bar is shown when ANY of the following is true, and hidden the moment
    ///  ALL of them become false:
    ///
    ///    1. A health change (damage or heal) occurred recently.
    ///       The bar appears, tweens to the new value, then lingers for
    ///       lingerDuration seconds before hiding.
    ///
    ///    2. It is this unit's turn.
    ///       The bar shows when TurnManager.OnTurnStarted fires for this unit
    ///       and hides when TurnManager.OnTurnEnded fires. Applies to both
    ///       player-controlled and AI-controlled units.
    ///
    ///    3. The player's cursor is hovering directly over this unit.
    ///       Uses InputManager.OnUnitHovered / OnUnitHoverExited, which are
    ///       driven by a unit-layer-only Physics.Raycast each frame.
    ///
    /// ── How it works ─────────────────────────────────────────────────────────
    ///  • On Start it resolves its owner via GetComponentInParent<Unit>().
    ///  • Three boolean flags track each show condition independently.
    ///  • EvaluateVisibility() is called whenever any flag changes and
    ///    shows or hides the bar via a single central decision.
    ///  • When revealed by a turn or hover (not damage), the bar snaps to the
    ///    unit's current health immediately — no tween, no snap-to-full.
    /// </summary>
    public class UnitHealthBarDisplay : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Root GameObject that holds the background and fill Image. " +
                 "Set inactive in the Inspector — shown and hidden at runtime.")]
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
        private const float HueGreen      = 120f / 360f; // 0.333...
        private const float HueRed        =   0f / 360f; // 0.0
        private const float BarSaturation = 1.0f;
        private const float BarValue      = 0.9f;

        [Header("Colour Thresholds")]
        [Tooltip("Health fraction at or below which the bar is always fully red.")]
        [SerializeField] [Range(0f, 0.5f)] private float redThreshold   = 0.15f;
        [Tooltip("Health fraction at or above which the bar is always fully green.")]
        [SerializeField] [Range(0.5f, 1f)] private float greenThreshold = 0.85f;

        // ── Tween & Linger ────────────────────────────────────────────────────
        [Header("Animation")]
        [Tooltip("How long the fill and colour slide to their new values, in seconds.")]
        [SerializeField] private float tweenDuration = 0.3f;

        [Tooltip("How long the bar stays visible after a health change tween completes " +
                 "before hiding, in seconds. Only applies when no other show condition " +
                 "(active turn, hover) is keeping the bar visible.")]
        [SerializeField] private float lingerDuration = 2f;

        // Tracked so we can cancel and restart mid-tween when health changes rapidly.
        private Coroutine _tweenCoroutine;

        // Runs after the tween completes to hold the bar visible for lingerDuration,
        // then clears _isDamageActive and re-evaluates visibility.
        private Coroutine _lingerCoroutine;

        // ── Visibility state ──────────────────────────────────────────────────
        // The bar is visible when ANY of these three conditions is true.
        // EvaluateVisibility() is the single gate that reads all three.

        /// <summary>True from OnTurnStarted until OnTurnEnded for this unit.</summary>
        private bool _isActiveTurn;

        /// <summary>True while the player's cursor is directly over this unit's collider.</summary>
        private bool _isHovered;

        /// <summary>
        /// True from the moment a health change fires until lingerDuration seconds
        /// after the resulting tween has completed. Covers both the tween in-flight
        /// and the post-tween linger period so the bar is never hidden mid-animation.
        /// </summary>
        private bool _isDamageActive;

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

            // Ensure the bar is hidden on spawn regardless of Inspector state.
            healthBarRoot.SetActive(false);

            Unit.OnHealthChanged           += HandleHealthChanged;
            TurnManager.OnTurnStarted      += HandleTurnStarted;
            TurnManager.OnTurnEnded        += HandleTurnEnded;
            InputManager.OnUnitHovered     += HandleUnitHovered;
            InputManager.OnUnitHoverExited += HandleUnitHoverExited;
        }

        private void OnDestroy()
        {
            Unit.OnHealthChanged           -= HandleHealthChanged;
            TurnManager.OnTurnStarted      -= HandleTurnStarted;
            TurnManager.OnTurnEnded        -= HandleTurnEnded;
            InputManager.OnUnitHovered     -= HandleUnitHovered;
            InputManager.OnUnitHoverExited -= HandleUnitHoverExited;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void HandleHealthChanged(Unit changedUnit)
        {
            if (changedUnit != _unit) return;
            RefreshBar();
        }

        private void HandleTurnStarted(Unit unit)
        {
            if (unit != _unit) return;
            _isActiveTurn = true;
            EvaluateVisibility();
        }

        private void HandleTurnEnded(Unit unit)
        {
            if (unit != _unit) return;
            _isActiveTurn = false;
            EvaluateVisibility();
        }

        private void HandleUnitHovered(Unit unit)
        {
            // Dead units (mid-downed-sequence or bodies) don't show a health bar on hover.
            if (unit != _unit || _unit.IsDead) return;
            _isHovered = true;
            EvaluateVisibility();
        }

        private void HandleUnitHoverExited(Unit unit)
        {
            if (unit != _unit) return;
            _isHovered = false;
            EvaluateVisibility();
        }

        // ── Visibility gate ───────────────────────────────────────────────────

        /// <summary>
        /// The single decision point for bar visibility. Called whenever any of the
        /// three show conditions changes. Shows at current health (no snap) when
        /// revealing due to turn/hover; hides immediately when all conditions clear.
        /// </summary>
        private void EvaluateVisibility()
        {
            bool shouldBeVisible = _isActiveTurn || _isHovered || _isDamageActive;

            if (shouldBeVisible && !healthBarRoot.activeSelf)
                ShowStatic();
            else if (!shouldBeVisible && healthBarRoot.activeSelf)
                HideImmediate();
        }

        /// <summary>
        /// Reveals the bar at the unit's current health value with no animation.
        /// Used when the bar becomes visible due to a turn start or mouse hover —
        /// no damage just occurred so the snap-to-full-then-drain is inappropriate.
        /// </summary>
        private void ShowStatic()
        {
            if (_unit.characterData == null || _unit.characterData.maxHealth <= 0) return;
            float fraction = Mathf.Clamp01((float)_unit.currentHealth / _unit.characterData.maxHealth);
            healthBarRoot.SetActive(true);
            ApplyBarValues(fraction);
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

            // Mark this health change as active so the bar stays visible through both
            // the tween and the linger period that follows. Cancel any running linger —
            // a new health change always resets the linger timer from scratch.
            _isDamageActive = true;
            CancelLinger();

            // First reveal: snap to FULL first, then tween down to the target value.
            // This ensures the player sees the bar appear at full health and drain to the
            // new value — even on a lethal first hit where targetFraction is 0. Without
            // this, a one-shot kill would snap the bar straight to zero with no visible
            // drain, and WaitForTweenComplete() would return immediately.
            //
            // Only applies when the bar was fully hidden. If it's already visible (e.g.
            // it's this unit's turn or the player is hovering them), the fill animates
            // directly from its current value — no snap needed.
            if (!healthBarRoot.activeSelf)
            {
                healthBarRoot.SetActive(true);
                ApplyBarValues(1f);

                // Health was somehow fully restored while hidden — no tween needed.
                if (Mathf.Approximately(targetFraction, 1f))
                {
                    _lingerCoroutine = StartCoroutine(LingerThenHide());
                    return;
                }
            }

            // Cancel any in-progress tween and slide from the current fill to the target.
            if (_tweenCoroutine != null)
                StopCoroutine(_tweenCoroutine);

            _tweenCoroutine = StartCoroutine(AnimateBar(targetFraction));
        }

        /// <summary>
        /// Slides fillAmount, colour, and the health number label from their current
        /// values to the target over tweenDuration seconds using SmoothStep easing.
        /// Kicks off the linger coroutine when the tween completes so the bar stays
        /// visible for lingerDuration before hiding (unless another condition keeps it up).
        /// </summary>
        private IEnumerator AnimateBar(float targetFraction, int? targetHealthOverride = null)
        {
            float startFill = healthBarImage.fillAmount;

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

            // WaitForTweenComplete() unblocks here (above), so AbilitySequencer is
            // free to continue BEFORE the linger begins. The linger is purely visual.
            _lingerCoroutine = StartCoroutine(LingerThenHide());
        }

        /// <summary>
        /// Holds the bar visible for lingerDuration after a health-change tween completes,
        /// then clears the damage-active flag and re-evaluates whether the bar should hide.
        /// If another condition (active turn, hover) is still true the bar stays up.
        /// </summary>
        private IEnumerator LingerThenHide()
        {
            yield return new WaitForSeconds(lingerDuration);
            _isDamageActive  = false;
            _lingerCoroutine = null;
            EvaluateVisibility();
        }

        private void CancelLinger()
        {
            if (_lingerCoroutine == null) return;
            StopCoroutine(_lingerCoroutine);
            _lingerCoroutine = null;
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
        /// Note: this does NOT wait for the linger — that is intentional. The
        /// sequencer only needs the tween to complete; the linger is purely visual.
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
        /// Participates in the linger system so the bar hides cleanly after the
        /// presentation completes.
        /// </summary>
        public void TweenToFraction(float targetFraction, int? displayHealthOverride = null)
        {
            if (healthBarRoot == null || healthBarImage == null) return;

            _isDamageActive = true;
            CancelLinger();

            if (!healthBarRoot.activeSelf)
                healthBarRoot.SetActive(true);

            if (_tweenCoroutine != null)
                StopCoroutine(_tweenCoroutine);

            _tweenCoroutine = StartCoroutine(AnimateBar(targetFraction, displayHealthOverride));
        }

        /// <summary>
        /// Instantly hides the health bar root with no animation. Called by
        /// UnitDownedSequencer after the hurt animation and tween have both completed
        /// on a unit that died, immediately before the death animation plays.
        /// Overrides all visibility conditions — dead units never show a health bar.
        /// </summary>
        public void HideImmediate()
        {
            if (_tweenCoroutine != null)
            {
                StopCoroutine(_tweenCoroutine);
                _tweenCoroutine = null;
            }

            CancelLinger();
            _isDamageActive = false;
            healthBarRoot.SetActive(false);
        }
    }
}