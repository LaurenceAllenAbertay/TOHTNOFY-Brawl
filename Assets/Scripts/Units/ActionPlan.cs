using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Pure data container describing one complete AI turn.
    /// The planner builds this; the executor reads it.
    /// No scoring logic lives here — that belongs in AIPlanner.
    /// </summary>
    public class ActionPlan
    {
        // ── Movement ──────────────────────────────────────────────────────────────

        /// <summary>Tile to move to before or after the ability. Null = no movement.</summary>
        public Tile movementTarget;

        /// <summary>True when the movement should be executed as a jump.</summary>
        public bool isJump;

        // ── Ability ───────────────────────────────────────────────────────────────

        /// <summary>The ability to use this turn. Null = no ability.</summary>
        public Ability abilityToUse;

        /// <summary>Slot index in the unit's loadout (0–2). -1 if no ability.</summary>
        public int abilitySlot = -1;

        /// <summary>Aim direction for directional abilities (Line, MovementLine). Zero otherwise.</summary>
        public Vector2Int aimDirection;

        /// <summary>Target tile for SingleTargeting or TeleportEffect abilities. Null otherwise.</summary>
        public Tile targetTile;

        /// <summary>Pre-selected tile list for MultiTileSelectionTargeting abilities.</summary>
        public List<Tile> preSelectedTiles;

        // ── Ordering ──────────────────────────────────────────────────────────────

        /// <summary>
        /// When true the ability fires BEFORE movement (attack-then-retreat).
        /// Only set when the target is already in range from the current tile.
        /// </summary>
        public bool isAbilityFirst;

        // ── Debug ─────────────────────────────────────────────────────────────────

        /// <summary>Human-readable reason this plan was chosen. Written by AIPlanner for logging.</summary>
        public string debugReason;
    }
}