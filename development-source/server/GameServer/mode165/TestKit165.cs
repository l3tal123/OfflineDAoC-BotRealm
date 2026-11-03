using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.GameEvents;
using DOL.GS.PacketHandler;
using DOL.GS.Realm;

namespace DOL.GS
{
    /// <summary>A BotRealm test character still to be levelled at its first login (TestKit165).</summary>
    [DataTable(TableName = "offline_true165_testkit")]
    public sealed class OfflineTrue165TestKitRecord : DataObject
    {
        [PrimaryKey] public string CharacterName { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public int TargetLevel { get; set; }
        [DataElement(AllowDbNull = false)] public bool Done { get; set; }
    }

    /// <summary>
    /// Owner test characters for BotRealm (owner 2026-10-30: a level 50 and a level 5 for every realm, to test the
    /// companions, the horn and their courier quests freely). LOCAL TESTING ONLY: it runs only when the file
    /// data\true165-test-characters.json exists, which no release ships (releases have no accounts).
    /// At server start the missing characters are made as a character creation makes them (race stats plus 30 points,
    /// the race's model, the starting equipment), standing at their realm's traveler's inn and bound there. At each
    /// one's first login it is levelled with the same steps as the GM /player level command.
    /// File: {"account":"name","characters":[{"name","realm","race","class","gender","level","gold"}...]}.
    /// </summary>
    public static class TestKit165
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private sealed record Wanted(string Name, int Realm, int Race, int Class, int Gender, int Level, int Gold);
        private sealed record KitFile(string Account, List<Wanted> Characters);

        /// <summary>Where each realm's test characters stand: beside its inn's innkeeper (add-inns165.json).</summary>
        private static readonly Dictionary<eRealm, (ushort Region, int X, int Y, int Z, ushort Heading)> Inns = new()
        {
            [eRealm.Albion] = (1, 462222, 649880, 1816, 4012),
            [eRealm.Midgard] = (100, 705734, 791172, 4544, 1192),   // the inn by Huginfell (moved 2026-10-30)
            [eRealm.Hibernia] = (200, 314794, 634077, 4866, 662),
        };

        public static string FilePath => Path.GetFullPath(Path.Combine(GameServer.Instance.Configuration.RootDirectory, "..", "data",
            "true165-test-characters.json"));

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            if (!Classic165Mode.IsOn || !File.Exists(FilePath))
                return;
            try
            {
                KitFile kit = JsonSerializer.Deserialize<KitFile>(File.ReadAllText(FilePath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (kit?.Characters == null || string.IsNullOrWhiteSpace(kit.Account))
                    return;
                DbAccount account = DOLDB<DbAccount>.SelectObject(DB.Column("Name").IsEqualTo(kit.Account));
                if (account == null)
                {
                    Log.Warn($"TEST_KIT_165 account {kit.Account} not found; no test characters made");
                    return;
                }
                foreach (Wanted w in kit.Characters)
                    Make(account, w);
            }
            catch (Exception ex)
            {
                Log.Error("TEST_KIT_165 could not make the test characters", ex);
            }
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args) =>
            GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));

