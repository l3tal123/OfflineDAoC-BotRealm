using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Launcher dashboard tab: the Bot Inspector. Right-click a bot in the Active Population list and choose
/// "Inspect bot" (double-clicking the row now shows the bot on the World Map) and this tab opens on that bot; the BOT
/// selector also lists every world bot. The server answers one on-demand, read-only description of the bot - money,
/// stats, equipment, backpack, vault, specializations and trained points, abilities, Realm Exchange listings, active
/// effects, activity, position and progress. A bot that is not in the world is described from its last saved state.
/// Nothing is polled: opening the tab after choosing another bot, changing the selector or pressing REFRESH sends one
/// request. SHOW ON MAP centres the Live World Map on the selected bot.
/// </summary>
internal sealed partial class MainForm
{
    // The server polls bot-inspect.request every 250 ms; one answer per request id. Matching the live bot
    // snapshot's patience keeps a slow save cycle from looking like a failure.
    internal const int BotInspectWaitMilliseconds = 4_000;

    private readonly BindingSource _inspectStatSource = new();
    private readonly BindingSource _inspectEquipmentSource = new();
    private readonly BindingSource _inspectBagSource = new();
    private readonly BindingSource _inspectVaultSource = new();
    private readonly BindingSource _inspectSpecSource = new();
    private readonly BindingSource _inspectAbilitySource = new();
    private readonly BindingSource _inspectListingSource = new();
    private readonly BindingSource _inspectEffectSource = new();
    private readonly DataGridView _inspectStatGrid = new();
    private readonly DataGridView _inspectEquipmentGrid = new();
    private readonly DataGridView _inspectBagGrid = new();
    private readonly DataGridView _inspectVaultGrid = new();
    private readonly DataGridView _inspectSpecGrid = new();
    private readonly DataGridView _inspectAbilityGrid = new();
    private readonly DataGridView _inspectListingGrid = new();
    private readonly DataGridView _inspectEffectGrid = new();
    private readonly TextBox _inspectOverview = new();
    private readonly ComboBox _inspectBot = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly Label _inspectStatus = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = DaocTheme.Muted,
        BackColor = DaocTheme.Tint(48, 38, 26),
        Font = new Font("Georgia", 8.5f, FontStyle.Italic),
        Padding = new Padding(8, 0, 0, 0),
    };
    private Button? _inspectButton;
    private Button? _refreshInspectButton;
    private Button? _showOnMapButton;
    private TabPage? _botInspectorTab;
    private TabPage? _inspectOverviewTab;
    private TabPage? _inspectStatsTab;
    private TabPage? _inspectEquipmentTab;
    private TabPage? _inspectBagTab;
    private TabPage? _inspectVaultTab;
    private TabPage? _inspectSpecTab;
    private TabPage? _inspectAbilityTab;
    private TabPage? _inspectListingTab;
    private TabPage? _inspectEffectTab;
    private bool _refreshingInspector;
    private bool _updatingInspectorChoices;
    private long _inspectedBotId;
    private DateTime _inspectedAt;

    private Control BuildBotInspectorPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(7),
            BackColor = DaocTheme.Tint(48, 38, 26),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = DaocTheme.Tint(69, 51, 31) };
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "✦  BOT INSPECTOR  ✦    money • stats • gear • bag • specs • training",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Georgia", 10f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            BackColor = DaocTheme.Tint(69, 51, 31),
            Padding = new Padding(11, 0, 0, 0),
            BorderStyle = BorderStyle.Fixed3D,
        });

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 1024,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 6, 0),
            BackColor = Color.Transparent,
        };
        controls.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 6, 6, 0),
            Text = "BOT",
            Font = new Font("Georgia", 8.25f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
        });
        StyleInput(_inspectBot);
        _inspectBot.SelectedIndexChanged += async (_, _) =>
        {
            if (!_updatingInspectorChoices)
                await RefreshBotInspectorAsync();
        };
        controls.Controls.Add(_inspectBot);
        _inspectButton = ActionButton("INSPECT BOT", DaocTheme.Tint(126, 104, 67));
        _inspectButton.Width = 132;
        _inspectButton.Click += async (_, _) => await RefreshBotInspectorAsync();
        controls.Controls.Add(_inspectButton);
        _refreshInspectButton = ActionButton("REFRESH", DaocTheme.Tint(96, 82, 56));
        _refreshInspectButton.Width = 110;
        _refreshInspectButton.Click += async (_, _) => await RefreshBotInspectorAsync();
        controls.Controls.Add(_refreshInspectButton);
        _showOnMapButton = ActionButton("SHOW ON MAP", DaocTheme.Tint(96, 82, 56));
        _showOnMapButton.Width = 128;
        _helpTip.SetToolTip(_showOnMapButton, "Open the Live World Map centred on the selected bot and follow it.");
        _showOnMapButton.Click += async (_, _) => await ShowInspectedBotOnMapAsync();
        controls.Controls.Add(_showOnMapButton);
        header.Controls.Add(controls);

        _inspectOverview.Multiline = true;
        _inspectOverview.ReadOnly = true;
        _inspectOverview.Dock = DockStyle.Fill;
        _inspectOverview.BorderStyle = BorderStyle.Fixed3D;
        _inspectOverview.BackColor = DaocTheme.Tint(24, 22, 19);
        _inspectOverview.ForeColor = DaocTheme.Parchment;
        _inspectOverview.Font = new Font("Consolas", 9.5f);
        _inspectOverview.ScrollBars = ScrollBars.Vertical;
        _inspectOverview.WordWrap = false;
        _inspectOverview.Text = "Right-click a bot in Active Population and choose \"Inspect bot\", or choose one above. Double-clicking a population row shows that bot on the World Map; SHOW ON MAP does the same for the bot selected here.";

        var body = new DaocTabControl { Dock = DockStyle.Fill };
        _inspectOverviewTab = InspectTab("Overview", _inspectOverview);
        _inspectStatsTab = InspectTab("Stats", ConfigureInspectGrid(_inspectStatGrid, _inspectStatSource,
            ("Stat", "Name", 130), ("Base", "Base", 70), ("Item bonus", "Items", 90), ("Buff bonus", "Buffs", 90), ("Total", "Total", 80)));
        _inspectEquipmentGrid.Columns.Add(TextColumn("Slot", "Slot", 130));
        _inspectEquipmentGrid.Columns.Add(TextColumn("Item", "Name", 280, DataGridViewAutoSizeColumnMode.Fill));
        _inspectEquipmentGrid.Columns.Add(TextColumn("Type", "Type", 90));
        _inspectEquipmentGrid.Columns.Add(TextColumn("Lvl", "Level", 50));
        _inspectEquipmentGrid.Columns.Add(TextColumn("Qlty", "Quality", 50));
        _inspectEquipmentGrid.Columns.Add(TextColumn("Con %", "ConditionPercent", 60));
        _inspectEquipmentGrid.Columns.Add(TextColumn("Dur %", "DurabilityPercent", 60));
        _inspectEquipmentGrid.Columns.Add(TextColumn("Value", "ValueText", 95));
        _inspectEquipmentGrid.Columns.Add(TextColumn("Notes", "Details", 240));
        _inspectEquipmentTab = InspectTab("Equipment", ConfigureInspectGrid(_inspectEquipmentGrid, _inspectEquipmentSource));
        _inspectBagGrid.Columns.Add(TextColumn("Slot", "Slot", 150));
        _inspectBagGrid.Columns.Add(TextColumn("Item", "Name", 260, DataGridViewAutoSizeColumnMode.Fill));
        _inspectBagGrid.Columns.Add(TextColumn("Type", "Type", 90));
        _inspectBagGrid.Columns.Add(TextColumn("Qty", "Count", 50));
        _inspectBagGrid.Columns.Add(TextColumn("Con %", "ConditionPercent", 60));
        _inspectBagGrid.Columns.Add(TextColumn("Dur %", "DurabilityPercent", 60));
        _inspectBagGrid.Columns.Add(TextColumn("Vendor", "SellText", 95));
        _inspectBagGrid.Columns.Add(TextColumn("Value", "ValueText", 95));
        _inspectBagGrid.Columns.Add(TextColumn("Notes", "Details", 220));
        _inspectBagTab = InspectTab("Backpack", ConfigureInspectGrid(_inspectBagGrid, _inspectBagSource));
        _inspectVaultGrid.Columns.Add(TextColumn("Slot", "Slot", 110));
        _inspectVaultGrid.Columns.Add(TextColumn("Item", "Name", 280, DataGridViewAutoSizeColumnMode.Fill));
        _inspectVaultGrid.Columns.Add(TextColumn("Type", "Type", 90));
        _inspectVaultGrid.Columns.Add(TextColumn("Qty", "Count", 50));
        _inspectVaultGrid.Columns.Add(TextColumn("Value", "ValueText", 95));
        _inspectVaultGrid.Columns.Add(TextColumn("Notes", "Details", 240));
        _inspectVaultTab = InspectTab("Vault", ConfigureInspectGrid(_inspectVaultGrid, _inspectVaultSource));
        _inspectSpecGrid.Columns.Add(TextColumn("Line", "Name", 260, DataGridViewAutoSizeColumnMode.Fill));
        _inspectSpecGrid.Columns.Add(TextColumn("Level", "Level", 70));
        _inspectSpecGrid.Columns.Add(TextColumn("Kind", "TrainableText", 110));
        _inspectSpecTab = InspectTab("Specialisation", ConfigureInspectGrid(_inspectSpecGrid, _inspectSpecSource));
        _inspectAbilityGrid.Columns.Add(TextColumn("Ability", "Name", 260, DataGridViewAutoSizeColumnMode.Fill));
        _inspectAbilityGrid.Columns.Add(TextColumn("Level", "Level", 55));
        _inspectAbilityGrid.Columns.Add(TextColumn("Key", "KeyName", 160));
        _inspectAbilityGrid.Columns.Add(TextColumn("Description", "Description", 420));
        _inspectAbilityTab = InspectTab("Abilities", ConfigureInspectGrid(_inspectAbilityGrid, _inspectAbilitySource));
        _inspectListingGrid.Columns.Add(TextColumn("Item", "ItemName", 260, DataGridViewAutoSizeColumnMode.Fill));
        _inspectListingGrid.Columns.Add(TextColumn("Qty", "Count", 55));
        _inspectListingGrid.Columns.Add(TextColumn("Price", "Price", 100));
        _inspectListingGrid.Columns.Add(TextColumn("Realm", "Realm", 85));
        _inspectListingGrid.Columns.Add(TextColumn("Expires / remaining", "Expires", 230));
        _inspectListingTab = InspectTab("Realm Exchange listings", ConfigureInspectGrid(_inspectListingGrid, _inspectListingSource));
        _inspectEffectGrid.Columns.Add(TextColumn("Effect", "Name", 300, DataGridViewAutoSizeColumnMode.Fill));
        _inspectEffectGrid.Columns.Add(TextColumn("Kind", "Kind", 90));
        _inspectEffectGrid.Columns.Add(TextColumn("Remaining", "Remaining", 110));
        _inspectEffectTab = InspectTab("Active effects", ConfigureInspectGrid(_inspectEffectGrid, _inspectEffectSource));

        foreach (TabPage page in new[]
                 {
                     _inspectOverviewTab!, _inspectStatsTab!, _inspectEquipmentTab!, _inspectBagTab!, _inspectVaultTab!,
                     _inspectSpecTab!, _inspectAbilityTab!, _inspectListingTab!, _inspectEffectTab!,
                 })
        {
            body.TabPages.Add(page);
        }

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(body, 0, 1);
        _inspectStatus.Text = "Choose a bot, then press INSPECT BOT. The server answers with its current state.";
        layout.Controls.Add(_inspectStatus, 0, 2);
        return layout;
    }

    private static TabPage InspectTab(string title, Control content) => new(title)
    {
        BackColor = DaocTheme.Panel,
        ForeColor = DaocTheme.Text,
        Padding = new Padding(3),
        Controls = { content },
    };

    /// <summary>Adds the named column pairs and makes every column read-only (the snapshot is the order).</summary>
    private static DataGridView ConfigureInspectGrid(DataGridView grid, BindingSource source,
        params (string Header, string Property, int Width)[] columns)
    {
        ConfigureExchangeGrid(grid, source);
        foreach ((string header, string property, int width) in columns)
            grid.Columns.Add(TextColumn(header, property, width));
        foreach (DataGridViewColumn column in grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        return grid;
    }

    /// <summary>Fills the inspector selector from the last Active Population snapshot.</summary>
    private void UpdateBotInspectorChoices()
    {
        if (_inspectBot.IsDisposed)
            return;

        long previous = SelectedInspectChoice()?.BotId ?? 0;
        List<InspectChoice> choices = _bots
            .Where(bot => bot.BotId is > 0)
            .OrderByDescending(bot => bot.IsOnline)
            .ThenBy(bot => bot.Realm)
            .ThenByDescending(bot => bot.Level)
            .ThenBy(bot => bot.Name)
            .Select(bot => new InspectChoice(bot.BotId!.Value, bot))
            .ToList();
        if (choices.Count == _inspectBot.Items.Count && choices.Select(choice => choice.ToString())
                .SequenceEqual(_inspectBot.Items.Cast<InspectChoice>().Select(choice => choice.ToString())))
            return;

        _updatingInspectorChoices = true;
        try
        {
            _inspectBot.BeginUpdate();
            _inspectBot.Items.Clear();
            foreach (InspectChoice choice in choices)
                _inspectBot.Items.Add(choice);
            InspectChoice? selected = choices.FirstOrDefault(choice => choice.BotId == previous) ??
                                      choices.FirstOrDefault(choice => choice.BotId == _inspectedBotId) ??
                                      choices.FirstOrDefault();
            _inspectBot.SelectedItem = selected;
            if (selected == null)
                _inspectStatus.Text = choices.Count == 0
                    ? "There are no world bots to inspect yet."
                    : "Choose a bot, then press INSPECT BOT.";
        }
        finally
        {
            _updatingInspectorChoices = false;
            _inspectBot.EndUpdate();
        }
    }

    private InspectChoice? SelectedInspectChoice() => _inspectBot.SelectedItem as InspectChoice;

    /// <summary>Entry point for the Active Population context menu and double-click.</summary>
    private async Task InspectBotAsync(BotRow bot)
    {
        if (bot.BotId is not long botId)
            return;

        UpdateBotInspectorChoices();
        InspectChoice? choice = _inspectBot.Items.Cast<InspectChoice>().FirstOrDefault(candidate => candidate.BotId == botId);
        if (choice == null)
        {
            _inspectStatus.Text = $"{bot.Name} is not a world bot, so there is no saved character to inspect.";
            return;
        }

        // Choose before switching tabs: the tab's Enter handler then sees the right bot and starts the
        // one request itself (the guard below stops a second one).
        if (!ReferenceEquals(_inspectBot.SelectedItem, choice))
        {
            _updatingInspectorChoices = true;
            try
            {
                _inspectBot.SelectedItem = choice;
            }
            finally
            {
                _updatingInspectorChoices = false;
            }
        }

        if (_botInspectorTab != null && _tabs != null && _tabs.SelectedTab != _botInspectorTab)
        {
            _tabs.SelectedTab = _botInspectorTab;
            return;
        }

        await RefreshBotInspectorAsync();
    }

    /// <summary>
    /// Opens the Live World Map centred on the bot the inspector is showing (owner 2026-11-02). The map follows the
    /// bot until it is dragged. A bot that is not currently in the world is reported in the inspector status line.
    /// </summary>
    private async Task ShowInspectedBotOnMapAsync()
    {
        InspectChoice? choice = SelectedInspectChoice();
        if (choice == null)
        {
            _inspectStatus.Text = "Choose a bot first, then press SHOW ON MAP.";
            return;
        }

        BotRow? bot = _bots.FirstOrDefault(candidate => candidate.BotId == choice.BotId);
        if (bot == null)
        {
            _inspectStatus.Text = $"{choice.DisplayName} is not in the current roster; refresh the dashboard first.";
            return;
        }

        if (await ShowBotOnWorldMapAsync(bot))
            _inspectStatus.Text = $"World Map: centred on {bot.Name}. The map follows the bot until you drag it.";
        else
            _inspectStatus.Text = _footer.Text;
    }

    private async Task RefreshBotInspectorAsync()
    {
        if (_refreshingInspector)
            return;

        InspectChoice? choice = SelectedInspectChoice();
        if (choice == null)
        {
            _inspectStatus.Text = "Choose a bot to inspect.";
            return;
        }

        _refreshingInspector = true;
        if (_inspectButton != null)
            _inspectButton.Enabled = false;
        if (_refreshInspectButton != null)
        {
            _refreshInspectButton.Enabled = false;
            _refreshInspectButton.Text = "READING…";
        }
        _inspectStatus.Text = $"Reading {choice.DisplayName}…";
        try
        {
            BotInspectSnapshot? snapshot;
            if (IsServerRunning())
            {
                long botId = choice.BotId;
                snapshot = await Task.Run(() => RequestBotInspectSnapshot(botId));
            }
            else
            {
                snapshot = null;
            }

            if (IsDisposed)
                return;

            if (snapshot == null)
            {
                _inspectStatus.Text = IsServerRunning()
                    ? $"The server did not answer the inspect request for {choice.DisplayName}. Try again in a moment."
                    : "Start the server to inspect a bot. The inspector reads the bot while it is in the world.";
                return;
            }

            if (!snapshot.Found)
            {
                _inspectStatus.Text = string.IsNullOrWhiteSpace(snapshot.Error)
                    ? $"{choice.DisplayName} is no longer in this world."
                    : snapshot.Error;
                return;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Error) && string.IsNullOrWhiteSpace(snapshot.Name))
            {
                _inspectStatus.Text = snapshot.Error;
                return;
            }

            _inspectedBotId = snapshot.BotId;
            _inspectedAt = DateTime.Now;
            ApplyBotInspectorSnapshot(snapshot);
            _dashboardFooterSummary =
                $"Bot Inspector: {snapshot.Name} • {(snapshot.Online ? "live" : "last saved state")} • updated {_inspectedAt:T}";
            _footer.Text = _dashboardFooterSummary;
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
                _inspectStatus.Text = $"Could not inspect the bot: {exception.Message}";
        }
        finally
        {
            _refreshingInspector = false;
            if (_inspectButton != null)
                _inspectButton.Enabled = true;
            if (_refreshInspectButton != null)
            {
                _refreshInspectButton.Enabled = true;
                _refreshInspectButton.Text = "REFRESH";
            }
        }
    }

    private BotInspectSnapshot? RequestBotInspectSnapshot(long botId)
    {
        string requestId = Guid.NewGuid().ToString("N");
        string requestPath = Path.Combine(_serverDirectory, "bot-inspect.request");
        string temporaryPath = requestPath + $".{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, $"{requestId}|{botId}");
            File.Move(temporaryPath, requestPath, true);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(BotInspectWaitMilliseconds);
        do
        {
            BotInspectSnapshot? snapshot = TryReadBotInspectSnapshot();
            if (snapshot != null && string.Equals(snapshot.RequestId, requestId, StringComparison.Ordinal))
                return snapshot;
            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);
        return null;
    }

    private BotInspectSnapshot? TryReadBotInspectSnapshot()
    {
        string path = StateFile("bot-inspect.json");
        if (!File.Exists(path))
            return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<BotInspectSnapshot>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void ApplyBotInspectorSnapshot(BotInspectSnapshot snapshot)
    {
        _inspectOverview.Text = BuildInspectOverview(snapshot);
        BindInspectRows(_inspectStatSource, snapshot.Stats);
        BindInspectRows(_inspectEquipmentSource, snapshot.Equipment);
        BindInspectRows(_inspectBagSource, snapshot.Bag);
        BindInspectRows(_inspectVaultSource, snapshot.Vault);
        BindInspectRows(_inspectSpecSource, snapshot.Specs);
        BindInspectRows(_inspectAbilitySource, snapshot.Abilities);
        BindInspectRows(_inspectListingSource, snapshot.Listings);
        BindInspectRows(_inspectEffectSource, snapshot.Effects);

        SetInspectTabCaption(_inspectStatsTab, "Stats", snapshot.Stats.Count);
        SetInspectTabCaption(_inspectEquipmentTab, "Equipment", snapshot.Equipment.Count);
        SetInspectTabCaption(_inspectBagTab, "Backpack", snapshot.Bag.Count);
        SetInspectTabCaption(_inspectVaultTab, "Vault", snapshot.Vault.Count);
        SetInspectTabCaption(_inspectSpecTab, "Specialisation", snapshot.Specs.Count);
        SetInspectTabCaption(_inspectAbilityTab, "Abilities", snapshot.Abilities.Count);
        SetInspectTabCaption(_inspectListingTab, "Realm Exchange listings", snapshot.Listings.Count);
        SetInspectTabCaption(_inspectEffectTab, "Active effects", snapshot.Effects.Count);

        string state = snapshot.Online ? "live" : "last saved state";
        string warning = string.IsNullOrWhiteSpace(snapshot.Warning) ? string.Empty : $" • {snapshot.Warning}";
        _inspectStatus.Text =
            $"{snapshot.Name} • {snapshot.RealmName} {snapshot.ClassName} level {snapshot.Level} • {state} • " +
            $"{snapshot.Equipment.Count} equipped, {snapshot.Bag.Count} in the backpack, {snapshot.Specs.Count} spec lines • " +
            $"read {_inspectedAt:T}{warning}";
    }

    private static void BindInspectRows<T>(BindingSource source, List<T> rows)
    {
        source.DataSource = rows ?? [];
        source.ResetBindings(false);
    }

    private static void SetInspectTabCaption(TabPage? page, string title, int count)
    {
        if (page != null)
            page.Text = $"{title} ({count})";
    }

    private static string BuildInspectOverview(BotInspectSnapshot snapshot)
    {
        var text = new StringBuilder();
        void Row(params (string Label, string Value)[] cells)
        {
            var line = new StringBuilder();
            foreach ((string label, string value) in cells)
                line.Append(label.PadRight(16)).Append((value ?? string.Empty).PadRight(24));
            text.AppendLine(line.ToString().TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            text.AppendLine(snapshot.Error);
            text.AppendLine();
        }

        text.AppendLine($"{snapshot.Name} — {snapshot.RealmName} {snapshot.RaceName} {snapshot.Gender} {snapshot.ClassName} level {snapshot.Level}");
        text.AppendLine(snapshot.Online ? "State: in the world (live values)" : "State: not in the world (last saved values)");
        text.AppendLine(new string('─', 118));
        Row(("Money", snapshot.Money.Length > 0 ? snapshot.Money : FormatCopper(snapshot.MoneyCopper)),
            ("Health", Vitals(snapshot.Health, snapshot.MaxHealth)),
            ("Experience", snapshot.ExperienceToNext.Length > 0 ? snapshot.ExperienceToNext : snapshot.Experience.ToString("N0", CultureInfo.InvariantCulture)));
        Row(("Realm points", $"{snapshot.RealmPoints:N0} (RL {snapshot.RealmLevel})"),
            ("Power", Vitals(snapshot.Mana, snapshot.MaxMana)),
            ("Endurance", Vitals(snapshot.Endurance, snapshot.MaxEndurance)));
        Row(("Deaths", snapshot.DeathCount.ToString(CultureInfo.InvariantCulture)),
            ("Recoveries", snapshot.RecoveryCount.ToString(CultureInfo.InvariantCulture)),
            ("Hired companion", snapshot.IsHiredCompanion ? "yes" : "no"));
        Row(("Spec points", $"{snapshot.UnspentSpecPoints} available, {snapshot.SpecPointsSpent} spent"),
            ("Realm abilities", $"{snapshot.TrainedRealmAbilityPoints} of {snapshot.RealmAbilityPoints} points"),
            ("Craft", snapshot.Craft));
        Row(("Spells", snapshot.SpellCount.ToString(CultureInfo.InvariantCulture)),
            ("Styles", snapshot.StyleCount.ToString(CultureInfo.InvariantCulture)),
            ("Guild", Empty(snapshot.Guild)));
        text.AppendLine();
        text.AppendLine("WHERE AND WHAT");
        Row(("Zone", Empty(snapshot.ZoneName)),
            ("Region", snapshot.RegionId > 0 ? $"#{snapshot.RegionId}" : "—"),
            ("Position", $"{snapshot.X}, {snapshot.Y}, {snapshot.Z}"));
        Row(("Alive", snapshot.IsAlive ? "yes" : "no"),
            ("In combat", snapshot.InCombat ? "yes" : "no"),
            ("Target", Empty(snapshot.TargetName)));
        Row(("Group", Empty(snapshot.Group)),
            ("Objective", Empty(snapshot.ObjectiveKind)),
            ("Phase", Empty(snapshot.ObjectivePhase)));
        text.AppendLine();
        text.AppendLine("ACTIVITY");
        text.AppendLine($"  {Empty(snapshot.Activity)}");
        text.AppendLine($"  Goal:      {Empty(snapshot.CurrentGoal)}");
        text.AppendLine($"  Progress:  {Empty(snapshot.ObjectiveProgress)}");
        text.AppendLine($"  Travelling:{(!string.IsNullOrWhiteSpace(snapshot.TravelDestination) ? " " + snapshot.TravelDestination : " —")}");
        if (!string.IsNullOrWhiteSpace(snapshot.BuildPlan))
        {
            text.AppendLine();
            text.AppendLine("BUILD PLAN");
            text.AppendLine("  " + snapshot.BuildPlan);
        }

        text.AppendLine();
        text.AppendLine(new string('─', 118));
        text.AppendLine($"Last saved: {LocalTime(snapshot.LastSavedUtc)}    Last update: {LocalTime(snapshot.LastUpdateUtc)}    " +
                        $"Read: {DateTime.Now:T}");
        if (!string.IsNullOrWhiteSpace(snapshot.Warning))
        {
            text.AppendLine();
            text.AppendLine("Note: " + snapshot.Warning);
        }
        return text.ToString();
    }

    private static string Vitals(int value, int maximum) => maximum > 0
        ? $"{value} / {maximum}"
        : value.ToString(CultureInfo.InvariantCulture);

    private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string LocalTime(string utcText) =>
        DateTime.TryParse(utcText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime utc)
            ? utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "—";

    private sealed record InspectChoice(long BotId, BotRow Bot)
    {
        public string DisplayName => $"{Bot.Name} ({Bot.Realm} {Bot.ClassName} L{Bot.Level}, {Bot.State})";
        public override string ToString() => DisplayName;
    }

    // ---- Wire format of state-165/bot-inspect.json (server AutonomousBotInspector.Snapshot) ----

    private sealed class BotInspectSnapshot
    {
        public DateTime UpdatedUtc { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public long BotId { get; set; }
        public bool Found { get; set; }
        public bool Online { get; set; }
        public string Error { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string RaceName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string RealmName { get; set; } = string.Empty;
        public int Level { get; set; }
        public long Experience { get; set; }
        public long ExperienceFloor { get; set; }
        public long ExperienceNext { get; set; }
        public string ExperienceToNext { get; set; } = string.Empty;
        public long RealmPoints { get; set; }
        public int RealmLevel { get; set; }
        public long MoneyCopper { get; set; }
        public string Money { get; set; } = string.Empty;
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public int Mana { get; set; }
        public int MaxMana { get; set; }
        public int Endurance { get; set; }
        public int MaxEndurance { get; set; }
        public string ZoneName { get; set; } = string.Empty;
        public int RegionId { get; set; }
        public int ZoneId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public bool IsAlive { get; set; }
        public bool InCombat { get; set; }
        public string Activity { get; set; } = string.Empty;
        public string CurrentGoal { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public string TravelDestination { get; set; } = string.Empty;
        public string ObjectiveProgress { get; set; } = string.Empty;
        public string ObjectiveKind { get; set; } = string.Empty;
        public string ObjectivePhase { get; set; } = string.Empty;
        public string Guild { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public int DeathCount { get; set; }
        public int RecoveryCount { get; set; }
        public bool IsHiredCompanion { get; set; }
        public string Craft { get; set; } = string.Empty;
        public int UnspentSpecPoints { get; set; }
        public int SpecPointsSpent { get; set; }
        public int RealmAbilityPoints { get; set; }
        public int TrainedRealmAbilityPoints { get; set; }
        public string BuildPlan { get; set; } = string.Empty;
        public string LastSavedUtc { get; set; } = string.Empty;
        public string LastUpdateUtc { get; set; } = string.Empty;
        public int SpellCount { get; set; }
        public int StyleCount { get; set; }
        public string Warning { get; set; } = string.Empty;
        public List<BotInspectStat> Stats { get; set; } = [];
        public List<BotInspectSpec> Specs { get; set; } = [];
        public List<BotInspectAbility> Abilities { get; set; } = [];
        public List<BotInspectItem> Equipment { get; set; } = [];
        public List<BotInspectItem> Bag { get; set; } = [];
        public List<BotInspectItem> Vault { get; set; } = [];
        public List<BotInspectListing> Listings { get; set; } = [];
        public List<BotInspectEffect> Effects { get; set; } = [];
    }

    private sealed class BotInspectStat
    {
        public string Name { get; set; } = string.Empty;
        public int Base { get; set; }
        public int Items { get; set; }
        public int Buffs { get; set; }
        public int Total { get; set; }
    }

    private sealed class BotInspectSpec
    {
        public string Name { get; set; } = string.Empty;
        public int Level { get; set; }
        public bool Trainable { get; set; }
        public string TrainableText => Trainable ? "trained" : "granted";
    }

    private sealed class BotInspectAbility
    {
        public string Name { get; set; } = string.Empty;
        public int Level { get; set; }
        public string KeyName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private sealed class BotInspectItem
    {
        public string Slot { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Level { get; set; }
        public int Quality { get; set; }
        public int Count { get; set; }
        public int ConditionPercent { get; set; }
        public int DurabilityPercent { get; set; }
        public int SellCopper { get; set; }
        public long Value { get; set; }
        public string Details { get; set; } = string.Empty;
        public string ValueText => FormatCopper(Value);
        public string SellText => SellCopper > 0 ? FormatCopper(SellCopper) : "—";
    }

    private sealed class BotInspectListing
    {
        public string ItemName { get; set; } = string.Empty;
        public int Count { get; set; }
        public int PriceCopper { get; set; }
        public string Realm { get; set; } = string.Empty;
        public string Expires { get; set; } = string.Empty;
        public string Price => FormatCopper(PriceCopper);
    }

    private sealed class BotInspectEffect
    {
        public string Name { get; set; } = string.Empty;
        public bool Positive { get; set; }
        public int RemainingSeconds { get; set; }
        public string Kind => Positive ? "buff" : "debuff";
        public string Remaining => RemainingSeconds <= 0 ? "—" : $"{RemainingSeconds / 60:00}:{RemainingSeconds % 60:00}";
    }
}
