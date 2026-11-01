using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using DOL.Database;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Serves the launcher's Bot Inspector: one complete, read-only description of one bot, on demand.
/// The launcher writes "bot-inspect.request" next to the server executable (a new request id and a
/// bot id), the timer below answers it in state-165/bot-inspect.json. The same file-based broker as
/// <see cref="AutonomousBotDashboard"/>, so nothing new listens on the network.
///
///  - a spawned bot is read from its live actor (stats, effects, in-memory inventory);
///  - a bot that is not in the world is described from its last saved OfflineWorldBotRecord and
///    persisted inventory rows, clearly marked as a saved state.
///
/// Nothing is written: this must never disturb the save pipeline or the AI. When the bot is live the
/// capture runs on the inspector's own timer thread against values the game loop publishes; inventory
/// and effect collections are read through their locking accessors.
/// </summary>
public static class AutonomousBotInspector
{
    public const int RequestPollIntervalMilliseconds = 250;
    private const int RequestIdMaxLength = 128;

    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBotInspector));
    private static Timer _timer;
    private static int _writing;
    private static string _lastRequestId = string.Empty;

    public static string FilePath => Classic165Mode.StatePath("bot-inspect.json");
    public static string RequestPath => Path.Combine(AppContext.BaseDirectory, "bot-inspect.request");

    public sealed record StatRow(string Name, int Base, int Items, int Buffs, int Total);
    public sealed record SpecRow(string Name, int Level, bool Trainable);
    public sealed record AbilityRow(string Name, int Level, string KeyName, string Description);
    public sealed record ItemRow(string Slot, string Name, string Type, int Level, int Quality, int Count,
        int ConditionPercent, int DurabilityPercent, int SellCopper, long Value, string Details);
    public sealed record EffectRow(string Name, bool Positive, int RemainingSeconds);
    public sealed record ListingRow(string ItemName, int Count, int PriceCopper, string Realm, string Expires);

    public sealed record Snapshot(
        DateTime UpdatedUtc, string RequestId, long BotId, bool Found, bool Online, string Error,
        string Name, string ClassName, string RaceName, string Gender, string RealmName,
        int Level, long Experience, long ExperienceFloor, long ExperienceNext, string ExperienceToNext,
        long RealmPoints, int RealmLevel, long MoneyCopper, string Money,
        int Health, int MaxHealth, int Mana, int MaxMana, int Endurance, int MaxEndurance,
        string ZoneName, int RegionId, int ZoneId, int X, int Y, int Z,
        bool IsAlive, bool InCombat, string Activity, string CurrentGoal, string TargetName,
        string TravelDestination, string ObjectiveProgress, string ObjectiveKind, string ObjectivePhase,
        string Guild, string Group, string Target, int DeathCount, int RecoveryCount, bool IsHiredCompanion,
        string Craft, int UnspentSpecPoints, int SpecPointsSpent, int RealmAbilityPoints,
        int TrainedRealmAbilityPoints, string BuildPlan, string LastSavedUtc, string LastUpdateUtc,
        int SpellCount, int StyleCount, string Warning,
        StatRow[] Stats, SpecRow[] Specs, AbilityRow[] Abilities, ItemRow[] Equipment, ItemRow[] Bag,
        ItemRow[] Vault, ListingRow[] Listings, EffectRow[] Effects);

    [GameServerStartedEvent]
    public static void Start(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _lastRequestId = string.Empty;
        _timer = new Timer(_ => PollForRequest(), null, 0, RequestPollIntervalMilliseconds);
    }

    [GameServerStoppedEvent]
    public static void Stop(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _timer = null;
    }

    private static void PollForRequest()
    {
        try
        {
            if (!File.Exists(RequestPath))
                return;

            string request;
            using (var stream = new FileStream(RequestPath, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                request = reader.ReadToEnd().Trim();

            if (!TryParseRequest(request, out string requestId, out long botId) ||
                string.Equals(requestId, _lastRequestId, StringComparison.Ordinal))
                return;

            if (Publish(requestId, botId))
                _lastRequestId = requestId;
        }
        catch (IOException)
        {
            // The launcher's atomic request replacement can overlap this tiny read window.
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warn("Bot Inspector request could not be read", exception);
        }
    }

    /// <summary>A request is "&lt;request id&gt;|&lt;bot id&gt;". Exposed for tests.</summary>
    public static bool TryParseRequest(string request, out string requestId, out long botId)
    {
        requestId = string.Empty;
        botId = 0;
        if (string.IsNullOrWhiteSpace(request) || request.Length > RequestIdMaxLength + 24)
            return false;

        int separator = request.IndexOf('|');
        if (separator <= 0 || separator >= request.Length - 1)
            return false;

        requestId = request[..separator].Trim();
        string botText = request[(separator + 1)..].Trim();
        return requestId.Length is > 0 and <= RequestIdMaxLength &&
               long.TryParse(botText, out botId) && botId > 0;
    }

    private static bool Publish(string requestId, long botId)
    {
        if (Interlocked.Exchange(ref _writing, 1) != 0)
            return false;

        try
        {
            Snapshot snapshot;
            try
            {
                snapshot = Capture(botId) with { RequestId = requestId, UpdatedUtc = DateTime.UtcNow };
            }
            catch (Exception exception)
            {
                Log.Warn($"Bot Inspector could not describe bot {botId}", exception);
                snapshot = new Snapshot(DateTime.UtcNow, requestId, botId, true, false,
                    $"The server could not read this bot: {exception.Message}",
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    0, 0, 0, 0, string.Empty, 0, 0, 0, string.Empty,
                    0, 0, 0, 0, 0, 0,
                    string.Empty, 0, 0, 0, 0, 0,
                    false, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, false,
                    string.Empty, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, 0, 0, string.Empty,
                    [], [], [], [], [], [], [], []);
            }

            string temporaryPath = FilePath + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write,
                       FileShare.Read, 64 * 1024, FileOptions.SequentialScan))
            {
                JsonSerializer.Serialize(stream, snapshot);
            }
            File.Move(temporaryPath, FilePath, true);
            return true;
        }
        catch (Exception exception)
        {
            Log.Warn("Bot Inspector snapshot failed", exception);
            return false;
        }
        finally
        {
            Volatile.Write(ref _writing, 0);
        }
    }

    /// <summary>Builds the read-only description of one bot. Exposed so tests can inspect it directly.</summary>
    public static Snapshot Capture(long botId)
    {
        if (botId <= 0)
            return Empty(botId, "No bot was chosen.");

        if (AutonomousBotRegistry.TryGet(botId, out GameBot live) && live?.PersistentRecord != null)
            return CaptureLive(live);

        return CaptureStored(botId);
    }

    private static Snapshot Empty(long botId, string error) =>
        new(DateTime.UtcNow, string.Empty, botId, true, false, error,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            0, 0, 0, 0, string.Empty, 0, 0, 0, string.Empty,
            0, 0, 0, 0, 0, 0,
            string.Empty, 0, 0, 0, 0, 0, false, false, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            0, 0, false, string.Empty, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, 0, 0,
            string.Empty, [], [], [], [], [], [], [], []);

    private static Snapshot CaptureLive(GameBot bot)
    {
        OfflineWorldBotRecord record = bot.PersistentRecord;
        var warnings = new List<string>();
        var equipment = new List<ItemRow>();
        var bag = new List<ItemRow>();
        var vault = new List<ItemRow>();
        var effects = new List<EffectRow>();
        var stats = new List<StatRow>();
        var specs = new List<SpecRow>();
        var abilities = new List<AbilityRow>();
        int spellCount = 0;
        int styleCount = 0;

        try
        {
            foreach (DbInventoryItem item in bot.Inventory.EquippedItems)
                equipment.Add(Describe(item, (eInventorySlot)item.SlotPosition));
            foreach (DbInventoryItem item in bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                bag.Add(Describe(item, (eInventorySlot)item.SlotPosition));
            foreach (DbInventoryItem item in bot.Inventory.GetItemRange(eInventorySlot.FirstVault, eInventorySlot.LastVault))
                vault.Add(Describe(item, (eInventorySlot)item.SlotPosition));
        }
        catch (Exception exception)
        {
            warnings.Add($"inventory: {exception.Message}");
        }

        try
        {
            foreach ((string name, eStat stat, eProperty property) in StatDefinitions)
            {
                int baseValue = bot.GetBaseStat(stat);
                int items = SafeValue(() => bot.GetModifiedFromItems(property));
                int buffs = SafeValue(() => bot.GetModifiedFromBuffs(property));
                int total = SafeValue(() => bot.GetModified(property), baseValue + items + buffs);
                stats.Add(new StatRow(name, baseValue, items, buffs, total));
            }
        }
        catch (Exception exception)
        {
            warnings.Add($"stats: {exception.Message}");
        }

        try
        {
            foreach (Specialization spec in bot.GetSpecList())
                specs.Add(new SpecRow(spec.Name ?? spec.KeyName, spec.Level, spec.Trainable));
            specs.Sort((first, second) =>
            {
                int byLevel = second.Level.CompareTo(first.Level);
                return byLevel != 0 ? byLevel : string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception exception)
        {
            warnings.Add($"specializations: {exception.Message}");
        }

        try
        {
            foreach (object entry in bot.GetAllAbilities())
            {
                if (entry is not Ability ability)
                    continue;
                string description = string.Empty;
                try
                {
                    description = string.Join(" ", ability.DelveInfo);
                }
                catch
                {
                    // An ability without a description is still worth listing.
                }
                if (description.Length > 200)
                    description = description[..200] + "…";
                abilities.Add(new AbilityRow(ability.Name, ability.Level, ability.KeyName, description));
            }
            abilities.Sort((first, second) => string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            warnings.Add($"abilities: {exception.Message}");
        }

        try
        {
            spellCount = bot.Spells?.Count ?? 0;
            styleCount = bot.Styles?.Count ?? 0;
        }
        catch (Exception exception)
        {
            warnings.Add($"spells/styles: {exception.Message}");
        }

        try
        {
            EffectListComponent effectList = ((IGamePlayer)bot).EffectListComponent;
            foreach (ECSGameSpellEffect effect in effectList.GetSpellEffects())
            {
                string name = effect?.Name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                long remaining = effect.GetRemainingTimeForClient();
                effects.Add(new EffectRow(name, effect.HasPositiveEffect, (int)Math.Max(0, remaining / 1000)));
            }
            effects.Sort((first, second) => second.RemainingSeconds.CompareTo(first.RemainingSeconds));
        }
        catch (Exception exception)
        {
            warnings.Add($"effects: {exception.Message}");
        }

        List<ListingRow> listings = ReadListings(bot.DatabaseID, warnings);
        int spentSpecPoints = SpecPointsSpent(specs);
        int availableSpecPoints = 0;
        try
        {
            availableSpecPoints = bot.CompanionSpecPoints;
        }
        catch (Exception exception)
        {
            warnings.Add($"spec points: {exception.Message}");
        }

        int realmAbilityPoints = 0;
        int trainedRealmAbilityPoints = 0;
        try
        {
            trainedRealmAbilityPoints = AutonomousBotRealmAbilities.TrainedPoints(bot);
            realmAbilityPoints = AutonomousBotRealmAbilities.Points(bot.Level, bot.RealmLevel);
        }
        catch (Exception exception)
        {
            warnings.Add($"realm abilities: {exception.Message}");
        }

        string craft = DescribeCraft(record);
        return Build(
            botId: bot.DatabaseID,
            found: true,
            online: true,
            error: string.Empty,
            name: bot.Name,
            className: record.ClassName ?? bot.CharacterClass?.Name ?? string.Empty,
            raceName: record.RaceName ?? string.Empty,
            gender: GenderName(record.Gender),
            realmName: GlobalConstants.RealmToName(bot.Realm),
            level: bot.Level,
            experience: bot.Experience,
            realmPoints: bot.AutonomousRealmPoints,
            realmLevel: bot.RealmLevel,
            moneyCopper: record.MoneyCopper,
            health: SafeValue(() => bot.Health), maxHealth: SafeValue(() => bot.MaxHealth),
            mana: SafeValue(() => bot.Mana), maxMana: SafeValue(() => bot.MaxMana),
            endurance: SafeValue(() => bot.Endurance), maxEndurance: SafeValue(() => bot.MaxEndurance),
            zoneName: bot.CurrentZone?.Description ?? record.ZoneName ?? string.Empty,
            regionId: bot.CurrentRegionID,
            zoneId: bot.CurrentZone?.ID ?? 0,
            x: bot.X, y: bot.Y, z: bot.Z,
            isAlive: bot.IsAlive, inCombat: SafeValueBool(() => bot.InCombat),
            activity: record.Activity ?? string.Empty,
            currentGoal: record.CurrentGoal ?? string.Empty,
            targetName: record.TargetName ?? string.Empty,
            travelDestination: record.TravelDestination ?? string.Empty,
            objectiveProgress: record.ObjectiveProgress ?? string.Empty,
            objectiveKind: record.ObjectiveKind ?? string.Empty,
            objectivePhase: record.ObjectivePhase ?? string.Empty,
            guild: bot.GuildName ?? string.Empty,
            group: DescribeGroup(bot),
            target: bot.TargetObject?.Name ?? string.Empty,
            deathCount: record.DeathCount,
            recoveryCount: record.RecoveryCount,
            isHired: SafeValueBool(() => HiredCompanions165.IsHired(bot)),
            craft: craft,
            unspentSpecPoints: availableSpecPoints,
            spentSpecPoints: spentSpecPoints,
            realmAbilityPoints: realmAbilityPoints,
            trainedRealmAbilityPoints: trainedRealmAbilityPoints,
            buildPlan: record.SerializedBuildPlan ?? string.Empty,
            lastSavedUtc: record.LastSavedUtc ?? string.Empty,
            lastUpdateUtc: record.LastUpdateUtc ?? string.Empty,
            spellCount: spellCount,
            styleCount: styleCount,
            warning: string.Join(" • ", warnings),
            stats: stats,
            specs: specs,
            abilities: abilities,
            equipment: equipment,
            bag: bag,
            vault: vault,
            listings: listings,
            effects: effects);
    }

    private static Snapshot CaptureStored(long botId)
    {
        OfflineWorldBotRecord record = DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(botId));
        if (record == null)
            return Empty(botId, "No bot with that id exists in this world.");

        var warnings = new List<string> { "This bot is not in the world right now: its last saved state is shown." };
        string ownerId = AutonomousBotEconomy.GetOwnerId(botId);
        var equipment = new List<ItemRow>();
        var bag = new List<ItemRow>();
        var vault = new List<ItemRow>();
        try
        {
            var stored = new BotInventory(ownerId).SelectPersonalItems(ownerId) as IList;
            foreach (object entry in stored ?? Array.Empty<DbInventoryItem>())
            {
                if (entry is not DbInventoryItem item || item.OwnerLot != 0)
                    continue;
                var slot = (eInventorySlot)item.SlotPosition;
                if (slot is >= eInventorySlot.MinEquipable and <= eInventorySlot.MaxEquipable)
                    equipment.Add(Describe(item, slot));
                else if (slot is >= eInventorySlot.FirstBackpack and <= eInventorySlot.LastBackpack)
                    bag.Add(Describe(item, slot));
                else if (slot is >= eInventorySlot.FirstVault and <= eInventorySlot.LastVault)
                    vault.Add(Describe(item, slot));
            }
        }
        catch (Exception exception)
        {
            warnings.Add($"inventory: {exception.Message}");
        }

        var specs = new List<SpecRow>();
        foreach (string entry in (record.SerializedSpecs ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = entry.Split('|', 2);
            if (parts.Length == 2 && int.TryParse(parts[1], out int level))
            {
                bool trainable = !parts[0].StartsWith("Realm", StringComparison.OrdinalIgnoreCase) &&
                                 !parts[0].StartsWith("Champion", StringComparison.OrdinalIgnoreCase);
                specs.Add(new SpecRow(parts[0], level, trainable));
            }
        }
        specs.Sort((first, second) =>
        {
            int byLevel = second.Level.CompareTo(first.Level);
            return byLevel != 0 ? byLevel : string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
        });

        int realmLevel = AutonomousBotRealmPointRewards.RealmLevelFor(record.RealmPoints);
        int realmAbilityPoints = AutonomousBotRealmAbilities.Points(record.Level, realmLevel);
        int trainedRealmAbilityPoints = GameBot.ParseTrainedRealmPoints(record.SerializedAbilities);
        List<ListingRow> listings = ReadListings(botId, warnings);
        return Build(
            botId: record.BotId,
            found: true,
            online: false,
            error: string.Empty,
            name: record.Name ?? string.Empty,
            className: record.ClassName ?? string.Empty,
            raceName: record.RaceName ?? string.Empty,
            gender: GenderName(record.Gender),
            realmName: GlobalConstants.RealmToName((eRealm)record.Realm),
            level: record.Level,
            experience: record.Experience,
            realmPoints: record.RealmPoints,
            realmLevel: realmLevel,
            moneyCopper: record.MoneyCopper,
            health: record.Health, maxHealth: 0,
            mana: record.Mana, maxMana: 0,
            endurance: record.Endurance, maxEndurance: 0,
            zoneName: record.ZoneName ?? string.Empty,
            regionId: record.RegionId,
            zoneId: record.ZoneId,
            x: record.X, y: record.Y, z: record.Z,
            isAlive: record.IsAlive, inCombat: false,
            activity: record.Activity ?? string.Empty,
            currentGoal: record.CurrentGoal ?? string.Empty,
            targetName: record.TargetName ?? string.Empty,
            travelDestination: record.TravelDestination ?? string.Empty,
            objectiveProgress: record.ObjectiveProgress ?? string.Empty,
            objectiveKind: record.ObjectiveKind ?? string.Empty,
            objectivePhase: record.ObjectivePhase ?? string.Empty,
            guild: string.Empty,
            group: string.Empty,
            target: string.Empty,
            deathCount: record.DeathCount,
            recoveryCount: record.RecoveryCount,
            isHired: HiredCompanions165.IsHired(botId),
            craft: DescribeCraft(record),
            unspentSpecPoints: Math.Max(0, record.UnspentSpecPoints),
            spentSpecPoints: SpecPointsSpent(specs),
            realmAbilityPoints: realmAbilityPoints,
            trainedRealmAbilityPoints: trainedRealmAbilityPoints,
            buildPlan: record.SerializedBuildPlan ?? string.Empty,
            lastSavedUtc: record.LastSavedUtc ?? string.Empty,
            lastUpdateUtc: record.LastUpdateUtc ?? string.Empty,
            spellCount: 0,
            styleCount: 0,
            warning: string.Join(" • ", warnings),
            stats: [],
            specs: specs,
            abilities: [],
            equipment: equipment,
            bag: bag,
            vault: vault,
            listings: listings,
            effects: []);
    }

    private static Snapshot Build(
        long botId, bool found, bool online, string error,
        string name, string className, string raceName, string gender, string realmName,
        int level, long experience, long realmPoints, int realmLevel, long moneyCopper,
        int health, int maxHealth, int mana, int maxMana, int endurance, int maxEndurance,
        string zoneName, int regionId, int zoneId, int x, int y, int z,
        bool isAlive, bool inCombat, string activity, string currentGoal, string targetName,
        string travelDestination, string objectiveProgress, string objectiveKind, string objectivePhase,
        string guild, string group, string target, int deathCount, int recoveryCount, bool isHired,
        string craft, int unspentSpecPoints, int spentSpecPoints, int realmAbilityPoints,
        int trainedRealmAbilityPoints, string buildPlan, string lastSavedUtc, string lastUpdateUtc,
        int spellCount, int styleCount, string warning,
        List<StatRow> stats, List<SpecRow> specs, List<AbilityRow> abilities, List<ItemRow> equipment,
        List<ItemRow> bag, List<ItemRow> vault, List<ListingRow> listings, List<EffectRow> effects)
    {
        long experienceFloor = experience > 0 && level > 1 ? GamePlayer.GetExperienceAmountForLevel(level - 1) : 0;
        long experienceNext = level >= GamePlayer.MAX_LEVEL ? 0 : GamePlayer.GetExperienceAmountForLevel(level);
        string experienceToNext = string.Empty;
        if (experienceNext > experienceFloor && experienceNext > 0)
        {
            long span = experienceNext - experienceFloor;
            long gained = Math.Clamp(experience - experienceFloor, 0, span);
            experienceToNext = $"{gained * 100 / span}% ({Math.Max(0, experienceNext - experience):N0} xp left)";
        }
        else if (level >= GamePlayer.MAX_LEVEL)
        {
            experienceToNext = "maximum level";
        }

        return new Snapshot(
            DateTime.UtcNow, string.Empty, botId, found, online, error,
            name, className, raceName, gender, realmName,
            level, experience, experienceFloor, experienceNext, experienceToNext,
            realmPoints, realmLevel, moneyCopper, FormatMoney(moneyCopper),
            health, maxHealth, mana, maxMana, endurance, maxEndurance,
            zoneName, regionId, zoneId, x, y, z,
            isAlive, inCombat, activity, currentGoal, targetName,
            travelDestination, objectiveProgress, objectiveKind, objectivePhase,
            guild, group, target, deathCount, recoveryCount, isHired,
            craft, unspentSpecPoints, spentSpecPoints, realmAbilityPoints,
            trainedRealmAbilityPoints, buildPlan, lastSavedUtc, lastUpdateUtc,
            spellCount, styleCount, warning,
            stats.ToArray(), specs.ToArray(), abilities.ToArray(), equipment.ToArray(), bag.ToArray(),
            vault.ToArray(), listings.ToArray(), effects.ToArray());
    }

    /// <summary>Spec points a bot has spent: every raise of a line costs the line's new level.</summary>
    public static int SpecPointsSpent(IEnumerable<SpecRow> specs) =>
        specs.Where(spec => spec.Trainable && spec.Level > 1)
            .Sum(spec => Math.Max(0, spec.Level * (spec.Level + 1) / 2 - 1));

    private static readonly (string Name, eStat Stat, eProperty Property)[] StatDefinitions =
    [
        ("Strength", eStat.STR, eProperty.Strength),
        ("Dexterity", eStat.DEX, eProperty.Dexterity),
        ("Constitution", eStat.CON, eProperty.Constitution),
        ("Quickness", eStat.QUI, eProperty.Quickness),
        ("Intelligence", eStat.INT, eProperty.Intelligence),
        ("Piety", eStat.PIE, eProperty.Piety),
        ("Empathy", eStat.EMP, eProperty.Empathy),
        ("Charisma", eStat.CHR, eProperty.Charisma),
    ];

    private static int SafeValue(Func<int> read, int fallback = 0)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Money.GetString needs the language tables; the inspector still works without them.</summary>
    private static string FormatMoney(long copper)
    {
        try
        {
            return Money.GetString(copper);
        }
        catch
        {
            return $"{copper:N0} copper";
        }
    }

    private static bool SafeValueBool(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return false;
        }
    }

    private static string DescribeGroup(GameBot bot)
    {
        try
        {
            Group group = bot.Group;
            return group == null ? string.Empty : group.Name;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DescribeCraft(OfflineWorldBotRecord record)
    {
        if (record == null)
            return "none";
        int skill = record.CraftSkill;
        if (skill == -1)
            return "none";
        if (skill <= 0)
            return "not given yet";
        string name = Enum.IsDefined(typeof(eCraftingSkill), skill) ? ((eCraftingSkill)skill).ToString() : $"skill {skill}";
        return $"{name} {record.CraftLevel}";
    }

    private static string GenderName(int gender) => gender switch
    {
        1 => "Male",
        2 => "Female",
        _ => "—",
    };

    private static ItemRow Describe(DbInventoryItem item, eInventorySlot slot)
    {
        DbItemTemplate template = null;
        try
        {
            template = item?.Template;
        }
        catch
        {
            // A missing template must not hide the rest of the inventory.
        }

        string name = template?.Name ?? item?.Name ?? "Unknown item";
        string type = template == null ? string.Empty : GlobalConstants.ObjectTypeToName(template.Object_Type);
        int level = template?.Level ?? 0;
        int quality = template?.Quality ?? 0;
        string details = string.Empty;
        if (template != null)
        {
            var parts = new List<string>();
            if (item.IsCrafted)
                parts.Add("crafted");
            if (item.IsROG)
                parts.Add("ROG");
            if (template.Object_Type is >= 10 and <= 14)
                parts.Add($"{template.DPS_AF:0.0} DPS · {template.SPD_ABS / 100.0:0.0}s");
            else if (template.Object_Type is >= 21 and <= 32)
                parts.Add($"AF {template.DPS_AF} · {template.SPD_ABS}% abs");
            if (template.IsMagical)
                parts.Add("magical");
            if (item.PoisonSpellID > 0)
                parts.Add(item.PoisonCharges > 0 ? $"poisoned ({item.PoisonCharges} charges)" : "poisoned");
            if (item.Charges > 0)
                parts.Add($"{item.Charges} charges");
            details = string.Join(" · ", parts);
        }

        return new ItemRow(
            SlotLabel(slot),
            name,
            type,
            level,
            quality,
            item?.Count ?? 0,
            item?.ConditionPercent ?? 0,
            item?.DurabilityPercent ?? 0,
            item?.SellPrice ?? 0,
            template?.Price ?? 0,
            details);
    }

    /// <summary>The equipment slot's player-facing name; backpack and vault slots keep their number.</summary>
    public static string SlotLabel(eInventorySlot slot) => slot switch
    {
        eInventorySlot.RightHandWeapon => "Right hand",
        eInventorySlot.LeftHandWeapon => "Left hand",
        eInventorySlot.TwoHandWeapon => "Two-handed",
        eInventorySlot.DistanceWeapon => "Ranged",
        eInventorySlot.HeadArmor => "Head",
        eInventorySlot.HandsArmor => "Hands",
        eInventorySlot.FeetArmor => "Feet",
        eInventorySlot.Jewelry => "Jewelry",
        eInventorySlot.TorsoArmor => "Torso",
        eInventorySlot.Cloak => "Cloak",
        eInventorySlot.LegsArmor => "Legs",
        eInventorySlot.ArmsArmor => "Arms",
        eInventorySlot.Neck => "Neck",
        eInventorySlot.Waist => "Belt",
        eInventorySlot.LeftBracer => "Left bracer",
        eInventorySlot.RightBracer => "Right bracer",
        eInventorySlot.LeftRing => "Left ring",
        eInventorySlot.RightRing => "Right ring",
        eInventorySlot.Mythical => "Mythical",
        eInventorySlot.Horse => "Horse",
        eInventorySlot.HorseArmor => "Horse armour",
        eInventorySlot.HorseBarding => "Horse barding",
        >= eInventorySlot.FirstQuiver and <= eInventorySlot.FourthQuiver => "Quiver",
        >= eInventorySlot.FirstBackpack and <= eInventorySlot.LastBackpack =>
            $"Bag {(slot - eInventorySlot.FirstBackpack) / 10 + 1} item {(slot - eInventorySlot.FirstBackpack) % 10 + 1}",
        >= eInventorySlot.FirstVault and <= eInventorySlot.LastVault => $"Vault {slot}",
        _ => slot.ToString(),
    };

    /// <summary>The bot's Realm Exchange listings: persisted Inventory rows owned by it, in an exchange lot.</summary>
    private static List<ListingRow> ReadListings(long botId, List<string> warnings)
    {
        var listings = new List<ListingRow>();
        if (botId <= 0)
            return listings;

        try
        {
            string ownerId = AutonomousBotEconomy.GetOwnerId(botId);
            var rows = DOLDB<DbInventoryItem>.SelectObjects(DB.Column("OwnerID").IsEqualTo(ownerId));
            foreach (DbInventoryItem item in rows ?? [])
            {
                if (item == null || !RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot))
                    continue;
                string realm = item.OwnerLot switch
                {
                    RealmExchangeBroker.AlbionOwnerLot => "Albion",
                    RealmExchangeBroker.MidgardOwnerLot => "Midgard",
                    RealmExchangeBroker.HiberniaOwnerLot => "Hibernia",
                    _ => "Unknown",
                };
                listings.Add(new ListingRow(
                    item.Name ?? "Unknown item",
                    item.Count,
                    item.SellPrice,
                    realm,
                    RealmExchangeExpiry.Describe(item, DateTime.UtcNow)));
            }
            listings.Sort((first, second) => string.Compare(first.ItemName, second.ItemName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            warnings.Add($"exchange listings: {exception.Message}");
        }

        return listings;
    }
}
