using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    public class UnitHealthBarDisplay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject healthBarRoot;

        [SerializeField] private Image healthBarImage;
        
        [SerializeField] private TMP_Text healthNumberLabel;
        
        private Unit _unit;

        private const float HueGreen      = 120f / 360f; 
        private const float HueRed        =   0f / 360f; 
        private const float BarSaturation = 1.0f;
        private const float BarValue      = 0.9f;

        [Header("Colour Thresholds")]
        [SerializeField] [Range(0f, 0.5f)] private float redThreshold   = 0.15f;
        [SerializeField] [Range(0.5f, 1f)] private float greenThreshold = 0.85f;
        
        [Header("Animation")]
        [SerializeField] private float tweenDuration = 0.3f;
        
        [SerializeField] private float lingerDuration = 2f;

        private Coroutine _tweenCoroutine;
        
        private Coroutine _lingerCoroutine;
        
        private bool _isActiveTurn;
        
        private bool _isHovered;
        
        private bool _isDamageActive;

        private void Start()
        {
            _unit = GetComponentInParent<Unit>();

            if (_unit == null)
            {
                enabled = false;
                return;
            }

            if (healthBarRoot == null || healthBarImage == null)
            {
                enabled = false;
                return;
            }
            
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

        private void EvaluateVisibility()
        {
            bool shouldBeVisible = _isActiveTurn || _isHovered || _isDamageActive;

            if (shouldBeVisible && !healthBarRoot.activeSelf)
                ShowStatic();
            else if (!shouldBeVisible && healthBarRoot.activeSelf)
                HideImmediate();
        }

        private void ShowStatic()
        {
            if (_unit.characterData == null || _unit.characterData.maxHealth <= 0) return;
            float fraction = Mathf.Clamp01((float)_unit.currentHealth / _unit.characterData.maxHealth);
            healthBarRoot.SetActive(true);
            ApplyBarValues(fraction);
        }

        private void RefreshBar()
        {
            if (_unit.characterData == null || _unit.characterData.maxHealth <= 0)
                return;

            float targetFraction = Mathf.Clamp01((float)_unit.currentHealth / _unit.characterData.maxHealth);
            
            _isDamageActive = true;
            CancelLinger();
            
            if (!healthBarRoot.activeSelf)
            {
                healthBarRoot.SetActive(true);
                ApplyBarValues(1f);
                
                if (Mathf.Approximately(targetFraction, 1f))
                {
                    _lingerCoroutine = StartCoroutine(LingerThenHide());
                    return;
                }
            }
            
            if (_tweenCoroutine != null)
                StopCoroutine(_tweenCoroutine);

            _tweenCoroutine = StartCoroutine(AnimateBar(targetFraction));
        }

        private IEnumerator AnimateBar(float targetFraction, int? targetHealthOverride = null)
        {
            float startFill = healthBarImage.fillAmount;

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
            
            ApplyBarValues(targetFraction, targetHealthOverride);
            _tweenCoroutine = null;
            
            _lingerCoroutine = StartCoroutine(LingerThenHide());
        }

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
        
        private void ApplyBarValues(float fraction, int? healthOverride = null)
        {
            healthBarImage.fillAmount = fraction;
            healthBarImage.color = ColourForFraction(fraction);
            int displayHealth = healthOverride ?? _unit.currentHealth;
            SetHealthNumberText(displayHealth, _unit.characterData.maxHealth);
        }

        private Color ColourForFraction(float fraction)
        {
            float t   = Mathf.InverseLerp(redThreshold, greenThreshold, fraction);
            float hue = Mathf.Lerp(HueRed, HueGreen, t);
            return Color.HSVToRGB(hue, BarSaturation, BarValue);
        }

        private void SetHealthNumberText(int current, int max)
        {
            if (healthNumberLabel == null) return;
            healthNumberLabel.text = $"{current}/{max}";
        }

        public IEnumerator WaitForTweenComplete()
        {
            while (_tweenCoroutine != null)
                yield return null;
        }

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