        private static void Make(DbAccount account, Wanted w)
        {
            eRealm realm = (eRealm)w.Realm;
            PlayerRace race = PlayerRace.AllRaces.FirstOrDefault(r => (int)r.ID == w.Race && r.Realm == realm);
            DbCoreCharacter existing = DOLDB<DbCoreCharacter>.SelectObject(DB.Column("Name").IsEqualTo(w.Name));
            if (existing != null)
            {
                // made at the Midgard inn's first site: move it (and its bind) to the inn by Huginfell
                if (realm == eRealm.Midgard && existing.AccountName == account.Name && existing.Region == 100 &&
                    Math.Abs(existing.Xpos - 708898) < 2000 && Math.Abs(existing.Ypos - 783032) < 2000 &&
                    Inns.TryGetValue(realm, out var moved) &&
                    DOLDB<OfflineTrue165TestKitRecord>.SelectObject(DB.Column("CharacterName").IsEqualTo(w.Name)) != null)
                {
                    existing.Xpos = moved.X; existing.Ypos = moved.Y; existing.Zpos = moved.Z; existing.Direction = moved.Heading;
                    existing.BindRegion = moved.Region; existing.BindXpos = moved.X; existing.BindYpos = moved.Y;
                    existing.BindZpos = moved.Z; existing.BindHeading = moved.Heading;
                    GameServer.Database.SaveObject(existing);
                    Log.Info($"TEST_KIT_165 moved {w.Name} to the Midgard inn by Huginfell");
                }
                // made already: repair one of ours made before the look fix (Catacombs figure, gender stored as eGender)
                if (race != null && existing.AccountName == account.Name &&
                    DOLDB<OfflineTrue165TestKitRecord>.SelectObject(DB.Column("CharacterName").IsEqualTo(w.Name)) != null &&
                    (existing.CreationModel != ClassicModel(race, w.Gender) || existing.Gender != (w.Gender == 1 ? 1 : 0)))
                {
                    existing.Gender = w.Gender == 1 ? 1 : 0;
                    existing.CreationModel = ClassicModel(race, w.Gender);
                    existing.CurrentModel = existing.CreationModel;
                    GameServer.Database.SaveObject(existing);
                    Log.Info($"TEST_KIT_165 repaired {w.Name}: classic model {existing.CreationModel}, gender {existing.Gender}");
                }
                return;
            }
            ICharacterClass charClass = ScriptMgr.FindCharacterClass(w.Class);
            if (race == null || charClass == null || !Inns.TryGetValue(realm, out var inn))
            {
                Log.Warn($"TEST_KIT_165 {w.Name}: race {w.Race} / class {w.Class} / realm {w.Realm} not valid");
                return;
            }
            var used = DOLDB<DbCoreCharacter>.SelectObjects(DB.Column("AccountName").IsEqualTo(account.Name))
                .Select(c => c.AccountSlot).ToHashSet();
            int slot = Enumerable.Range(w.Realm * 100, 10).FirstOrDefault(s => !used.Contains(s), -1);
            if (slot < 0)
            {
                Log.Warn($"TEST_KIT_165 {w.Name}: no free {realm} character slot on {account.Name}");
                return;
            }
            eGender gender = w.Gender == 1 ? eGender.Female : eGender.Male;
            Dictionary<eStat, int> stats = new(GlobalConstants.STARTING_STATS_DICT[race.ID]);
            // the 30 creation points as a player would spend them: 10 each in the class's three stats
            foreach (eStat stat in new[] { charClass.PrimaryStat, charClass.SecondaryStat, charClass.TertiaryStat }.Distinct())
                if (stats.ContainsKey(stat))
                    stats[stat] += 10;
            var ch = new DbCoreCharacter
            {
                AccountName = account.Name,
                Name = w.Name,
                Level = 1,
                Realm = w.Realm,
                Class = w.Class,
                Race = w.Race,
                Gender = w.Gender == 1 ? 1 : 0,   // the character table's own values: 0 male, 1 female
                AccountSlot = slot,
                CreationModel = ClassicModel(race, w.Gender),
                Strength = stats[eStat.STR], Dexterity = stats[eStat.DEX], Constitution = stats[eStat.CON],
                Quickness = stats[eStat.QUI], Intelligence = stats[eStat.INT], Piety = stats[eStat.PIE],
                Empathy = stats[eStat.EMP], Charisma = stats[eStat.CHR],
                CreationDate = DateTime.Now,
                Endurance = 100, MaxEndurance = 100, Concentration = 100,
                MaxSpeed = GamePlayer.PLAYER_BASE_SPEED,
                Region = inn.Region, Xpos = inn.X, Ypos = inn.Y, Zpos = inn.Z, Direction = inn.Heading,
                BindRegion = inn.Region, BindXpos = inn.X, BindYpos = inn.Y, BindZpos = inn.Z, BindHeading = inn.Heading,
                Gold = Math.Max(0, w.Gold),
            };
            ch.CurrentModel = ch.CreationModel;
            GameServer.Database.AddObject(ch);
            // the starting equipment, as at a real creation (1.65: the class's weapon and the training shield)
            CreationStartupEquipment.OnCharacterCreation(DatabaseEvent.CharacterCreated, null, new CharacterEventArgs(ch, null));
            GameServer.Database.SaveObject(ch);
            GameServer.Database.AddObject(new OfflineTrue165TestKitRecord { CharacterName = w.Name, TargetLevel = Math.Clamp(w.Level, 1, 50) });
            Log.Info($"TEST_KIT_165 made {w.Name} ({realm} {race.ID} {(eCharacterClass)w.Class}) on {account.Name}, level {w.Level} at first login");
        }

        /// <summary>The 1.65 figure the original client's creation screen gives this race and sex (first face and hair),
        /// with the creation size bits it sends (0x1000), not the Catacombs figure.</summary>
        private static ushort ClassicModel(PlayerRace race, int gender)
        {
            ushort classic = ClassicFigures165.Model(race.ID, gender == 1, 0, 0);
            return classic != 0 ? (ushort)(classic | 0x1000) : (ushort)race.GetModel(gender == 1 ? eGender.Female : eGender.Male);
        }

        private static void OnPlayerEntered(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is not GamePlayer player)
                return;
            OfflineTrue165TestKitRecord kit = DOLDB<OfflineTrue165TestKitRecord>.SelectObject(DB.Column("CharacterName").IsEqualTo(player.Name));
            if (kit == null)
                return;
            // a kit character levelled before experience was set with the level: give it its level's experience
            if (player.Experience < player.GetExperienceValueForLevel(player.Level))
            {
                player.Experience = player.GetExperienceValueForLevel(player.Level);
                player.Out.SendUpdatePoints();
                player.SaveIntoDatabase();
            }
            if (kit.Done)
                return;
            if (kit.TargetLevel > player.Level)
                LevelTo(player, (byte)kit.TargetLevel);
            kit.Done = true;
            kit.Dirty = true;
            GameServer.Database.SaveObject(kit);
            player.Out.SendMessage($"Test character: you are level {player.Level}. Train your skills at your trainer.",
                eChatType.CT_Important, eChatLoc.CL_SystemWindow);
        }

        /// <summary>The GM /player level steps (commands\gmcommands\Player.cs).</summary>
        private static void LevelTo(GamePlayer player, byte newLevel)
        {
            int curLevel = player.Level;
            bool curSecondStage = player.IsLevelSecondStage;
            if (curSecondStage)
                player.GainExperience(eXPSource.Other, player.GetExperienceValueForLevel(++curLevel));
            if (newLevel != curLevel || !curSecondStage)
                player.Level = newLevel;   // the Level setter grants the spec points, abilities and stats of each level
            // experience to match, so the XP bar and later gains start at the new level (not at level 1's zero)
            if (player.Experience < player.GetExperienceValueForLevel(newLevel))
                player.Experience = player.GetExperienceValueForLevel(newLevel);
            player.Out.SendUpdatePlayer();
            player.Out.SendUpdatePoints();
            player.Out.SendCharStatsUpdate();
            player.UpdatePlayerStatus();
            player.SaveIntoDatabase();
        }
    }
}
