using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;

namespace DOL.GS
{
    /// <summary>
    /// The Horn Handler at each BotRealm traveler's inn (owner 2026-10-30, plan section 18): the Horn of Rallying.
    /// PvE realm events (the dragon, the epic dungeon, Summoner's Hall, Darkness Falls expeditions) never start on their
    /// own; a level-50 player who has turned in the horn's courier quest starts one here, in the horn
    /// window (both clients, ops 20-23 of the hire window's packet), opened by right-clicking the horn. Relic raids and
    /// keep sieges are not on the horn: they keep happening by themselves and also start when a player attacks a keep.
    /// </summary>
    public class HornHandler165 : GameNPC
    {
        public const int RequiredLevel = 50;

        public override bool AddToWorld()
        {
            if (!base.AddToWorld())
                return false;
            InnQuestMarkers165.Register(this);
            return true;
        }

        public override bool RemoveFromWorld()
        {
            InnQuestMarkers165.Unregister(this);
            return base.RemoveFromWorld();
        }

        /// <summary>The turn-in icon while the player's "The Horn of Rallying" quest is open (InnQuestMarkers165).</summary>
        public override eQuestIndicator GetQuestIndicator(GamePlayer player) =>
            InnQuestMarkers165.TurnsInFor(this, player) ? eQuestIndicator.Finish : base.GetQuestIndicator(player);

        /// <summary>Right-click: he talks and turns in the horn's courier quest; the horn window opens from the horn
        /// itself (owner 2026-10-30: his chat and the horn box separate).</summary>
        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            TurnTo(player, 5000);
            if (!Classic165Mode.IsOn)
                return true;
            if (player.Level < RequiredLevel)
            {
                SayTo(player, eChatLoc.CL_PopupWindow,
                    "This is the Horn of Rallying. When it sounds, the realm's champions gather for the great hunts. " +
                    $"Come back when you are level {RequiredLevel}, and the horn may be yours to sound.");
                return true;
            }
            if (player.IsDoingQuest(typeof(HornOfRallying165)) is HornOfRallying165 quest)
            {
                SayTo(player, eChatLoc.CL_PopupWindow,
                    "So the courier found you. Few are trusted with the Horn of Rallying, but your name is known across the realm. " +
                    "When you sound it, our champions gather at the muster point for the great hunt you choose, and you may lead them. " +
                    "Right-click the horn whenever you would sound it.");
                quest.FinishQuest();
                InnQuestMarkers165.TurnedIn(this, player);
            }
            else if (!MaySound(player))
                SayTo(player, eChatLoc.CL_PopupWindow,
                    "Only those the realm has sent for may sound the Horn of Rallying. A courier will find you when your name is called.");
            else
                SayTo(player, eChatLoc.CL_PopupWindow,
                    "The Horn of Rallying stands ready. Right-click the horn whenever you would sound it.");
            // his talk and the horn window are separate (owner 2026-10-30): the window opens from the horn itself
            return true;
        }

