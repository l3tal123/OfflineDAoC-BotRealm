using System.Text.Json;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Launcher dashboard tab: Server Stats. It shows the world's lifetime tallies (monsters killed, deaths, money
/// looted, PvP kills, realm points earned, keeps captured) and four Top 10 leaderboards. Counters cover players and
/// gamebots alike. The data comes from the server's file broker, the same one the Bot Inspector uses: the launcher
/// writes <c>server-stats.request</c>, the server answers in <c>state-165/server-stats.json</c>. Read-only and on
/// demand: opening the tab or pressing REFRESH asks once; nothing polls in the background and nothing is written to
/// the world database.
/// </summary>
internal sealed partial class MainForm
{
    private const int ServerStatsWaitMilliseconds = 5_000;

    private readonly BindingSource _serverStatsMobsKilledSource = new();
    private readonly BindingSource _serverStatsDeathsSource = new();
    private readonly BindingSource _serverStatsPvpKillsSource = new();
    private readonly BindingSource _serverStatsKeepsCapturedSource = new();

    private readonly Label _serverStatsMobsKilledValue = CardValue();
    private readonly Label _serverStatsDeathsValue = CardValue();
    private readonly Label _serverStatsMoneyValue = CardValue();
    private readonly Label _serverStatsPvpKillsValue = CardValue();
    private readonly Label _serverStatsRealmPointsValue = CardValue();
    private readonly Label _serverStatsKeepsCapturedValue = CardValue();
    private readonly Label _serverStatsTracked = new()
    {
        Dock = DockStyle.Fill,
        Text = "tracked: 0 players, 0 bots",
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = DaocTheme.Muted,
        BackColor = Color.Transparent,
        Font = new Font("Georgia", 8f, FontStyle.Italic),
        Padding = new Padding(12, 0, 0, 0),
    };

    private readonly DataGridView _serverStatsMobsKilledGrid = new();
    private readonly DataGridView _serverStatsDeathsGrid = new();
    private readonly DataGridView _serverStatsPvpKillsGrid = new();
    private readonly DataGridView _serverStatsKeepsCapturedGrid = new();

