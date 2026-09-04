using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [RequireComponent(typeof(Collider))]
    public class Occluder : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer[] spriteRenderers;

        public Collider Collider { get; private set; }
        public SpriteRenderer[] SpriteRenderers => spriteRenderers;

        void Awake()
        {
            Collider = GetComponent<Collider>();

            if (spriteRenderers == null || spriteRenderers.Length == 0)
                spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        }
    }
}