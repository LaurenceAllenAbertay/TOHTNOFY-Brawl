using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Tile Effects/Damage Tile Effect")]
    public class DamageTileEffect : TileEffectData
    {
        [Header("Damage Settings")]
        [SerializeField] private float aiDangerValue = 15f;

        public override IEnumerator Apply(TileEffectContext ctx)
        {
            if (ctx == null) yield break;
            
            if (ctx.unitOnTile == null) yield break;

            int damage = Mathf.RoundToInt(ctx.effectPower);
            if (damage <= 0) yield break;
            
            var unitAnimator = ctx.unitOnTile.GetComponent<UnitAnimator>();
            var animator     = ctx.unitOnTile.GetComponent<Animator>();

            if (unitAnimator != null)
            {
                unitAnimator.PlayHurt();

                yield return new WaitForSeconds(0.1f);

                if (animator != null)
                {
                    while (animator.GetCurrentAnimatorStateInfo(0).IsName("Hurt") &&
                           animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f)
                    {
                        yield return null;
                    }
                }
            }

            ctx.unitOnTile.ReceiveDamage(damage);

            Debug.Log($"[TileEffect] {effectName} dealt {damage} damage to {ctx.unitOnTile.name} " +
                      $"on tile {ctx.tile.gridPosition}.");
        }

        public override float GetAIDangerValue() => aiDangerValue;
    }
}