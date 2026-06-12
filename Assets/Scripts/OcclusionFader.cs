using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Attach to the Main Camera.
    ///
    /// Fades SpriteRenderers that obscure the player's view using three simultaneous rules:
    ///
    ///   1. RAYCAST   — checks sprites between the camera and the active unit each frame.
    ///   2. PROXIMITY — fades sprites physically close to the camera.
    ///   3. HOVER     — checks sprites between the camera and the currently hovered tile.
    ///
    /// </summary>
    public class OcclusionFader : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────

        [Header("Fade Values")]
        [Tooltip("Alpha sprites fade TO when occluding. 0.2–0.3 gives a good ghosted look.")]
        [Range(0f, 1f)]
        [SerializeField] private float fadedAlpha = 0.25f;

        [Tooltip("Speed sprites fade OUT when they become occluders (alpha/sec).")]
        [SerializeField] private float fadeOutSpeed = 10f;

        [Tooltip("Speed sprites fade back IN when no longer occluders. " +
                 "Slightly slower than fade-out feels natural.")]
        [SerializeField] private float fadeInSpeed = 6f;

        [Header("Raycast (Camera to Active Unit)")]
        [Tooltip("How close (world units) a sprite's position must be to the camera-unit " +
                 "line to be considered an occluder. Increase if wide canopies are missed.")]
        [SerializeField] private float unitRayThreshold = 1.2f;

        [Header("Proximity (Near Camera)")]
        [Tooltip("Sprites within this world-unit distance of the camera always fade, " +
                 "regardless of the ray check. Catches near-edge trees that block tiles " +
                 "before the line-of-sight ray even starts.")]
        [SerializeField] private float proximityFadeDistance = 2.5f;

        [Header("Hover (Camera to Hovered Tile)")]
        [Tooltip("How close (world units) a sprite must be to the camera-tile line " +
                 "to be faded when that tile is hovered.")]
        [SerializeField] private float hoverRayThreshold = 1.0f;

        [Header("Occluder Discovery")]
        [Tooltip("How often (seconds) the list of Occluder-layer sprites is refreshed. " +
                 "Keeps the list up to date if trees are spawned at runtime without " +
                 "costing a FindObjectsByType every single frame.")]
        [SerializeField] private float refreshInterval = 2f;

        // ── Private state ─────────────────────────────────────────────────────────

        // All SpriteRenderers on the Occluder layer — refreshed periodically.
        private SpriteRenderer[] allOccluderRenderers = new SpriteRenderer[0];
        private float nextRefreshTime;

        // Every renderer we are currently fading → its target alpha this frame.
        private readonly Dictionary<SpriteRenderer, float> trackedSprites
            = new Dictionary<SpriteRenderer, float>();

        private Tile hoveredTile;
        private TurnManager turnManager;

        // ── Unity lifecycle ───────────────────────────────────────────────────────

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

            // Restore full opacity so nothing stays faded if the component is removed.
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
            // Periodically refresh the renderer list in case trees spawn at runtime.
            if (Time.time >= nextRefreshTime)
                RefreshOccluderList();

            var thisFrameOccluders = new HashSet<SpriteRenderer>();

            CollectLineOccluders(thisFrameOccluders);       // Idea 1
            CollectProximityOccluders(thisFrameOccluders);  // Idea 2
            CollectHoverOccluders(thisFrameOccluders);      // Idea 3

            // Register newly found occluders at their current alpha (no pop on entry).
            foreach (var sr in thisFrameOccluders)
            {
                if (!trackedSprites.ContainsKey(sr))
                    trackedSprites[sr] = sr.color.a;
            }

            // Tick every tracked sprite toward its target and evict fully-recovered ones.
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

        // ── Tile hover callbacks ──────────────────────────────────────────────────

        private void HandleTileHovered(Tile tile)     => hoveredTile = tile;
        private void HandleTileHoverExited(Tile tile)
        {
            if (hoveredTile == tile) hoveredTile = null;
        }

        // ── Occluder collection ───────────────────────────────────────────────────

        /// <summary>
        /// Idea 1: fade sprites that sit between the camera and the active unit.
        /// </summary>
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

        /// <summary>
        /// Idea 2: fade sprites physically close to the camera regardless of direction.
        /// </summary>
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

        /// <summary>
        /// Idea 3: fade sprites that sit between the camera and the hovered tile.
        /// </summary>
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

        // ── Core geometry ─────────────────────────────────────────────────────────

        /// <summary>
        /// Iterates every known Occluder-layer renderer and adds any whose world position
        /// is within <paramref name="threshold"/> world units of the line segment from
        /// <paramref name="lineStart"/> to <paramref name="lineEnd"/>, and that sits
        /// between the two endpoints (not behind either one).
        /// </summary>
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

                // Project onto the line to find the closest point.
                float t = Vector3.Dot(toSprite, lineDirNorm);

                // Ignore sprites behind the camera or past the target.
                if (t <= 0f || t >= lineLength) continue;

                // Distance from the sprite to the closest point on the line.
                Vector3 closestPoint    = lineStart + lineDirNorm * t;
                float   distToLine      = Vector3.Distance(sr.transform.position, closestPoint);

                if (distToLine <= threshold)
                    occluders.Add(sr);
            }
        }

        // ── Occluder list refresh ─────────────────────────────────────────────────

        /// <summary>
        /// Finds all SpriteRenderers in the scene that are on the "Occluder" layer.
        /// Called once at Start and then every <see cref="refreshInterval"/> seconds.
        /// </summary>
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
