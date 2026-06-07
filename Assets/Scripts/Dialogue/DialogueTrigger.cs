namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// All recognised dialogue trigger types.
    ///
    /// The DialogueManager subscribes internally to existing global events
    /// (UnitManager.OnUnitDied, UnitManager.OnUnitDamaged) for triggers that
    /// map cleanly to those events.
    ///
    /// For contextual moments not covered by a global event, call
    ///     DialogueManager.Trigger(DialogueTrigger.X, instigator: someUnit);
    /// at the relevant point in the codebase.
    ///
    /// Adding a new trigger:
    ///   1. Add a value here.
    ///   2. Either subscribe to an existing event in DialogueManager.SubscribeToEvents(),
    ///      or place a manual DialogueManager.Trigger() call at the relevant code site.
    ///   3. Add entries for it in the relevant CharacterDialogueData assets in the Inspector.
    /// </summary>
    public enum DialogueTrigger
    {
        // ── Ally death ────────────────────────────────────────────────────────
        /// <summary>The very first allied unit to die this combat.</summary>
        FirstAllyDowned,

        /// <summary>Every subsequent allied unit death after the first.</summary>
        SubsequentAllyDowned,

        /// <summary>The speaking unit is now the only player unit remaining.</summary>
        LastAllyAlive,

        // ── Kill confirmation ─────────────────────────────────────────────────
        /// <summary>
        /// A player unit delivers the killing blow to an enemy.
        /// Instigator = the unit that dealt the killing blow.
        /// Speaker pool excludes the instigator (type 3 — bystander reaction).
        /// </summary>
        AllyDownsEnemy,

        // ── Health thresholds ─────────────────────────────────────────────────
        /// <summary>
        /// An enemy unit's health drops to or below 50 % for the first time.
        /// Fires once per enemy per combat.
        /// </summary>
        EnemyBelowHalfHealth,
    }
}