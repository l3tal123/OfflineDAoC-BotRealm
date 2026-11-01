namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode autoloot (owner 2026-11-01: "enable the autoloot feature for all the players and bots so the loot
    /// doesn't drop on the ground if the autoloot is enabled"). The inherited 1.65 loot rules left every coin and drop on
    /// the ground (AbstractServerRules.DropLoot) and made /autoloot a GM-only command. Now every player may turn it on
    /// with /autoloot on, every gamebot has it on by default, and a kill's reward is paid straight to its owner instead
    /// of becoming a bag.
    /// The setting belongs to the owner the loot would go to, not to the killer: a player-led party follows its
    /// leader's setting, an autonomous bot group and its members are bots, and a realm expedition already pays straight
    /// to its roster.
    /// </summary>
    public static class AutoLoot165
    {
        /// <summary>
        /// Whether this kill-reward owner takes its coin and drops straight into its bags. False leaves the bag on the
        /// ground for the owner to pick up by hand, as before. Only consulted in True 1.65 Mode; normal mode keeps the
        /// inherited unconditional autoloot.
        /// </summary>
        public static bool WantsLoot(IGameStaticItemOwner owner) => owner switch
        {
            GamePlayer player => player.Autoloot,
            // A gamebot led by a player leaves the party's loot to that player: companions never loot, and an invited
            // world bot's bags belong to the player's group (BotLootWalk165.OwnerFor ignores player-led groups too).
            GameBot bot => bot.Autoloot && !bot.IsPlayerLedGroup,
            // An autonomous bot group pays its members (Group.TryAutoPickUp* handles the split); a player-led group
            // follows its leader's setting, and the group then hands the loot to that leader.
            Group group => group.LivingLeader is GamePlayer leader ? leader.Autoloot : true,
            BattleGroup battlegroup => battlegroup.GetBGTreasurer()?.Autoloot ?? false,
            // Realm expeditions already distribute loot straight to their roster (RealmRaidLootOwner).
            RealmRaidLootOwner => true,
            _ => true,
        };

        /// <summary>Convenience form for the callers that already know the mode (DropLoot).</summary>
        public static bool WantsLootInCurrentMode(IGameStaticItemOwner owner) =>
            !Classic165Mode.IsOn || WantsLoot(owner);
    }
}
