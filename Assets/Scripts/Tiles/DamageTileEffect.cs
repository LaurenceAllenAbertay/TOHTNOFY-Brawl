using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Deals damage to any unit standing on this tile when it triggers at round end.
    /// Apply() is a coroutine: it plays the hurt animation and waits for it to finish
    /// before calling ReceiveDamage, so the visual and the HP change are in sync.
    /// If the tile is empty the effect still ticks its duration down — fire doesn't need
    /// a victim to keep burning.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Tile Effects > Damage Tile Effect
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Tile Effects/Damage Tile Effect")]
    public class DamageTileEffect : TileEffectData
    {
        [Header("Damage Settings")]
        [Tooltip("Flat danger score reported to the AI when it considers stepping on this tile. " +
                 "Should broadly match expected damage so the AI avoids appropriately.")]
        [SerializeField] private float aiDangerValue = 15f;

        public override IEnumerator Apply(TileEffectContext ctx)
        {
            if (ctx == null) yield break;

            // Tile is empty this round — fire keeps burning but hits nobody.
            if (ctx.unitOnTile == null) yield break;

            int damage = Mathf.RoundToInt(ctx.effectPower);
            if (damage <= 0) yield break;

            // ── Visual feedback ───────────────────────────────────────────────────
            // Play the hurt animation and wait for it to complete so the HP drop is
            // not silent. This mirrors how ability damage works in ExecuteTimedEffects.
            var unitAnimator = ctx.unitOnTile.GetComponent<UnitAnimator>();
            var animator     = ctx.unitOnTile.GetComponent<Animator>();

            if (unitAnimator != null)
            {
                unitAnimator.PlayHurt();

                // Brief delay to ensure the animation state has actually started before we poll it
                yield return new WaitForSeconds(0.1f);

                // Wait for the Hurt animation to complete before applying damage
                if (animator != null)
                {
                    while (animator.GetCurrentAnimatorStateInfo(0).IsName("Hurt") &&
                           animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f)
                    {
                        yield return null;
                    }
                }
            }

            // ── Gameplay change ───────────────────────────────────────────────────
            // ReceiveDamage handles shields, guards, dodge, and death notification.
            // If the unit dies here, TurnManager.HandleUnitDied fires via UnitManager.OnUnitDied
            // and removes the unit from turnOrder before StartNextTurn() is called.
            ctx.unitOnTile.ReceiveDamage(damage);

            Debug.Log($"[TileEffect] {effectName} dealt {damage} damage to {ctx.unitOnTile.name} " +
                      $"on tile {ctx.tile.gridPosition}.");
        }

        public override float GetAIDangerValue() => aiDangerValue;
    }
}