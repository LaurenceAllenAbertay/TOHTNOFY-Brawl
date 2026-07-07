using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    public class StatusEffectIconDisplay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform iconContainer;
        
        [SerializeField] private GameObject iconPrefab;

        private Unit _unit;

        private readonly List<GameObject> _spawnedIcons = new List<GameObject>();

        private void Start()
        {
            _unit = GetComponentInParent<Unit>();

            if (_unit == null)
            {
                enabled = false;
                return;
            }

            if (iconContainer == null)
            {
                enabled = false;
                return;
            }

            if (iconPrefab == null)
            {
                enabled = false;
                return;
            }

            StatusEffectManager.OnStatusEffectApplied += HandleEffectApplied;
            StatusEffectManager.OnStatusEffectRemoved += HandleEffectRemoved;
            
            RebuildIcons();
        }

        private void OnDestroy()
        {
            StatusEffectManager.OnStatusEffectApplied -= HandleEffectApplied;
            StatusEffectManager.OnStatusEffectRemoved -= HandleEffectRemoved;
        }
        
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

        public void HideImmediate()
        {
            gameObject.SetActive(false);
        }

        private void RebuildIcons()
        {
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