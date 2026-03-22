using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Effects/Teleport Effect")]
public class TeleportEffect : AbilityEffect
{
    public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
    {
        if (ctx?.caster?.currentTile == null) return;

        // Get the tiles selected by the targeting system
        var targetTiles = ctx.ability.targeting.GetTraversal(ctx);

        if (targetTiles.Count > 0)
        {
            var destinationTile = targetTiles[0];

            // Double-check that the destination is valid and unoccupied
            if (destinationTile != null && !destinationTile.occupied && destinationTile.passableTerrain)
            {
                // Teleport the caster
                ctx.caster.SetCurrentTile(destinationTile);

                Debug.Log($"{ctx.caster.name} teleported to {destinationTile.name}");
            }
            else
            {
                Debug.Log($"Teleport failed - destination tile is invalid or occupied");
            }
        }
        else
        {
            Debug.Log($"Teleport failed - no valid destination found");
        }
    }
}