using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// True 1.65 Mode autoloot (owner 2026-11-01: "enable the autoloot feature for all the players and bots so the loot
    /// doesn't drop on the ground if the autoloot is enabled"). The rule is asked once per kill reward owner: whoever
    /// would receive the coin or drop decides whether it is paid straight into its bags or left on the ground.
    /// Inert objects: only the state the rule reads is initialised; no world, database, timers or login.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_AutoLoot165
    {
        private bool _mode;

        [SetUp]
        public void Save() => _mode = Properties.CLASSIC165_MODE;

        [TearDown]
        public void Restore() => Properties.CLASSIC165_MODE = _mode;

        private static T Inert<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static void SetField(Type type, object target, string name, object value)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null)
                    continue;
                field.SetValue(target, value);
                return;
            }

            Assert.Fail($"field {name} not found on {type}");
        }

        private static DbCoreCharacter Character(bool autoloot) => new() { Autoloot = autoloot };

        /// <summary>A player whose /autoloot setting is the given one (the flag lives on the character row).</summary>
        private static GamePlayer Player(bool autoloot)
        {
            GamePlayer player = Inert<GamePlayer>();
            SetField(typeof(GamePlayer), player, "m_dbCharacter", Character(autoloot));
            return player;
        }

        private static void SetPlayerAutoloot(GamePlayer player, bool autoloot) =>
            ((DbCoreCharacter)typeof(GamePlayer).GetField("m_dbCharacter",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player)).Autoloot = autoloot;

        [Test]
        public void PlayerFollowsOwnAutolootSetting()
        {
            GamePlayer player = Player(false);
            Assert.That(AutoLoot165.WantsLoot(player), Is.False, "autoloot off leaves the bag on the ground");

            SetPlayerAutoloot(player, true);
            Assert.That(AutoLoot165.WantsLoot(player), Is.True, "/autoloot on pays the bag straight to the player");
        }

        [Test]
        public void BotTakesItsOwnKillsButLeavesAPlayerLedPartys()
        {
            GameBot bot = Inert<GameBot>();
            bot.Autoloot = true;
            Assert.That(AutoLoot165.WantsLoot(bot), Is.True, "a solo autonomous bot autoloots");

            SetField(typeof(GameBot), bot, "<PlayerGroupLeader>k__BackingField", Player(false));
            Assert.That(AutoLoot165.WantsLoot(bot), Is.False,
                "an invited world bot leaves a player-led party's loot to the player, whose setting decides");

            bot.Autoloot = false;
            Assert.That(AutoLoot165.WantsLoot(bot), Is.False);
        }

        [Test]
        public void BotGroupAutolootsAndPlayerGroupFollowsItsLeader()
        {
            GameBot leaderBot = Inert<GameBot>();
            leaderBot.Autoloot = true;
            Group botGroup = new(leaderBot);
            Assert.That(AutoLoot165.WantsLoot(botGroup), Is.True, "an autonomous bot group pays its members");

            GamePlayer leader = Player(false);
            Group party = new(leader);
            Assert.That(AutoLoot165.WantsLoot(party), Is.False, "a player-led group follows its leader's autoloot");

            SetPlayerAutoloot(leader, true);
            Assert.That(AutoLoot165.WantsLoot(party), Is.True);
        }

        [Test]
        public void BattlegroupFollowsItsTreasurerAndNeverInventsOne()
        {
            BattleGroup battlegroup = Inert<BattleGroup>();
            Assert.That(AutoLoot165.WantsLoot(battlegroup), Is.False, "no treasurer: the bag stays on the ground");

            SetField(typeof(BattleGroup), battlegroup, "battlegroupTreasurer", Player(false));
            Assert.That(AutoLoot165.WantsLoot(battlegroup), Is.False);

            SetPlayerAutoloot((GamePlayer)typeof(BattleGroup).GetField("battlegroupTreasurer",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(battlegroup), true);
            Assert.That(AutoLoot165.WantsLoot(battlegroup), Is.True);
        }

        [Test]
        public void NormalModeKeepsTheInheritedUnconditionalAutoloot()
        {
            GamePlayer player = Player(false);

            Properties.CLASSIC165_MODE = false;
            Assert.That(AutoLoot165.WantsLootInCurrentMode(player), Is.True, "normal mode never consulted the setting");

            Properties.CLASSIC165_MODE = true;
            Assert.That(AutoLoot165.WantsLootInCurrentMode(player), Is.False);
        }
    }
}
