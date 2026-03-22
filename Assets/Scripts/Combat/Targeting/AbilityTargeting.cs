using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

public abstract class AbilityTargeting : ScriptableObject
{
    [Header("Traversal Settings")]
    [Tooltip("If true, the ability can pass through walls")]
    public bool affectsThroughWalls = false;

    [Tooltip("If true, the ability can pass over gaps and impassable terrain")]
    public bool affectsOverGaps = true;

    public abstract List<Tile> GetTraversal(AbilityContext ctx);

    // Resolve which units to actually hit, respecting filters / pass-through / maxTargets.
    public virtual List<Unit> SelectTargets(AbilityContext ctx)
    {
        var result = new List<Unit>();
        if (ctx?.ability == null || ctx.caster == null) return result;

        var tiles = GetTraversal(ctx);
        foreach (var t in tiles)
        {
            var u = t.currentUnit;
            if (u == null) continue;

            bool isAlly = IsAlly(ctx.caster, u);
            if ((isAlly && ctx.ability.canHitAllies) || (!isAlly && ctx.ability.canHitEnemies))
            {
                result.Add(u);

                if (result.Count >= ctx.ability.maxTargets)
                    break;

                if (!ctx.ability.passThroughUnits)
                    break;
            }
        }
        return result;
    }

    protected bool IsAlly(Unit a, Unit b)
    {
        // Simple heuristic: Players vs Enemies. Replace with a TeamId later?
        bool aIsEnemy = a is EnemyUnit;
        bool bIsEnemy = b is EnemyUnit;
        return aIsEnemy == bIsEnemy;
    }

    // NEW FUNCTION: Helper method for wall checking that all targeting types can use
    protected bool IsBlockedByWall(Tile fromTile, Tile toTile)
    {
        if (fromTile == null || toTile == null) return true;

        Vector3 startPos = fromTile.transform.position + Vector3.up * 0.5f;
        Vector3 endPos = toTile.transform.position + Vector3.up * 0.5f;
        Vector3 direction = (endPos - startPos).normalized;
        float distance = Vector3.Distance(startPos, endPos);

        int wallsLayerMask = LayerMask.GetMask("Walls");

        if (Physics.Raycast(startPos, direction, out RaycastHit hit, distance * 0.9f, wallsLayerMask))
        {
            return true; // Wall blocks the path
        }

        return false;
    }
}