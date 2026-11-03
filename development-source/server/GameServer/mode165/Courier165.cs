using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;

namespace DOL.GS
{
    /// <summary>
    /// The BotRealm couriers (owner 2026-10-30; the old Rebuilt design's level-5 courier, plus the horn's level-50
    /// messenger of plan section 18). On reaching level 5 (or logging in at 5+ without it), a realm-themed courier comes
    /// to the player, gives its message, and the quest goes straight into the quest log; then it leaves. The level-5
    /// quest is turned in at the traveler's inn's innkeeper, the level-50 one at its Horn Handler, which earns the right
    /// to sound the Horn of Rallying.
    /// The courier is only for the look: only its player sees it, it can't be targeted or fought, and it leaves on a
    /// timer whatever happens. The quest is in the log before it appears.
    /// </summary>
    public static class Courier165
    {
        public const int InnLevel = 5;
        public const int HornLevel = 50;

        public sealed record Inn(string Town, string Zone, string Innkeeper, string HornHandler, string Courier, ushort Model,
            string Arrives);

        /// <summary>Each realm's inn (add-inns165.json) and courier (the old design's inns.json).</summary>
        public static readonly Dictionary<eRealm, Inn> Inns = new()
        {
            [eRealm.Albion] = new("Caer Witrin", "Avalon Marsh", "Gwenllian", "Wystan", "a glowing sphere", 912,
                "A glowing sphere drifts up to you and glows softly."),
            [eRealm.Midgard] = new("Huginfell", "West Svealand", "Halvar", "Hallveig", "a messenger wolf", 47,
                "A messenger wolf trots up to you with a message."),
            [eRealm.Hibernia] = new("Connla", "Shannon Estuary", "Muireann", "Cormac", "a messenger fairy", 631,
                "A messenger fairy flutters up to you."),
        };

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnCheck));
            GameEventMgr.AddHandler(GamePlayerEvent.LevelUp, new DOLEventHandler(OnCheck));
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnCheck));
            GameEventMgr.RemoveHandler(GamePlayerEvent.LevelUp, new DOLEventHandler(OnCheck));
        }

        private static void OnCheck(DOLEvent e, object sender, EventArgs args)
        {
            if (!Classic165Mode.IsOn || sender is not GamePlayer player || !Inns.TryGetValue(player.Realm, out Inn inn))
                return;
            // A little after login or the level-up message, so it doesn't arrive mid-loading.
            new ECSGameTimer(player, _ =>
            {
                if (player.ObjectState != GameObject.eObjectState.Active)
                    return 0;
                RemoveStrays(player);
                if (player.Level >= HornLevel && TryGive(player, () => new HornOfRallying165(player, 1)))
                    Send(player, inn, $"\"Champion of {GlobalConstants.RealmToName(player.Realm)}! {inn.HornHandler} keeps the Horn of Rallying " +
                                      $"at the traveler's inn near {inn.Town}. When it sounds, the realm gathers for the great hunts, and it is yours " +
                                      $"to sound now. Seek {inn.HornHandler} out.\"");
                else if (player.Level >= InnLevel && HiredCompanions165.RosterOf(player).Count == 0 &&
                         TryGive(player, () => new RoadsideInn165(player, 1)))
                    Send(player, inn, $"\"Adventurer! {inn.Innkeeper}, the innkeeper of the traveler's inn near {inn.Town}, asks for you. " +
                                      $"Travellers gather there, companions who would gladly fight at your side. Seek {inn.Innkeeper} out.\"");
                return 0;
            }).Start(e == GamePlayerEvent.GameEntered ? 8000 : 3000);
        }

        /// <summary>Gives the quest unless the player has it or has finished it. The quest is only made once it is given:
        /// making one saves it at once, which left a fresh copy at every login that came back as active (owner 2026-10-30:
        /// "I get the breadcrumb quests every time I log in ... even after I've done them").</summary>
        private static bool TryGive<T>(GamePlayer player, Func<T> make) where T : AbstractQuest
        {
            if (player.HasFinishedQuest(typeof(T)) > 0 || player.IsDoingQuest(typeof(T)) != null)
                return false;
            return player.AddQuest(make());
        }

        /// <summary>Removes the copies those logins left: an active courier quest the player has already finished.</summary>
        private static void RemoveStrays(GamePlayer player)
        {
            foreach (Type type in new[] { typeof(RoadsideInn165), typeof(HornOfRallying165) })
            {
                if (player.HasFinishedQuest(type) == 0)
                    continue;
                foreach (AbstractQuest stray in player.QuestList.Keys.Where(q => q.GetType() == type).ToList())
                {
                    if (player.QuestList.TryRemove(stray, out byte index))
                    {
                        player.AvailableQuestIndexes.Enqueue(index);
                        player.Out.SendQuestRemove(index);
                    }
                    stray.DeleteFromDatabase();
                }
            }
        }

        private static void Send(GamePlayer player, Inn inn, string words)
        {
            var courier = new CourierNpc(player, inn);
            if (!courier.AddToWorld())
                return;
            player.Out.SendMessage(inn.Arrives, eChatType.CT_Emote, eChatLoc.CL_SystemWindow);
            new ECSGameTimer(courier, _ =>
            {
                if (player.ObjectState == GameObject.eObjectState.Active)
                    player.Out.SendMessage($"{Capital(inn.Courier)} says, {words}", eChatType.CT_Say, eChatLoc.CL_ChatWindow);
                return 0;
            }).Start(1500);
            new ECSGameTimer(courier, _ =>
            {
                courier.Delete();
                return 0;
            }).Start(9000);
        }

        private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        /// <summary>The courier creature: only its player sees it, nothing can target or fight it.</summary>
        private sealed class CourierNpc : GameNPC
        {
            private readonly GamePlayer _for;

            public CourierNpc(GamePlayer player, Inn inn) : base()
            {
                _for = player;
                Name = inn.Courier;
                Model = inn.Model;
                Level = 1;
                Realm = player.Realm;
                Flags = eFlags.PEACE | eFlags.CANTTARGET;
                if (inn.Model == 631 || inn.Model == 912)
                    Flags |= eFlags.FLYING;
                CurrentRegionID = player.CurrentRegionID;
                double angle = (player.Heading + 2048) * Math.PI * 2 / 4096;   // just in front of the player
                X = player.X + (int)(-Math.Sin(angle) * -90);
                Y = player.Y + (int)(Math.Cos(angle) * -90);
                Z = player.Z;
                Heading = (ushort)((player.Heading + 2048) % 4096);            // facing the player
                SetOwnBrain(new BlankBrain());
            }

            public override bool IsVisibleTo(GameObject checkObject) => checkObject == _for && base.IsVisibleTo(checkObject);
        }
    }
}

