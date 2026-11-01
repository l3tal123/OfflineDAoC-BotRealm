using System.ComponentModel;
using System.Data.SQLite;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The launcher dashboard's live world map tab (fork feature): every online autonomous gamebot drawn as a dot on a
/// map built from the True 1.65 world's own zone data (the server's Zones/Regions tables), with the client's painted
/// zone maps used as the background when the new client's converted content is installed. A dot fades as its
/// snapshot ages, and hovering one shows the bot's name, level, class and current task.
///
/// It is read-only: the live positions come from the server's existing bot-world.json snapshot
/// (<see cref="AutonomousBotDashboard"/> on the server side, the same snapshot the Active Population tab uses).
/// No gameplay state is changed and no extra game-loop work is added beyond the snapshot the dashboard already takes.
/// </summary>
internal sealed partial class MainForm
{
    private BotWorldMapControl? _worldMap;
    private ComboBox? _worldMapRegion;
    private Label? _worldMapHint;
    private readonly System.Windows.Forms.Timer _worldMapRefresh = new() { Interval = 2_000 };
    private bool _refreshingWorldMap;
    private bool _updatingWorldMapChoices;
    private string _worldMapChoiceSignature = string.Empty;
    private LiveBotSnapshot? _lastWorldMapSnapshot;

    internal const int WorldMapRefreshMilliseconds = 2_000;

