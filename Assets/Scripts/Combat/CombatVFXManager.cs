using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Singleton responsible for instantiating all combat visual effects.
    /// AbilitySequencer, ChargeEffect, and any other system that needs to
    /// spawn cast or hit VFX should call this instead of calling Instantiate directly.
    /// Attach this component to a persistent manager GameObject in the scene.
    /// </summary>
    public class CombatVFXManager : MonoBehaviour
    {
        public static CombatVFXManager Instance { get; private set; }

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = false;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Spawns the cast effect prefab at the caster's position, applying
        /// directional mirroring and optional parenting as configured on the ability.
        /// </summary>
        public void SpawnCastEffect(AbilityContext ctx, Transform casterTransform)
        {
            if (ctx?.ability == null || casterTransform == null) return;
            var ability = ctx.ability;
            if (ability.CastEffectPrefab == null) return;

            Vector3 adjustedOffset = ability.CastEffectOffset;
            Quaternion rotation = casterTransform.rotation;

            if (ctx.aimDir == Vector2Int.left)
            {
                adjustedOffset.x = -adjustedOffset.x;
                rotation = Quaternion.Euler(0, 180, 0);
            }
            else if (ctx.aimDir == Vector2Int.up)
            {
                rotation = Quaternion.Euler(0, 90, 0);
            }
            else if (ctx.aimDir == Vector2Int.down)
            {
                rotation = Quaternion.Euler(0, 270, 0);
            }

            Vector3 spawnPos = casterTransform.position + adjustedOffset;
            var effect = Instantiate(ability.CastEffectPrefab, spawnPos, rotation);

            if (ability.ParentCastEffectToCaster)
            {
                effect.transform.SetParent(casterTransform);
                effect.transform.localPosition = adjustedOffset;
            }

            Destroy(effect, 3f);

            if (enableDebugLogging)
                Debug.Log($"[CombatVFXManager] Spawned cast effect for {ability.abilityName}");
        }

        /// <summary>
        /// Spawns the hit effect prefab at each target's position, applying
        /// optional parenting as configured on the ability.
        /// </summary>
        public void SpawnHitEffects(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx?.ability == null || targets == null) return;
            var ability = ctx.ability;
            if (ability.HitEffectPrefab == null) return;

            foreach (var target in targets)
            {
                if (target == null) continue;

                Vector3 spawnPos = target.transform.position + ability.HitEffectOffset;
                var effect = Instantiate(ability.HitEffectPrefab, spawnPos, Quaternion.identity);

                if (ability.ParentHitEffectToTarget)
                {
                    effect.transform.SetParent(target.transform);
                    effect.transform.localPosition = ability.HitEffectOffset;
                }

                Destroy(effect, 2f);
            }

            if (enableDebugLogging)
                Debug.Log($"[CombatVFXManager] Spawned hit effects on {targets.Count} target(s) for {ctx.ability.abilityName}");
        }
    }
}