namespace DOL.GS.Quests
{
    /// <summary>BotRealm, level 5: the courier's word about the traveler's inn; turned in at its innkeeper.</summary>
    public class RoadsideInn165 : BaseQuest
    {
        public RoadsideInn165() : base() { }
        public RoadsideInn165(GamePlayer player) : this(player, 1) { }
        public RoadsideInn165(GamePlayer player, int step) : base(player, step) { }
        public RoadsideInn165(GamePlayer player, DbQuest dbQuest) : base(player, dbQuest) { }

        public override string Name => "The Traveler's Inn";

        public override string Description
        {
            get
            {
                if (Step < 1)
                    return base.Description;
                Courier165.Inns.TryGetValue(QuestPlayer.Realm, out Courier165.Inn inn);
                return inn == null ? base.Description
                    : $"{char.ToUpperInvariant(inn.Courier[0])}{inn.Courier[1..]} brought word of a traveler's inn on the road near {inn.Town}, " +
                      $"in {inn.Zone}. Travel there and speak with {inn.Innkeeper}, the innkeeper, about the companions who gather there.";
            }
        }

        public override bool CheckQuestQualification(GamePlayer player) => Classic165Mode.IsOn && player.Level >= Courier165.InnLevel;

        public override void Notify(DOLEvent e, object sender, EventArgs args)
        {
            if (Step == 1 && sender == QuestPlayer && e == GameObjectEvent.InteractWith &&
                args is InteractWithEventArgs { Target: Innkeeper165 keeper } && keeper.Realm == QuestPlayer.Realm)
            {
                keeper.SayTo(QuestPlayer, PacketHandler.eChatLoc.CL_PopupWindow,
                    "So the courier found you. Travellers gather here, ready to fight at your side for a share of the road. " +
                    "Your first companion costs you nothing. Choose one, and I'll write their name in my book.");
                FinishQuest();
                InnQuestMarkers165.TurnedIn(keeper, QuestPlayer);
            }
        }
    }

    /// <summary>BotRealm, level 50: the messenger's word about the Horn of Rallying; turned in at the Horn Handler,
    /// which earns the right to sound it.</summary>
    public class HornOfRallying165 : BaseQuest
    {
        public HornOfRallying165() : base() { }
        public HornOfRallying165(GamePlayer player) : this(player, 1) { }
        public HornOfRallying165(GamePlayer player, int step) : base(player, step) { }
        public HornOfRallying165(GamePlayer player, DbQuest dbQuest) : base(player, dbQuest) { }

        public override string Name => "The Horn of Rallying";

        public override string Description
        {
            get
            {
                if (Step < 1)
                    return base.Description;
                Courier165.Inns.TryGetValue(QuestPlayer.Realm, out Courier165.Inn inn);
                return inn == null ? base.Description
                    : $"A courier brought word that {inn.HornHandler}, who keeps the Horn of Rallying at the traveler's inn near {inn.Town} " +
                      $"in {inn.Zone}, would speak with you. Champions who sound the horn call the realm to its great hunts.";
            }
        }

        public override bool CheckQuestQualification(GamePlayer player) => Classic165Mode.IsOn && player.Level >= Courier165.HornLevel;

        /// <summary>Turned in by the Horn Handler itself (HornHandler165.Interact), which then shows the horn's choices.</summary>
        public override void Notify(DOLEvent e, object sender, EventArgs args) { }
    }
}