        /// <summary>Level 50 and the horn's courier quest turned in (GMs may always).</summary>
        public static bool MaySound(GamePlayer player) =>
            player.Client.Account.PrivLevel > (uint)ePrivLevel.Player ||
            player.Level >= RequiredLevel && player.HasFinishedQuest(typeof(HornOfRallying165)) > 0;
    }

    /// <summary>The Horn of Rallying itself, on its platform at each inn (a server object over the client's horn
    /// platform scenery): right-click it to open the horn window.</summary>
    public class HornOfRallyingObject165 : GameStaticItem
    {
        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player) || !Classic165Mode.IsOn)
                return false;
            if (HornHandler165.MaySound(player))
            {
                HornWindow165.Open(player);
                return true;
            }
            string handler = GetNPCsInRadius(1500).OfType<HornHandler165>().FirstOrDefault()?.Name ?? "the Horn Handler";
            player.Out.SendMessage(player.Level < HornHandler165.RequiredLevel
                    ? $"The Horn of Rallying. Only champions of level {HornHandler165.RequiredLevel} may sound it."
                    : $"The Horn of Rallying. Speak with {handler} before you sound it.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return true;
        }
    }

    /// <summary>The horn window (plan section 18, ops 20-23 of the DebugMode 'H' packet; same layout for both clients).</summary>
    public static class HornWindow165
    {
        private static readonly HashSet<GamePlayer> Open_ = [];

        /// <summary>The PvE events of the player's realm (dragon, epic dungeon, Summoner's Hall, Darkness Falls).</summary>
        public static AutonomousRealmRaid.Definition[] Events(GamePlayer player) =>
            AutonomousRealmRaid.Definitions.Where(d => d.Realm == player.Realm).ToArray();

        /// <summary>The player has a Horn Handler of their realm close by.</summary>
        public static bool AtHorn(GamePlayer player) =>
            player.Client.Account.PrivLevel > (uint)ePrivLevel.Player ||
            player.GetNPCsInRadius(WorldMgr.INTERACT_DISTANCE * 4).OfType<HornHandler165>().Any(h => h.Realm == player.Realm) ||
            player.GetItemsInRadius((ushort)(WorldMgr.INTERACT_DISTANCE * 2)).OfType<HornOfRallyingObject165>().Any();

        public static void Open(GamePlayer player)
        {
            lock (Open_)
                Open_.Add(player);
            SendList(player);
        }

        public static void Close(GamePlayer player)
        {
            lock (Open_)
                Open_.Remove(player);
            Hire165.Send(player, 22, []);
        }

        public static void SendList(GamePlayer player)
        {
            AutonomousRealmRaid.Definition[] events = Events(player);
            AutonomousRealmRaid.Summary[] all = AutonomousRealmRaid.Snapshot();
            var open = new byte[50];
            open[0] = (byte)Math.Min(events.Length, 12);
            open[1] = (byte)player.Realm;
            Put(open, 2, "THE HORN OF RALLYING", 48);
            Hire165.Send(player, 20, open);
            for (int i = 0; i < Math.Min(events.Length, 12); i++)
            {
                AutonomousRealmRaid.Summary s = all.FirstOrDefault(x => x.Id == events[i].Id);
                string state = s?.State ?? "Encounter unavailable";
                byte code = state switch
                {
                    "Available" => 0,
                    "Respawning" => 2,
                    "Event cooldown" => 3,
                    "Encounter unavailable" => 4,
                    _ => 1,   // a raid is assembling or fighting
                };
                // Ready only when the horn could really start it: the 300 free level-50 champions a forced raid reserves
                int free = code == 0 ? AutonomousRealmRaid.ForcedRecruitsAvailable(events[i].Id, events[i].Realm) : 0;
                if (code == 0 && free < RealmRaidRecruitmentPolicy.MaximumBots)
                    code = 4;
                uint seconds = code is 2 or 3 ? (uint)Math.Max(0, (s?.Remaining ?? 0) / 1000) : 0;
                var row = new byte[122];
                row[0] = (byte)i;
                row[1] = code;
                BitConverter.GetBytes(seconds).CopyTo(row, 2);
                BitConverter.GetBytes((ushort)Math.Clamp(s?.Assigned ?? 0, 0, ushort.MaxValue)).CopyTo(row, 6);
                BitConverter.GetBytes((ushort)Math.Clamp(events[i].SuggestedBots, 0, ushort.MaxValue)).CopyTo(row, 8);
                Put(row, 10, events[i].Name, 48);
                // the numbers as text too: the original client can show only this line (both clients the same)
                string status = code switch
                {
                    0 => $"Ready - {free} free champions (300 needed)",
                    4 when s?.State == "Available" => $"Not enough free champions: {free} of {RealmRaidRecruitmentPolicy.MaximumBots}",
                    2 => $"Respawning: {Duration(seconds)}",
                    3 => $"Cooling down: {Duration(seconds)}",
                    4 => "Not possible now",
                    _ => $"{state.Replace(" — ", " - ")} - {s?.Present ?? 0} of {events[i].SuggestedBots} bots",
                };
                Put(row, 58, status, 64);
                Hire165.Send(player, 21, row);
            }
        }

        private static string Duration(uint seconds)
        {
            uint minutes = Math.Max(1, (seconds + 59) / 60);
            return minutes >= 60 ? $"{minutes / 60} h {minutes % 60} min" : $"{minutes} min";
        }

        public static void Message(GamePlayer player, string text)
        {
            var body = new byte[122];
            Put(body, 0, text, 122);
            Hire165.Send(player, 23, body);
        }

        /// <summary>Sounds the horn for the player's index-th event; returns the line to show.</summary>
        public static string Sound(GamePlayer player, int index)
        {
            if (!HornHandler165.MaySound(player))
                return "Only those the realm has sent for may sound the Horn of Rallying.";
            if (!AtHorn(player))
                return "You must stand at the Horn of Rallying to sound it.";
            AutonomousRealmRaid.Definition[] events = Events(player);
            if (index < 0 || index >= events.Length)
                return "Choose a great hunt first.";
            AutonomousRealmRaid.Definition chosen = events[index];
            bool started = AutonomousRealmRaid.Start(chosen.Id, player.Realm, out string reason, forced: true);
            if (!started)
                return $"The horn cannot be sounded for {chosen.Name} now: {reason}";
            foreach (GamePlayer near in player.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                near.Out.SendMessage($"{player.Name} sounds the Horn of Rallying: the realm gathers for {chosen.Name}!",
                    eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            return $"The horn sounds across the land! The realm rallies for {chosen.Name}.";
        }

        private static void Put(byte[] buffer, int at, string text, int max)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(text ?? string.Empty);
            Array.Copy(bytes, 0, buffer, at, Math.Min(bytes.Length, max - 1));
        }
    }
}

namespace DOL.GS.Commands
{
    /// <summary>The horn window's buttons (BotRealm): /horn list | sound &lt;index&gt; | close.</summary>
    [CmdAttribute("&horn", ePrivLevel.Player, "The Horn of Rallying window", "/horn list|sound <index>|close")]
    public sealed class HornCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null || !Classic165Mode.IsOn)
                return;
            string verb = args.Length > 1 ? args[1].ToLowerInvariant() : "list";
            switch (verb)
            {
                case "close":
                    HornWindow165.Close(player);
                    return;
                case "sound":
                    int index = args.Length > 2 && int.TryParse(args[2], out int i) ? i : -1;
                    HornWindow165.Message(player, HornWindow165.Sound(player, index));
                    HornWindow165.SendList(player);
                    return;
                default:
                    if (!HornHandler165.MaySound(player) || !HornWindow165.AtHorn(player))
                    {
                        HornWindow165.Message(player, "Speak with the Horn Handler at the traveler's inn.");
                        return;
                    }
                    HornWindow165.SendList(player);
                    return;
            }
        }
    }
}
