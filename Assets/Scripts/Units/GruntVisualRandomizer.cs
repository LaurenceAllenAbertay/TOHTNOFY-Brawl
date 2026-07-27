using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [DefaultExecutionOrder(-100)]
    public class GruntVisualRandomizer : MonoBehaviour
    {
        [Header("Head Variants")]
        [SerializeField] private GameObject[] headVariants;

        [Header("Body Variants")]
        [SerializeField] private GameObject[] bodyVariants;

        [Header("Weapon Variants")]
        [SerializeField] private GameObject[] weaponVariants;

        private void Awake()
        {
            if (GruntVariantManager.Instance != null)
            {
                var combo = GruntVariantManager.Instance.GetNextCombo(
                    Length(headVariants), Length(bodyVariants), Length(weaponVariants));

                Activate(headVariants, combo.headIndex);
                Activate(bodyVariants, combo.bodyIndex);
                Activate(weaponVariants, combo.weaponIndex);
            }
            else
            {
                Debug.LogWarning($"[GruntVisualRandomizer] No GruntVariantManager found in scene on '{gameObject.name}' — " +
                                  "falling back to independent random parts. Add a GruntVariantManager to this scene " +
                                  "for non-repeating combinations.");
                PickOne(headVariants);
                PickOne(bodyVariants);
                PickOne(weaponVariants);
            }
        }

        private static int Length(GameObject[] variants) => variants?.Length ?? 0;

        private static void Activate(GameObject[] variants, int index)
        {
            if (variants == null || variants.Length == 0) return;

            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] == null) continue;
                variants[i].SetActive(i == index);
            }
        }

        private static void PickOne(GameObject[] variants)
        {
            if (variants == null || variants.Length == 0) return;

            int chosen = Random.Range(0, variants.Length);

            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] == null) continue;
                variants[i].SetActive(i == chosen);
            }
        }
    }
}