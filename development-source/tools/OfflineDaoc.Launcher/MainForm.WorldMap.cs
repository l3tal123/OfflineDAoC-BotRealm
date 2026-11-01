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
    private Label? _worldMapZoom;
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
            Width = 260,
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
        Button zoomOut = CompactWorldMapButton("−");
        _helpTip.SetToolTip(zoomOut, "Zoom the map out (or scroll the wheel down over the map).");
        zoomOut.Click += (_, _) => _worldMap?.ZoomOut();
        bar.Controls.Add(zoomOut);
        Button zoomIn = CompactWorldMapButton("+");
        _helpTip.SetToolTip(zoomIn, "Zoom the map in (or scroll the wheel up over the map).");
        zoomIn.Click += (_, _) => _worldMap?.ZoomIn();
        bar.Controls.Add(zoomIn);
        Button zoomReset = CompactWorldMapButton("RESET");
        zoomReset.Width = 62;
        _helpTip.SetToolTip(zoomReset, "Return to the whole map at 100%.");
        zoomReset.Click += (_, _) => _worldMap?.ResetView();
        bar.Controls.Add(zoomReset);
        _worldMapZoom = new Label
        {
            Text = "100%",
            AutoSize = true,
            ForeColor = DaocTheme.GoldLight,
            Font = new Font("Georgia", 8.5f, FontStyle.Bold),
            Margin = new Padding(10, 4, 0, 0),
        };
        bar.Controls.Add(_worldMapZoom);
        _worldMapHint = new Label
        {
            Text = "wheel zooms to 1000%  •  drag pans  •  hover for details  •  double-click a dot selects",
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
        _worldMap.BotActivated += SelectBotFromWorldMap;
        _worldMap.ZoomChanged += SyncWorldMapZoomLabel;
        page.Controls.Add(_worldMap, 0, 1);
        return page;
    }

    private static Button CompactWorldMapButton(string text)
    {
        Button button = ActionButton(text, DaocTheme.Tint(126, 104, 67));
        if (button is RuneButton rune)
            rune.ShowOrnaments = false;
        button.Width = 30;
        button.Height = 22;
        button.Font = new Font("Georgia", 9f, FontStyle.Bold);
        button.Margin = new Padding(8, 3, 0, 0);
        button.AccessibleName = text;
        return button;
    }

    private void SyncWorldMapZoomLabel()
    {
        if (_worldMapZoom != null && _worldMap != null)
            _worldMapZoom.Text = $"{_worldMap.ZoomPercent}%";
    }

    /// <summary>
    /// Double-clicking a dot on the live world map brings that bot up in the Active Population tab: the tab switches,
    /// any filter that would hide the row is relaxed, and the row is selected and scrolled into view. It changes no
    /// game state; it is the same selection the grid itself offers.
    /// </summary>
    private void SelectBotFromWorldMap(long botId)
    {
        BotRow? bot = _bots.FirstOrDefault(row => row.BotId == botId);
        if (bot == null)
        {
            _footer.Text = "World map: that bot is no longer in the roster — refresh to see the current population.";
            return;
        }

        DataGridViewRow? row = FindBotGridRow(botId);
        if (row == null && (_search.Text.Length > 0 || _realmFilter.SelectedIndex != 0 || _onlineOnly.Checked))
        {
            // The row exists but the Active Population filter would hide it: relax the filter rather than fail.
            if (_realmFilter.SelectedIndex != 0)
                _realmFilter.SelectedItem = "All realms";
            if (_search.Text.Length > 0)
                _search.Text = string.Empty;
            if (_onlineOnly.Checked)
                _onlineOnly.Checked = false;
            ApplyFilter();
            row = FindBotGridRow(botId);
        }

        if (row == null)
        {
            _footer.Text = $"World map: {bot.Name} could not be shown in Active Population.";
            return;
        }

        if (_tabs != null && _populationTab != null)
            _tabs.SelectedTab = _populationTab;
        try
        {
            _grid.ClearSelection();
            row.Selected = true;
            _grid.CurrentCell = row.Cells[0];
            if (row.Index >= 0)
                _grid.FirstDisplayedScrollingRowIndex = row.Index;
        }
        catch (InvalidOperationException)
        {
            // The grid is mid-refresh; the row stays selected on the next paint.
        }
        _grid.Focus();
        _footer.Text = $"World map: selected {bot.Name} — level {bot.Level} {bot.ClassName}, {bot.ZoneName}.";
    }

    private DataGridViewRow? FindBotGridRow(long botId)
    {
        foreach (DataGridViewRow row in _grid.Rows)
            if (row.DataBoundItem is BotRow bound && bound.BotId == botId)
                return row;
        return null;
    }

    /// <summary>
    /// Brings one bot up on the Live World Map (owner 2026-11-02): from the Active Population right-click menu and
    /// double-click, the Server Stats leaderboards and the Bot Inspector. The tab switches, the bot's region is
    /// selected, the view zooms in on its live position and follows it until the map is dragged. The position comes
    /// from the same bot-world snapshot the population tab uses; a bot with no live position (offline, queued, or the
    /// server stopped) is reported in the footer and, on the double-click path, falls back to the Bot Inspector.
    /// </summary>
    private async Task<bool> ShowBotOnWorldMapAsync(BotRow bot, bool allowInspectorFallback = false)
    {
        if (bot?.BotId is not long botId)
            return false;

        LiveBotStatus? live = FindLiveBotOnWorldMap(botId);
        if (live == null && IsServerRunning())
        {
            // The map may not have been opened yet this session: one snapshot request, then decide.
            LiveBotSnapshot? snapshot = await Task.Run(RequestLiveBotSnapshot);
            if (snapshot != null)
                _lastWorldMapSnapshot = snapshot;
            live = FindLiveBotOnWorldMap(botId);
        }
        if (live == null)
        {
            _footer.Text = IsServerRunning()
                ? $"World Map: {bot.Name} has no live position right now — it is offline, still in the login queue, or not on the map yet."
                : $"World Map: the server is not running, so {bot.Name} has no live position to show.";
            if (allowInspectorFallback)
                await InspectBotAsync(bot);
            return false;
        }

        return ShowLiveBotOnWorldMap(live, bot.Name);
    }

    private LiveBotStatus? FindLiveBotOnWorldMap(long botId) =>
        _lastWorldMapSnapshot?.Bots?.FirstOrDefault(candidate => candidate.BotId == botId && candidate.RegionId > 0);

    /// <summary>Selects the bot's region and asks the map control to centre and follow it. This changes no game state.</summary>
    private bool ShowLiveBotOnWorldMap(LiveBotStatus live, string displayName)
    {
        if (_worldMap == null || _worldMapTab == null || _tabs == null)
            return false;

        if (_lastWorldMapSnapshot != null)
            _worldMap.SetSnapshot(_lastWorldMapSnapshot);
        UpdateWorldMapChoices();
        SelectWorldMapRegion(live.RegionId);
        if (!_worldMap.ShowBot(live.BotId))
        {
            _footer.Text = $"World Map: {displayName}'s region ({live.ZoneName}) is not on the map yet.";
            return false;
        }
        _tabs.SelectedTab = _worldMapTab;
        SyncWorldMapZoomLabel();
        _footer.Text =
            $"World Map: following {displayName} — level {live.Level}, {live.ZoneName}. Wheel zooms (up to 1000%); drag the map to stop following and pan.";
        return true;
    }

    /// <summary>Moves the VIEW selector to a region; the combo's handler then resets the map view to fit.</summary>
    private void SelectWorldMapRegion(int regionId)
    {
        if (_worldMapRegion != null)
        {
            for (int index = 0; index < _worldMapRegion.Items.Count; index++)
            {
                if (_worldMapRegion.Items[index] is not WorldMapChoice choice || choice.RegionId != regionId)
                    continue;
                _worldMapRegion.SelectedIndex = index;
                return;
            }
        }
        _worldMap!.SelectedRegionId = regionId;
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
            SyncWorldMapZoomLabel();
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

        // One bot can be singled out by "Show on map" (owner 2026-11-02): a gold ring and a name tag mark it, and
        // until the map is dragged the view keeps it centred as new snapshots arrive.
        private long? _focusedBotId;
        private long? _followBotId;
        private bool _focusPending;

        // View state: the map content is laid out once at 100% inside _mapArea, and this zoom/pan pair maps those
        // content coordinates onto the control. Zooming never re-lays-out the map, so painted zone maps stay sharp
        // (the source PNGs are larger than their on-screen rectangles at 100%).
        private const float MinZoom = 1f;
        private const float MaxZoom = 10f;          // 1000%: enough to read a single camp on a painted zone map
        private const float FocusZoom = 3f;         // where a "show on map" jump lands, in the bot's zone
        private const float ZoomStep = 1.25f;
        private Rectangle _mapArea;
        private float _zoom = 1f;
        private PointF _pan;
        private bool _panning;
        private Point _panAnchorScreen;
        private PointF _panAnchorOffset;

        public BotWorldMapControl(string databasePath, string? clientFolder)
        {
            _databasePath = databasePath;
            _clientFolder = clientFolder;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.StandardClick | ControlStyles.StandardDoubleClick |
                     ControlStyles.Selectable, true);
            TabStop = false;
            BackColor = DaocTheme.Panel;
            ForeColor = DaocTheme.Text;
            Cursor = Cursors.Default;
        }

        /// <summary>Raised with the bot id when a dot is double-clicked; the host selects that bot in the population grid.</summary>
        public event Action<long>? BotActivated;

        /// <summary>Raised whenever the zoom level changes, so the host can show the percentage.</summary>
        public event Action? ZoomChanged;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ZoomPercent => (int)Math.Round(_zoom * 100);

        /// <summary>Zooms in one step around the middle of the map.</summary>
        public void ZoomIn() => SetZoom(_zoom * ZoomStep, MapCentre());

        /// <summary>Zooms out one step around the middle of the map.</summary>
        public void ZoomOut() => SetZoom(_zoom / ZoomStep, MapCentre());

        /// <summary>Returns to the whole map at 100%.</summary>
        public void ResetView()
        {
            _focusedBotId = null;
            _followBotId = null;
            _focusPending = false;
            bool changed = Math.Abs(_zoom - MinZoom) > 0.0001f || _pan != PointF.Empty;
            _zoom = MinZoom;
            _pan = PointF.Empty;
            ClampPan();
            if (!changed)
                return;
            ZoomChanged?.Invoke();
            Invalidate();
        }

        /// <summary>
        /// Centres the view on one bot and follows it while its snapshots arrive (owner 2026-11-02). The bot must be
        /// in a region this map knows; the host switches the tab and selects the region first. Dragging the map,
        /// resetting the view or choosing another region ends the follow; the gold ring stays on the bot until then.
        /// </summary>
        public bool ShowBot(long botId)
        {
            LiveBotStatus? bot = _snapshot?.Bots?.FirstOrDefault(candidate =>
                candidate.BotId == botId && candidate.RegionId > 0 && _regions.ContainsKey(candidate.RegionId));
            if (bot == null)
                return false;

            SelectedRegionId = bot.RegionId;   // fits the region and drops any earlier focus
            _focusedBotId = bot.BotId;
            _followBotId = bot.BotId;
            _zoom = FocusZoom;
            _pan = PointF.Empty;
            _focusPending = true;
            ClampPan();
            ZoomChanged?.Invoke();
            Invalidate();
            return true;
        }

        private Point MapCentre() => _mapArea.Width > 0
            ? new Point(_mapArea.X + _mapArea.Width / 2, _mapArea.Y + _mapArea.Height / 2)
            : new Point(Width / 2, Height / 2);

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
                // A different view starts fitted: a zoom meant for one region is not meaningful in another, and a
                // bot singled out in the old region must not stay "followed" in this one.
                _focusedBotId = null;
                _followBotId = null;
                _focusPending = false;
                _zoom = MinZoom;
                _pan = PointF.Empty;
                ClampPan();
                ZoomChanged?.Invoke();
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

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            // Take focus so wheel events arrive here instead of the tab control underneath.
            if (!Focused)
                Focus();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta == 0 || _mapArea.Width <= 0 || _mapArea.Height <= 0)
                return;
            SetZoom(e.Delta > 0 ? _zoom * ZoomStep : _zoom / ZoomStep, e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Focused)
                Focus();
            // Middle-drag always pans; left-drag pans a zoomed map but not when it starts on a dot (that is a select).
            bool canPan = e.Button == MouseButtons.Middle ||
                          (e.Button == MouseButtons.Left && _zoom > MinZoom && HitTestBot(e.Location) == null);
            if (!canPan)
                return;
            _panning = true;
            // A drag is the user taking the camera back from "show on map": stop following, keep the ring.
            _followBotId = null;
            _panAnchorScreen = e.Location;
            _panAnchorOffset = _pan;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_panning)
            {
                _pan = new PointF(_panAnchorOffset.X + (e.X - _panAnchorScreen.X),
                    _panAnchorOffset.Y + (e.Y - _panAnchorScreen.Y));
                ClampPan();
                Invalidate();
                return;
            }
            LiveBotStatus? nearest = HitTestBot(e.Location);
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

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_panning)
                return;
            _panning = false;
            Cursor = _hoverBot != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_panning)
                return;
            if (_hoverBot != null)
            {
                _hoverBot = null;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left)
                return;
            LiveBotStatus? bot = HitTestBot(e.Location);
            if (bot != null)
                BotActivated?.Invoke(bot.BotId);
        }

        private LiveBotStatus? HitTestBot(Point location)
        {
            LiveBotStatus? nearest = null;
            float best = HitRadius * HitRadius;
            foreach ((LiveBotStatus bot, _, PointF at, _) in _dots)
            {
                float dx = at.X - location.X, dy = at.Y - location.Y;
                float distance = dx * dx + dy * dy;
                if (distance < best)
                {
                    best = distance;
                    nearest = bot;
                }
            }
            return nearest;
        }

        private float HitRadius => Math.Max(8f, 4.5f * (float)Math.Sqrt(_zoom));

        // ------------------------------------------------------------------ view transform

        /// <summary>Content coordinates are the 100% layout inside <see cref="_mapArea"/>; this maps them to the screen.</summary>
        private PointF ViewPoint(float x, float y) =>
            new(_mapArea.X + x * _zoom + _pan.X, _mapArea.Y + y * _zoom + _pan.Y);

        private RectangleF ViewRect(RectangleF content)
        {
            PointF origin = ViewPoint(content.X, content.Y);
            return new RectangleF(origin.X, origin.Y, content.Width * _zoom, content.Height * _zoom);
        }

        private void SetZoom(float zoom, Point anchor)
        {
            zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
            if (Math.Abs(zoom - _zoom) < 0.0001f)
                return;
            // Keep the content point under the anchor (the cursor) exactly where it is while the scale changes.
            float anchorX = (anchor.X - _mapArea.X - _pan.X) / _zoom;
            float anchorY = (anchor.Y - _mapArea.Y - _pan.Y) / _zoom;
            _zoom = zoom;
            _pan = new PointF(anchor.X - _mapArea.X - anchorX * _zoom,
                anchor.Y - _mapArea.Y - anchorY * _zoom);
            ClampPan();
            ZoomChanged?.Invoke();
            Invalidate();
        }

        /// <summary>Keeps the map area covering its content: no gap beyond an edge, and centred when it is smaller.</summary>
        private void ClampPan()
        {
            if (_mapArea.Width <= 0 || _mapArea.Height <= 0)
                return;
            float scaledWidth = _mapArea.Width * _zoom;
            float scaledHeight = _mapArea.Height * _zoom;
            _pan.X = scaledWidth <= _mapArea.Width
                ? (_mapArea.Width - scaledWidth) / 2f
                : Math.Min(0f, Math.Max(_mapArea.Width - scaledWidth, _pan.X));
            _pan.Y = scaledHeight <= _mapArea.Height
                ? (_mapArea.Height - scaledHeight) / 2f
                : Math.Min(0f, Math.Max(_mapArea.Height - scaledHeight, _pan.Y));
        }

        /// <summary>
        /// Keeps the followed bot under the middle of the map as new snapshots arrive (owner 2026-11-02). The same
        /// content layout the detailed region view paints with is used, so the dot and the camera cannot disagree.
        /// </summary>
        private void FollowFocusedBot()
        {
            if (_followBotId is not long botId || _mapArea.Width <= 0 || _mapArea.Height <= 0)
                return;
            if (!_regions.TryGetValue(_selectedRegionId, out MapRegion? region))
                return;
            LiveBotStatus? bot = _snapshot?.Bots?.FirstOrDefault(candidate =>
                candidate.BotId == botId && candidate.RegionId == _selectedRegionId);
            if (bot == null)
                return;

            var contentArea = new RectangleF(0, 0, _mapArea.Width, _mapArea.Height);
            var mapRect = new RectangleF(contentArea.X, contentArea.Y + 6, contentArea.Width, contentArea.Height - 6);
            RectangleF bounds = RegionFitBounds(region, mapRect);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            float scale = bounds.Width / Math.Max(1, region.WorldWidth);
            float contentX = bounds.X + (bot.X - region.MinX) * scale;
            float contentY = bounds.Y + (bot.Y - region.MinY) * scale;
            _pan = new PointF(_mapArea.Width / 2f - contentX * _zoom, _mapArea.Height / 2f - contentY * _zoom);
            ClampPan();
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

            _mapArea = mapArea;
            if (_focusPending)
            {
                // The first paint after a jump: land at a readable zoom, then centre.
                _focusPending = false;
                _zoom = FocusZoom;
            }
            FollowFocusedBot();
            ClampPan();

            _dots.Clear();
            var contentArea = new RectangleF(0, 0, mapArea.Width, mapArea.Height);
            GraphicsState view = graphics.Save();
            graphics.SetClip(mapArea, CombineMode.Intersect);
            if (_selectedRegionId != 0 && _regions.TryGetValue(_selectedRegionId, out MapRegion? single))
                DrawRegion(graphics, single, contentArea, alpha, detailed: true, now);
            else
                DrawOverview(graphics, contentArea, alpha, now);
            graphics.Restore(view);

            DrawHeader(graphics, header, ageSeconds);
            if (_selectedRegionId != 0 && _regions.TryGetValue(_selectedRegionId, out MapRegion? selectedRegion))
                DrawRegionTitle(graphics, selectedRegion, mapArea);
            if (_dots.Count == 0)
            {
                using var empty = new SolidBrush(DaocTheme.Muted);
                graphics.DrawString(_serverRunning ? "No bots have a live position yet." : "Server is not running.",
                    _statusFont, empty, mapArea.X + 8, mapArea.Y + (_selectedRegionId != 0 ? 34 : 8));
            }
            if (_zoom > MinZoom)
                DrawZoomBadge(graphics, mapArea);
            DrawRealmLegend(graphics, mapArea);
            DrawTooltip(graphics);
        }

        private void DrawZoomBadge(Graphics graphics, Rectangle mapArea)
        {
            string following = _followBotId is long followedId ? $"   •   FOLLOWING {FocusedName(followedId)}" : string.Empty;
            string text = $"ZOOM {ZoomPercent}%{following}   •   drag to pan   •   RESET to fit";
            SizeF size = graphics.MeasureString(text, _zoneFont);
            var box = new RectangleF(mapArea.Right - size.Width - 20, mapArea.Bottom - size.Height - 12,
                size.Width + 12, size.Height + 4);
            using (var fill = new SolidBrush(Color.FromArgb(190, DaocTheme.StoneDark)))
                graphics.FillRectangle(fill, box);
            using (var border = new Pen(Color.FromArgb(150, DaocTheme.StoneLight)))
                graphics.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
            using var brush = new SolidBrush(_followBotId.HasValue ? DaocTheme.GoldLight : DaocTheme.Muted);
            graphics.DrawString(text, _zoneFont, brush, box.X + 6, box.Y + 2);
        }

        private string FocusedName(long botId)
        {
            string? name = _snapshot?.Bots?.FirstOrDefault(bot => bot.BotId == botId)?.Name;
            if (string.IsNullOrWhiteSpace(name))
                return "BOT";
            name = name.ToUpperInvariant();
            return name.Length <= 22 ? name : name[..21] + "…";
        }

        /// <summary>The realm colour key, with the live count of bots each realm has on the map (owner 2026-11-02).</summary>
        private void DrawRealmLegend(Graphics graphics, Rectangle mapArea)
        {
            if (_snapshot?.Bots == null)
                return;
            int albion = 0, midgard = 0, hibernia = 0;
            foreach (LiveBotStatus bot in _snapshot.Bots)
            {
                if (bot.RegionId <= 0 || !_regions.ContainsKey(bot.RegionId))
                    continue;
                switch (bot.Realm)
                {
                    case 1: albion++; break;
                    case 2: midgard++; break;
                    case 3: hibernia++; break;
                }
            }
            if (albion + midgard + hibernia == 0)
                return;
            (string Text, int Count, Color Color)[] entries =
            [
                ("Albion", albion, DaocTheme.Albion),
                ("Midgard", midgard, DaocTheme.Midgard),
                ("Hibernia", hibernia, DaocTheme.Hibernia),
            ];
            var sizes = new SizeF[entries.Length];
            float width = 12;
            for (int index = 0; index < entries.Length; index++)
            {
                sizes[index] = graphics.MeasureString($"{entries[index].Text} {entries[index].Count:N0}", _zoneFont);
                width += 10 + sizes[index].Width + 10;
            }
            const float height = 22;
            // Top-right, clear of the region caption (top-left) and the zoom/follow badge (bottom-right).
            var box = new RectangleF(mapArea.Right - width - 8, mapArea.Y + 8, width, height);
            using (var fill = new SolidBrush(Color.FromArgb(190, DaocTheme.StoneDark)))
                graphics.FillRectangle(fill, box);
            using (var border = new Pen(Color.FromArgb(140, DaocTheme.StoneLight)))
                graphics.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
            float x = box.X + 8;
            for (int index = 0; index < entries.Length; index++)
            {
                using (var dot = new SolidBrush(entries[index].Color))
                    graphics.FillEllipse(dot, x, box.Y + height / 2f - 3f, 6f, 6f);
                x += 10;
                using var text = new SolidBrush(DaocTheme.Parchment);
                graphics.DrawString($"{entries[index].Text} {entries[index].Count:N0}", _zoneFont, text, x, box.Y + 4f);
                x += sizes[index].Width + 10;
            }
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

        private void DrawOverview(Graphics graphics, RectangleF area, float alpha, DateTime now)
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
                var cell = new RectangleF(
                    area.X + column * (cellWidth + gap),
                    area.Y + row * (cellHeight + gap),
                    cellWidth,
                    cellHeight);
                DrawRegionPanel(graphics, shown[i], cell, alpha, detailed: false, now);
            }
        }

        private void DrawRegionPanel(Graphics graphics, MapRegion region, RectangleF cell, float alpha, bool detailed,
            DateTime now)
        {
            Rectangle screenCell = Rectangle.Round(ViewRect(cell));
            using (var panel = new SolidBrush(Color.FromArgb(150, DaocTheme.StoneDark)))
                graphics.FillRectangle(panel, screenCell);
            using (var border = new Pen(Color.FromArgb(180, DaocTheme.StoneLight)))
                graphics.DrawRectangle(border, screenCell.X, screenCell.Y, screenCell.Width - 1, screenCell.Height - 1);

            const int labelHeight = 20;
            var titleRect = ViewRect(new RectangleF(cell.X + 6, cell.Y + 4, cell.Width - 12, labelHeight));
            using (var titleBrush = new SolidBrush(RealmColor(region.Realm)))
                graphics.DrawString(region.Name.ToUpperInvariant(), _regionFont, titleBrush, titleRect.X, titleRect.Y);
            using (var countBrush = new SolidBrush(DaocTheme.Muted))
            {
                int count = CountBotsInRegion(region.Id);
                string countText = count == 1 ? "1 bot" : $"{count} bots";
                SizeF size = graphics.MeasureString(countText, _zoneFont);
                graphics.DrawString(countText, _zoneFont, countBrush,
                    screenCell.Right - 8 - size.Width, screenCell.Y + 7);
            }

            var mapRect = new RectangleF(cell.X + 6, cell.Y + labelHeight + 2, cell.Width - 12, cell.Height - labelHeight - 10);
            DrawRegionMap(graphics, region, mapRect, alpha, detailed);

            // Frame the region's zone area with the realm colour so the panels read apart.
            RectangleF bounds = ViewRect(RegionFitBounds(region, mapRect));
            using (var frame = new Pen(Color.FromArgb(150, RealmColor(region.Realm)), detailed ? 2f : 1f))
                graphics.DrawRectangle(frame, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        private void DrawRegion(Graphics graphics, MapRegion region, RectangleF area, float alpha, bool detailed,
            DateTime now)
        {
            var mapRect = new RectangleF(area.X, area.Y + 6, area.Width, area.Height - 6);
            DrawRegionMap(graphics, region, mapRect, alpha, detailed);
        }

        /// <summary>The selected region's caption, drawn as a screen overlay so it stays readable while the map is zoomed.</summary>
        private void DrawRegionTitle(Graphics graphics, MapRegion region, Rectangle area)
        {
            using var title = new SolidBrush(DaocTheme.GoldLight);
            int count = CountBotsInRegion(region.Id);
            string text = $"{region.Name.ToUpperInvariant()} — {(count == 1 ? "1 bot" : count + " bots")}";
            SizeF size = graphics.MeasureString(text, _regionFont);
            using var background = new SolidBrush(Color.FromArgb(210, DaocTheme.StoneDark));
            graphics.FillRectangle(background, area.X + 4, area.Y + 2, size.Width + 12, size.Height + 6);
            graphics.DrawString(text, _regionFont, title, area.X + 10, area.Y + 5);
        }

        private void DrawRegionMap(Graphics graphics, MapRegion region, RectangleF mapRect, float alpha, bool detailed)
        {
            RectangleF bounds = RegionFitBounds(region, mapRect);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;
            float scale = bounds.Width / Math.Max(1, region.WorldWidth);
            RectangleF screenBounds = ViewRect(bounds);

            using (var water = new SolidBrush(Color.FromArgb(120, DaocTheme.Void)))
                graphics.FillRectangle(water, screenBounds);

            foreach (MapZone zone in region.Zones)
            {
                var zoneRect = ViewRect(new RectangleF(
                    bounds.X + (zone.OffsetX - region.MinX) * scale,
                    bounds.Y + (zone.OffsetY - region.MinY) * scale,
                    Math.Max(1f, zone.Width * scale),
                    Math.Max(1f, zone.Height * scale)));
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
                // Zone names: at high zoom the name of the zone you are inside is the only orientation left, so it is
                // drawn over the painted map too once the view is close enough (owner 2026-11-02).
                bool readable = zoneRect.Width > 150 && zoneRect.Height > 48;
                if (detailed && (image == null && zoneRect.Width > 58 && zoneRect.Height > 22 ||
                                 image != null && _zoom >= 2f && readable))
                {
                    SizeF nameSize = graphics.MeasureString(zone.Name, _zoneFont);
                    var chip = new RectangleF(zoneRect.X + 3, zoneRect.Y + 2, nameSize.Width + 8, nameSize.Height + 2);
                    if (image != null && chip.Width < zoneRect.Width)
                    {
                        using var chipFill = new SolidBrush(Color.FromArgb(150, DaocTheme.StoneDark));
                        graphics.FillRectangle(chipFill, chip);
                    }
                    using var label = new SolidBrush(Color.FromArgb(225, DaocTheme.Parchment));
                    graphics.DrawString(zone.Name, _zoneFont, label, chip.X + 4, chip.Y + 1);
                }
            }

            // Dots last, on top of the map. A dot outside the region bounds (a stale snapshot edge) is skipped.
            foreach (LiveBotStatus bot in _snapshot?.Bots ?? [])
            {
                if (bot.RegionId != region.Id || !_regions.ContainsKey(bot.RegionId))
                    continue;
                float contentX = bounds.X + (bot.X - region.MinX) * scale;
                float contentY = bounds.Y + (bot.Y - region.MinY) * scale;
                if (!bounds.Contains(contentX, contentY))
                    continue;
                PointF at = ViewPoint(contentX, contentY);
                if (!screenBounds.Contains(at))
                    continue;
                Color color = RealmColor(bot.Realm);
                color = Color.FromArgb((int)Math.Round(255 * alpha), color.R, color.G, color.B);
                DrawDot(graphics, bot, at.X, at.Y, color);
                _dots.Add((bot, screenBounds, at, color));
            }
        }

        private void DrawDot(Graphics graphics, LiveBotStatus bot, float x, float y, Color color)
        {
            bool hovered = ReferenceEquals(bot, _hoverBot);
            bool focused = _focusedBotId == bot.BotId;
            // Small dots (owner 2026-11-02: the map is a picture of the world, not a bag of pebbles). They grow a
            // little with the zoom and on hover, so a single bot still stays findable and easy to click.
            float zoomScale = Math.Min(1.5f, Math.Max(1f, (float)Math.Sqrt(_zoom)));
            float radius = (hovered ? 4f : 2.5f) * zoomScale;
            using (var outline = new SolidBrush(Color.FromArgb(Math.Max(55, (int)color.A), 8, 8, 10)))
                graphics.FillEllipse(outline, x - radius - 1.1f, y - radius - 1.1f, 2 * radius + 2.2f, 2 * radius + 2.2f);
            if (!bot.IsAlive)
            {
                using var deadPen = new Pen(Color.FromArgb(Math.Max(70, (int)color.A), 190, 190, 190), 1.4f);
                graphics.DrawEllipse(deadPen, x - radius, y - radius, 2 * radius, 2 * radius);
            }
            else
            {
                using (var fill = new SolidBrush(color))
                    graphics.FillEllipse(fill, x - radius, y - radius, 2 * radius, 2 * radius);
            }
            if (focused)
            {
                // The bot singled out by "Show on map": a gold ring and its name, readable at any zoom.
                using var focusRing = new Pen(Color.FromArgb(235, DaocTheme.GoldLight), 1.8f);
                graphics.DrawEllipse(focusRing, x - radius - 3.6f, y - radius - 3.6f, 2 * radius + 7.2f, 2 * radius + 7.2f);
                DrawDotNameTag(graphics, bot.Name, x, y + radius + 5f);
            }
            if (hovered)
            {
                using var ring = new Pen(Color.FromArgb(235, DaocTheme.GoldLight), 1.4f);
                graphics.DrawEllipse(ring, x - radius - 2f, y - radius - 2f, 2 * radius + 4f, 2 * radius + 4f);
            }
        }

        /// <summary>The focused bot's name, drawn as a small chip under its dot.</summary>
        private void DrawDotNameTag(Graphics graphics, string name, float x, float y)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            SizeF size = graphics.MeasureString(name, _zoneFont);
            float left = x - size.Width / 2f - 4f;
            var box = new RectangleF(left, y, size.Width + 8f, size.Height + 2f);
            using (var fill = new SolidBrush(Color.FromArgb(215, DaocTheme.StoneDark)))
                graphics.FillRectangle(fill, box);
            using (var border = new Pen(Color.FromArgb(180, DaocTheme.Gold)))
                graphics.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
            using var text = new SolidBrush(DaocTheme.GoldLight);
            graphics.DrawString(name, _zoneFont, text, left + 4f, y + 1f);
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

        /// <summary>Fits a region's zone rectangle inside an area, in content (100%) coordinates.</summary>
        private static RectangleF RegionFitBounds(MapRegion region, RectangleF area)
        {
            if (region.WorldWidth <= 0 || region.WorldHeight <= 0)
                return RectangleF.Empty;
            float scale = Math.Min(area.Width / (float)region.WorldWidth, area.Height / (float)region.WorldHeight);
            float width = region.WorldWidth * scale;
            float height = region.WorldHeight * scale;
            return new RectangleF(area.X + (area.Width - width) / 2f,
                area.Y + (area.Height - height) / 2f, width, height);
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
