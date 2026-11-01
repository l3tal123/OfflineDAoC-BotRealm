using System;
using System.Linq;
using System.IO;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode (owner 2026-10-29/30): the launcher toggle that runs the server as a true patch-1.65 game, Classic and
    /// Shrouded Isles as on 1 November 2003, with everything freeshards added switched off. Only the bot systems, the
    /// Realm Exchange and the bot-tuned bosses stay. It runs from its own database (opendaoc-165.sqlite3.db), which
    /// carries the server property classic165_mode = True; normal mode's database has it False or absent. Every 1.65
    /// rule in the code checks <see cref="IsOn"/>, so normal mode never changes.
    /// Plan: DAOC HIATUS\Claude Takeover GitHub\1.65 MODE PLAN.md.
    /// </summary>
    public static class Classic165Mode
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        public const string StateFolderName = "state-165";

        /// <summary>True when the database the server opened is a 1.65 Mode database.</summary>
        public static bool IsOn => ServerProperties.Properties.CLASSIC165_MODE;

        /// <summary>
        /// Where the per-mode state files live (bot goals, the bot and RvR world snapshots, realm event records):
        /// the server folder in normal mode, its state-165 subfolder in 1.65 Mode. The launcher-to-server request and
        /// result files stay in the server folder in both modes, because only one mode runs at a time.
        /// </summary>
        public static string StateDirectory(string serverDirectory, bool on)
        {
            if (!on)
                return serverDirectory;
            string path = Path.Combine(serverDirectory, StateFolderName);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>The XP rates in use: locked to 1x for players and bots in True 1.65 Mode (owner 2026-10-30), whatever the
        /// launcher or a GM sets; normal mode uses the server properties as before.</summary>
        public static double PlayerXpRate => IsOn ? 1.0 : ServerProperties.Properties.XP_RATE;

        /// <summary>RvR event musters and marches in 1.65 Mode: bots walk without hasteners (plan section 6), so the
        /// travel-bound time limits are half again as long. Normal mode keeps its values.</summary>
        public static long TravelTime(long normal) => IsOn ? normal * 3 / 2 : normal;
        public static double BotXpRate => IsOn ? 1.0 : ServerProperties.Properties.BOT_XP_RATE;

        // Script namespaces and types that are freeshard or custom content: their start-up hooks do nothing in True 1.65
        // Mode (owner: everything freeshards added is removed; only the bot systems and the Realm Exchange stay).
        private static readonly string[] FreeshardScriptNamespaces =
        [
            "DOL.GS.DailyQuest", "DOL.GS.WeeklyQuest", "DOL.GS.WeeklyQuests", "DOL.GS.MonthlyQuest",
        ];

        private static readonly string[] FreeshardScriptTypes =
        [
            "HelpSirLukas", "PlayTheLastSong", "PowerOfNature",                     // Atlas memorial quests
            "BountyMasterRuntime", "FactionEmissaryRuntime", "ReputationMapMarkers", // custom bounty and reputation
        ];

        public static bool IsFreeshardScript(Type type)
        {
            if (type == null)
                return false;
            string ns = type.Namespace ?? string.Empty;
            foreach (string prefix in FreeshardScriptNamespaces)
                if (ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal))
                    return true;
            for (Type t = type; t != null; t = t.DeclaringType)
            {
                if (Array.IndexOf(FreeshardScriptTypes, t.Name) >= 0 ||
                    t.Name.Contains("Sluaghbinder", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        // Freeshard cheat and convenience commands: GM-only in True 1.65 Mode (owner 2026-10-30, plan section 19). A normal
        // player account is refused with a message; a GM account ("Make Me a GM") still has them, and the real GM commands
        // are untouched. Bot-system commands that play by the rules (hiring, companion stances, /pull) stay. /spawn conjures
        // a helper out of nowhere (1.65 companions are hired at the inns), so it is off too (owner 2026-10-30: "/spawn
        // commands are still working in true 1.65 mode"). /autoloot is not on this list (owner 2026-11-01): it is an
        // ordinary player setting, and the loot it takes goes to the same owners it always did (AutoLoot165).
        private static readonly string[] GmOnlyCommands =
        [
            "&tele", "&tc", "&grind",                                           // travel and auto-play
            "&freelevel", "&level", "&vault",                                   // free levels, vault anywhere (/autoloot stays: it is a player choice, owner 2026-11-01)
            "&mobs", "&bountylocation", "&bountyrent", "&classrog", "&buff",    // monster finder, bounties, ROGs, buffs
            "&hardcore", "&xp", "&rp", "&dropmr", "&api",                       // freeshard toggles and the web API
            "&raid",                                                            // /raid 40 and /raid 80 (plan section 17)
            "&spawn",                                                           // conjured helpers (1.65: hired at the inns)
            // Not in the 1.64 client's command list or the DAoC manual (owner 2026-11-01: "if these were period accurate
            // leave them in"; /transfercorpse is in both and stays): weapon slot swap, crafting macro, chat. /sell is
            // not on this list (owner 2026-11-01): selling to a merchant is an ordinary player action and /sell all is
            // the requested quality-of-life form; the bag and range forms come with the same command.
            "&switch", "&craftmacro", "&faction",
        ];

        private static readonly Logging.Logger ClickLog = Logging.LoggerManager.Create(typeof(Classic165Mode));

        /// <summary>
        /// True 1.65 Mode diagnostics (owner 2026-10-30: what the old client sends on right-clicking the Horn of Rallying):
        /// one log line per object click request, with the id the client sent and what it resolves to here.
        /// Grep the server log for "CLICK_165".
        /// </summary>
        public static void LogClick(string kind, GamePlayer player, int sentId, int extra, GameObject resolved)
        {
            if (!IsOn || player == null)
                return;
            string what = resolved == null ? "nothing (no object with that id in the region)" :
                $"{resolved.GetType().Name} \"{resolved.Name}\" model {(resolved as GameStaticItem)?.Model ?? (resolved as GameNPC)?.Model ?? 0} " +
                $"at {resolved.X},{resolved.Y},{resolved.Z} dist {(int)player.GetDistanceTo(resolved)}";
            var horn = player.GetItemsInRadius(2500).OfType<HornOfRallyingObject165>().FirstOrDefault();
            ClickLog.Info($"CLICK_165 {kind} player={player.Name} region={player.CurrentRegionID} pos={player.X},{player.Y},{player.Z} " +
                          $"sentId={sentId} extra={extra} target={(player.TargetObject == null ? "none" : $"{player.TargetObject.Name} id {player.TargetObject.ObjectID}")} " +
                          $"resolved={what}" + (horn != null ? $" | nearest horn object id {horn.ObjectID} model {horn.Model}" : ""));
        }

        /// <summary>
        /// The model word an NPC create packet carries. The 1.127 client reads model = word &amp; 0x7FF and an NPC's classic
        /// hair colour = (word &gt;&gt; 13) &amp; 7 (bits 11-12 are another flag; 2026-10-30, from the client's NPC
        /// create handler). In True 1.65 Mode the hire preview figure and hired companions carry their chosen hair colour
        /// there; every other NPC is sent as before.
        /// </summary>
        public static bool HairInModelWord = true;   // back on: the original client is dropped (owner 2026-10-30), the new client reads it

        public static ushort WireModel(GameNPC npc)
        {
            ushort model = npc.Model;
            // (the original client lost the hire preview and drew a companion invisible with these bits; it is no longer
            // supported, owner 2026-10-30)
            if (!HairInModelWord || !IsOn || (model & 0xF800) != 0)
                return model;
            int hair = npc switch
            {
                Hire165.PreviewNpc preview => preview.HairColour,
                GameBot bot => HiredCompanions165.HairColourOf(bot),
                _ => -1,
            };
            return hair is > 0 and <= 7 ? (ushort)(model | (hair << 13)) : model;
        }

        /// <summary>True when this command is refused for a normal player account in True 1.65 Mode.</summary>
        public static bool IsGmOnlyCommand(string command) =>
            IsOn && command != null && Array.Exists(GmOnlyCommands, c => string.Equals(c, command, StringComparison.OrdinalIgnoreCase));

        public const string GmOnlyCommandMessage = "That command does not exist in True 1.65 Mode.";

        /// <summary>
        /// True when a new character of this base class and race has a class to train into at level 5: an advanced class
        /// of that base which is not disabled and allows the race. In True 1.65 Mode a base class with none is refused at
        /// creation, so nobody is stuck at level 5. Example: an Inconnu Acolyte can only become a Heretic, which is disabled.
        /// The 1.64 client's own creation screen calls that "NOT SUPPORTED" (checked in the 1.64 client, 2026-10-30).
        /// </summary>
        public static bool BaseClassHasCareer(int baseClassId, int race)
        {
            ICharacterClass baseClass = ScriptMgr.FindCharacterClass(baseClassId);
            if (baseClass == null || baseClass.HasAdvancedFromBaseClass())
                return true;                                    // not a base class: the normal checks decide
            var disabled = Util.SplitCSV(ServerProperties.Properties.DISABLED_CLASSES ?? string.Empty, true);
            foreach (eCharacterClass id in Enum.GetValues<eCharacterClass>())
            {
                if ((int) id == baseClassId || disabled.Contains(((int) id).ToString()))
                    continue;
                ICharacterClass advanced = ScriptMgr.FindCharacterClass((int) id);
                if (advanced == null || !advanced.HasAdvancedFromBaseClass() || !advanced.GetType().IsSubclassOf(baseClass.GetType()))
                    continue;
                if (advanced.EligibleRaces.Exists(r => (int) r.ID == race))
                    return true;
            }
            return false;
        }

        /// <summary>The region of each realm's traveler's inn (Avalon Marsh, West Svealand, Shannon Estuary), where the
        /// Realm Exchange stands in True 1.65 Mode (owner 2026-10-30).</summary>
        public static ushort InnRegion(eRealm realm) => realm switch
        {
            eRealm.Albion => 1,
            eRealm.Midgard => 100,
            eRealm.Hibernia => 200,
            _ => 0,
        };

        /// <summary>Where a Realm Exchange broker counts for bots: the capitals in normal mode, the inn regions in 1.65.</summary>
        public static bool IsExchangeRegion(Region region) =>
            region != null && (IsOn ? region.ID is 1 or 100 or 200 : region.IsCapitalCity);

        public static string StatePath(string fileName) =>
            Path.Combine(StateDirectory(AppContext.BaseDirectory, IsOn), fileName);

        /// <summary>Right after the server properties are read: logs the mode and reloads the per-mode settings.</summary>
        public static void OnPropertiesLoaded(string serverDirectory)
        {
            if (Log.IsInfoEnabled)
                Log.Info($"WORLD_MODE mode={(IsOn ? "1.65" : "normal")} state={StateDirectory(serverDirectory, IsOn)}");
            if (IsOn)
                AutonomousBotGoalPolicy.Initialize(StateDirectory(serverDirectory, true));
        }
    }
}
