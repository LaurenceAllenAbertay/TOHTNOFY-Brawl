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

        [Header("Raycast (Camera to Active Unit)")]
        [SerializeField] private float unitRayThreshold = 1.2f;

        [Header("Proximity (Near Camera)")]
        [SerializeField] private float proximityFadeDistance = 2.5f;

        [Header("Hover (Camera to Hovered Tile)")]
        [SerializeField] private float hoverRayThreshold = 1.0f;

        [Header("Occluder Discovery")]
        [SerializeField] private float refreshInterval = 2f;

        private SpriteRenderer[] allOccluderRenderers = new SpriteRenderer[0];
        private float nextRefreshTime;

        private readonly Dictionary<SpriteRenderer, float> trackedSprites
            = new Dictionary<SpriteRenderer, float>();

        private Tile hoveredTile;
        private TurnManager turnManager;

        void Start()
        {
            turnManager = FindAnyObjectByType<TurnManager>();

            Tile.OnTileHovered     += HandleTileHovered;
            Tile.OnTileHoverExited += HandleTileHoverExited;

            RefreshOccluderList();
        }

        void OnDestroy()
        {
            Tile.OnTileHovered     -= HandleTileHovered;
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
            if (Time.time >= nextRefreshTime)
                RefreshOccluderList();

            var thisFrameOccluders = new HashSet<SpriteRenderer>();

            CollectLineOccluders(thisFrameOccluders);  
            CollectProximityOccluders(thisFrameOccluders); 
            CollectHoverOccluders(thisFrameOccluders);     

            foreach (var sr in thisFrameOccluders)
            {
                if (!trackedSprites.ContainsKey(sr))
                    trackedSprites[sr] = sr.color.a;
            }

            var toRemove = new List<SpriteRenderer>();
            foreach (var kvp in trackedSprites)
            {
                SpriteRenderer sr = kvp.Key;
                if (sr == null) { toRemove.Add(sr); continue; }

                bool  isOccluder = thisFrameOccluders.Contains(sr);
                float target     = isOccluder ? fadedAlpha : 1f;
                float speed      = (target < sr.color.a) ? fadeOutSpeed : fadeInSpeed;

                Color c = sr.color;
                c.a = Mathf.MoveTowards(c.a, target, speed * Time.deltaTime);
                sr.color = c;

                if (!isOccluder && Mathf.Approximately(c.a, 1f))
                    toRemove.Add(sr);
            }

            foreach (var sr in toRemove)
                trackedSprites.Remove(sr);
        }

        private void HandleTileHovered(Tile tile)     => hoveredTile = tile;
        private void HandleTileHoverExited(Tile tile)
        {
            if (hoveredTile == tile) hoveredTile = null;
        }

        private void CollectLineOccluders(HashSet<SpriteRenderer> occluders)
        {
            Unit activeUnit = turnManager?.CurrentUnit;
            if (activeUnit == null) return;

            CollectAlongLine(
                transform.position,
                activeUnit.transform.position,
                unitRayThreshold,
                excludeTransform: activeUnit.transform,
                occluders);
        }

        private void CollectProximityOccluders(HashSet<SpriteRenderer> occluders)
        {
            Vector3 camPos = transform.position;
            foreach (var sr in allOccluderRenderers)
            {
                if (sr == null) continue;
                if (Vector3.Distance(camPos, sr.transform.position) <= proximityFadeDistance)
                    occluders.Add(sr);
            }
        }

        private void CollectHoverOccluders(HashSet<SpriteRenderer> occluders)
        {
            if (hoveredTile == null) return;

            CollectAlongLine(
                transform.position,
                hoveredTile.transform.position,
                hoverRayThreshold,
                excludeTransform: null,
                occluders);
        }
        
        private void CollectAlongLine(
            Vector3 lineStart,
            Vector3 lineEnd,
            float   threshold,
            Transform excludeTransform,
            HashSet<SpriteRenderer> occluders)
        {
            Vector3 lineDir    = lineEnd - lineStart;
            float   lineLength = lineDir.magnitude;

            if (lineLength < 0.001f) return;

            Vector3 lineDirNorm = lineDir / lineLength;

            foreach (var sr in allOccluderRenderers)
            {
                if (sr == null) continue;
                if (excludeTransform != null && sr.transform == excludeTransform) continue;

                Vector3 toSprite = sr.transform.position - lineStart;
                
                float t = Vector3.Dot(toSprite, lineDirNorm);
                
                if (t <= 0f || t >= lineLength) continue;
                
                Vector3 closestPoint    = lineStart + lineDirNorm * t;
                float   distToLine      = Vector3.Distance(sr.transform.position, closestPoint);

                if (distToLine <= threshold)
                    occluders.Add(sr);
            }
        }

        private void RefreshOccluderList()
        {
            nextRefreshTime = Time.time + refreshInterval;

            int occluderLayer = LayerMask.NameToLayer("Occluder");
            var all           = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            var filtered      = new List<SpriteRenderer>();

            foreach (var sr in all)
            {
                if (sr.gameObject.layer == occluderLayer)
                    filtered.Add(sr);
            }

            allOccluderRenderers = filtered.ToArray();
        }
    }
}
