using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Events;

namespace DOL.GS
{
    /// <summary>
    /// BotRealm: a new character starts with 1 gold, enough to buy basic gear (owner 2026-11-01). The rule is the
    /// server property <c>starting_money</c> (10,000 copper, set in runtime/botrealm/true165-rules.json); this class pays
    /// the same 1 gold once to every character that already existed before the rule, and records a marker row per
    /// character so a restart, a restored backup or a crash never pays the same purse twice.
    /// World bots get the same purse (owner 2026-11-02): the launcher's ADD BOTS writes it into a new bot's
    /// MoneyCopper, and <see cref="PayBots"/> tops up a roster of older bots once, marked on the bot's own record,
    /// so bots are not left unable to buy their first gear.
    /// </summary>
    public static class WelcomePurse165
    {
        /// <summary>1 gold: 1 gold = 100 silver = 10,000 copper.</summary>
        public const long PurseCopper = 10_000;

        /// <summary>The custom parameter that records "this character has had the welcome purse".</summary>
        public const string MarkerKey = "OfflineWelcomePurse165";

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(WelcomePurse165));

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            if (!Classic165Mode.IsOn)
                return;
            try
            {
                int paid = Pay(GameServer.Database);
                GameEventMgr.AddHandler(DatabaseEvent.CharacterCreated, OnCharacterCreated);
                if (paid > 0)
                    Log.Info($"WELCOME_PURSE_165 paid {paid} existing character(s) {Money.GetString(PurseCopper)} each");
            }
            catch (Exception error)
            {
                Log.Error("WELCOME_PURSE_165 could not pay the welcome purse; it is retried at the next server start.", error);
            }
        }

        private static void OnCharacterCreated(DOLEvent e, object sender, EventArgs args)
        {
            if (args is not CharacterEventArgs created || created.Character == null)
                return;
            try
            {
                // A new character already starts with the purse (starting_money); recording the marker now keeps the
                // one-time sweep from paying it a second purse at the next server start.
                GameServer.Database.AddObject(
                    new DbCoreCharacterXCustomParam(created.Character.ObjectId, MarkerKey, "1"));
            }
            catch (Exception error)
            {
                Log.Error($"WELCOME_PURSE_165 could not record the welcome purse for {created.Character.Name}.", error);
            }
        }

        /// <summary>
        /// Pays the 1 gold welcome purse to one world bot: a bot below the purse is topped up to it, and the record's
        /// marker is set so a restart never pays again. Returns true when the record changed (the caller saves it).
        /// The purse is a floor, not a gift on top: a bot that already has more coin keeps it.
        /// </summary>
        public static bool PayBot(OfflineWorldBotRecord bot)
        {
            if (bot == null || bot.WelcomePursePaid)
                return false;
            if (bot.MoneyCopper < PurseCopper)
                bot.MoneyCopper = PurseCopper;
            bot.WelcomePursePaid = true;
            bot.Dirty = true;   // the object database saves only dirty records
            return true;
        }

        /// <summary>
        /// Pays the welcome purse to every unpaid world bot in a roster and returns the changed records, which the
        /// caller saves. Used on the population controller's roster read, so the bots it logs in already carry the
        /// coin and a bot added later is paid on the next roster read.
        /// </summary>
        public static List<OfflineWorldBotRecord> PayBots(IEnumerable<OfflineWorldBotRecord> bots)
        {
            var paid = new List<OfflineWorldBotRecord>();
            if (bots == null)
                return paid;
            foreach (OfflineWorldBotRecord bot in bots)
                if (PayBot(bot))
                    paid.Add(bot);
            return paid;
        }

        /// <summary>
        /// Pays the welcome purse to every character that has not been paid yet. Each character's money is saved before
        /// its marker row, so an interrupted run pays that character again rather than never; already-paid characters
        /// and new characters marked at creation are skipped. Returns how many characters were paid.
        /// </summary>
        public static int Pay(IObjectDatabase database)
        {
            if (database == null)
                return 0;

            HashSet<string> paid = database.SelectAllObjects<DbCoreCharacterXCustomParam>()
                .Where(param => param != null && MarkerKey.Equals(param.KeyName, StringComparison.OrdinalIgnoreCase))
                .Select(param => param.DOLCharactersObjectId)
                .ToHashSet(StringComparer.Ordinal);

            int count = 0;
            foreach (DbCoreCharacter character in database.SelectAllObjects<DbCoreCharacter>())
            {
                if (character == null || string.IsNullOrEmpty(character.ObjectId) || paid.Contains(character.ObjectId))
                    continue;

                long money = Money.GetMoney(character.Mithril, character.Platinum, character.Gold,
                    character.Silver, character.Copper) + PurseCopper;
                character.Mithril = Money.GetMithril(money);
                character.Platinum = Money.GetPlatinum(money);
                character.Gold = Money.GetGold(money);
                character.Silver = Money.GetSilver(money);
                character.Copper = Money.GetCopper(money);

                if (!database.SaveObject(character))
                    continue;
                if (!database.AddObject(new DbCoreCharacterXCustomParam(character.ObjectId, MarkerKey, "1")))
                    continue;
                count++;
            }

            return count;
        }
    }
}
