using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [DefaultExecutionOrder(-200)]
    public class GruntVariantManager : MonoBehaviour
    {
        public static GruntVariantManager Instance { get; private set; }

        public readonly struct GruntVariantCombo
        {
            public readonly int headIndex;
            public readonly int bodyIndex;
            public readonly int weaponIndex;

            public GruntVariantCombo(int headIndex, int bodyIndex, int weaponIndex)
            {
                this.headIndex   = headIndex;
                this.bodyIndex   = bodyIndex;
                this.weaponIndex = weaponIndex;
            }
        }

        private readonly List<GruntVariantCombo> bag = new List<GruntVariantCombo>();

        private int headCount;
        private int bodyCount;
        private int weaponCount;
        private bool isInitialised;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public GruntVariantCombo GetNextCombo(int headVariantCount, int bodyVariantCount, int weaponVariantCount)
        {
            EnsureInitialised(headVariantCount, bodyVariantCount, weaponVariantCount);

            if (bag.Count == 0)
                RefillAndShuffle();

            if (bag.Count == 0)
                return new GruntVariantCombo(0, 0, 0);

            int lastIndex = bag.Count - 1;
            GruntVariantCombo combo = bag[lastIndex];
            bag.RemoveAt(lastIndex);
            return combo;
        }

        private void EnsureInitialised(int headVariantCount, int bodyVariantCount, int weaponVariantCount)
        {
            if (isInitialised &&
                headCount == headVariantCount &&
                bodyCount == bodyVariantCount &&
                weaponCount == weaponVariantCount)
                return;

            headCount   = Mathf.Max(0, headVariantCount);
            bodyCount   = Mathf.Max(0, bodyVariantCount);
            weaponCount = Mathf.Max(0, weaponVariantCount);
            isInitialised = true;

            bag.Clear();
        }

        private void RefillAndShuffle()
        {
            bag.Clear();

            if (headCount == 0 || bodyCount == 0 || weaponCount == 0)
                return;

            for (int h = 0; h < headCount; h++)
                for (int b = 0; b < bodyCount; b++)
                    for (int w = 0; w < weaponCount; w++)
                        bag.Add(new GruntVariantCombo(h, b, w));

            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
        }
    }
}