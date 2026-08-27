using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public abstract class AbilityTargeting : ScriptableObject
    {
        [Header("Traversal Settings")]
        public bool affectsThroughWalls = false;
        public bool affectsOverGaps = true;
        public bool affectsUpperLayers = false;
        public bool affectsLowerLayers = false;

        public virtual bool UsesDirectionalInput => true;

        public virtual bool UsesHoverTracking => false;

        public virtual bool ConfirmsOnTileClick => false;

        public virtual void ShowEnterPreview(AbilityContext ctx, Tile hoveredTile) { }

        public virtual void ShowHoverPreview(AbilityContext ctx, Tile hoveredTile) { }

        public virtual void OnMouseMoved(AbilityContext ctx, Tile hoveredTile) { }

        public virtual bool OnTileClicked(Tile tile, AbilityContext ctx, out AbilityContext outCtx)
        {
            outCtx = null;
            return false;
        }

        public virtual void OnCancel(AbilityContext ctx) { }

        public virtual void ResetForNewTurn() { }

        public virtual bool IsValidAimDirection(Vector2Int aimDir) => true;

        public virtual Vector2Int ResolveAimDirection(Vector3 rawDir)
        {
            if (Mathf.Abs(rawDir.x) > Mathf.Abs(rawDir.z))
                return rawDir.x > 0 ? Vector2Int.right : Vector2Int.left;
            else
                return rawDir.z > 0 ? Vector2Int.up : Vector2Int.down;
        }

        public abstract List<Tile> GetTraversal(AbilityContext ctx);
        
        public virtual List<Unit> SelectTargets(AbilityContext ctx)
        {
            var result = new List<Unit>();
            if (ctx?.ability == null || ctx.caster == null) return result;

            var tiles = GetTraversal(ctx);
            foreach (var t in tiles)
            {
                var u = t.currentUnit;
                if (u == null) continue;

                bool accepted = false;

                if (u.IsNeutral)
                {
                    if (ctx.ability.canTargetNeutral)
                        accepted = true;
                }
                else
                {
                    bool isAlly = IsAlly(ctx.caster, u);
                    if ((isAlly && ctx.ability.canHitAllies) || (!isAlly && ctx.ability.canHitEnemies))
                        accepted = true;
                }

                if (accepted)
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
            if (a == null || b == null) return false;
            return a.IsAllyOf(b);
        }
        
        protected void ShowTraversalPreview(AbilityContext ctx)
        {
            var tiles = GetTraversal(ctx);
            GridManager.Instance.ClearAllHighlights();
            foreach (var tile in tiles)
            {
                var u = tile.currentUnit;
                if (u != null)
                {
                    bool canHit;
                    if (u.IsNeutral)
                        canHit = ctx.ability.canTargetNeutral;
                    else
                    {
                        bool isAlly = IsAlly(ctx.caster, u);
                        canHit = (isAlly && ctx.ability.canHitAllies) ||
                                 (!isAlly && ctx.ability.canHitEnemies);
                    }
                    tile.Highlight(canHit ? TileHighlightType.AttackRange : TileHighlightType.Danger);
                }
                else
                {
                    tile.Highlight(TileHighlightType.Danger);
                }
            }
        }

        protected bool IsBlockedByWall(Tile fromTile, Tile toTile)
        {
            if (fromTile == null || toTile == null) return true;

            Vector3 startPos = fromTile.transform.position + Vector3.up * 0.5f;
            Vector3 endPos   = toTile.transform.position   + Vector3.up * 0.5f;
            Vector3 direction = (endPos - startPos).normalized;
            float distance = Vector3.Distance(startPos, endPos);

            int wallsLayerMask = LayerMask.GetMask("Walls");
            return Physics.Raycast(startPos, direction, out _, distance * 0.9f, wallsLayerMask);
        }
        
        protected bool IsAllowedByLayerFlags(Tile center, Tile candidate, out int dist3D)
        {
            dist3D = int.MaxValue;
            if (center == null || candidate == null) return false;
            
            bool sameLevel = GridManager.Instance.IsSameYLevel(center, candidate);

            if (!sameLevel)
            {
                bool candidateIsAbove = candidate.transform.position.y > center.transform.position.y;

                if (candidateIsAbove && !affectsUpperLayers) return false;
                if (!candidateIsAbove && !affectsLowerLayers) return false;
            }
            
            dist3D = GridManager.Instance.GetGridDistance(center, candidate, includeYLevel: true);
            return true;
        }
    }
}