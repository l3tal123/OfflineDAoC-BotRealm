using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using NUnit.Framework;
using Duty = DOL.GS.BotGroupInvite.Duty;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Player-grouping wrappers ported from the Living Server reference: the autonomous gamebot invitation duties, the
    /// 6,000-unit /invite-by-name reach, and releasing invited gamebots when their player leaves the group (issue #33).
    /// Pure: no database and no live world, only actors built from uninitialised objects.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotGroupInvite165
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameLiving> _actors = new();

        #region Test doubles

        private sealed class TestPlayer : GamePlayer
        {
            private TestPlayer() : base(null, null) { }
            public override bool IsAlive => true;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
        }

        private sealed class TestBot : GameBot
        {
            private TestBot() : base((OfflineWorldBotRecord)null) { }
            public bool Alive = true;
            public eRealm TestRealm = eRealm.Albion;
            public int TestX, TestY, TestZ;
            public override bool IsAlive => Alive;
            public override eRealm Realm { get => TestRealm; set => TestRealm = value; }
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override bool IsCrowdControlled => false;
            public override bool InCombat => false;
            public override short MaxSpeed => 200;
            public override int X => TestX;
            public override int Y => TestY;
            public override int Z => TestZ;
            public override void StopAttack() { }
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (GameLiving actor in _actors)
                ServiceObjectStore.Remove(actor.effectListComponent);
            _actors.Clear();
        }

        private T Actor<T>() where T : GameLiving
        {
            T actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            actor.ObjectState = GameObject.eObjectState.Active;
            Field(typeof(GameLiving), actor, "<TempProperties>k__BackingField", new PropertyCollection());
            if (actor is GameNPC npc)
            {
                Field(typeof(GameNPC), actor, "m_brains", new ArrayList());
                Field(typeof(GameNPC), actor, "m_spells", new List<Spell>());
                npc.movementComponent = new NpcMovementComponent(npc);
            }
            actor.effectListComponent = EffectListComponent.Create(actor);
            _actors.Add(actor);
            return actor;
        }

        private TestBot MakeBot(string name)
        {
            TestBot bot = Actor<TestBot>();
            bot.Name = name;
            bot.Alive = true;
            bot.TestRealm = eRealm.Albion;
            var brain = new BotBrain { Body = bot };
            Field(typeof(GameNPC), bot, "m_ownBrain", brain);
            return bot;
        }

        private static void Autonomous(GameBot bot, bool value = true) =>
            Field(typeof(GameBot), bot, "<IsAutonomousWorldBot>k__BackingField", value);

        private static void Field(Type type, object owner, string name, object value) =>
            type.GetField(name, Hidden).SetValue(owner, value);

        private static Region NewRegion(ushort id) =>
            new(new RegionData { Id = id, Name = "invite test", Description = "invite test", Mobs = [] });

        #endregion

        #region Invitation duties

        [Test]
        public void AFreeBotHasNoDutyAndNoBusyMessage()
        {
            Assert.That(BotGroupInvite.DutyOf(true, false, false, false, false, false, false), Is.EqualTo(Duty.None));
            Assert.That(BotGroupInvite.BusyMessage("Aldwin", Duty.None), Is.Null);
            Assert.That(BotGroupInvite.DutyOf((GameBot)null), Is.EqualTo(Duty.None));
        }

        [TestCase(false, false, false, false, false, false, false, Duty.Dead)]
        [TestCase(true, true, false, false, false, false, false, Duty.Siege)]
        [TestCase(true, false, true, false, false, false, false, Duty.Relic)]
        [TestCase(true, false, false, true, false, false, false, Duty.RvrEvent)]
        [TestCase(true, false, false, false, true, false, false, Duty.RealmRaid)]
        [TestCase(true, false, false, false, false, true, false, Duty.StableRoute)]
        [TestCase(true, false, false, false, false, false, true, Duty.Helper)]
        [TestCase(false, true, true, true, true, true, true, Duty.Dead)]
        [TestCase(true, true, true, true, true, true, true, Duty.Siege)]
        [TestCase(true, false, true, true, false, false, false, Duty.Relic)]
        public void EachDutyRefusesTheInvitation(bool alive, bool siege, bool relic, bool rvr, bool raid, bool stable, bool helper, Duty expected)
        {
            Duty duty = BotGroupInvite.DutyOf(alive, siege, relic, rvr, raid, stable, helper);
            Assert.That(duty, Is.EqualTo(expected));
            string message = BotGroupInvite.BusyMessage("Aldwin", duty);
            Assert.That(message, Does.StartWith("Aldwin "));
            Assert.That(message, Does.EndWith("."));
        }

        [Test]
        public void TheSiegeMessageSaysSo() =>
            Assert.That(BotGroupInvite.BusyMessage("Aldwin", Duty.Siege), Is.EqualTo("Aldwin is busy with a siege right now."));

        [Test]
        public void EveryDutyHasItsOwnMessage()
        {
            Duty[] duties = Enum.GetValues<Duty>().Where(duty => duty != Duty.None).ToArray();
            string[] messages = duties.Select(duty => BotGroupInvite.BusyMessage("X", duty)).ToArray();
            Assert.That(messages, Has.None.Null);
            Assert.That(messages.Distinct().Count(), Is.EqualTo(duties.Length));
        }

        [Test]
        public void AnAutonomousWorldBotOnDutyIsRefused()
        {
            TestBot bot = MakeBot("Aldwin");
            Autonomous(bot);
            Assert.That(BotGroupInvite.DutyOf(bot), Is.EqualTo(Duty.None), "a free world bot answers");

            bot.Alive = false;
            Assert.That(BotGroupInvite.DutyOf(bot), Is.EqualTo(Duty.Dead));

            bot.Alive = true;
            bot.TempProperties.SetProperty("SiegeJobUntil", GameLoop.GameLoopTime + 60_000);
            Assert.That(BotGroupInvite.DutyOf(bot), Is.EqualTo(Duty.Siege));

            bot.TempProperties.RemoveProperty("SiegeJobUntil");
            bot.TempProperties.SetProperty(GameRelic.PLAYER_CARRY_RELIC_WEAK,
                (GameRelic)RuntimeHelpers.GetUninitializedObject(typeof(GameRelic)));
            Assert.That(BotGroupInvite.DutyOf(bot), Is.EqualTo(Duty.Relic));

            bot.TempProperties.RemoveProperty(GameRelic.PLAYER_CARRY_RELIC_WEAK);
            // The force tag is left behind when an event ends, so a tag no running event knows is not a duty.
            bot.TempProperties.SetProperty("RvrEventForce", "no-such-force");
            Assert.That(BotGroupInvite.DutyOf(bot), Is.EqualTo(Duty.None));

            Field(typeof(GameBot), bot, "<IsOnStableMasterRoute>k__BackingField", true);
            Assert.That(BotGroupInvite.DutyOf(bot), Is.EqualTo(Duty.StableRoute));

            Field(typeof(GameBot), bot, "<IsOnStableMasterRoute>k__BackingField", false);
            Field(typeof(GameBot), bot, "<IsTemporaryGroupHelper>k__BackingField", true);
            Assert.That(BotGroupInvite.DutyOf(bot), Is.EqualTo(Duty.Helper));
        }

        [Test]
        public void ACompanionBotIsNeverOnDuty()
        {
            TestBot companion = MakeBot("Companion");
            companion.Alive = false;
            Field(typeof(GameBot), companion, "<IsOnStableMasterRoute>k__BackingField", true);
            Assert.That(BotGroupInvite.DutyOf(companion), Is.EqualTo(Duty.None), "companion bots are never on duty here");
        }

        #endregion

        #region Invite by name: realm, region and range

        [Test]
        public void TheByNameReachIsTheReferenceRange()
        {
            Assert.That(BotGroupInvite.InviteByNameRange, Is.EqualTo(6_000));
        }

        private (TestPlayer Player, TestBot Bot) ByNamePair()
        {
            Region region = NewRegion(100);
            TestPlayer player = Actor<TestPlayer>();
            player.CurrentRegion = region;
            player.Realm = eRealm.Albion;
            TestBot bot = MakeBot("Aldwin");
            bot.CurrentRegion = region;
            return (player, bot);
        }

        [Test]
        public void ANearbyWorldBotOfTheSameRegionIsCloseEnough()
        {
            (TestPlayer player, TestBot bot) = ByNamePair();
            Autonomous(bot);
            bot.TestX = 5_900;
            Assert.That(BotGroupInvite.IsWorldBotOfPlayerRealm(player, bot), Is.True);
            Assert.That(BotGroupInvite.CloseEnoughToInviteByName(player, bot), Is.True);
        }

        [Test]
        public void AWorldBotExactlyAtTheRangeIsCloseEnough()
        {
            (TestPlayer player, TestBot bot) = ByNamePair();
            bot.TestX = BotGroupInvite.InviteByNameRange;
            Assert.That(BotGroupInvite.CloseEnoughToInviteByName(player, bot), Is.True);
        }

        [Test]
        public void AWorldBotBeyondTheRangeIsTooFar()
        {
            (TestPlayer player, TestBot bot) = ByNamePair();
            bot.TestX = BotGroupInvite.InviteByNameRange + 1;
            Assert.That(BotGroupInvite.CloseEnoughToInviteByName(player, bot), Is.False);
        }

        [Test]
        public void AWorldBotInAnotherRegionIsTooFar()
        {
            (TestPlayer player, TestBot bot) = ByNamePair();
            bot.CurrentRegion = NewRegion(101);
            bot.TestX = 100;
            Assert.That(BotGroupInvite.CloseEnoughToInviteByName(player, bot), Is.False);
        }

        [Test]
        public void AMissingRegionOrActorIsNeverCloseEnough()
        {
            (TestPlayer player, TestBot bot) = ByNamePair();
            player.CurrentRegion = null;
            Assert.That(BotGroupInvite.CloseEnoughToInviteByName(player, bot), Is.False);
            Assert.That(BotGroupInvite.CloseEnoughToInviteByName(null, bot), Is.False);
            Assert.That(BotGroupInvite.CloseEnoughToInviteByName(player, null), Is.False);
        }

        [Test]
        public void OnlyAnAutonomousWorldBotOfThePlayersRealmIsReachedByName()
        {
            (TestPlayer player, TestBot bot) = ByNamePair();
            Autonomous(bot);
            Assert.That(BotGroupInvite.IsWorldBotOfPlayerRealm(player, bot), Is.True);
            Assert.That(BotGroupInvite.IsWorldBotOfPlayerRealm(null, bot), Is.False);
            Assert.That(BotGroupInvite.IsWorldBotOfPlayerRealm(player, null), Is.False);

            Autonomous(bot, false);
            Assert.That(BotGroupInvite.IsWorldBotOfPlayerRealm(player, bot), Is.False,
                "a companion bot is invited by targeting it");

            Autonomous(bot);
            bot.TestRealm = eRealm.Midgard;
            Assert.That(BotGroupInvite.IsWorldBotOfPlayerRealm(player, bot), Is.False,
                "a bot of another realm falls through to the player lookup");

            bot.TestRealm = eRealm.Albion;
            Field(typeof(GameBot), bot, "<IsTemporaryGroupHelper>k__BackingField", true);
            Assert.That(BotGroupInvite.IsWorldBotOfPlayerRealm(player, bot), Is.False,
                "temporary /spawn helpers are not reached by name");
        }

        #endregion

        #region Releasing gamebots when their player leaves (issue #33)

        private (TestPlayer Player, TestBot Tank, TestBot Damage, TestBot Healer) Party()
        {
            TestPlayer player = Actor<TestPlayer>();
            player.Realm = eRealm.Albion;
            var group = new Group(player);
            Add(group, player);

            TestBot tank = MakeBot("Tank"), damage = MakeBot("Damage"), healer = MakeBot("Healer");
            foreach (TestBot bot in new[] { tank, damage, healer })
            {
                Add(group, bot);
                Assert.That(bot.EnterPlayerLedGroup(player), Is.True);
            }

            return (player, tank, damage, healer);
        }

        private static List<GameLiving> Members(Group group) =>
            (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);

        private static void Add(Group group, GameLiving member)
        {
            Members(group).Add(member);
            member.Group = group;
        }

        private static void Remove(GameLiving member)
        {
            Members(member.Group).Remove(member);
            member.Group = null;
        }

        [Test]
        public void GamebotsStopFollowingAPlayerWhoLeavesTheirGroup()
        {
            (TestPlayer player, TestBot tank, TestBot damage, TestBot healer) = Party();
            Group group = player.Group;
            foreach (TestBot bot in new[] { tank, damage, healer })
                Autonomous(bot);

            Remove(player);
            group.ReleaseGamebotsLedBy(player);

            foreach (TestBot bot in new[] { tank, damage, healer })
            {
                Assert.That(bot.PlayerGroupLeader, Is.Null, bot.Name);
                // Both were what made BotGroupInvite.TryInvite answer "already following another player-led group".
                Assert.That(bot.Group, Is.Null, bot.Name + " goes back to its own play");
            }
        }

        [Test]
        public void GamebotsFollowThePlayerWhoStaysInTheGroup()
        {
            (TestPlayer player, TestBot tank, TestBot damage, _) = Party();
            Group group = player.Group;

            TestPlayer friend = Actor<TestPlayer>();
            friend.Realm = eRealm.Albion;
            Add(group, friend);

            foreach (TestBot bot in new[] { tank, damage })
                Autonomous(bot);
            Assert.That(group.GamebotsLedBy(player), Has.Count.EqualTo(2));

            Remove(player);
            group.ReleaseGamebotsLedBy(player);

            Assert.That(tank.PlayerGroupLeader, Is.SameAs(friend));
            Assert.That(damage.PlayerGroupLeader, Is.SameAs(friend));
            Assert.That(tank.Group, Is.SameAs(group));
        }

        [Test]
        public void CompanionsInTheGroupAreLeftAsBefore()
        {
            (TestPlayer player, TestBot tank, _, _) = Party();
            Group group = player.Group;

            Remove(player);
            group.ReleaseGamebotsLedBy(player);

            Assert.That(tank.PlayerGroupLeader, Is.SameAs(player), "only invited gamebots are released");
            Assert.That(tank.Group, Is.SameAs(group));
        }

        [Test]
        public void TemporaryHelpersAreNotReleasedByTheirPlayer()
        {
            (TestPlayer player, TestBot tank, TestBot damage, _) = Party();
            Group group = player.Group;
            foreach (TestBot bot in new[] { tank, damage })
            {
                Autonomous(bot);
                Field(typeof(GameBot), bot, "<IsTemporaryGroupHelper>k__BackingField", true);
            }

            Assert.That(group.GamebotsLedBy(player), Is.Empty, "temporary /spawn helpers are not released");

            Remove(player);
            group.ReleaseGamebotsLedBy(player);

            Assert.That(damage.PlayerGroupLeader, Is.SameAs(player));
            Assert.That(damage.Group, Is.SameAs(group));
        }

        #endregion
    }
}