    private Control BuildWorldMapPanel()
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Color.Transparent,
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(12, 7, 0, 0),
            WrapContents = false,
        };
        bar.Controls.Add(new Label
        {
            Text = "VIEW",
            AutoSize = true,
            ForeColor = DaocTheme.GoldLight,
            Font = new Font("Georgia", 8.5f, FontStyle.Bold),
            Margin = new Padding(0, 4, 8, 0),
        });
        _worldMapRegion = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 300,
            Font = new Font("Georgia", 9f),
            BackColor = DaocTheme.StoneDark,
            ForeColor = DaocTheme.Text,
            FlatStyle = FlatStyle.Flat,
        };
        _worldMapRegion.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingWorldMapChoices || _worldMap == null)
                return;
            _worldMap.SelectedRegionId = _worldMapRegion.SelectedItem is WorldMapChoice choice ? choice.RegionId : 0;
            _worldMap.Invalidate();
        };
        bar.Controls.Add(_worldMapRegion);
        _worldMapHint = new Label
        {
            Text = "live positions, refreshed every 2 s — hover a dot for name, level, class and task",
            AutoSize = true,
            ForeColor = DaocTheme.Muted,
            Font = new Font("Georgia", 8.5f, FontStyle.Italic),
            Margin = new Padding(16, 4, 0, 0),
        };
        bar.Controls.Add(_worldMapHint);
        page.Controls.Add(bar, 0, 0);

        _worldMap = new BotWorldMapControl(_database, _newClient?.Folder)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 2, 12, 10),
        };
        page.Controls.Add(_worldMap, 0, 1);
        return page;
    }

    private async Task RefreshWorldMapAsync()
    {
        if (_refreshingWorldMap || _worldMap == null)
            return;

        bool running = IsServerRunning();
        _worldMap.SetServerRunning(running);
        if (!running)
        {
            UpdateWorldMapChoices();
            _worldMap.Invalidate();
            return;
        }

        _refreshingWorldMap = true;
        try
        {
            LiveBotSnapshot? snapshot = await Task.Run(RequestLiveBotSnapshot);
            if (snapshot != null)
                _lastWorldMapSnapshot = snapshot;
            _worldMap.SetSnapshot(_lastWorldMapSnapshot);
            UpdateWorldMapChoices();
            if (_worldMapHint != null)
                _worldMapHint.Text = "live positions, refreshed every 2 s — hover a dot for name, level, class and task";
        }
        catch (Exception)
        {
            // The Active Population refresh reports failures in the footer; the map keeps its last frame.
        }
        finally
        {
            _refreshingWorldMap = false;
        }
    }

    private void UpdateWorldMapChoices()
    {
        if (_worldMap == null || _worldMapRegion == null)
            return;

        List<(int RegionId, string Label, int Online)> choices = _worldMap.RegionChoices;
        string signature = string.Join("|", choices.Select(choice => $"{choice.RegionId}:{choice.Label}:{choice.Online}"));
        if (signature == _worldMapChoiceSignature && _worldMapRegion.Items.Count == choices.Count + 1)
            return;
        _worldMapChoiceSignature = signature;

        int selectedRegion = _worldMap.SelectedRegionId;
        _updatingWorldMapChoices = true;
        try
        {
            _worldMapRegion.Items.Clear();
            _worldMapRegion.Items.Add(new WorldMapChoice(0, "World overview"));
            foreach ((int regionId, string label, int online) in choices)
                _worldMapRegion.Items.Add(new WorldMapChoice(regionId, $"{label} ({online})"));
            int index = 0;
            for (int i = 1; i < _worldMapRegion.Items.Count; i++)
                if (_worldMapRegion.Items[i] is WorldMapChoice choice && choice.RegionId == selectedRegion)
                    index = i;
            _worldMapRegion.SelectedIndex = index;
        }
        finally
        {
            _updatingWorldMapChoices = false;
        }
    }

    private sealed record WorldMapChoice(int RegionId, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Draws the live bot world map. The map geometry comes from the world database's own Zones and Regions tables, so
    /// every zone (open world, old frontiers, dungeons, capitals, battlegrounds) has a place; the new client's painted
    /// zone maps are laid over those rectangles when the converted content is installed.
    /// </summary>
    private sealed class BotWorldMapControl : Control
    {
        private readonly string _databasePath;
        private readonly string? _clientFolder;
        private readonly Dictionary<int, MapRegion> _regions = [];
        private readonly Dictionary<int, Bitmap?> _zoneImages = [];
        private readonly List<(LiveBotStatus Bot, RectangleF Panel, PointF At, Color Color)> _dots = [];
        private readonly List<(int RegionId, string Label, int Online)> _choices = [];
        private readonly Font _titleFont = new("Georgia", 12f, FontStyle.Bold);
        private readonly Font _regionFont = new("Georgia", 9f, FontStyle.Bold);
        private readonly Font _zoneFont = new("Georgia", 7f);
        private readonly Font _statusFont = new("Georgia", 8.5f, FontStyle.Italic);
        private readonly Font _tooltipTitleFont = new("Georgia", 9.5f, FontStyle.Bold);
        private readonly Font _tooltipFont = new("Georgia", 8.5f);
        private readonly Font _tooltipSmallFont = new("Georgia", 8f, FontStyle.Italic);

        private bool _geometryLoaded;
        private bool _geometryFailed;
        private string? _geometryError;
        private string? _mapsDirectory;
        private bool _mapsDirectorySearched;
        private bool _serverRunning;
        private LiveBotSnapshot? _snapshot;
        private LiveBotStatus? _hoverBot;
        private int _selectedRegionId;

        public BotWorldMapControl(string databasePath, string? clientFolder)
        {
            _databasePath = databasePath;
            _clientFolder = clientFolder;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = DaocTheme.Panel;
            ForeColor = DaocTheme.Text;
            Cursor = Cursors.Default;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedRegionId
        {
            get => _selectedRegionId;
            set
            {
                if (_selectedRegionId == value)
                    return;
                _selectedRegionId = value;
                Invalidate();
            }
        }

        public List<(int RegionId, string Label, int Online)> RegionChoices
        {
            get
            {
                EnsureGeometry();
                BuildChoices();
                return [.. _choices];
            }
        }

        public void SetServerRunning(bool running)
        {
            if (_serverRunning == running)
                return;
            _serverRunning = running;
            Invalidate();
        }

        public void SetSnapshot(LiveBotSnapshot? snapshot)
        {
            if (snapshot != null)
            {
                _snapshot = snapshot;
                if (_selectedRegionId != 0 && !_regions.ContainsKey(_selectedRegionId))
                    _selectedRegionId = 0;
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
                _regionFont.Dispose();
                _zoneFont.Dispose();
                _statusFont.Dispose();
                _tooltipTitleFont.Dispose();
                _tooltipFont.Dispose();
                _tooltipSmallFont.Dispose();
                foreach (Bitmap? image in _zoneImages.Values)
                    image?.Dispose();
                _zoneImages.Clear();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ input

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            LiveBotStatus? nearest = null;
            float best = 8f * 8f;
            foreach ((LiveBotStatus bot, _, PointF at, _) in _dots)
            {
                float dx = at.X - e.X, dy = at.Y - e.Y;
                float distance = dx * dx + dy * dy;
                if (distance < best)
                {
                    best = distance;
                    nearest = bot;
                }
            }
            if (!ReferenceEquals(nearest, _hoverBot))
            {
                _hoverBot = nearest;
                Cursor = nearest != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            else if (nearest != null)
            {
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverBot != null)
            {
                _hoverBot = null;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        // ------------------------------------------------------------------ paint

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle client = ClientRectangle;
            using (var background = new SolidBrush(DaocTheme.Void))
                graphics.FillRectangle(background, client);
            using (var border = new Pen(DaocTheme.StoneLight))
                graphics.DrawRectangle(border, client.X, client.Y, client.Width - 1, client.Height - 1);

            EnsureGeometry();
            if (_geometryFailed || _regions.Count == 0)
            {
                DrawMessage(graphics,
                    _geometryError ?? "No world zones found yet. Start the server once to create the world database.",
                    client);
                return;
            }

            double ageSeconds = SnapshotAgeSeconds();
            float alpha = DotAlpha(ageSeconds);
            DateTime now = DateTime.UtcNow;

            const int headerHeight = 30;
            Rectangle header = new(client.X + 14, client.Y + 8, client.Width - 28, headerHeight);
            Rectangle mapArea = new(client.X + 10, client.Y + headerHeight + 12, client.Width - 20, client.Height - headerHeight - 26);
            if (mapArea.Width < 80 || mapArea.Height < 80)
                return;

            _dots.Clear();
            if (_selectedRegionId != 0 && _regions.TryGetValue(_selectedRegionId, out MapRegion? single))
                DrawRegion(graphics, single, mapArea, alpha, detailed: true, now);
            else
                DrawOverview(graphics, mapArea, alpha, now);

            DrawHeader(graphics, header, ageSeconds);
            if (_dots.Count == 0)
            {
                using var empty = new SolidBrush(DaocTheme.Muted);
                graphics.DrawString(_serverRunning ? "No bots have a live position yet." : "Server is not running.",
                    _statusFont, empty, mapArea.X + 8, mapArea.Y + 8);
            }
            DrawTooltip(graphics);
        }

        private double SnapshotAgeSeconds()
        {
            if (_snapshot == null)
                return double.MaxValue;
            return Math.Max(0, (DateTime.UtcNow - _snapshot.UpdatedUtc.ToUniversalTime()).TotalSeconds);
        }

        /// <summary>A dot is solid for the first few seconds and fades to a faint mark as its snapshot ages.</summary>
        private static float DotAlpha(double ageSeconds)
        {
            if (ageSeconds <= 4)
                return 1f;
            return Math.Max(0.10f, 1f - (float)((ageSeconds - 4) / 20));
        }

        private void DrawHeader(Graphics graphics, Rectangle header, double ageSeconds)
        {
            using (var title = new SolidBrush(DaocTheme.GoldLight))
                graphics.DrawString("LIVE WORLD MAP", _titleFont, title, header.X, header.Y);

            int online = _snapshot?.Bots?.Count(bot => bot.RegionId > 0 && _regions.ContainsKey(bot.RegionId)) ?? 0;
            int total = _snapshot?.Bots?.Count ?? 0;
            string age = ageSeconds == double.MaxValue ? "no snapshot yet" : $"updated {ageSeconds:0.0} s ago";
            Color ageColor = ageSeconds <= 8 ? DaocTheme.Success : ageSeconds <= 20 ? DaocTheme.Gold : DaocTheme.Danger;
            string status = _serverRunning
                ? $"{online:N0} of {total:N0} bots on the map   •   {age}"
                : "Server not running — last known positions   •   " + age;
            SizeF titleSize = graphics.MeasureString("LIVE WORLD MAP", _titleFont);
            using (var statusBrush = new SolidBrush(ageColor))
                graphics.DrawString(status, _statusFont, statusBrush, header.X + titleSize.Width + 18, header.Y + 5);
        }

        private static void DrawMessage(Graphics graphics, string message, Rectangle area)
        {
            using var brush = new SolidBrush(DaocTheme.Muted);
            using var font = new Font("Georgia", 10f, FontStyle.Italic);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(message, font, brush, area, format);
        }

        // ---------------------------------------------------------------- overview

        private void DrawOverview(Graphics graphics, Rectangle area, float alpha, DateTime now)
        {
            List<MapRegion> shown = [];
            HashSet<int> withBots = _snapshot?.Bots
                ?.Where(bot => bot.RegionId > 0 && _regions.ContainsKey(bot.RegionId))
                .Select(bot => bot.RegionId)
                .ToHashSet() ?? [];
            if (withBots.Count == 0)
            {
                foreach (int id in new[] { 1, 100, 200 })
                    if (_regions.TryGetValue(id, out MapRegion? realm))
                        shown.Add(realm);
            }
            else
            {
                shown.AddRange(_regions.Values.Where(region => withBots.Contains(region.Id)).OrderBy(region => region.Id));
            }
            if (shown.Count == 0)
                return;

            int columns = shown.Count == 1 ? 1 : shown.Count <= 4 ? 2 : 3;
            int rows = (shown.Count + columns - 1) / columns;
            int gap = 10;
            float cellWidth = (area.Width - gap * (columns - 1)) / (float)columns;
            float cellHeight = (area.Height - gap * (rows - 1)) / (float)rows;
            if (cellWidth < 90 || cellHeight < 70)
                return;

            for (int i = 0; i < shown.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                var cell = new Rectangle(
                    (int)Math.Round(area.X + column * (cellWidth + gap)),
                    (int)Math.Round(area.Y + row * (cellHeight + gap)),
                    (int)Math.Round(cellWidth),
                    (int)Math.Round(cellHeight));
                DrawRegionPanel(graphics, shown[i], cell, alpha, detailed: false, now);
            }
        }

        private void DrawRegionPanel(Graphics graphics, MapRegion region, Rectangle cell, float alpha, bool detailed,
            DateTime now)
        {
            using (var panel = new SolidBrush(Color.FromArgb(150, DaocTheme.StoneDark)))
                graphics.FillRectangle(panel, cell);
            using (var border = new Pen(Color.FromArgb(180, DaocTheme.StoneLight)))
                graphics.DrawRectangle(border, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);

            const int labelHeight = 20;
            var titleRect = new Rectangle(cell.X + 6, cell.Y + 4, cell.Width - 12, labelHeight);
            using (var titleBrush = new SolidBrush(RealmColor(region.Realm)))
                graphics.DrawString(region.Name.ToUpperInvariant(), _regionFont, titleBrush, titleRect);
            using (var countBrush = new SolidBrush(DaocTheme.Muted))
            {
                int count = CountBotsInRegion(region.Id);
                string countText = count == 1 ? "1 bot" : $"{count} bots";
                SizeF size = graphics.MeasureString(countText, _zoneFont);
                graphics.DrawString(countText, _zoneFont, countBrush, cell.Right - 8 - size.Width, cell.Y + 7);
            }

            var mapRect = new Rectangle(cell.X + 6, cell.Y + labelHeight + 2, cell.Width - 12, cell.Height - labelHeight - 10);
            DrawRegionMap(graphics, region, mapRect, alpha, detailed);

            // Frame the region's zone area with the realm colour so the panels read apart.
            RectangleF bounds = RegionScreenBounds(region, mapRect);
            using (var frame = new Pen(Color.FromArgb(150, RealmColor(region.Realm)), detailed ? 2f : 1f))
                graphics.DrawRectangle(frame, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        private void DrawRegion(Graphics graphics, MapRegion region, Rectangle area, float alpha, bool detailed,
            DateTime now)
        {
            var mapRect = new Rectangle(area.X, area.Y + 6, area.Width, area.Height - 6);
            DrawRegionMap(graphics, region, mapRect, alpha, detailed);
            using (var title = new SolidBrush(DaocTheme.GoldLight))
            {
                int count = CountBotsInRegion(region.Id);
                string text = $"{region.Name.ToUpperInvariant()} — {(count == 1 ? "1 bot" : count + " bots")}";
                SizeF size = graphics.MeasureString(text, _regionFont);
                using var background = new SolidBrush(Color.FromArgb(210, DaocTheme.StoneDark));
                graphics.FillRectangle(background, area.X + 4, area.Y + 2, size.Width + 12, size.Height + 6);
                graphics.DrawString(text, _regionFont, title, area.X + 10, area.Y + 5);
            }
        }

        private void DrawRegionMap(Graphics graphics, MapRegion region, Rectangle mapRect, float alpha, bool detailed)
        {
            RectangleF bounds = RegionScreenBounds(region, mapRect);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            float scale = bounds.Width / Math.Max(1, region.WorldWidth);

            using (var water = new SolidBrush(Color.FromArgb(120, DaocTheme.Void)))
                graphics.FillRectangle(water, bounds);

            foreach (MapZone zone in region.Zones)
            {
                var zoneRect = new RectangleF(
                    bounds.X + (zone.OffsetX - region.MinX) * scale,
                    bounds.Y + (zone.OffsetY - region.MinY) * scale,
                    Math.Max(1f, zone.Width * scale),
                    Math.Max(1f, zone.Height * scale));
                Bitmap? image = ZoneImage(zone.Id);
                if (image != null)
                {
                    graphics.DrawImage(image, zoneRect);
                }
                else
                {
                    using var fill = new SolidBrush(Color.FromArgb(70, RealmColor(zone.Realm != 0 ? zone.Realm : region.Realm)));
                    graphics.FillRectangle(fill, zoneRect);
                }
                using var zonePen = new Pen(Color.FromArgb(detailed ? 70 : 35, DaocTheme.Iron), 1f);
                graphics.DrawRectangle(zonePen, zoneRect.X, zoneRect.Y, zoneRect.Width, zoneRect.Height);
                if (detailed && image == null && zoneRect.Width > 58 && zoneRect.Height > 22)
                {
                    using var label = new SolidBrush(Color.FromArgb(220, DaocTheme.Parchment));
                    var textRect = new RectangleF(zoneRect.X + 3, zoneRect.Y + 2, zoneRect.Width - 6, zoneRect.Height - 4);
                    graphics.DrawString(zone.Name, _zoneFont, label, textRect);
                }
            }

            // Dots last, on top of the map. A dot outside the region bounds (a stale snapshot edge) is skipped.
            foreach (LiveBotStatus bot in _snapshot?.Bots ?? [])
            {
                if (bot.RegionId != region.Id || !_regions.ContainsKey(bot.RegionId))
                    continue;
                float x = bounds.X + (bot.X - region.MinX) * scale;
                float y = bounds.Y + (bot.Y - region.MinY) * scale;
                if (!bounds.Contains(x, y))
                    continue;
                Color color = RealmColor(bot.Realm);
                color = Color.FromArgb((int)Math.Round(255 * alpha), color.R, color.G, color.B);
                DrawDot(graphics, bot, x, y, color);
                _dots.Add((bot, bounds, new PointF(x, y), color));
            }
        }

        private void DrawDot(Graphics graphics, LiveBotStatus bot, float x, float y, Color color)
        {
            bool hovered = ReferenceEquals(bot, _hoverBot);
            float radius = hovered ? 6f : 4.2f;
            using (var outline = new SolidBrush(Color.FromArgb(Math.Max(60, (int)color.A), 8, 8, 10)))
                graphics.FillEllipse(outline, x - radius - 1.6f, y - radius - 1.6f, 2 * radius + 3.2f, 2 * radius + 3.2f);
            if (!bot.IsAlive)
            {
                using var deadPen = new Pen(Color.FromArgb(Math.Max(70, (int)color.A), 190, 190, 190), 1.6f);
                graphics.DrawEllipse(deadPen, x - radius, y - radius, 2 * radius, 2 * radius);
                return;
            }
            using (var fill = new SolidBrush(color))
                graphics.FillEllipse(fill, x - radius, y - radius, 2 * radius, 2 * radius);
            if (hovered)
            {
                using var ring = new Pen(Color.FromArgb(235, DaocTheme.GoldLight), 1.6f);
                graphics.DrawEllipse(ring, x - radius - 2.5f, y - radius - 2.5f, 2 * radius + 5f, 2 * radius + 5f);
            }
        }

        private void DrawTooltip(Graphics graphics)
        {
            if (_hoverBot == null || _dots.Count == 0)
                return;
            Point cursor = PointToClient(Cursor.Position);
            LiveBotStatus bot = _hoverBot;
            MapRegion? region = _regions.GetValueOrDefault(bot.RegionId);
            string name = string.IsNullOrWhiteSpace(bot.Name) ? $"Bot {bot.BotId}" : bot.Name;
            string className = string.IsNullOrWhiteSpace(bot.ClassName) ? "—" : bot.ClassName;
            string realm = bot.Realm switch { 1 => "Albion", 2 => "Midgard", 3 => "Hibernia", _ => "—" };
            string task = string.IsNullOrWhiteSpace(bot.Activity) ? "No reported task" : bot.Activity;
            if (task.Length > 78)
                task = task[..75] + "…";
            string where = string.IsNullOrWhiteSpace(bot.ZoneName) ? "—" : bot.ZoneName;
            if (region != null)
                where += " — " + region.Name;
            double age = SnapshotAgeSeconds();
            string ageText = age == double.MaxValue ? "age unknown" : $"updated {age:0.0} s ago";

            (string Text, Font Font, Color Color)[] lines =
            [
                (name, _tooltipTitleFont, DaocTheme.GoldLight),
                ($"Level {bot.Level} {className} — {realm}", _tooltipFont, DaocTheme.Parchment),
                (task, _tooltipFont, DaocTheme.Text),
                (where, _tooltipSmallFont, DaocTheme.Muted),
                (ageText, _tooltipSmallFont, age <= 8 ? DaocTheme.Success : DaocTheme.Gold),
            ];

            float width = 0;
            float height = 6;
            float[] widths = new float[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                SizeF size = graphics.MeasureString(lines[i].Text, lines[i].Font);
                widths[i] = size.Width;
                width = Math.Max(width, size.Width);
                height += size.Height + 2;
            }
            width += 18;

            float x = cursor.X + 14;
            float y = cursor.Y + 14;
            if (x + width > ClientRectangle.Right - 4)
                x = cursor.X - width - 12;
            if (y + height > ClientRectangle.Bottom - 4)
                y = cursor.Y - height - 12;
            x = Math.Max(ClientRectangle.X + 4, x);
            y = Math.Max(ClientRectangle.Y + 4, y);

            var box = new RectangleF(x, y, width, height);
            using (var fill = new SolidBrush(Color.FromArgb(238, DaocTheme.StoneDark)))
                graphics.FillRectangle(fill, box);
            using (var border = new Pen(DaocTheme.Gold, 1f))
                graphics.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);

            float lineY = y + 4;
            for (int i = 0; i < lines.Length; i++)
            {
                using var brush = new SolidBrush(lines[i].Color);
                graphics.DrawString(lines[i].Text, lines[i].Font, brush, x + 9, lineY);
                lineY += graphics.MeasureString(lines[i].Text, lines[i].Font).Height + 2;
            }
        }

        // ---------------------------------------------------------------- geometry

        private void EnsureGeometry()
        {
            if (_geometryLoaded)
                return;
            _geometryLoaded = true;
            try
            {
                if (!File.Exists(_databasePath))
                {
                    _geometryFailed = true;
                    _geometryError = "The world database does not exist yet.";
                    return;
                }
                using var connection = new SQLiteConnection(
                    $"Data Source={_databasePath};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT Z.ZoneID, Z.Name, Z.RegionID, Z.OffsetX, Z.OffsetY, Z.Width, Z.Height, Z.Realm, " +
                    "COALESCE(R.Description, R.Name, 'Region') " +
                    "FROM Zones Z LEFT JOIN Regions R ON R.RegionID = Z.RegionID";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int zoneId = reader.GetInt32(0);
                    string name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    int regionId = reader.GetInt32(2);
                    long offsetX = (long)reader.GetInt32(3) * 8192;
                    long offsetY = (long)reader.GetInt32(4) * 8192;
                    long width = (long)reader.GetInt32(5) * 8192;
                    long height = (long)reader.GetInt32(6) * 8192;
                    int realm = reader.GetInt32(7);
                    string regionName = reader.IsDBNull(8) ? "Region " + regionId : reader.GetString(8);
                    if (width <= 0 || height <= 0)
                        continue;
                    if (!_regions.TryGetValue(regionId, out MapRegion? region))
                    {
                        region = new MapRegion(regionId, regionName);
                        _regions[regionId] = region;
                    }
                    region.Zones.Add(new MapZone(zoneId, name, offsetX, offsetY, width, height, realm));
                }
                foreach (MapRegion region in _regions.Values)
                    region.Complete();
                _geometryFailed = _regions.Count == 0;
            }
            catch (Exception exception)
            {
                _geometryFailed = true;
                _geometryError = "Could not read the world zones: " + exception.Message;
            }
        }

        private void BuildChoices()
        {
            _choices.Clear();
            if (_geometryFailed)
                return;
            Dictionary<int, int> online = [];
            foreach (LiveBotStatus bot in _snapshot?.Bots ?? [])
            {
                if (bot.RegionId > 0 && _regions.ContainsKey(bot.RegionId))
                    online[bot.RegionId] = online.GetValueOrDefault(bot.RegionId) + 1;
            }
            foreach (MapRegion region in _regions.Values
                         .Where(region => online.ContainsKey(region.Id) && region.Zones.Count > 0)
                         .OrderBy(region => region.Realm).ThenBy(region => region.Name, StringComparer.Ordinal))
            {
                _choices.Add((region.Id, region.Name, online[region.Id]));
            }
        }

        private int CountBotsInRegion(int regionId)
        {
            int count = 0;
            foreach (LiveBotStatus bot in _snapshot?.Bots ?? [])
                if (bot.RegionId == regionId)
                    count++;
            return count;
        }

        private RectangleF RegionScreenBounds(MapRegion region, Rectangle mapRect)
        {
            if (region.WorldWidth <= 0 || region.WorldHeight <= 0)
                return RectangleF.Empty;
            float scale = Math.Min(mapRect.Width / (float)region.WorldWidth, mapRect.Height / (float)region.WorldHeight);
            float width = region.WorldWidth * scale;
            float height = region.WorldHeight * scale;
            return new RectangleF(mapRect.X + (mapRect.Width - width) / 2f,
                mapRect.Y + (mapRect.Height - height) / 2f, width, height);
        }

        private static int RealmOfRegion(int regionId) => regionId switch
        {
            1 or 2 or 10 or 20 or 21 or 22 or 23 or 24 or 50 or 51 or 60 or 61 or 62 => 1,
            100 or 101 or 102 or 125 or 126 or 127 or 128 or 129 or 130 or 150 or 151 or 160 or 161 => 2,
            180 or 181 or 190 or 191 or 192 or 193 or 194 or 200 or 201 or 202 or 220 or 221 or 222 or 223 or 224 => 3,
            _ => 0,
        };

        private static Color RealmColor(int realm) => realm switch
        {
            1 => DaocTheme.Albion,
            2 => DaocTheme.Midgard,
            3 => DaocTheme.Hibernia,
            _ => DaocTheme.Gold,
        };

        private Bitmap? ZoneImage(int zoneId)
        {
            if (_zoneImages.TryGetValue(zoneId, out Bitmap? cached))
                return cached;
            Bitmap? image = null;
            string? maps = MapsDirectory();
            if (maps != null)
            {
                foreach (string subfolder in new[] { "original", "x4plus" })
                {
                    string path = Path.Combine(maps, subfolder, $"z{zoneId:000}.png");
                    if (!File.Exists(path))
                        continue;
                    try
                    {
                        image = new Bitmap(path);
                        break;
                    }
                    catch (Exception)
                    {
                        image = null;
                    }
                }
            }
            _zoneImages[zoneId] = image;
            return image;
        }

        private string? MapsDirectory()
        {
            if (_mapsDirectorySearched)
                return _mapsDirectory;
            _mapsDirectorySearched = true;
            if (string.IsNullOrWhiteSpace(_clientFolder))
                return null;
            string path = Path.Combine(_clientFolder, "godot", "content", "maps");
            if (Directory.Exists(path))
                _mapsDirectory = path;
            return _mapsDirectory;
        }

        private sealed class MapZone(int id, string name, long offsetX, long offsetY, long width, long height, int realm)
        {
            public int Id { get; } = id;
            public string Name { get; } = name;
            public long OffsetX { get; } = offsetX;
            public long OffsetY { get; } = offsetY;
            public long Width { get; } = width;
            public long Height { get; } = height;
            public int Realm { get; } = realm;
        }

        private sealed class MapRegion(int id, string name)
        {
            public int Id { get; } = id;
            public string Name { get; } = name;
            public List<MapZone> Zones { get; } = [];
            public long MinX { get; private set; }
            public long MinY { get; private set; }
            public long MaxX { get; private set; }
            public long MaxY { get; private set; }
            public long WorldWidth => MaxX - MinX;
            public long WorldHeight => MaxY - MinY;
            public int Realm { get; private set; }

            public void Complete()
            {
                if (Zones.Count == 0)
                    return;
                MinX = Zones.Min(zone => zone.OffsetX);
                MinY = Zones.Min(zone => zone.OffsetY);
                MaxX = Zones.Max(zone => zone.OffsetX + zone.Width);
                MaxY = Zones.Max(zone => zone.OffsetY + zone.Height);
                Realm = RealmOfRegion(Id);
            }
        }
    }
}
