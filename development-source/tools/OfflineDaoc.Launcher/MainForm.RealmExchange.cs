using System.Data.SQLite;
using System.Globalization;

namespace OfflineDaoc.Launcher;

/// <summary>
/// Launcher dashboard tab: the Realm Exchange. It shows every item currently listed in each realm's exchange
/// (the real persisted listings the brokers sell), who listed it and its price, plus the durable purchase
/// history the server records for completed trades. Read-only and on demand: opening the tab or pressing
/// REFRESH reads the world database; nothing polls it and nothing is written.
/// </summary>
internal sealed partial class MainForm
{
    // RealmExchangeBroker.GetOwnerLot: the exchange listings' OwnerLot per realm.
    private const ushort ExchangeAlbionOwnerLot = 65001;
    private const ushort ExchangeMidgardOwnerLot = 65002;
    private const ushort ExchangeHiberniaOwnerLot = 65003;

    private readonly BindingSource _exchangeListingSource = new();
    private readonly List<ExchangeListingRow> _exchangeListings = [];
    private readonly BindingSource _exchangePurchaseSource = new();
    private readonly List<ExchangePurchaseRow> _exchangePurchases = [];
    private readonly DataGridView _exchangeListingGrid = new();
    private readonly DataGridView _exchangePurchaseGrid = new();
    private readonly ComboBox _exchangeRealm = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly TextBox _exchangeSearch = new() { PlaceholderText = "Search item, seller or buyer", Width = 320 };
    private readonly Label _exchangeStatus = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = DaocTheme.Muted,
        BackColor = DaocTheme.Tint(48, 38, 26),
        Font = new Font("Georgia", 8.5f, FontStyle.Italic),
        Padding = new Padding(8, 0, 0, 0),
    };
    private Button? _refreshExchangeButton;
    private bool _refreshingExchange;
    private DateTime _exchangeReadAt;
    private string _exchangeListingSortProperty = "ItemName";
    private bool _exchangeListingSortAscending = true;
    private string _exchangePurchaseSortProperty = "Sold";
    private bool _exchangePurchaseSortAscending = false;

    private Control BuildRealmExchangePanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(7),
            BackColor = DaocTheme.Tint(48, 38, 26),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = DaocTheme.Tint(69, 51, 31) };
        header.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "✦  THE REALM EXCHANGE  ✦    Camelot • Jordheim • Tir na Nog",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Georgia", 10f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            BackColor = DaocTheme.Tint(69, 51, 31),
            Padding = new Padding(11, 0, 0, 0),
            BorderStyle = BorderStyle.Fixed3D,
        });
        _refreshExchangeButton = ActionButton("REFRESH REALM EXCHANGE", DaocTheme.Tint(126, 104, 67));
        _refreshExchangeButton.Dock = DockStyle.Right;
        _refreshExchangeButton.Width = 215;
        _refreshExchangeButton.Click += async (_, _) => await RefreshRealmExchangeAsync();
        header.Controls.Add(_refreshExchangeButton);
        layout.Controls.Add(header, 0, 0);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 6, 0, 3),
            BackColor = DaocTheme.Tint(48, 38, 26),
        };
        filters.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 7, 6, 0),
            Text = "REALM",
            Font = new Font("Georgia", 8.25f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
        });
        _exchangeRealm.Items.AddRange(["All realms", "Albion", "Midgard", "Hibernia"]);
        _exchangeRealm.SelectedIndex = 0;
        StyleInput(_exchangeRealm);
        filters.Controls.Add(_exchangeRealm);
        StyleInput(_exchangeSearch);
        filters.Controls.Add(_exchangeSearch);
        filters.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(12, 7, 0, 0),
            Text = "All listings for the chosen realm • read from the world database when this tab opens or you press REFRESH",
            ForeColor = DaocTheme.Tint(196, 174, 126),
            Font = new Font("Georgia", 8f, FontStyle.Italic),
        });
        layout.Controls.Add(filters, 0, 1);

        ConfigureExchangeGrid(_exchangeListingGrid, _exchangeListingSource);
        _exchangeListingGrid.Columns.Add(TextColumn("Item", "ItemName", 260, DataGridViewAutoSizeColumnMode.Fill));
        _exchangeListingGrid.Columns.Add(TextColumn("Level", "ItemLevel", 55));
        _exchangeListingGrid.Columns.Add(TextColumn("Qty", "Quantity", 55));
        _exchangeListingGrid.Columns.Add(TextColumn("Listed by", "Seller", 185));
        _exchangeListingGrid.Columns.Add(TextColumn("Price", "Price", 115));
        _exchangeListingGrid.Columns.Add(TextColumn("Realm", "Realm", 85));
        _exchangeListingGrid.Columns.Add(TextColumn("Expires / remaining", "Expires", 210));
        foreach (DataGridViewColumn column in _exchangeListingGrid.Columns)
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
        _exchangeListingGrid.ColumnHeaderMouseClick += (_, eventArgs) => SortExchangeListingsByColumn(eventArgs.ColumnIndex);

        ConfigureExchangeGrid(_exchangePurchaseGrid, _exchangePurchaseSource);
        _exchangePurchaseGrid.Columns.Add(TextColumn("Sold", "Sold", 150));
        _exchangePurchaseGrid.Columns.Add(TextColumn("Item", "ItemName", 260, DataGridViewAutoSizeColumnMode.Fill));
        _exchangePurchaseGrid.Columns.Add(TextColumn("Qty", "Quantity", 55));
        _exchangePurchaseGrid.Columns.Add(TextColumn("Seller", "Seller", 170));
        _exchangePurchaseGrid.Columns.Add(TextColumn("Buyer", "Buyer", 170));
        _exchangePurchaseGrid.Columns.Add(TextColumn("Price", "Price", 115));
        _exchangePurchaseGrid.Columns.Add(TextColumn("Realm", "Realm", 85));
        foreach (DataGridViewColumn column in _exchangePurchaseGrid.Columns)
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
        _exchangePurchaseGrid.ColumnHeaderMouseClick += (_, eventArgs) => SortExchangePurchasesByColumn(eventArgs.ColumnIndex);

        var body = new DaocTabControl { Dock = DockStyle.Fill };
        var listingsTab = new TabPage("Live listings") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text, Padding = new Padding(3) };
        listingsTab.Controls.Add(_exchangeListingGrid);
        var purchasesTab = new TabPage("Purchase history") { BackColor = DaocTheme.Panel, ForeColor = DaocTheme.Text, Padding = new Padding(3) };
        purchasesTab.Controls.Add(_exchangePurchaseGrid);
        body.TabPages.Add(listingsTab);
        body.TabPages.Add(purchasesTab);
        layout.Controls.Add(body, 0, 2);

        _exchangeStatus.Text = "Open this tab or press REFRESH REALM EXCHANGE to read the world database.";
        layout.Controls.Add(_exchangeStatus, 0, 3);
        return layout;
    }

    private static void ConfigureExchangeGrid(DataGridView grid, BindingSource source)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.BackgroundColor = DaocTheme.Tint(205, 187, 144);
        grid.BorderStyle = BorderStyle.Fixed3D;
        grid.GridColor = DaocTheme.Tint(127, 103, 64);
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = DaocTheme.Tint(80, 59, 35),
            ForeColor = DaocTheme.GoldLight,
            SelectionBackColor = DaocTheme.Tint(80, 59, 35),
            Font = new Font("Georgia", 8.25f, FontStyle.Bold),
            Padding = new Padding(2),
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = DaocTheme.Tint(215, 199, 157),
            ForeColor = DaocTheme.Ink,
            SelectionBackColor = DaocTheme.Tint(117, 85, 45),
            SelectionForeColor = Color.White,
            Padding = new Padding(2),
            Font = new Font("Georgia", 8.25f),
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = DaocTheme.Tint(201, 182, 138) };
        grid.ColumnHeadersHeight = 28;
        grid.RowTemplate.Height = 25;
        grid.DataSource = source;
    }

    private async Task RefreshRealmExchangeAsync()
    {
        if (_refreshingExchange)
            return;

        _refreshingExchange = true;
        if (_refreshExchangeButton != null)
        {
            _refreshExchangeButton.Enabled = false;
            _refreshExchangeButton.Text = "READING EXCHANGE…";
        }
        _exchangeStatus.Text = "Reading Realm Exchange listings and purchase history…";
        try
        {
            (List<ExchangeListingRow> listings, List<ExchangePurchaseRow> purchases) = await Task.Run(ReadRealmExchange);
            if (IsDisposed)
                return;
            _exchangeListings.Clear();
            _exchangeListings.AddRange(listings);
            _exchangePurchases.Clear();
            _exchangePurchases.AddRange(purchases);
            _exchangeReadAt = DateTime.Now;
            if (!File.Exists(_database))
            {
                _exchangeStatus.Text = "The world database isn't ready yet — start the server once, then refresh.";
                return;
            }
            ApplyExchangeFilter();
            _dashboardFooterSummary = $"Realm Exchange {_exchangeListings.Count:N0} listings • {_exchangePurchases.Count:N0} purchases • updated {_exchangeReadAt:T}";
            _footer.Text = _dashboardFooterSummary;
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
                _exchangeStatus.Text = $"Could not read the Realm Exchange: {exception.Message}";
        }
        finally
        {
            _refreshingExchange = false;
            if (_refreshExchangeButton != null)
            {
                _refreshExchangeButton.Text = "REFRESH REALM EXCHANGE";
                _refreshExchangeButton.Enabled = true;
            }
        }
    }

    /// <summary>The world database read: live listings (real persisted broker items) and the recorded sales ledger.</summary>
    private (List<ExchangeListingRow> Listings, List<ExchangePurchaseRow> Purchases) ReadRealmExchange()
    {
        var listings = new List<ExchangeListingRow>();
        var purchases = new List<ExchangePurchaseRow>();
        if (!File.Exists(_database))
            return (listings, purchases);

        using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
        connection.Open();
        ReadExchangeListings(connection, listings);
        ReadExchangePurchases(connection, purchases);
        return (listings, purchases);
    }

    private static void ReadExchangeListings(SQLiteConnection connection, List<ExchangeListingRow> listings)
    {
        if (!TableExists(connection, "Inventory") || !TableExists(connection, "ItemTemplate"))
            return;

        bool uniqueItems = TableExists(connection, "ItemUnique");
        bool characters = TableExists(connection, "DOLCharacters");
        bool bots = TableExists(connection, "offline_world_bots");
        string itemName = uniqueItems
            ? "COALESCE(NULLIF(u.Name, ''), NULLIF(t.Name, ''), 'Unknown item')"
            : "COALESCE(NULLIF(t.Name, ''), 'Unknown item')";
        string itemLevel = uniqueItems ? "COALESCE(u.Level, t.Level, 0)" : "COALESCE(t.Level, 0)";
        string seller = characters && bots ? "COALESCE(c.Name, b.Name)"
            : characters ? "c.Name" : bots ? "b.Name" : "NULL";
        string listedUtc = ColumnExists(connection, "Inventory", "RealmExchangeListedUtc")
            ? "COALESCE(i.RealmExchangeListedUtc, '')" : "''";
        string sql = $"""
            SELECT {itemName} AS ItemName,
                   {seller} AS SellerName,
                   {(bots ? "(b.BotId IS NOT NULL)" : "0")} AS IsBot,
                   {itemLevel} AS ItemLevel,
                   i.Count,
                   i.SellPrice,
                   CASE i.OwnerLot
                       WHEN {ExchangeAlbionOwnerLot} THEN 'Albion'
                       WHEN {ExchangeMidgardOwnerLot} THEN 'Midgard'
                       WHEN {ExchangeHiberniaOwnerLot} THEN 'Hibernia'
                       ELSE 'Unknown' END AS RealmName,
                   {listedUtc} AS ListedUtc
            FROM Inventory i
            LEFT JOIN ItemTemplate t ON t.Id_nb = i.ITemplate_Id
            {(uniqueItems ? "LEFT JOIN ItemUnique u ON u.Id_nb = i.UTemplate_Id" : string.Empty)}
            {(characters ? "LEFT JOIN DOLCharacters c ON c.DOLCharacters_ID = i.OwnerID" : string.Empty)}
            {(bots ? "LEFT JOIN offline_world_bots b ON i.OwnerID = ('offlinebot:' || b.BotId)" : string.Empty)}
            WHERE i.OwnerLot IN ({ExchangeAlbionOwnerLot}, {ExchangeMidgardOwnerLot}, {ExchangeHiberniaOwnerLot})
              AND i.SellPrice > 0
            ORDER BY RealmName, ItemName
            """;
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using SQLiteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string sellerName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            bool isBot = !reader.IsDBNull(2) && reader.GetInt32(2) != 0;
            string listed = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
            listings.Add(new ExchangeListingRow(
                reader.IsDBNull(0) ? "Unknown item" : reader.GetString(0),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                DescribeSeller(sellerName, isBot),
                reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                reader.IsDBNull(6) ? "Unknown" : reader.GetString(6),
                DescribeExpiry(isBot, listed)));
        }
    }

    private static string DescribeSeller(string name, bool isBot)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Unknown seller";
        return name + (isBot ? " (bot)" : " (player)");
    }

    /// <summary>A bot listing lasts 24h from its first load (RealmExchangeExpiry); a player listing never expires.</summary>
    private static string DescribeExpiry(bool isBot, string listedUtc)
    {
        if (!isBot)
            return "Never (player listing)";
        if (!DateTime.TryParse(listedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime listed))
            return "24h from first load";
        DateTime expiresUtc = listed.ToUniversalTime().AddHours(24);
        TimeSpan remaining = expiresUtc - DateTime.UtcNow;
        return remaining <= TimeSpan.Zero
            ? "Expired — awaiting removal"
            : $"{(int)remaining.TotalHours}h {remaining.Minutes:00}m left • {expiresUtc.ToLocalTime():MMM d HH:mm}";
    }

    private static void ReadExchangePurchases(SQLiteConnection connection, List<ExchangePurchaseRow> purchases)
    {
        if (!TableExists(connection, "realm_exchange_sales"))
            return;

        using var command = connection.CreateCommand();
        // SaleSequence grows forever; the ledger keeps the latest RealmExchangeSaleLedger.Capacity rows, so newest first.
        command.CommandText = """
            SELECT SoldUtc, Realm, ItemName, Quantity, SellerId, SellerName, BuyerId, BuyerName, PriceCopper
            FROM realm_exchange_sales
            WHERE SaleSequence > 0
            ORDER BY SaleSequence DESC
            """;
        using SQLiteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string soldUtc = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            DateTime soldLocal = DateTime.TryParse(soldUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime sold)
                ? sold.ToLocalTime() : DateTime.MinValue;
            string realm = reader.IsDBNull(1) ? "Unknown"
                : reader.GetInt32(1) switch { 1 => "Albion", 2 => "Midgard", 3 => "Hibernia", _ => "Unknown" };
            purchases.Add(new ExchangePurchaseRow(
                soldLocal,
                soldLocal == DateTime.MinValue ? soldUtc : soldLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                realm,
                reader.IsDBNull(2) ? "Unknown item" : reader.GetString(2),
                reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                DescribeParticipant(reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.IsDBNull(5) ? string.Empty : reader.GetString(5)),
                DescribeParticipant(reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    reader.IsDBNull(7) ? string.Empty : reader.GetString(7)),
                reader.IsDBNull(8) ? 0 : reader.GetInt64(8)));
        }
    }

    private static string DescribeParticipant(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Unknown";
        return name + (id.StartsWith("offlinebot:", StringComparison.OrdinalIgnoreCase) ? " (bot)" : " (player)");
    }

    private void ApplyExchangeFilter()
    {
        string realm = _exchangeRealm.SelectedItem as string ?? "All realms";
        string search = _exchangeSearch.Text.Trim();
        IEnumerable<ExchangeListingRow> listings = _exchangeListings.Where(row =>
            (realm == "All realms" || row.Realm.Equals(realm, StringComparison.OrdinalIgnoreCase)) &&
            (search.Length == 0 || $"{row.ItemName} {row.Seller} {row.Realm}".Contains(search, StringComparison.OrdinalIgnoreCase)));
        IEnumerable<ExchangePurchaseRow> purchases = _exchangePurchases.Where(row =>
            (realm == "All realms" || row.Realm.Equals(realm, StringComparison.OrdinalIgnoreCase)) &&
            (search.Length == 0 || $"{row.ItemName} {row.Seller} {row.Buyer} {row.Realm}".Contains(search, StringComparison.OrdinalIgnoreCase)));

        List<ExchangeListingRow> visibleListings = SortExchangeListings(listings);
        List<ExchangePurchaseRow> visiblePurchases = SortExchangePurchases(purchases);
        _exchangeListingSource.DataSource = visibleListings;
        _exchangeListingSource.ResetBindings(false);
        _exchangePurchaseSource.DataSource = visiblePurchases;
        _exchangePurchaseSource.ResetBindings(false);
        UpdateExchangeSortGlyphs();

        if (_exchangeReadAt == default)
        {
            _exchangeStatus.Text = "Open this tab or press REFRESH REALM EXCHANGE to read the world database.";
            return;
        }
        long traded = visiblePurchases.Sum(row => row.PriceCopper);
        string listingsText = _exchangeListings.Count == 0
            ? "No items are listed on the exchange."
            : $"{visibleListings.Count:N0} of {_exchangeListings.Count:N0} listings";
        string purchasesText = _exchangePurchases.Count == 0
            ? "no completed purchases recorded yet"
            : $"{visiblePurchases.Count:N0} of {_exchangePurchases.Count:N0} completed purchases";
        _exchangeStatus.Text =
            $"{listingsText} • {purchasesText} • {FormatCopper(traded)} traded in this view • read {_exchangeReadAt:T}";
    }

    private void SortExchangeListingsByColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _exchangeListingGrid.Columns.Count)
            return;
        string property = _exchangeListingGrid.Columns[columnIndex].DataPropertyName;
        if (string.IsNullOrWhiteSpace(property))
            return;
        _exchangeListingSortAscending = property.Equals(_exchangeListingSortProperty, StringComparison.Ordinal)
            ? !_exchangeListingSortAscending : true;
        _exchangeListingSortProperty = property;
        ApplyExchangeFilter();
    }

    private void SortExchangePurchasesByColumn(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _exchangePurchaseGrid.Columns.Count)
            return;
        string property = _exchangePurchaseGrid.Columns[columnIndex].DataPropertyName;
        if (string.IsNullOrWhiteSpace(property))
            return;
        _exchangePurchaseSortAscending = property.Equals(_exchangePurchaseSortProperty, StringComparison.Ordinal)
            ? !_exchangePurchaseSortAscending : true;
        _exchangePurchaseSortProperty = property;
        ApplyExchangeFilter();
    }

    private List<ExchangeListingRow> SortExchangeListings(IEnumerable<ExchangeListingRow> rows)
    {
        var sorted = rows.ToList();
        sorted.Sort((first, second) =>
        {
            int result = _exchangeListingSortProperty switch
            {
                "ItemLevel" => first.ItemLevel.CompareTo(second.ItemLevel),
                "Quantity" => first.Quantity.CompareTo(second.Quantity),
                "Price" => first.PriceCopper.CompareTo(second.PriceCopper),
                "Seller" => string.Compare(first.Seller, second.Seller, StringComparison.OrdinalIgnoreCase),
                "Realm" => string.Compare(first.Realm, second.Realm, StringComparison.OrdinalIgnoreCase),
                "Expires" => string.Compare(first.Expires, second.Expires, StringComparison.OrdinalIgnoreCase),
                _ => string.Compare(first.ItemName, second.ItemName, StringComparison.OrdinalIgnoreCase),
            };
            if (result == 0)
                result = string.Compare(first.ItemName, second.ItemName, StringComparison.OrdinalIgnoreCase);
            return _exchangeListingSortAscending ? result : -result;
        });
        return sorted;
    }

    private List<ExchangePurchaseRow> SortExchangePurchases(IEnumerable<ExchangePurchaseRow> rows)
    {
        var sorted = rows.ToList();
        sorted.Sort((first, second) =>
        {
            int result = _exchangePurchaseSortProperty switch
            {
                "ItemName" => string.Compare(first.ItemName, second.ItemName, StringComparison.OrdinalIgnoreCase),
                "Quantity" => first.Quantity.CompareTo(second.Quantity),
                "Seller" => string.Compare(first.Seller, second.Seller, StringComparison.OrdinalIgnoreCase),
                "Buyer" => string.Compare(first.Buyer, second.Buyer, StringComparison.OrdinalIgnoreCase),
                "Price" => first.PriceCopper.CompareTo(second.PriceCopper),
                "Realm" => string.Compare(first.Realm, second.Realm, StringComparison.OrdinalIgnoreCase),
                _ => first.SoldUtcLocal.CompareTo(second.SoldUtcLocal),
            };
            if (result == 0)
                result = first.SoldUtcLocal.CompareTo(second.SoldUtcLocal);
            return _exchangePurchaseSortAscending ? result : -result;
        });
        return sorted;
    }

    private void UpdateExchangeSortGlyphs()
    {
        foreach (DataGridViewColumn column in _exchangeListingGrid.Columns)
        {
            column.HeaderCell.SortGlyphDirection = column.DataPropertyName.Equals(_exchangeListingSortProperty, StringComparison.Ordinal)
                ? _exchangeListingSortAscending ? SortOrder.Ascending : SortOrder.Descending
                : SortOrder.None;
        }
        foreach (DataGridViewColumn column in _exchangePurchaseGrid.Columns)
        {
            column.HeaderCell.SortGlyphDirection = column.DataPropertyName.Equals(_exchangePurchaseSortProperty, StringComparison.Ordinal)
                ? _exchangePurchaseSortAscending ? SortOrder.Ascending : SortOrder.Descending
                : SortOrder.None;
        }
    }

    private sealed record ExchangeListingRow(string ItemName, int ItemLevel, int Quantity, string Seller, long PriceCopper, string Realm, string Expires)
    {
        public string Price => FormatCopper(PriceCopper);
    }

    private sealed record ExchangePurchaseRow(DateTime SoldUtcLocal, string Sold, string Realm, string ItemName, int Quantity, string Seller, string Buyer, long PriceCopper)
    {
        public string Price => FormatCopper(PriceCopper);
    }
}
