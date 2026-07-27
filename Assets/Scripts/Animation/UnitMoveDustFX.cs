using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UnitMoveDustFX : MonoBehaviour
    {
        [SerializeField] private ParticleSystem burstFX;
        [SerializeField] private ParticleSystem trailFX;

        public void PlayBurst(Vector2Int direction)
        {
            if (burstFX == null) return;

            ApplyDirection(burstFX.transform, direction);
            burstFX.Play();
        }

        public void StartTrail(Vector2Int direction)
        {
            if (trailFX == null) return;

            ApplyDirection(trailFX.transform, direction);

            if (!trailFX.isPlaying)
                trailFX.Play();
        }

        public void UpdateTrailDirection(Vector2Int direction)
        {
            if (trailFX == null) return;

            ApplyDirection(trailFX.transform, direction);
        }

        public void StopTrail()
        {
            if (trailFX == null) return;

            trailFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private static void ApplyDirection(Transform fxTransform, Vector2Int direction)
        {
            if (direction == Vector2Int.zero) return;

            float yRotation = DirectionToYRotation(direction);
            fxTransform.localRotation = Quaternion.Euler(0f, yRotation, 0f);
        }

        private static float DirectionToYRotation(Vector2Int direction)
        {
            if (direction == new Vector2Int(0, -1))  return 90f;
            if (direction == new Vector2Int(1, -1))  return 45f;
            if (direction == new Vector2Int(1, 0))   return 0f;
            if (direction == new Vector2Int(1, 1))   return 315f;
            if (direction == new Vector2Int(0, 1))   return 270f;
            if (direction == new Vector2Int(-1, 1))  return 225f;
            if (direction == new Vector2Int(-1, 0))  return 180f;
            if (direction == new Vector2Int(-1, -1)) return 135f;

            return 0f;
        }
    }
}