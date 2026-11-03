using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// BotRealm gamebots start as a base class, like a 1.65 player (owner 2026-10-30).
    /// - The advanced class the launcher rolled for a new bot is its pre-decided class (record PlannedClassId). When the
    ///   fresh level-1 bot first enters the world it becomes that class's base class (Fighter, Acolyte, Viking, Seer,
    ///   Guardian, Naturalist...), so the launcher shows the base class.
    /// - Levels 1-4 as the base class: it trains only the lines its base class has, using its planned class's spec plan,
    ///   weapons and role, and casts what the base lines give it.
    /// - At level 5 its next trainer errand goes to a trainer of the planned class, which promotes it as GameTrainer does
    ///   a player; then it trains on. The record's class becomes the advanced one, so the launcher shows that.
    /// Hired companions are not touched: they are hired at level 5, already advanced (owner rule).
    /// </summary>
    public static class BaseClassBots165
    {
        public const int PromotionLevel = 5;
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BaseClassBots165));

        /// <summary>A fresh, never-placed level-1 gamebot record in BotRealm: keep the rolled class as the plan and
        /// start as its base class. Returns true when the record changed.</summary>
        public static bool PrepareFresh(OfflineWorldBotRecord record)
        {
            if (!Classic165Mode.IsOn || record == null || record.PlannedClassId != 0 || record.Level > 1 ||
                record.Experience != 0 || HiredCompanions165.IsHired(record.BotId))
                return false;
            ICharacterClass baseClass = ScriptMgr.FindCharacterBaseClass(record.ClassId);
            if (baseClass == null || baseClass.ID == record.ClassId)
                return false;
            record.PlannedClassId = record.ClassId;
            record.ClassId = baseClass.ID;
            record.ClassName = baseClass.Name;
            record.Dirty = true;
            return true;
        }

        /// <summary>The class whose spec plan, weapons and role the bot follows: its plan while it is a base class.</summary>
        public static eCharacterClass SpecClass(OfflineWorldBotRecord record) =>
            (eCharacterClass)(record != null && record.PlannedClassId != 0 ? record.PlannedClassId : record?.ClassId ?? 0);

        public static bool NeedsPromotion(GameBot bot) =>
            bot?.PersistentRecord is { PlannedClassId: > 0 } record && bot.CharacterClass != null &&
            bot.CharacterClass.ID != record.PlannedClassId && bot.Level >= PromotionLevel;

        /// <summary>The class trainer the bot's next errand must reach: the planned class's while it waits to be promoted.</summary>
        public static eCharacterClass TrainerClass(GameBot bot) =>
            NeedsPromotion(bot) ? (eCharacterClass)bot.PersistentRecord.PlannedClassId : (eCharacterClass)bot.CharacterClass.ID;

        /// <summary>At a trainer of its planned class: promoted the way GameTrainer.PromotePlayer promotes a player
        /// (the base lines keep their levels, the class's own lines and skills come with the new class).</summary>
        public static bool TryPromote(GameBot bot, GameTrainer trainer)
        {
            if (!NeedsPromotion(bot) || trainer == null || (int)trainer.TrainedClass != bot.PersistentRecord.PlannedClassId)
                return false;
            OfflineWorldBotRecord record = bot.PersistentRecord;
            ICharacterClass advanced = ScriptMgr.FindCharacterClass(record.PlannedClassId);
            if (advanced == null || !bot.SetCharacterClass(advanced.ID))
                return false;
            bot.ClassId = (byte)advanced.ID;
            bot.SetClassName(advanced.Name);
            bot.LoadClassSpecializations(false);
            record.ClassId = advanced.ID;
            record.ClassName = advanced.Name;
            record.PlannedClassId = 0;
            record.Dirty = true;
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            Log.Info($"BOT_PROMOTED_165 bot={bot.Name} id={bot.DatabaseID} level={bot.Level} class={advanced.Name} " +
                     $"trainer=\"{trainer.Name}\" region={trainer.CurrentRegionID} at={trainer.X},{trainer.Y}");
            foreach (GamePlayer near in trainer.GetPlayersInRadius(WorldMgr.SAY_DISTANCE))
                near.Out.SendMessage($"{trainer.GetName(0, true)} says, \"Welcome to our ranks, {bot.Name}. You are a {advanced.Name} now.\"",
                    eChatType.CT_Say, eChatLoc.CL_ChatWindow);
            return true;
        }
    }
}
