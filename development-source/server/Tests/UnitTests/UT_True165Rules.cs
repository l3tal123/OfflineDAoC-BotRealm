using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // True 1.65 Mode rules (owner 2026-10-30): the freeshard cheat commands are GM-only, and a base class that leads
    // nowhere for its race (the Inconnu Acolyte, whose only career is the disabled Heretic) can't be created.
    [TestFixture]
    public class UT_True165Rules
    {
        private bool _mode;
        private string _disabled;
        private string _races;

        [SetUp]
        public void Save()
        {
            _mode = Properties.CLASSIC165_MODE;
            _disabled = Properties.DISABLED_CLASSES;
            _races = Properties.DISABLED_RACES;
        }

        [TearDown]
        public void Restore()
        {
            Properties.CLASSIC165_MODE = _mode;
            Properties.DISABLED_CLASSES = _disabled;
            Properties.DISABLED_RACES = _races;
        }

        [TestCase("&grind")]
        [TestCase("&tc")]
        [TestCase("&spawn")]
        [TestCase("&tele")]
        [TestCase("&raid")]
        public void CheatCommandsAreGmOnlyIn165Mode(string command)
        {
            Properties.CLASSIC165_MODE = true;
            Assert.That(Classic165Mode.IsGmOnlyCommand(command), Is.True);
            Properties.CLASSIC165_MODE = false;
            Assert.That(Classic165Mode.IsGmOnlyCommand(command), Is.False, "normal mode keeps every command");
        }

        [TestCase("&autoloot")]
        [TestCase("&sell")]      // owner 2026-11-01: /sell all is an ordinary player action (bag selling included)
        [TestCase("&say")]
        [TestCase("&stuck")]
        [TestCase("&summon")]
        [TestCase(null)]
        public void OrdinaryCommandsStay(string command)
        {
            Properties.CLASSIC165_MODE = true;
            Assert.That(Classic165Mode.IsGmOnlyCommand(command), Is.False);
        }

        [Test]
        public void InconnuAcolyteHasNoCareerWhileHereticIsDisabled()
        {
            Properties.DISABLED_CLASSES = "33;34;39;58-62";
            Assert.Multiple(() =>
            {
                Assert.That(Classic165Mode.BaseClassHasCareer((int) eCharacterClass.Acolyte, (int) eRace.Inconnu), Is.False);
                Assert.That(Classic165Mode.BaseClassHasCareer((int) eCharacterClass.Acolyte, (int) eRace.Briton), Is.True);
                Assert.That(Classic165Mode.BaseClassHasCareer((int) eCharacterClass.Acolyte, (int) eRace.Avalonian), Is.True);
            });
            // Heretic (Catacombs) lists no races in this server, so even with it enabled an Inconnu Acolyte has no career
            Properties.DISABLED_CLASSES = string.Empty;
            Assert.That(Classic165Mode.BaseClassHasCareer((int) eCharacterClass.Acolyte, (int) eRace.Inconnu), Is.False);
        }

        [Test]
        public void AdvancedClassesAreLeftToTheNormalChecks()
        {
            Properties.DISABLED_CLASSES = "33;34;39;58-62";
            Assert.That(Classic165Mode.BaseClassHasCareer((int) eCharacterClass.Cleric, (int) eRace.Inconnu), Is.True);
        }

        [Test]
        public void HireWindowOffersOnlyClassicAndShroudedIslesRacesAndRealAdvancedClasses()
        {
            Properties.DISABLED_RACES = "16-21";
            Properties.DISABLED_CLASSES = "33;34;39;58-62";
            var albion = Hire165.Races(eRealm.Albion);
            Assert.That(albion, Does.Contain(eRace.Briton).And.Contain(eRace.Inconnu));
            Assert.That(albion, Does.Not.Contain(eRace.HalfOgre).And.Not.Contain(eRace.Korazh));
            var briton = Hire165.Classes(eRealm.Albion, eRace.Briton);
            Assert.That(briton, Does.Contain(eCharacterClass.Armsman).And.Contain(eCharacterClass.Cleric));
            Assert.That(briton, Does.Not.Contain(eCharacterClass.Fighter), "hired at level 5: an advanced class, not a base class");
            Assert.That(briton, Does.Not.Contain(eCharacterClass.Heretic));
            Assert.That(Hire165.Classes(eRealm.Midgard, eRace.Troll), Does.Not.Contain(eCharacterClass.Cleric));
            Assert.That(Hire165.Model(new Hire165.Design { Race = eRace.Briton, Gender = eGender.Female }), Is.Not.Zero);
        }

        [Test]
        public void HirePreviewUsesTheClassicFigureForFaceAndHair()
        {
            Assert.That(ClassicFigures165.Model(eRace.Briton, false, 0, 0), Is.EqualTo(471));
            Assert.That(ClassicFigures165.Model(eRace.Briton, false, 0, 1), Is.EqualTo(473));
            Assert.That(ClassicFigures165.Faces(eRace.Firbolg, true), Is.EqualTo(5));
            Assert.That(ClassicFigures165.Hairs(eRace.Highlander, true), Is.EqualTo(4));
        }

        [Test]
        public void HireLookPickerStepsThroughEveryClassicLook()
        {
            // owner 2026-10-30: one LOOK picker of the looks a player could pick (figtofig3main.csv), in the client's order
            Assert.That(ClassicFigures165.LookCount(eRace.Briton, false), Is.EqualTo(12));
            Assert.That(ClassicFigures165.LookModel(eRace.Briton, false, 0), Is.EqualTo(14));
            Assert.That(ClassicFigures165.LookModel(eRace.Briton, false, 4), Is.EqualTo(471));
            Assert.That(ClassicFigures165.LookModel(eRace.Briton, false, 12), Is.EqualTo(0));
            Assert.That(ClassicFigures165.LookCount(eRace.Firbolg, false), Is.EqualTo(11));
            var d = new Hire165.Design { Race = eRace.Briton, Gender = eGender.Male, Face = 4 };
            Assert.That(Hire165.Model(d), Is.EqualTo(471));
            Assert.That(Hire165.LookCount(d), Is.EqualTo(12));
        }

        [Test]
        public void HireHairColoursFollowTheLooksHairSet()
        {
            // owner 2026-10-30: hair colour per look, from its hair set in the period hair.csv (distinct textures)
            Assert.That(ClassicFigures165.HairColourCount(14), Is.EqualTo(8));    // Briton male, set 1
            Assert.That(ClassicFigures165.HairColourCount(137), Is.EqualTo(4));   // Troll male, set 7 repeats four
            Assert.That(ClassicFigures165.HairColourCount(48), Is.EqualTo(3));    // Saracen male, set 12
            var d = new Hire165.Design { Race = eRace.Briton, Gender = eGender.Male, Face = 0, HairColour = 9 };
            Assert.That(Hire165.HairCount(d), Is.EqualTo(8));
        }
    
        [TestCase(eCharacterClass.Armsman, eCharacterClass.Fighter)]
        [TestCase(eCharacterClass.Cleric, eCharacterClass.Acolyte)]
        [TestCase(eCharacterClass.Necromancer, eCharacterClass.Disciple)]
        [TestCase(eCharacterClass.Minstrel, eCharacterClass.AlbionRogue)]
        [TestCase(eCharacterClass.Healer, eCharacterClass.Seer)]
        [TestCase(eCharacterClass.Bonedancer, eCharacterClass.Mystic)]
        [TestCase(eCharacterClass.Hunter, eCharacterClass.MidgardRogue)]
        [TestCase(eCharacterClass.Hero, eCharacterClass.Guardian)]
        [TestCase(eCharacterClass.Bard, eCharacterClass.Naturalist)]
        [TestCase(eCharacterClass.Valewalker, eCharacterClass.Forester)]
        public void FreshBotsStartAsTheBaseClassOfTheirPlannedClass(eCharacterClass planned, eCharacterClass start)
        {
            Properties.CLASSIC165_MODE = true;
            var record = new OfflineWorldBotRecord { BotId = 987654321, ClassId = (int)planned, ClassName = planned.ToString(), Level = 1 };
            Assert.That(BaseClassBots165.PrepareFresh(record), Is.True);
            Assert.That(record.ClassId, Is.EqualTo((int)start));
            Assert.That(record.PlannedClassId, Is.EqualTo((int)planned));
            Assert.That(BaseClassBots165.SpecClass(record), Is.EqualTo(planned));
            Assert.That(BaseClassBots165.PrepareFresh(record), Is.False, "only once");
        }

        [Test]
        public void ExistingAndNormalModeBotsKeepTheirClass()
        {
            Properties.CLASSIC165_MODE = true;
            var levelled = new OfflineWorldBotRecord { BotId = 987654322, ClassId = (int)eCharacterClass.Armsman, Level = 7 };
            Assert.That(BaseClassBots165.PrepareFresh(levelled), Is.False);
            Properties.CLASSIC165_MODE = false;
            var fresh = new OfflineWorldBotRecord { BotId = 987654323, ClassId = (int)eCharacterClass.Armsman, Level = 1 };
            Assert.That(BaseClassBots165.PrepareFresh(fresh), Is.False);
            Assert.That(fresh.ClassId, Is.EqualTo((int)eCharacterClass.Armsman));
        }

        // Period Hunter data (Prima guide, quoted in CharmSpellHandler): caster level, pet level reached, spell % and cap
        [TestCase(17, 13, 80, 16)]
        [TestCase(22, 17, 80, 25)]
        [TestCase(11, 8, 80, 10)]
        [TestCase(44, 36, 82, 41)]
        [TestCase(50, 41, 82, 41)]
        public void HunterCharmsReachThePeriodPetLevels(int casterLevel, int petLevel, int percent, int cap)
        {
            Assert.That(DOL.GS.Spells.CharmSpellHandler.TooStrong165(cap, percent, casterLevel, petLevel), Is.False);
            Assert.That(DOL.GS.Spells.CharmSpellHandler.TooStrong165(cap, percent, casterLevel, petLevel + 1), Is.True);
        }

        [Test]
        public void CharmResistFollowsTheOriginalFormulas()
        {
            Assert.That(DOL.GS.Spells.CharmSpellHandler.ResistChance165(false, 50, 50, 50), Is.EqualTo(15));
            Assert.That(DOL.GS.Spells.CharmSpellHandler.ResistChance165(true, 45, 45, 45), Is.EqualTo(10));   // 30 + 15 - 45 = 0
            Assert.That(DOL.GS.Spells.CharmSpellHandler.ResistChance165(true, 45, 45, 40), Is.EqualTo(1));
            Assert.That(DOL.GS.Spells.CharmSpellHandler.ResistChance165(true, 45, 45, 50), Is.EqualTo(85));   // 10 + 25 x 3
            Assert.That(DOL.GS.Spells.CharmSpellHandler.ResistChance165(true, 50, 50, 60), Is.EqualTo(99));
        }
}
}