    private readonly Label _serverStatsStatus = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = DaocTheme.Muted,
        BackColor = DaocTheme.Tint(48, 38, 26),
        Font = new Font("Georgia", 8.5f, FontStyle.Italic),
        Padding = new Padding(8, 0, 0, 0),
    };

    private Button? _refreshServerStatsButton;
    private bool _refreshingServerStats;

    private Control BuildServerStatsPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(7),
            BackColor = DaocTheme.Tint(48, 38, 26),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = DaocTheme.Tint(69, 51, 31) };
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "✦  SERVER STATS  ✦    the world, since this build",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Georgia", 10f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            BackColor = DaocTheme.Tint(69, 51, 31),
            Padding = new Padding(11, 0, 0, 0),
            BorderStyle = BorderStyle.Fixed3D,
        });
        _refreshServerStatsButton = ActionButton("REFRESH SERVER STATS", DaocTheme.Tint(126, 104, 67));
        _refreshServerStatsButton.Dock = DockStyle.Right;
        _refreshServerStatsButton.Width = 215;
        _refreshServerStatsButton.Click += async (_, _) => await RefreshServerStatsAsync();
        header.Controls.Add(_refreshServerStatsButton);
        layout.Controls.Add(header, 0, 0);

        var totals = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0),
        };
        for (int index = 0; index < 6; index++)
            totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6f));
        totals.Controls.Add(Card("MOBS KILLED", _serverStatsMobsKilledValue, DaocTheme.Gold), 0, 0);
        totals.Controls.Add(Card("DEATHS", _serverStatsDeathsValue, DaocTheme.Danger), 1, 0);
        totals.Controls.Add(Card("MONEY LOOTED", _serverStatsMoneyValue, DaocTheme.GoldLight), 2, 0);
        totals.Controls.Add(Card("PVP KILLS", _serverStatsPvpKillsValue, DaocTheme.Danger), 3, 0);
        totals.Controls.Add(Card("REALM POINTS", _serverStatsRealmPointsValue, DaocTheme.Success), 4, 0);
        totals.Controls.Add(Card("KEEPS CAPTURED", _serverStatsKeepsCapturedValue, DaocTheme.Gold), 5, 0);
        layout.Controls.Add(totals, 0, 1);
        layout.Controls.Add(_serverStatsTracked, 0, 2);

        var boards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        boards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        boards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        boards.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        boards.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        ConfigureServerStatsGrid(_serverStatsMobsKilledGrid, _serverStatsMobsKilledSource);
        ConfigureServerStatsGrid(_serverStatsDeathsGrid, _serverStatsDeathsSource);
        ConfigureServerStatsGrid(_serverStatsPvpKillsGrid, _serverStatsPvpKillsSource);
        ConfigureServerStatsGrid(_serverStatsKeepsCapturedGrid, _serverStatsKeepsCapturedSource);
        // A top list ranks players and bots together; double-clicking a bot row opens the Bot Inspector on it (owner
        // 2026-11-02). A player row is reported in the status line, since only world bots can be inspected.
        foreach (DataGridView board in new[]
                 {
                     _serverStatsMobsKilledGrid, _serverStatsDeathsGrid, _serverStatsPvpKillsGrid, _serverStatsKeepsCapturedGrid,
                 })
        {
            board.CellDoubleClick += async (_, eventArgs) =>
            {
                if (eventArgs.RowIndex < 0 || board.Rows[eventArgs.RowIndex].DataBoundItem is not ServerStatsRow row)
                    return;
                await InspectServerStatsEntryAsync(row);
            };
        }
        boards.Controls.Add(ServerStatsLeaderboard("Top 10 — Mobs killed", _serverStatsMobsKilledGrid), 0, 0);
        boards.Controls.Add(ServerStatsLeaderboard("Top 10 — Deaths", _serverStatsDeathsGrid), 1, 0);
        boards.Controls.Add(ServerStatsLeaderboard("Top 10 — PvP kills", _serverStatsPvpKillsGrid), 0, 1);
        boards.Controls.Add(ServerStatsLeaderboard("Top 10 — Keeps captured", _serverStatsKeepsCapturedGrid), 1, 1);
        layout.Controls.Add(boards, 0, 3);

        _serverStatsStatus.Text = "Open this tab or press REFRESH SERVER STATS to ask the server for its stats. Double-click a bot in a top list to open its Bot Inspector.";
        layout.Controls.Add(_serverStatsStatus, 0, 4);
        return layout;
    }

    private static Control ServerStatsLeaderboard(string title, DataGridView grid)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(3),
            Padding = new Padding(3),
            BackColor = Color.Transparent,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Georgia", 8.5f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            BackColor = DaocTheme.Tint(69, 51, 31),
            Padding = new Padding(6, 0, 0, 0),
            BorderStyle = BorderStyle.Fixed3D,
        }, 0, 0);
        panel.Controls.Add(grid, 0, 1);
        return panel;
    }

    private static void ConfigureServerStatsGrid(DataGridView grid, BindingSource source)
    {
        ConfigureExchangeGrid(grid, source);
        grid.Columns.Add(TextColumn("Rank", "Rank", 48));
        grid.Columns.Add(TextColumn("Name", "Name", 180, DataGridViewAutoSizeColumnMode.Fill));
        grid.Columns.Add(TextColumn("Kind", "Kind", 64));
        grid.Columns.Add(TextColumn("Realm", "Realm", 82));
        DataGridViewTextBoxColumn count = TextColumn("Count", "Value", 82);
        count.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        count.DefaultCellStyle.Format = "N0";
        grid.Columns.Add(count);
        // The rows are already the server's ranking: no column sorting on these leaderboards.
        foreach (DataGridViewColumn column in grid.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
    }

    private async Task RefreshServerStatsAsync()
    {
        if (_refreshingServerStats)
            return;

        _refreshingServerStats = true;
        if (_refreshServerStatsButton != null)
        {
            _refreshServerStatsButton.Enabled = false;
            _refreshServerStatsButton.Text = "READING SERVER STATS…";
        }
        _serverStatsStatus.Text = "Asking the server for its stats…";
        try
        {
            ServerStatsRead read = await Task.Run(ReadServerStats);
            if (IsDisposed)
                return;
            ApplyServerStatsSnapshot(read.Snapshot);
            if (read.Snapshot == null)
            {
                _serverStatsStatus.Text =
                    "Server Stats starts collecting when the server runs with this build. Start the server once, then refresh.";
            }
            else if (read.LastPublished)
            {
                _serverStatsStatus.Text =
                    $"Counts players and bots • as last published by the server ({DescribeGeneratedUtc(read.Snapshot)}) • refresh once this build's server is running.";
            }
            else
            {
                _serverStatsStatus.Text =
                    $"Counts players and bots • request {ShortenRequestId(read.Snapshot.RequestId)} • published {DescribeGeneratedUtc(read.Snapshot)}";
            }
            _helpTip.SetToolTip(_serverStatsStatus, read.Snapshot == null
                ? string.Empty
                : $"RequestId {read.Snapshot.RequestId}");
        }
        catch (Exception exception)
        {
            // Any unexpected broker/file failure is reported in the status line, never as an unhandled exception.
            if (!IsDisposed)
                _serverStatsStatus.Text = $"Could not read server stats: {exception.Message}";
        }
        finally
        {
            _refreshingServerStats = false;
            if (_refreshServerStatsButton != null)
            {
                _refreshServerStatsButton.Text = "REFRESH SERVER STATS";
                _refreshServerStatsButton.Enabled = true;
            }
        }
    }

    private void ApplyServerStatsSnapshot(ServerStatsSnapshot? snapshot)
    {
        _serverStatsMobsKilledValue.Text = (snapshot?.MobKills ?? 0).ToString("N0");
        _serverStatsDeathsValue.Text = (snapshot?.Deaths ?? 0).ToString("N0");
        _serverStatsMoneyValue.Text = FormatCopper(snapshot?.MoneyLootedCopper ?? 0);
        _serverStatsPvpKillsValue.Text = (snapshot?.PvpKills ?? 0).ToString("N0");
        _serverStatsRealmPointsValue.Text = (snapshot?.RealmPoints ?? 0).ToString("N0");
        _serverStatsKeepsCapturedValue.Text = (snapshot?.KeepsCaptured ?? 0).ToString("N0");
        _serverStatsTracked.Text =
            $"tracked: {(snapshot?.PlayersTracked ?? 0):N0} players, {(snapshot?.BotsTracked ?? 0):N0} bots";
        BindServerStatsRows(_serverStatsMobsKilledSource, ToServerStatsRows(snapshot?.TopMobKills));
        BindServerStatsRows(_serverStatsDeathsSource, ToServerStatsRows(snapshot?.TopDeaths));
        BindServerStatsRows(_serverStatsPvpKillsSource, ToServerStatsRows(snapshot?.TopPvpKills));
        BindServerStatsRows(_serverStatsKeepsCapturedSource, ToServerStatsRows(snapshot?.TopKeepsCaptured));
    }

    /// <summary>
    /// Double-clicking a top-list row opens the Bot Inspector on that bot (owner 2026-11-02). The boards rank players
    /// and bots together, so a player-character row is reported in the status line rather than opened; a bot row is
    /// matched by name against the current roster, because the server's stat snapshot carries no bot id.
    /// </summary>
    private async Task InspectServerStatsEntryAsync(ServerStatsRow row)
    {
        if (!string.Equals(row.Kind, "Bot", StringComparison.OrdinalIgnoreCase))
        {
            _serverStatsStatus.Text =
                $"{row.Name} is a player character, not a world bot: only bots can be opened in the Bot Inspector.";
            return;
        }

        BotRow? bot = _bots.FirstOrDefault(candidate => candidate.BotId is > 0 &&
            string.Equals(candidate.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        if (bot == null)
        {
            _serverStatsStatus.Text =
                $"{row.Name} is not in the current world bot roster (it may have been deleted, or the roster is stale). Refresh the dashboard, then try again.";
            return;
        }

        await InspectBotAsync(bot);
    }

    private static void BindServerStatsRows(BindingSource source, List<ServerStatsRow> rows)
    {
        source.DataSource = rows;
        source.ResetBindings(false);
    }

    private static List<ServerStatsRow> ToServerStatsRows(List<ServerStatsLeaderRow>? rows)
    {
        var ranked = new List<ServerStatsRow>();
        if (rows == null)
            return ranked;
        foreach (ServerStatsLeaderRow row in rows)
        {
            ranked.Add(new ServerStatsRow(
                ranked.Count + 1,
                string.IsNullOrWhiteSpace(row.Name) ? "Unknown" : row.Name,
                string.IsNullOrWhiteSpace(row.Kind) ? "Unknown" : row.Kind,
                RealmName(row.Realm),
                row.Value));
        }
        return ranked;
    }

    /// <summary>The broker read. Runs off the UI thread: one request when the server is up, then the last file.</summary>
    private ServerStatsRead ReadServerStats()
    {
        ServerStatsSnapshot? fresh = IsServerRunning() ? RequestServerStatsSnapshot() : null;
        if (fresh != null)
            return new ServerStatsRead(fresh, false);
        ServerStatsSnapshot? published = TryReadServerStatsSnapshot();
        return new ServerStatsRead(published, published != null);
    }

    /// <summary>
    /// Writes the request file atomically (the server watches it) and waits up to five seconds for the snapshot that
    /// carries this request id. Same pattern as RequestBotInspectSnapshot.
    /// </summary>
    private ServerStatsSnapshot? RequestServerStatsSnapshot()
    {
        string requestId = Guid.NewGuid().ToString("N");
        string requestPath = Path.Combine(_serverDirectory, "server-stats.request");
        string temporaryPath = requestPath + $".{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, requestId);
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

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(ServerStatsWaitMilliseconds);
        do
        {
            ServerStatsSnapshot? snapshot = TryReadServerStatsSnapshot();
            if (snapshot != null && string.Equals(snapshot.RequestId, requestId, StringComparison.Ordinal))
                return snapshot;
            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);
        return null;
    }

    /// <summary>Reads state-165/server-stats.json. Same read pattern as TryReadBotInspectSnapshot.</summary>
    private ServerStatsSnapshot? TryReadServerStatsSnapshot()
    {
        string path = StateFile("server-stats.json");
        if (!File.Exists(path))
            return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<ServerStatsSnapshot>(stream,
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

    private static string DescribeGeneratedUtc(ServerStatsSnapshot snapshot)
    {
        return snapshot.GeneratedUtc == default
            ? "time unknown"
            : snapshot.GeneratedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string ShortenRequestId(string requestId)
    {
        return requestId.Length <= 12 ? requestId : requestId[..12] + "…";
    }

    private sealed record ServerStatsRead(ServerStatsSnapshot? Snapshot, bool LastPublished);

    private sealed record ServerStatsRow(int Rank, string Name, string Kind, string Realm, long Value);

    // ---- Wire format of state-165/server-stats.json (server-side broker) ----

    private sealed class ServerStatsSnapshot
    {
        public string RequestId { get; set; } = string.Empty;
        public DateTime GeneratedUtc { get; set; }
        public long MobKills { get; set; }
        public long Deaths { get; set; }
        public long MoneyLootedCopper { get; set; }
        public long PvpKills { get; set; }
        public long RealmPoints { get; set; }
        public long KeepsCaptured { get; set; }
        public int PlayersTracked { get; set; }
        public int BotsTracked { get; set; }
        public List<ServerStatsLeaderRow> TopMobKills { get; set; } = [];
        public List<ServerStatsLeaderRow> TopDeaths { get; set; } = [];
        public List<ServerStatsLeaderRow> TopPvpKills { get; set; } = [];
        public List<ServerStatsLeaderRow> TopKeepsCaptured { get; set; } = [];
    }

    private sealed class ServerStatsLeaderRow
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int Realm { get; set; }
        public int Level { get; set; }
        public long Value { get; set; }
    }
}
