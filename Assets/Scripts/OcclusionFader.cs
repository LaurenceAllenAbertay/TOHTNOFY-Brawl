using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class OcclusionFader : MonoBehaviour
    {
        [Header("Fade Values")]
        [Range(0f, 1f)]
        [SerializeField] private float fadedAlpha = 0.25f;

        [SerializeField] private float fadeOutSpeed = 10f;

        [SerializeField] private float fadeInSpeed = 6f;

        [Header("Occlusion")]
        [SerializeField] private LayerMask occluderMask;
        [SerializeField] private float targetHeightOffset = 0.5f;

        [Header("Camera Proximity Fade")]
        [SerializeField] private float proximityFadeStartDistance = 3f;
        [SerializeField] private float proximityFadeEndDistance = 1f;

        private Camera cam;
        private TurnManager turnManager;
        private AbilityTargetingController targetingController;

        private readonly Dictionary<SpriteRenderer, float> trackedSprites
            = new Dictionary<SpriteRenderer, float>();

        private readonly List<RaycastHit> hitBuffer = new List<RaycastHit>();
        private readonly Dictionary<SpriteRenderer, float> thisFrameOccluders = new Dictionary<SpriteRenderer, float>();

        private Tile hoveredTile;

        void Start()
        {
            cam = GetComponent<Camera>();
            turnManager = FindAnyObjectByType<TurnManager>();
            targetingController = FindAnyObjectByType<AbilityTargetingController>();

            Tile.OnTileHovered += HandleTileHovered;
            Tile.OnTileHoverExited += HandleTileHoverExited;
        }

        void OnDestroy()
        {
            Tile.OnTileHovered -= HandleTileHovered;
            Tile.OnTileHoverExited -= HandleTileHoverExited;

            foreach (var kvp in trackedSprites)
            {
                if (kvp.Key == null) continue;
                Color c = kvp.Key.color;
                c.a = 1f;
                kvp.Key.color = c;
            }
        }

        void LateUpdate()
        {
            thisFrameOccluders.Clear();

            CollectOccludersFor(GetActiveUnitTargetPoint());

            if (ShouldOccludeForHover())
                CollectOccludersFor(hoveredTile.transform.position + Vector3.up * targetHeightOffset);

            ApplyFade();
        }

        private void HandleTileHovered(Tile tile) => hoveredTile = tile;
        private void HandleTileHoverExited(Tile tile)
        {
            if (hoveredTile == tile) hoveredTile = null;
        }

        private bool ShouldOccludeForHover()
        {
            if (hoveredTile == null) return false;
            if (targetingController == null) return true;
            return targetingController.IsTargetingAbility;
        }

        private Vector3? GetActiveUnitTargetPoint()
        {
            Unit activeUnit = turnManager?.CurrentUnit;
            if (activeUnit == null || activeUnit.currentTile == null) return null;

            return activeUnit.currentTile.transform.position + Vector3.up * targetHeightOffset;
        }

        private void CollectOccludersFor(Vector3? maybeTarget)
        {
            if (maybeTarget == null || cam == null) return;

            Vector3 target = maybeTarget.Value;
            Vector3 origin = cam.transform.position;
            Vector3 toTarget = target - origin;
            float distance = toTarget.magnitude;

            if (distance < 0.001f) return;

            Ray ray = new Ray(origin, toTarget / distance);

            hitBuffer.Clear();
            var hits = Physics.RaycastAll(ray, distance, occluderMask, QueryTriggerInteraction.Collide);
            hitBuffer.AddRange(hits);

            foreach (var hit in hitBuffer)
            {
                var occluder = hit.collider.GetComponentInParent<Occluder>();
                if (occluder == null) continue;

                var sprites = occluder.SpriteRenderers;
                for (int i = 0; i < sprites.Length; i++)
                {
                    if (sprites[i] == null) continue;

                    if (!thisFrameOccluders.TryGetValue(sprites[i], out float existingDistance)
                        || hit.distance < existingDistance)
                    {
                        thisFrameOccluders[sprites[i]] = hit.distance;
                    }
                }
            }
        }

        private void ApplyFade()
        {
            foreach (var kvp in thisFrameOccluders)
            {
                if (!trackedSprites.ContainsKey(kvp.Key))
                    trackedSprites[kvp.Key] = kvp.Key.color.a;
            }

            List<SpriteRenderer> toRemove = null;
            foreach (var kvp in trackedSprites)
            {
                SpriteRenderer sr = kvp.Key;
                if (sr == null)
                {
                    (toRemove ??= new List<SpriteRenderer>()).Add(sr);
                    continue;
                }

                bool isOccluder = thisFrameOccluders.TryGetValue(sr, out float camDistance);
                float target = isOccluder ? GetTargetAlpha(camDistance) : 1f;
                float speed = (target < sr.color.a) ? fadeOutSpeed : fadeInSpeed;

                Color c = sr.color;
                c.a = Mathf.MoveTowards(c.a, target, speed * Time.deltaTime);
                sr.color = c;

                if (!isOccluder && Mathf.Approximately(c.a, 1f))
                    (toRemove ??= new List<SpriteRenderer>()).Add(sr);
            }

            if (toRemove != null)
            {
                foreach (var sr in toRemove)
                    trackedSprites.Remove(sr);
            }
        }

        private float GetTargetAlpha(float camDistance)
        {
            float proximityT = Mathf.InverseLerp(
                proximityFadeEndDistance, proximityFadeStartDistance, camDistance);

            return fadedAlpha * Mathf.Clamp01(proximityT);
        }
    }
}