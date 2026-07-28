using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Infrastructure;
using Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;
using Almutamakkin.DatabaseBridge.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Almutamakkin.DatabaseBridge.App;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly AppSettingsStore _settingsStore;
    private readonly IDatabaseProfileStore _profileStore;
    private readonly IDatabaseConnectionTester _connectionTester;
    private readonly ISqlServerDiscovery _discovery;
    private readonly IActiveRequestTracker _activeRequestTracker;
    private readonly ILiveQueryActivityFeed _liveQueryActivityFeed;
    private readonly ISecretProtector _secretProtector;
    private readonly LocalTestCommandTransport _localTransport;
    private readonly WebSocketCommandTransport _webSocketTransport;
    private readonly SupabaseBridgeTransport _supabaseTransport;
    private readonly BridgeHostService _bridgeHostService;
    private readonly GitHubReleaseUpdateChecker _updateChecker;
    private readonly IServiceProvider _serviceProvider;
    private readonly Button _updateButton = new()
    {
        Text = "تحديث متاح",
        Width = 124,
        Height = 40,
        Visible = false,
        BackColor = Color.FromArgb(12, 144, 128),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
    };
    private readonly NotifyIcon _updateNotifyIcon = new()
    {
        Icon = SystemIcons.Information,
        Text = "جسر قاعدة البيانات — المتمكن",
        Visible = true,
    };

    private readonly Label _bridgeStatusLabel = new() { AutoSize = true };
    private readonly Label _databaseStatusLabel = new() { AutoSize = true };
    private readonly Label _transportModeLabel = new() { AutoSize = true };
    private readonly Label _tunnelIdLabel = new() { AutoSize = true };
    private readonly Label _pairingCodeLabel = new() { AutoSize = true, Font = new Font("Consolas", 14F, FontStyle.Bold) };
    private readonly Label _pairingExpiresLabel = new() { AutoSize = true };
    private readonly PictureBox _pairingQrPicture = new()
    {
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.White,
        Width = 200,
        Height = 200,
        BorderStyle = BorderStyle.FixedSingle,
    };
    private string _lastQrPayload = string.Empty;
    private readonly Label _lastPollStatusLabel = new() { AutoSize = true, MaximumSize = new Size(640, 0) };
    private readonly Label _activeQueriesLabel = new() { AutoSize = true };
    private readonly Label _lastRequestLabel = new() { AutoSize = true, MaximumSize = new Size(640, 0) };
    private readonly Label _lastErrorLabel = new() { AutoSize = true, ForeColor = UITheme.ErrorColor, MaximumSize = new Size(640, 0) };
    private readonly ComboBox _transportCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly Button _startButton = new() { Text = "Start Bridge", Width = 120 };
    private readonly Button _stopButton = new() { Text = "Stop Bridge", Width = 120, Enabled = false };
    private readonly Button _registerBridgeButton = new() { Text = "تسجيل جهاز الجسر", Width = 140 };
    private readonly Button _refreshPairingButton = new() { Text = "تحديث رمز الاقتران", Width = 150 };
    private readonly Button _testDatabaseButton = new() { Text = "Test Database", Width = 120 };
    private readonly Button _openLogsButton = new() { Text = "Open Logs", Width = 120 };
    private readonly Button _openTestConsoleButton = new() { Text = "Open Test Console", Width = 140 };
    private readonly Button _fetchDatabasesButton = new() { Text = "جلب قواعد البيانات", Width = 150 };
    private readonly Button _remoteDatabaseButton = new() { Text = "ربط قاعدة بعيدة", Width = 150 };
    private readonly Button _profilesButton = new() { Text = "ملفات الاتصال", Width = 140 };
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _liveInvoiceTimer = new() { Interval = 8000 };
    private readonly System.Windows.Forms.Timer _changeWatchTimer = new() { Interval = 30_000 };
    private readonly System.Windows.Forms.Timer _domainWatchTimer = new() { Interval = 60_000 };
    private bool _liveInvoiceTickRunning;
    private bool _changeWatchTickRunning;
    private bool _domainWatchTickRunning;
    private string _lastChangeWatchSummary = "المراقبة: في انتظار التشغيل";
    private string _lastDomainWatchSummary = "مراقبة الدلتا: متوقفة";
    private readonly Label _liveFeaturesStatusLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Top,
        Height = 24,
        Font = new Font("Segoe UI", 9.5F),
        ForeColor = Color.FromArgb(67, 56, 202),
    };
    private readonly Label _changeWatchStatusLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Top,
        Height = 24,
        Font = new Font("Segoe UI", 9F),
        ForeColor = Color.FromArgb(22, 101, 52),
    };
    private readonly Label _domainWatchStatusLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Top,
        Height = 24,
        Font = new Font("Segoe UI", 9F),
        ForeColor = Color.FromArgb(91, 33, 182),
        Text = "مراقبة الدلتا: متوقفة",
    };

    private readonly Panel _topNavPanel = new() { Height = 60, Dock = DockStyle.Top };
    private readonly Panel _mainContentPanel = new() { Dock = DockStyle.Fill };
    private readonly Panel _dashboardPanel = new() { Dock = DockStyle.Fill };
    private readonly Panel _logsPanel = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Button _navDashboardButton = new() { Text = "قاعدة البيانات", Width = 150, Height = 40 };
    private readonly Button _navPrinterButton = new() { Text = "الطابعة", Width = 150, Height = 40 };
    private readonly Button _navLogsButton = new() { Text = "السجلات", Width = 150, Height = 40 };
    private readonly Button _navRemoteDbButton = new()
    {
        Text = "ربط قاعدة بعيدة",
        Width = 160,
        Height = 40,
        BackColor = Color.FromArgb(0, 120, 215),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
    };
    private readonly Button _navLocalDbButton = new()
    {
        Text = "اتصال محلي",
        Width = 140,
        Height = 40,
        BackColor = Color.FromArgb(22, 101, 52),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
    };
    private readonly Button _navSyncSnapshotsButton = new()
    {
        Text = "مزامنة اللقطات",
        Width = 150,
        Height = 40,
        BackColor = Color.FromArgb(22, 163, 74),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
    };
    private readonly Button _testDebtNotificationButton = new()
    {
        Text = "اختبار إشعار دين",
        Width = 150,
        Height = 40,
        BackColor = Color.FromArgb(180, 83, 9),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
    };
    private readonly Button _testShiftCloseNotificationButton = new()
    {
        Text = "اختبار إغلاق وردية",
        Width = 160,
        Height = 40,
        BackColor = Color.FromArgb(124, 58, 237),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
    };
    private readonly Panel _printerPanel = new() { Dock = DockStyle.Fill, Visible = false };
    private Form? _embeddedPrinterForm;
    private Almutamakkin.BarcodeBridge.Server.BridgeServerController? _printerServer;
    private Almutamakkin.BarcodeBridge.Logging.BridgeLogHub? _printerLogs;
    private readonly RichTextBox _logsRichTextBox = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 10F),
        BackColor = Color.White,
    };
    private readonly Label _logsHintLabel = new()
    {
        Text = "سجل الجسر الحي — انسخه أو صوّره عند حدوث خطأ.",
        AutoSize = true,
        ForeColor = UITheme.TextMuted,
    };
    private readonly Button _refreshLogsButton = new() { Text = "تحديث السجل", Width = 120, Height = 34 };
    private readonly Button _copyLogsButton = new() { Text = "نسخ السجل", Width = 120, Height = 34 };
    private readonly ComboBox _dbProfileCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Visible = false };
    private readonly ListBox _dbListBox = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 11F),
        IntegralHeight = false,
        BorderStyle = BorderStyle.FixedSingle,
    };
    private readonly ListBox _liveQueryListBox = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        BorderStyle = BorderStyle.None,
        Font = new Font("Segoe UI", 9.5F),
    };
    private readonly Label _connectionPillLabel = new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 13F, FontStyle.Bold),
        Padding = new Padding(8, 2, 8, 2),
    };
    private readonly Label _phoneDbStatusLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Top,
        Height = 24,
        Font = new Font("Segoe UI", 9.5F),
        ForeColor = Color.FromArgb(22, 101, 52),
    };
    private readonly Label _snapshotDbStatusLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Top,
        Height = 24,
        Font = new Font("Segoe UI", 9.5F),
        ForeColor = Color.FromArgb(30, 64, 175),
    };
    private readonly Label _activeDbHintLabel = new()
    {
        AutoSize = false,
        Dock = DockStyle.Bottom,
        Height = 28,
        ForeColor = Color.FromArgb(22, 101, 52),
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
    };

    private bool _suppressDbListEvents;
    private readonly Button _copyPairingCodeButton = new() { Text = "نسخ الرمز", Width = 100, Height = 36 };
    private long _lastLogPosition;
    private string _currentLogFile = "";


    private ICommandTransport? _activeTransport;
    private bool _bridgeRunning;
    private string? _lastDatabaseTestSummary;
    private Uri? _availableUpdateUri;

    public MainForm(
        AppSettings settings,
        AppSettingsStore settingsStore,
        IDatabaseProfileStore profileStore,
        IDatabaseConnectionTester connectionTester,
        ISqlServerDiscovery discovery,
        IActiveRequestTracker activeRequestTracker,
        ILiveQueryActivityFeed liveQueryActivityFeed,
        ISecretProtector secretProtector,
        LocalTestCommandTransport localTransport,
        WebSocketCommandTransport webSocketTransport,
        SupabaseBridgeTransport supabaseTransport,
        BridgeHostService bridgeHostService,
        GitHubReleaseUpdateChecker updateChecker,
        IServiceProvider serviceProvider)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _profileStore = profileStore;
        _connectionTester = connectionTester;
        _discovery = discovery;
        _activeRequestTracker = activeRequestTracker;
        _liveQueryActivityFeed = liveQueryActivityFeed;
        _secretProtector = secretProtector;
        _localTransport = localTransport;
        _webSocketTransport = webSocketTransport;
        _supabaseTransport = supabaseTransport;
        _bridgeHostService = bridgeHostService;
        _updateChecker = updateChecker;
        _serviceProvider = serviceProvider;

        InitializeComponent();
        WireEvents();
        RefreshStatusLabels();

        Shown += async (_, _) =>
        {
            await TryAutoStartSupabaseTunnelAsync();
            await CheckForBridgeUpdateAsync();
        };
        FormClosed += (_, _) => _updateNotifyIcon.Dispose();
    }

    private async Task CheckForBridgeUpdateAsync()
    {
        try
        {
            var installedVersion = typeof(MainForm).Assembly.GetName().Version ?? new Version(1, 0, 0);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var update = await _updateChecker.GetLatestAsync(installedVersion, cancellation.Token);
            if (update is null)
            {
                return;
            }

            _availableUpdateUri = update.ReleasePageUri;
            _updateButton.Text = $"تحديث {update.Version}";
            _updateButton.Visible = true;
            _updateNotifyIcon.BalloonTipTitle = "تحديث جديد لجسر المتمكن";
            _updateNotifyIcon.BalloonTipText = $"الإصدار {update.Version} متاح. اضغط هنا لفتح صفحة التنزيل.";
            _updateNotifyIcon.BalloonTipClicked += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(update.ReleasePageUri.AbsoluteUri)
                    {
                        UseShellExecute = true,
                    });
                }
                catch
                {
                    // Opening the browser is optional and must not affect the bridge.
                }
            };
            _updateNotifyIcon.ShowBalloonTip(10_000);
        }
        catch
        {
            // Version checks are best-effort. An unavailable GitHub API must not delay the bridge.
        }
    }

    private async Task TryAutoStartSupabaseTunnelAsync()
    {
        if (_bridgeRunning)
        {
            return;
        }

        _settings.TransportMode = TransportMode.SupabaseTunnel;
        _transportCombo.SelectedIndex = 2;
        EnsureSupabaseDefaults();

        try
        {
            if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
                string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret) ||
                _settings.TunnelId.StartsWith("LAB-", StringComparison.OrdinalIgnoreCase))
            {
                await RegisterBridgeDeviceAsync(showDialog: false);
            }

            if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
                string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
            {
                _connectionPillLabel.Text = "يلزم تسجيل الجسر أولاً";
                _connectionPillLabel.ForeColor = UITheme.ErrorColor;
                return;
            }

            await StartBridgeAsync(showConnectivityDialog: true);
            if (_profileStore.GetAll().Count == 0)
            {
                FetchDatabasesAsync();
            }
        }
        catch (Exception ex)
        {
            _lastErrorLabel.Text = SupabaseCloudConnectivity.FormatUserMessage(ex);
            _connectionPillLabel.Text = "غير متصل";
            _connectionPillLabel.ForeColor = UITheme.ErrorColor;
        }
    }

    private void InitializeComponent()
    {
        Text = "جسر قاعدة البيانات — المتمكن";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(920, 780);
        MinimumSize = new Size(820, 700);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Icon = TryLoadApplicationIcon(Path.Combine(
            AppContext.BaseDirectory,
            "assets",
            "branding",
            "almutamakkin.ico")) ?? SystemIcons.Application;

        _transportCombo.Items.AddRange(new object[] { "Local Test", "WebSocket", "Supabase Tunnel" });
        _transportCombo.SelectedIndex = 2;
        _settings.TransportMode = TransportMode.SupabaseTunnel;
        _transportCombo.Visible = false;
        _startButton.Visible = false;
        _stopButton.Visible = false;
        _registerBridgeButton.Visible = false;
        _testDatabaseButton.Visible = false;
        _profilesButton.Text = "إعدادات متقدمة";
        _fetchDatabasesButton.Text = "اتصال محلي تلقائي";
        _fetchDatabasesButton.Width = 180;
        _remoteDatabaseButton.Text = "اتصال شبكي";
        _remoteDatabaseButton.Width = 150;
        _navDashboardButton.Text = "قاعدة البيانات";
        _navLogsButton.Text = "السجلات";
        _navPrinterButton.Text = "الطابعة";
        _navLocalDbButton.Text = "اتصال محلي";
        _navRemoteDbButton.Text = "اتصال شبكي";
        _navSyncSnapshotsButton.Text = "مزامنة أبوغريس";
        _testDebtNotificationButton.Text = "اختبار إشعار دين";
        _testShiftCloseNotificationButton.Text = "اختبار إغلاق وردية";

        _topNavPanel.Padding = new Padding(20, 10, 20, 10);
        _topNavPanel.Height = 78;
        _topNavPanel.BackColor = Color.FromArgb(14, 31, 68);
        var topNavLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        topNavLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topNavLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        var topNavActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0),
        };
        topNavActions.Controls.AddRange([
            _navDashboardButton,
            _navLocalDbButton,
            _navPrinterButton,
            _navLogsButton,
            _navRemoteDbButton,
            _updateButton,
        ]);
        var brandPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 6, 0) };
        var brandTitle = new Label
        {
            Text = "المتمكن",
            AutoSize = true,
            Location = new Point(82, 4),
            Font = new Font("Segoe UI Variable Display", 15F, FontStyle.Bold),
            ForeColor = Color.White,
        };
        var brandSubtitle = new Label
        {
            Text = "DATABASE BRIDGE",
            AutoSize = true,
            Location = new Point(2, 31),
            Font = new Font("Segoe UI Variable Text", 8F, FontStyle.Bold),
            ForeColor = Color.FromArgb(139, 224, 214),
        };
        brandPanel.Controls.AddRange([brandTitle, brandSubtitle]);
        topNavLayout.Controls.Add(topNavActions, 0, 0);
        topNavLayout.Controls.Add(brandPanel, 1, 0);
        _topNavPanel.Controls.Add(topNavLayout);

        _dashboardPanel.Padding = new Padding(24);
        var outerCard = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var innerCard = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
        outerCard.Controls.Add(innerCard);
        _dashboardPanel.Controls.Add(outerCard);

        var statusBar = new Panel { Dock = DockStyle.Top, Height = 96, Padding = new Padding(8, 6, 8, 6) };
        var statusTop = new Panel { Dock = DockStyle.Top, Height = 36 };
        _connectionPillLabel.Location = new Point(8, 4);
        _lastPollStatusLabel.AutoSize = true;
        _lastPollStatusLabel.Location = new Point(200, 12);
        _lastPollStatusLabel.ForeColor = UITheme.TextMuted;
        _lastPollStatusLabel.Visible = false;
        statusTop.Controls.Add(_connectionPillLabel);
        statusTop.Controls.Add(_lastPollStatusLabel);
        _liveFeaturesStatusLabel.Visible = false;
        _snapshotDbStatusLabel.Visible = false;
        _changeWatchStatusLabel.Visible = false;
        _phoneDbStatusLabel.Dock = DockStyle.Top;
        statusBar.Controls.Add(_phoneDbStatusLabel);
        statusBar.Controls.Add(_domainWatchStatusLabel);
        statusBar.Controls.Add(statusTop);
        innerCard.Controls.Add(statusBar);

        var heroPanel = new Panel { Dock = DockStyle.Top, Height = 240, Padding = new Padding(8) };
        var heroLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var qrHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        _pairingQrPicture.Dock = DockStyle.Fill;
        qrHost.Controls.Add(_pairingQrPicture);

        var pairingInfo = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 4, 4) };
        var scanHint = new Label
        {
            Text = "١) امسح الرمز من تطبيق المتمكن",
            AutoSize = true,
            Font = UITheme.HeaderFont,
            Location = new Point(0, 4),
        };
        _pairingCodeLabel.Location = new Point(0, 44);
        _pairingCodeLabel.AutoSize = true;
        _tunnelIdLabel.Font = UITheme.MonoFont;
        _tunnelIdLabel.Location = new Point(0, 80);
        _tunnelIdLabel.AutoSize = true;
        _pairingExpiresLabel.Location = new Point(0, 112);
        _pairingExpiresLabel.AutoSize = true;
        _copyPairingCodeButton.Text = "نسخ الرمز";
        _copyPairingCodeButton.Location = new Point(0, 150);
        _copyPairingCodeButton.Width = 120;
        _refreshPairingButton.Text = "تحديث الرمز";
        _refreshPairingButton.Location = new Point(130, 150);
        _refreshPairingButton.Height = 36;
        pairingInfo.Controls.AddRange([
            scanHint,
            _pairingCodeLabel,
            _tunnelIdLabel,
            _pairingExpiresLabel,
            _copyPairingCodeButton,
            _refreshPairingButton,
        ]);
        heroLayout.Controls.Add(qrHost, 0, 0);
        heroLayout.Controls.Add(pairingInfo, 1, 0);
        heroPanel.Controls.Add(heroLayout);
        innerCard.Controls.Add(heroPanel);

        var dbPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var dbHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, 0, 4),
        };
        dbHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        dbHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var dbTitle = new Label
        {
            Text = "٢) الاتصال النشط بقاعدة البيانات",
            AutoSize = true,
            Font = UITheme.HeaderFont,
            Padding = new Padding(0, 4, 0, 8),
        };
        var dbActions = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 0, 0, 4),
        };
        _fetchDatabasesButton.Height = 36;
        _fetchDatabasesButton.AutoSize = true;
        _remoteDatabaseButton.Height = 36;
        _remoteDatabaseButton.AutoSize = true;
        _remoteDatabaseButton.BackColor = Color.FromArgb(0, 120, 215);
        _remoteDatabaseButton.ForeColor = Color.White;
        _remoteDatabaseButton.FlatStyle = FlatStyle.Flat;
        _profilesButton.Height = 36;
        _profilesButton.AutoSize = true;
        dbActions.Controls.AddRange([_profilesButton, _remoteDatabaseButton, _fetchDatabasesButton]);
        dbHeader.Controls.Add(dbTitle, 0, 0);
        dbHeader.Controls.Add(dbActions, 0, 1);

        var dbListHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        _activeDbHintLabel.Dock = DockStyle.Bottom;
        _dbListBox.Dock = DockStyle.Fill;
        dbListHost.Controls.Add(_dbListBox);
        dbListHost.Controls.Add(_activeDbHintLabel);
        _activeDbHintLabel.BringToFront();
        _dbListBox.BringToFront();

        var liveQueriesPanel = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(0, 12, 0, 0) };
        var liveQueriesTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "الاستعلامات الجارية من الهاتف",
            Font = UITheme.HeaderFont,
            Padding = new Padding(0, 0, 0, 4),
        };
        _liveQueryListBox.BackColor = UITheme.PanelHighlight;
        liveQueriesPanel.Controls.Add(_liveQueryListBox);
        liveQueriesPanel.Controls.Add(liveQueriesTitle);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(0, 8, 0, 0) };
        _lastErrorLabel.Dock = DockStyle.Fill;
        _lastErrorLabel.ForeColor = UITheme.ErrorColor;
        footer.Controls.Add(_lastErrorLabel);

        dbPanel.Controls.Add(dbListHost);
        dbPanel.Controls.Add(liveQueriesPanel);
        dbPanel.Controls.Add(footer);
        dbPanel.Controls.Add(dbHeader);
        innerCard.Controls.Add(dbPanel);

        // Dock order: fill first, then top panels
        dbPanel.BringToFront();
        heroPanel.BringToFront();
        statusBar.BringToFront();

        outerCard.BackColor = UITheme.BorderColor;
        innerCard.BackColor = UITheme.PanelColor;
        heroPanel.BackColor = UITheme.PanelHighlight;
        statusBar.BackColor = UITheme.BackgroundColor;

        _logsPanel.Padding = new Padding(24);
        var logsCardOuter = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = UITheme.BorderColor };
        var logsCardInner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = UITheme.PanelColor };
        var logsHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8),
        };
        logsHeader.Controls.AddRange([_copyLogsButton, _refreshLogsButton, _logsHintLabel]);
        logsCardInner.Controls.Add(_logsRichTextBox);
        logsCardInner.Controls.Add(logsHeader);
        logsCardOuter.Controls.Add(logsCardInner);
        _logsPanel.Controls.Add(logsCardOuter);

        _mainContentPanel.Controls.Add(_dashboardPanel);
        _mainContentPanel.Controls.Add(_printerPanel);
        _mainContentPanel.Controls.Add(_logsPanel);
        Controls.Add(_mainContentPanel);
        Controls.Add(_topNavPanel);
        UITheme.ApplyTheme(this);
    }

    private static void AddStatusRow(TableLayoutPanel layout, int row, string title, Control valueControl)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        }, 0, row);
        layout.Controls.Add(valueControl, 1, row);
    }

    private static Icon? TryLoadApplicationIcon(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var source = new Icon(path);
            return (Icon)source.Clone();
        }
        catch
        {
            // Branding is optional. A damaged or unsupported icon must never
            // prevent the bridge window from opening.
            return null;
        }
    }

    private void OpenAvailableUpdatePage()
    {
        if (_availableUpdateUri is null)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_availableUpdateUri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            _lastErrorLabel.Text = "تعذر فتح صفحة التحديث. تحقق من اتصال الإنترنت.";
        }
    }

    private void WireEvents()
    {
        _startButton.Click += async (_, _) => await StartBridgeAsync();
        _stopButton.Click += async (_, _) => await StopBridgeAsync();
        _registerBridgeButton.Click += async (_, _) => await RegisterBridgeDeviceAsync();
        _refreshPairingButton.Click += async (_, _) => await RefreshPairingCodeAsync(rotateCode: false);
        _testDatabaseButton.Click += async (_, _) => await TestFirstEnabledProfileAsync();
        _fetchDatabasesButton.Click += (_, _) => FetchDatabasesAsync();
        _remoteDatabaseButton.Click += (_, _) => ConnectRemoteDatabaseAsync();
        _openLogsButton.Click += (_, _) => OpenLogsFolder();
        _refreshLogsButton.Click += (_, _) => PollLogs(forceFullReload: true);
        _copyLogsButton.Click += (_, _) => CopyDisplayedLogs();
        _openTestConsoleButton.Click += (_, _) => OpenTestConsole();
        _profilesButton.Click += (_, _) => OpenProfiles();
        _transportCombo.SelectedIndexChanged += (_, _) => UpdateTransportModeFromUi();
        _refreshTimer.Tick += (_, _) => { RefreshStatusLabels(); RefreshLiveQueryActivity(); PollLogs(); };
        _liveInvoiceTimer.Tick += async (_, _) => await TickLiveActiveInvoiceAsync();
        _changeWatchTimer.Tick += async (_, _) => await TickChangeWatchAsync();
        _domainWatchTimer.Tick += async (_, _) => await TickDomainWatchAsync();
        _refreshTimer.Start();
        
        _navDashboardButton.Click += (_, _) => ShowSection(dashboard: true);
        _navLocalDbButton.Click += (_, _) => FetchDatabasesAsync();
        _navPrinterButton.Click += (_, _) => ShowSection(printer: true);
        _navLogsButton.Click += (_, _) => ShowSection(logs: true);
        _navRemoteDbButton.Click += (_, _) => ConnectRemoteDatabaseAsync();
        _updateButton.Click += (_, _) => OpenAvailableUpdatePage();
        _navSyncSnapshotsButton.Click += async (_, _) => await SyncSnapshotsAsync();
        _testDebtNotificationButton.Click += async (_, _) =>
            await ForcePublishNotificationSnapshotsAsync();
        _testShiftCloseNotificationButton.Click += async (_, _) =>
            await ForcePublishNotificationSnapshotsAsync();
        _copyPairingCodeButton.Click += (_, _) => CopyPairingPayload();

        _dbListBox.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressDbListEvents)
            {
                return;
            }

            if (_dbListBox.SelectedItem is not DatabaseListItem item)
            {
                return;
            }

            ApplySelectedDatabase(item.Profile);
        };

        FormClosed += async (_, _) =>
        {
            _refreshTimer.Stop();
            _liveInvoiceTimer.Stop();
            _changeWatchTimer.Stop();
            _domainWatchTimer.Stop();
            var oldQr = _pairingQrPicture.Image;
            _pairingQrPicture.Image = null;
            oldQr?.Dispose();
            if (_embeddedPrinterForm is not null)
            {
                _embeddedPrinterForm.Dispose();
                _embeddedPrinterForm = null;
            }
            if (_printerServer is not null)
            {
                await _printerServer.DisposeAsync();
                _printerServer = null;
            }
            await StopBridgeAsync();
        };
    }

    private void ShowSection(bool dashboard = false, bool printer = false, bool logs = false)
    {
        _dashboardPanel.Visible = dashboard;
        _printerPanel.Visible = printer;
        _logsPanel.Visible = logs;
        if (logs)
        {
            PollLogs(forceFullReload: true);
        }
        if (printer)
        {
            EnsurePrinterHost();
        }
    }

    private void EnsurePrinterHost()
    {
        if (_embeddedPrinterForm is not null)
        {
            return;
        }

        try
        {
            var store = new Almutamakkin.BarcodeBridge.Configuration.EncryptedSettingsStore();
            _printerLogs = new Almutamakkin.BarcodeBridge.Logging.BridgeLogHub();
            _printerServer = new Almutamakkin.BarcodeBridge.Server.BridgeServerController(
                _printerLogs,
                store.DataDirectory);
            var printerForm = new Almutamakkin.BarcodeBridge.MainForm(
                store,
                _printerLogs,
                _printerServer,
                startMinimized: false,
                embedded: true);
            _printerPanel.Controls.Add(printerForm);
            printerForm.Show();
            _embeddedPrinterForm = printerForm;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "تعذر فتح قسم الطابعة:\r\n" + ex.Message,
                "الطابعة",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void CopyPairingPayload()
    {
        if (string.IsNullOrWhiteSpace(_lastQrPayload))
        {
            MessageBox.Show(
                this,
                "لا يوجد رمز اقتران جاهز. سجّل الجهاز أو حدّث الرمز أولاً.",
                "رمز الربط",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Clipboard.SetText(_lastQrPayload);
    }

    private void UpdatePairingQr()
    {
        var code = _settings.LastPairingCode?.Trim() ?? string.Empty;
        var tunnel = _settings.TunnelId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(tunnel))
        {
            _lastQrPayload = string.Empty;
            var empty = _pairingQrPicture.Image;
            _pairingQrPicture.Image = null;
            empty?.Dispose();
            return;
        }

        string payload;
        try
        {
            payload = DatabaseBridgePairingQr.CreatePayload(code, tunnel);
        }
        catch
        {
            return;
        }

        if (string.Equals(payload, _lastQrPayload, StringComparison.Ordinal))
        {
            return;
        }

        _lastQrPayload = payload;
        var image = DatabaseBridgePairingQr.CreateQrBitmap(code, tunnel);
        var previous = _pairingQrPicture.Image;
        _pairingQrPicture.Image = image;
        previous?.Dispose();
    }
    
    private void PollLogs(bool forceFullReload = false)
    {
        try
        {
            var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
            var expectedLogFile = Path.Combine(Almutamakkin.DatabaseBridge.Infrastructure.LabPaths.LogsDirectory, $"bridge-{dateStr}.log");
            if (!System.IO.File.Exists(expectedLogFile)) return;
            
            if (forceFullReload || _currentLogFile != expectedLogFile)
            {
                _currentLogFile = expectedLogFile;
                _lastLogPosition = 0;
                _logsRichTextBox.Clear();
            }
            
            using var fs = new System.IO.FileStream(expectedLogFile, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            if (fs.Length > _lastLogPosition)
            {
                fs.Position = _lastLogPosition;
                using var sr = new System.IO.StreamReader(fs);
                var newData = sr.ReadToEnd();
                _lastLogPosition = fs.Position;
                
                if (!string.IsNullOrEmpty(newData))
                {
                    _logsRichTextBox.AppendText(newData);
                    _logsRichTextBox.ScrollToCaret();
                }
            }
        }
        catch { /* ignore read errors */ }
    }

    private void CopyDisplayedLogs()
    {
        if (string.IsNullOrWhiteSpace(_logsRichTextBox.Text))
        {
            _lastErrorLabel.Text = "لا توجد سجلات معروضة لنسخها بعد.";
            return;
        }

        try
        {
            Clipboard.SetText(_logsRichTextBox.Text);
            _lastErrorLabel.Text = "تم نسخ سجل الجسر إلى الحافظة.";
        }
        catch (Exception exception)
        {
            _lastErrorLabel.Text = $"تعذر نسخ السجل: {exception.Message}";
        }
    }

    private void RefreshLiveQueryActivity()
    {
        var activeName = _settings.ActiveDatabaseProfileName?.Trim();
        var active = string.IsNullOrWhiteSpace(activeName)
            ? null
            : _profileStore.GetAll().FirstOrDefault(profile =>
                profile.IsEnabled &&
                string.Equals(profile.ProfileName, activeName, StringComparison.OrdinalIgnoreCase));
        var activeSystem = active is null
            ? null
            : IsInfinityProfile(active) ? "infinity" : "marketing";

        // Only in-flight requests — finished queries disappear immediately.
        var activities = _liveQueryActivityFeed.GetActive()
            .Where(activity => activeSystem is null || string.Equals(activity.System, activeSystem, StringComparison.Ordinal))
            .ToList();

        _liveQueryListBox.BeginUpdate();
        try
        {
            _liveQueryListBox.Items.Clear();
            if (activities.Count == 0)
            {
                _liveQueryListBox.Items.Add("لا يوجد استعلام جارٍ الآن");
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var activity in activities)
            {
                var route = activity.ConnectionKind == DatabaseConnectionKind.Network ? "شبكي" : "محلي";
                var systemLabel = string.Equals(activity.System, "infinity", StringComparison.OrdinalIgnoreCase)
                    ? "إنفينيتي"
                    : "أبوغريس";
                var elapsedMs = Math.Max(0, (long)(now - activity.StartedAtUtc).TotalMilliseconds);
                _liveQueryListBox.Items.Add(
                    $"{activity.DisplayName} · {systemLabel} · {route} · جارٍ ({elapsedMs} مللي ثانية)");
            }
        }
        finally
        {
            _liveQueryListBox.EndUpdate();
        }
    }

    private async Task StartBridgeAsync(bool showConnectivityDialog = true)
    {
        try
        {
            UpdateTransportModeFromUi();
            EnsureSupabaseDefaults();

            if (_settings.TransportMode == TransportMode.SupabaseTunnel)
            {
                if (!await EnsureCloudReachableAsync(
                        "تشغيل الجسر",
                        showDialog: showConnectivityDialog))
                {
                    return;
                }
            }

            _activeTransport = _settings.TransportMode switch
            {
                TransportMode.WebSocket => _webSocketTransport,
                TransportMode.SupabaseTunnel => _supabaseTransport,
                _ => _localTransport,
            };

            _bridgeHostService.Attach(_activeTransport);
            await _activeTransport.ConnectAsync(CancellationToken.None);

            _startButton.Enabled = false;
            _stopButton.Enabled = true;
            _transportCombo.Enabled = false;
            _registerBridgeButton.Enabled = false;
            _bridgeRunning = true;
            _lastErrorLabel.Text = "-";
            if (_settings.TransportMode == TransportMode.SupabaseTunnel)
            {
                await RefreshPairingCodeAsync(rotateCode: false, showDialog: false);
            }

            // Do not auto-run Marketing SQL on a timer. Phone on-demand
            // bridge-relay must stay free; live invoice / change-watch are
            // manual only (operator can start them from the UI later).
            _liveInvoiceTimer.Stop();
            _changeWatchTimer.Stop();
            _liveFeaturesStatusLabel.Text =
                "حي: متوقف — الفاتورة عند الطلب عبر الجسر فقط";
            _liveFeaturesStatusLabel.ForeColor = UITheme.TextMuted;
            _lastChangeWatchSummary = "المراقبة: متوقفة";

            // Phase 0/1 change-stream foundation: opt-in only. Default OFF
            // keeps this timer stopped exactly like today.
            if (_settings.EnableChangeStreamWatch)
            {
                _domainWatchTimer.Interval = Math.Max(5, _settings.ChangeWatchIntervalSeconds) * 1000;
                _domainWatchTimer.Start();
                _lastDomainWatchSummary = "مراقبة الدلتا: قيد التشغيل — بانتظار أول فحص";
            }
            else
            {
                _domainWatchTimer.Stop();
                _lastDomainWatchSummary = "مراقبة الدلتا: متوقفة";
            }

            RefreshStatusLabels();
        }
        catch (Exception ex)
        {
            var message = SupabaseCloudConnectivity.FormatUserMessage(ex);
            _lastErrorLabel.Text = message;
            if (showConnectivityDialog)
            {
                ShowConnectivityFailureDialog("تشغيل الجسر", message, allowRetry: false);
            }
        }
    }

    private async Task<bool> EnsureCloudReachableAsync(string actionTitle, bool showDialog = true)
    {
        EnsureSupabaseDefaults();

        while (true)
        {
            _lastErrorLabel.Text = "جاري فحص الاتصال بالسحابة…";
            RefreshStatusLabels();

            var check = await SupabaseCloudConnectivity.CheckAsync(
                _settings.SupabaseUrl,
                _settings.AnonKey,
                CancellationToken.None);

            if (check.Success)
            {
                return true;
            }

            _lastErrorLabel.Text = check.MessageAr;
            if (!showDialog)
            {
                return false;
            }

            var choice = ShowConnectivityFailureDialog(actionTitle, check.MessageAr, allowRetry: true);
            if (choice == DialogResult.Retry)
            {
                continue;
            }

            return false;
        }
    }

    private void EnsureSupabaseDefaults()
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(_settings.SupabaseUrl))
        {
            _settings.SupabaseUrl = SupabaseBridgeTransport.DefaultSupabaseFunctionsBaseUrl;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(_settings.AnonKey))
        {
            _settings.AnonKey = SupabaseBridgeTransport.DefaultAnonKey;
            changed = true;
        }

        if (changed)
        {
            _settingsStore.Save(_settings);
        }
    }

    /// <summary>
    /// Retry = أعد المحاولة, Yes = افتح إعدادات الشبكة, Cancel/Abort = أغلق.
    /// </summary>
    private DialogResult ShowConnectivityFailureDialog(
        string title,
        string messageAr,
        bool allowRetry)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            RightToLeft = RightToLeft.Yes,
            RightToLeftLayout = true,
            ClientSize = new Size(520, 360),
        };

        var messageBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = dialog.BackColor,
            Text = messageAr,
            Location = new Point(16, 16),
            Size = new Size(488, 260),
        };

        var retryButton = new Button
        {
            Text = "إعادة المحاولة",
            DialogResult = DialogResult.Retry,
            Location = new Point(16, 300),
            Width = 120,
            Visible = allowRetry,
            Enabled = allowRetry,
        };
        var networkButton = new Button
        {
            Text = "إعدادات الشبكة",
            Location = new Point(148, 300),
            Width = 130,
        };
        networkButton.Click += (_, _) => SupabaseCloudConnectivity.TryOpenNetworkSettings();

        var closeButton = new Button
        {
            Text = "إغلاق",
            DialogResult = DialogResult.Cancel,
            Location = new Point(392, 300),
            Width = 100,
        };

        dialog.Controls.AddRange([messageBox, retryButton, networkButton, closeButton]);
        dialog.CancelButton = closeButton;
        if (allowRetry)
        {
            dialog.AcceptButton = retryButton;
        }
        else
        {
            dialog.AcceptButton = closeButton;
        }

        return dialog.ShowDialog(this);
    }

    private async Task StopBridgeAsync()
    {
        if (_activeTransport is null)
        {
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _transportCombo.Enabled = true;
            _registerBridgeButton.Enabled = true;
            RefreshStatusLabels();
            return;
        }

        try
        {
            _bridgeHostService.Detach();
            await _activeTransport.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _lastErrorLabel.Text = ex.Message;
        }
        finally
        {
            _activeTransport = null;
            _bridgeRunning = false;
            _liveInvoiceTimer.Stop();
            _changeWatchTimer.Stop();
            _domainWatchTimer.Stop();
            _lastChangeWatchSummary = "المراقبة: متوقفة";
            _lastDomainWatchSummary = "مراقبة الدلتا: متوقفة";
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _transportCombo.Enabled = true;
            _registerBridgeButton.Enabled = true;
            RefreshStatusLabels();
        }
    }

    private async Task TickLiveActiveInvoiceAsync()
    {
        if (!_bridgeRunning || _liveInvoiceTickRunning)
        {
            return;
        }

        if (!UsesActiveMarketingSnapshotRoute())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
            string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            return;
        }

        _liveInvoiceTickRunning = true;
        try
        {
            var sync = _serviceProvider.GetRequiredService<LiveActiveInvoiceSyncService>();
            var result = await sync.PublishMarketingAsync(CancellationToken.None);
            _liveFeaturesStatusLabel.Text = result.Success
                ? result.Message.Contains("تخطي", StringComparison.Ordinal)
                    ? $"حي: فاتورة بلا تغيير ({result.RowCount}) · نواقص/صلاحية/ديون عبر الجسر"
                    : $"حي: فاتورة حالية ({result.RowCount}) · نواقص/صلاحية/ديون عبر الجسر"
                : $"حي: فشل بث الفاتورة — {result.Message}";
            _liveFeaturesStatusLabel.ForeColor = result.Success
                ? Color.FromArgb(67, 56, 202)
                : UITheme.ErrorColor;
        }
        catch (Exception ex)
        {
            _liveFeaturesStatusLabel.Text = $"حي: خطأ الفاتورة — {ex.Message}";
            _liveFeaturesStatusLabel.ForeColor = UITheme.ErrorColor;
        }
        finally
        {
            _liveInvoiceTickRunning = false;
        }
    }

    private async Task TickChangeWatchAsync()
    {
        if (!_bridgeRunning || _changeWatchTickRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.TunnelId) ||
            string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            _lastChangeWatchSummary = "المراقبة: يلزم تسجيل الجسر";
            _changeWatchStatusLabel.Text = _lastChangeWatchSummary;
            return;
        }

        _changeWatchTickRunning = true;
        try
        {
            var watch = _serviceProvider.GetRequiredService<ChangeWatchService>();
            var results = await watch.TickAsync(CancellationToken.None);
            var published = results.Where(result => result.Published).ToList();
            var checkedCount = results.Count(result => result.Checked);
            if (published.Count > 0)
            {
                _lastChangeWatchSummary =
                    "المراقبة (خلفية): نُشر " +
                    string.Join(" · ", published.Select(result => result.DisplayName));
                _changeWatchStatusLabel.ForeColor = Color.FromArgb(22, 163, 74);
            }
            else
            {
                _lastChangeWatchSummary =
                    $"المراقبة تعمل في الخلفية — فُحص {checkedCount} بدون تغيير يحتاج نشراً";
                _changeWatchStatusLabel.ForeColor = Color.FromArgb(22, 101, 52);
            }
        }
        catch (Exception ex)
        {
            _lastChangeWatchSummary = $"المراقبة: خطأ — {ex.Message}";
            _changeWatchStatusLabel.ForeColor = UITheme.ErrorColor;
        }
        finally
        {
            _changeWatchTickRunning = false;
            _changeWatchStatusLabel.Text = _lastChangeWatchSummary;
        }
    }

    /// <summary>
    /// Phase 0/1 change-stream foundation tick: cheap fingerprint-only
    /// check via DomainWatchService. Never publishes a full snapshot and
    /// never runs shortages/live-invoice SQL. No-op unless
    /// EnableChangeStreamWatch is true (checked here too as a safety net,
    /// since the timer itself is only started when the flag is on).
    /// </summary>
    private async Task TickDomainWatchAsync()
    {
        if (!_bridgeRunning || _domainWatchTickRunning || !_settings.EnableChangeStreamWatch)
        {
            return;
        }

        _domainWatchTickRunning = true;
        try
        {
            var watch = _serviceProvider.GetRequiredService<DomainWatchService>();
            var results = await watch.TickAsync(CancellationToken.None);
            var changed = results.Where(result => result.Changed).ToList();
            var checkedCount = results.Count(result => result.Checked);
            var enabledCount = results.Count(result => result.Enabled);

            if (changed.Count > 0)
            {
                _lastDomainWatchSummary =
                    "مراقبة الدلتا: تغيّر في " +
                    string.Join(" · ", changed.Select(result => $"{result.DisplayName} (#{result.Revision})"));
                _domainWatchStatusLabel.ForeColor = Color.FromArgb(22, 163, 74);
            }
            else
            {
                if (checkedCount > 0)
                {
                    _lastDomainWatchSummary =
                        $"مراقبة الدلتا: تعمل — فُحص {checkedCount} بدون تغيير";
                    _domainWatchStatusLabel.ForeColor = Color.FromArgb(91, 33, 182);
                }
                else if (enabledCount > 0)
                {
                    _lastDomainWatchSummary =
                        "مراقبة الدلتا: النطاقات مفعّلة لكن تعذر الاتصال بقاعدة البيانات";
                    _domainWatchStatusLabel.ForeColor = UITheme.ErrorColor;
                }
                else
                {
                    _lastDomainWatchSummary = "مراقبة الدلتا: لا نطاقات مفعّلة";
                    _domainWatchStatusLabel.ForeColor = UITheme.TextMuted;
                }
            }
        }
        catch (Exception ex)
        {
            _lastDomainWatchSummary = $"مراقبة الدلتا: خطأ — {ex.Message}";
            _domainWatchStatusLabel.ForeColor = UITheme.ErrorColor;
        }
        finally
        {
            _domainWatchTickRunning = false;
            _domainWatchStatusLabel.Text = _lastDomainWatchSummary;
        }
    }

    private async Task RegisterBridgeDeviceAsync(bool showDialog = true)
    {
        if (_bridgeRunning)
        {
            if (showDialog)
            {
                MessageBox.Show(this,
                    "أوقف الجسر قبل تسجيل جهاز نفق جديد.",
                    "تسجيل الجسر",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return;
        }

        try
        {
            _registerBridgeButton.Enabled = false;
            EnsureSupabaseDefaults();
            if (!await EnsureCloudReachableAsync("تسجيل الجسر", showDialog: showDialog))
            {
                return;
            }

            var result = await SupabaseBridgeTransport.RegisterAsync(
                _settings.SupabaseUrl,
                _settings.AnonKey,
                CancellationToken.None);

            _settings.TunnelId = result.TunnelId!;
            _settings.EncryptedDeviceSecret = _secretProtector.Protect(result.DeviceSecret!);
            _settings.LastPairingCode = result.PairingCode;
            _settings.LastPairingExpiresAtUtc = result.PairingExpiresAt;
            _settings.TransportMode = TransportMode.SupabaseTunnel;
            if (string.IsNullOrWhiteSpace(_settings.SupabaseUrl))
            {
                _settings.SupabaseUrl = SupabaseBridgeTransport.DefaultSupabaseFunctionsBaseUrl;
            }

            if (string.IsNullOrWhiteSpace(_settings.AnonKey))
            {
                _settings.AnonKey = SupabaseBridgeTransport.DefaultAnonKey;
            }

            _settingsStore.Save(_settings);
            RefreshStatusLabels();

            if (!showDialog)
            {
                return;
            }

            using var dialog = new Form
            {
                Text = "Bridge Registration — تسجيل الجسر",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(420, 180),
            };

            var instructions = new Label
            {
                Text = "Enter this pairing code in the mobile app:\r\nأدخل رمز الاقتران في تطبيق الهاتف",
                AutoSize = true,
                Location = new Point(16, 16),
            };
            var pairingCodeBox = new TextBox
            {
                Text = result.PairingCode ?? "(none)",
                ReadOnly = true,
                Location = new Point(16, 56),
                Width = 380,
                Font = new Font("Consolas", 12F, FontStyle.Bold),
            };
            var copyButton = new Button
            {
                Text = "Copy — نسخ",
                Location = new Point(16, 96),
                Width = 100,
            };
            copyButton.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(result.PairingCode))
                {
                    Clipboard.SetText(result.PairingCode);
                }
            };
            var closeButton = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Location = new Point(316, 96),
                Width = 80,
            };

            dialog.Controls.AddRange([instructions, pairingCodeBox, copyButton, closeButton]);
            dialog.AcceptButton = closeButton;
            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            var message = SupabaseCloudConnectivity.FormatUserMessage(ex);
            _lastErrorLabel.Text = message;
            if (showDialog)
            {
                ShowConnectivityFailureDialog("تسجيل الجسر", message, allowRetry: false);
            }
        }
        finally
        {
            _registerBridgeButton.Enabled = !_bridgeRunning;
        }
    }

    private async Task RefreshPairingCodeAsync(bool rotateCode, bool showDialog = true)
    {
        if (string.IsNullOrWhiteSpace(_settings.TunnelId))
        {
            if (showDialog)
            {
                MessageBox.Show(this,
                    "سجّل جهاز الجسر أولاً.",
                    "تحديث رمز الاقتران",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.EncryptedDeviceSecret))
        {
            if (showDialog)
            {
                MessageBox.Show(this,
                    "سر الجهاز غير محفوظ. أعد تسجيل الجسر.",
                    "تحديث رمز الاقتران",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return;
        }

        try
        {
            _refreshPairingButton.Enabled = false;
            EnsureSupabaseDefaults();

            if (!await EnsureCloudReachableAsync(
                    "تحديث رمز الاقتران",
                    showDialog: showDialog))
            {
                return;
            }

            var deviceSecret = _secretProtector.Unprotect(_settings.EncryptedDeviceSecret);
            var result = await _supabaseTransport.RefreshPairingAsync(
                _settings.TunnelId,
                deviceSecret,
                _settings.SupabaseUrl,
                _settings.AnonKey,
                rotateCode,
                CancellationToken.None);

            _settings.LastPairingCode = result.PairingCode;
            _settings.LastPairingExpiresAtUtc = result.PairingExpiresAt;
            _settingsStore.Save(_settings);
            RefreshStatusLabels();

            if (showDialog)
            {
                MessageBox.Show(this,
                    $"رمز الاقتران: {result.PairingCode}\r\n" +
                    $"معرف النفق: {_settings.TunnelId}\r\n" +
                    $"صالح حتى: {FormatPairingExpiry(result.PairingExpiresAt)}",
                    "تحديث رمز الاقتران",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            var message = SupabaseCloudConnectivity.FormatUserMessage(ex);
            _lastErrorLabel.Text = message;
            if (showDialog)
            {
                ShowConnectivityFailureDialog("تحديث رمز الاقتران", message, allowRetry: false);
            }
        }
        finally
        {
            _refreshPairingButton.Enabled = !_bridgeRunning || _settings.TransportMode == TransportMode.SupabaseTunnel;
        }
    }

    private async Task TestFirstEnabledProfileAsync()
    {
        var profile = _profileStore.GetAll().FirstOrDefault(item => item.IsEnabled);
        if (profile is null)
        {
            MessageBox.Show(this, "No enabled database profile found.", "Test Database", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            var result = await _connectionTester.TestAsync(profile, CancellationToken.None);
            _lastDatabaseTestSummary = result.Success
                ? $"{profile.ProfileName}: OK ({result.ServerName}/{result.DatabaseName})"
                : $"{profile.ProfileName}: FAILED — {result.Message}";

            if (!result.Success)
            {
                _lastErrorLabel.Text = result.Message;
            }

            RefreshStatusLabels();

            MessageBox.Show(this,
                result.Success
                    ? $"Connected to {result.ServerName}/{result.DatabaseName} as {result.LoginName}"
                    : $"{result.Message}\n{result.Details}",
                "Test Database",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _lastErrorLabel.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Test Database", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void FetchDatabasesAsync()
    {
        using var dialog = new DatabaseFetchDialog(_discovery);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SaveFetchedDatabaseSelections(dialog.SelectedDatabases, "جلب قواعد البيانات");
    }

    private void ConnectRemoteDatabaseAsync()
    {
        using var dialog = new RemoteDatabaseConnectDialog(
            _discovery,
            _connectionTester,
            _secretProtector);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Keep local Marketing/Infinity profiles untouched; store remote under *__remote.
        SaveFetchedDatabaseSelections(
            dialog.SelectedDatabases,
            "ربط قاعدة بعيدة",
            preserveLocalCanonicalProfiles: true);
    }

    private Task SyncSnapshotsAsync()
    {
        _navSyncSnapshotsButton.Enabled = false;
        try
        {
            var sync = _serviceProvider.GetRequiredService<ActivitySnapshotSyncService>();
            using var dialog = new SnapshotSyncProgressDialog(
                (progress, cancellationToken) =>
                    sync.SyncMarketingFirstWaveAsync(cancellationToken, progress));
            dialog.ShowDialog(this);

            var results = dialog.Results;
            var anyFailed = results.Count == 0 || results.Any(result => !result.Success);
            _lastErrorLabel.Text = anyFailed
                ? "بعض لقطات أبوغريس فشلت — راجع نافذة التقدم."
                : "اكتملت مزامنة لقطات أبوغريس (بدون نواقص/صلاحية/ديون — هذه حية عبر الجسر).";
        }
        catch (Exception ex)
        {
            _lastErrorLabel.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "مزامنة اللقطات", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _navSyncSnapshotsButton.Enabled = true;
        }

        return Task.CompletedTask;
    }

    private async Task ForcePublishNotificationSnapshotsAsync()
    {
        _testDebtNotificationButton.Enabled = false;
        _testShiftCloseNotificationButton.Enabled = false;
        try
        {
            var sync = _serviceProvider.GetRequiredService<ActivitySnapshotSyncService>();
            var results = await sync.PublishNotificationEventsAsync().ConfigureAwait(true);
            var anyFailed = results.Count == 0 || results.Any(result => !result.Success);
            if (anyFailed)
            {
                var failure = results.FirstOrDefault(result => !result.Success);
                var message = failure?.Message ?? "فشل نشر لقطات الإشعارات.";
                _lastErrorLabel.Text = message;
                MessageBox.Show(
                    this,
                    message,
                    "اختبار إشعارات",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var debtRows = results
                .FirstOrDefault(result => result.SnapshotType == "debt_invoice_events")
                ?.RowCount ?? 0;
            var shiftRows = results
                .FirstOrDefault(result => result.SnapshotType == "shift_close_events")
                ?.RowCount ?? 0;
            var summary =
                $"أحداث الديون: {debtRows} صفاً\nإغلاق الورديات: {shiftRows} صفاً";
            _lastErrorLabel.Text = "تم رفع لقطات اختبار الإشعارات.";
            MessageBox.Show(
                this,
                summary,
                "اختبار إشعارات",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _lastErrorLabel.Text = ex.Message;
            MessageBox.Show(
                this,
                ex.Message,
                "اختبار إشعارات",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _testDebtNotificationButton.Enabled = true;
            _testShiftCloseNotificationButton.Enabled = true;
        }
    }

    private void SaveFetchedDatabaseSelections(
        IReadOnlyList<DatabaseFetchSelection> selections,
        string dialogTitle,
        bool preserveLocalCanonicalProfiles = false)
    {
        try
        {
            _fetchDatabasesButton.Enabled = false;
            _remoteDatabaseButton.Enabled = false;
            var added = 0;
            var updated = 0;
            string? selectedLiveProfileName = null;

            foreach (var selection in selections)
            {
                var profileName = selection.ProfileName;
                if (preserveLocalCanonicalProfiles)
                {
                    if (IsCanonicalAppProfile(profileName))
                    {
                        profileName = ActivitySnapshotSyncService.ToRemoteProfileName(profileName);
                    }
                    else if (!profileName.EndsWith("__remote", StringComparison.OrdinalIgnoreCase))
                    {
                        profileName = $"{profileName}__remote";
                    }
                }

                DatabaseProfile? existing;
                if (preserveLocalCanonicalProfiles)
                {
                    existing = _profileStore.GetAll()
                        .FirstOrDefault(profile =>
                            string.Equals(profile.ProfileName, profileName, StringComparison.OrdinalIgnoreCase) ||
                            (profile.ProfileName.EndsWith("__remote", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(profile.ServerName, selection.ServerName, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(profile.DatabaseName, selection.DatabaseName, StringComparison.OrdinalIgnoreCase)));
                }
                else
                {
                    existing = _profileStore.GetAll()
                        .FirstOrDefault(profile =>
                            string.Equals(profile.ProfileName, profileName, StringComparison.OrdinalIgnoreCase) ||
                            (string.Equals(profile.ServerName, selection.ServerName, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(profile.DatabaseName, selection.DatabaseName, StringComparison.OrdinalIgnoreCase)));
                }

                var encryptedPassword =
                    selection.AuthenticationMode == SqlAuthenticationMode.SqlAuthentication &&
                    !string.IsNullOrEmpty(selection.PlainPassword)
                        ? _secretProtector.Protect(selection.PlainPassword)
                        : null;

                if (existing is null)
                {
                    _profileStore.Save(new DatabaseProfile
                    {
                        ProfileName = profileName,
                        ServerName = selection.ServerName,
                        DatabaseName = selection.DatabaseName,
                        ConnectionKind = preserveLocalCanonicalProfiles
                            ? DatabaseConnectionKind.Network
                            : DatabaseConnectionKind.Local,
                        AuthenticationMode = selection.AuthenticationMode,
                        UserName = selection.UserName,
                        EncryptedPassword = encryptedPassword,
                        TrustServerCertificate = true,
                        EncryptConnection = false,
                        PermissionLevel = SqlPermissionLevel.ReadOnly,
                        CommandTimeoutSeconds = 30,
                        MaximumRows = 1000,
                        IsEnabled = true,
                    });
                    added++;
                }
                else
                {
                    if (preserveLocalCanonicalProfiles &&
                        IsCanonicalAppProfile(existing.ProfileName) &&
                        ActivitySnapshotSyncService.IsLocalServer(existing.ServerName))
                    {
                        // Safety: never mutate local canonical profiles from remote dialog.
                        continue;
                    }

                    existing.ServerName = selection.ServerName;
                    existing.DatabaseName = selection.DatabaseName;
                    existing.ProfileName = profileName;
                    existing.ConnectionKind = preserveLocalCanonicalProfiles
                        ? DatabaseConnectionKind.Network
                        : DatabaseConnectionKind.Local;
                    existing.AuthenticationMode = selection.AuthenticationMode;
                    existing.UserName = selection.UserName;
                    if (encryptedPassword is not null)
                    {
                        existing.EncryptedPassword = encryptedPassword;
                    }
                    else if (selection.AuthenticationMode == SqlAuthenticationMode.WindowsAuthentication)
                    {
                        existing.EncryptedPassword = null;
                        existing.UserName = null;
                    }

                    _profileStore.Save(existing);
                    updated++;
                }

                if (IsPharmacyDatabaseName(selection.DatabaseName))
                {
                    selectedLiveProfileName ??= profileName;
                }

                if (preserveLocalCanonicalProfiles &&
                    string.Equals(
                        ActivitySnapshotSyncService.ToRemoteProfileName("Marketing"),
                        profileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _settings.SnapshotMarketingProfileName = profileName;
                    _settingsStore.Save(_settings);
                }

                if (preserveLocalCanonicalProfiles &&
                    string.Equals(
                        ActivitySnapshotSyncService.ToRemoteProfileName("InfinityRetailDB"),
                        profileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _settings.SnapshotInfinityProfileName = profileName;
                    _settingsStore.Save(_settings);
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedLiveProfileName))
            {
                _settings.ActiveDatabaseProfileName = selectedLiveProfileName;
                _settingsStore.Save(_settings);
            }

            ReloadDbProfileCombo();
            RefreshStatusLabels();

            MessageBox.Show(
                this,
                preserveLocalCanonicalProfiles
                    ? $"تم حفظ الاتصال البعيد دون تعديل المحلي.\nجديد: {added}\nمحدّث: {updated}"
                    : $"تمت مزامنة ملفات الاتصال.\nجديد: {added}\nمحدّث: {updated}",
                dialogTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _lastErrorLabel.Text = ex.Message;
            MessageBox.Show(this, ex.Message, dialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _fetchDatabasesButton.Enabled = true;
            _remoteDatabaseButton.Enabled = true;
        }
    }

    private void ApplySelectedDatabase(DatabaseProfile selected)
    {
        var all = _profileStore.GetAll().ToList();
        var match = all.FirstOrDefault(profile => profile.Id == selected.Id)
            ?? all.FirstOrDefault(profile =>
                string.Equals(profile.ProfileName, selected.ProfileName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        // Keep every discovered profile enabled so Marketing + Infinity can both work.
        foreach (var profile in all)
        {
            if (!profile.IsEnabled)
            {
                profile.IsEnabled = true;
                _profileStore.Save(profile);
            }
        }

        match.IsEnabled = true;
        _profileStore.Save(match);

        // Remote snapshot profiles must never overwrite the local canonical Marketing/Infinity rows.
        var isRemoteProfile = match.ProfileName.EndsWith("__remote", StringComparison.OrdinalIgnoreCase)
            || !ActivitySnapshotSyncService.IsLocalServer(match.ServerName);

        if (isRemoteProfile &&
            string.Equals(match.DatabaseName, "Marketing", StringComparison.OrdinalIgnoreCase))
        {
            _settings.SnapshotMarketingProfileName = match.ProfileName;
        }

        if (isRemoteProfile &&
            string.Equals(match.DatabaseName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase))
        {
            _settings.SnapshotInfinityProfileName = match.ProfileName;
        }

        _settings.ActiveDatabaseProfileName = match.ProfileName;
        _settingsStore.Save(_settings);
        _dbProfileCombo.SelectedItem = match;
        UpdateActiveDbHint(match);
        RefreshConnectionSummary();
        RefreshStatusLabels();
    }

    private static bool IsCanonicalAppProfile(string profileName) =>
        string.Equals(profileName, "Marketing", StringComparison.OrdinalIgnoreCase)
        || string.Equals(profileName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase);

    private void RebindCanonicalProfile(string canonicalName, DatabaseProfile source)
    {
        var existing = _profileStore.GetByName(canonicalName);
        if (existing is null)
        {
            _profileStore.Save(new DatabaseProfile
            {
                ProfileName = canonicalName,
                ServerName = source.ServerName,
                DatabaseName = source.DatabaseName,
                AuthenticationMode = source.AuthenticationMode,
                UserName = source.UserName,
                EncryptedPassword = source.EncryptedPassword,
                TrustServerCertificate = source.TrustServerCertificate,
                EncryptConnection = source.EncryptConnection,
                PermissionLevel = source.PermissionLevel,
                CommandTimeoutSeconds = source.CommandTimeoutSeconds,
                MaximumRows = source.MaximumRows,
                IsEnabled = true,
            });
            return;
        }

        existing.ServerName = source.ServerName;
        existing.DatabaseName = source.DatabaseName;
        existing.AuthenticationMode = source.AuthenticationMode;
        existing.UserName = source.UserName;
        existing.EncryptedPassword = source.EncryptedPassword;
        existing.TrustServerCertificate = source.TrustServerCertificate;
        existing.EncryptConnection = source.EncryptConnection;
        existing.IsEnabled = true;
        _profileStore.Save(existing);
    }

    private void UpdateActiveDbHint(DatabaseProfile? profile)
    {
        if (profile is null)
        {
            _activeDbHintLabel.Text = "لا يوجد ملف Marketing أو Infinity — اربط محلياً أو بعيداً.";
            return;
        }

        var system = SystemDisplayName(profile);
        var role = profile.ConnectionKind == DatabaseConnectionKind.Network ? "شبكي" : "محلي";
        _activeDbHintLabel.Text = $"{role}: {system} · {profile.DatabaseName} @ {profile.ServerName}";
    }

    private void ReloadDbProfileCombo()
    {
        NormalizeLegacyConnectionKinds();

        var selectedName = _settings.ActiveDatabaseProfileName
            ?? (_dbListBox.SelectedItem as DatabaseListItem)?.Profile.ProfileName
            ?? (_dbProfileCombo.SelectedItem as DatabaseProfile)?.ProfileName;

        _suppressDbListEvents = true;
        try
        {
            _dbProfileCombo.Items.Clear();
            _dbListBox.Items.Clear();

            // Show both Marketing (أبوغريس) and InfinityRetailDB.
            var profiles = _profileStore.GetAll()
                .Where(IsPharmacyProfile)
                .OrderBy(ProfileSortKey)
                .ThenBy(item => item.ProfileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _dbProfileCombo.DisplayMember = nameof(DatabaseProfile.ProfileName);

            if (profiles.Count == 0)
            {
                _dbProfileCombo.SelectedIndex = -1;
                _dbListBox.SelectedIndex = -1;
                UpdateActiveDbHint(null);
                RefreshConnectionSummary();
                return;
            }

            var match = profiles.FirstOrDefault(profile =>
                string.Equals(profile.ProfileName, selectedName, StringComparison.OrdinalIgnoreCase));
            var selected = match
                ?? profiles.FirstOrDefault(IsLocalMarketingProfile)
                ?? profiles.FirstOrDefault(IsLocalInfinityProfile)
                ?? profiles.FirstOrDefault(IsRemoteMarketingProfile)
                ?? profiles.FirstOrDefault(IsRemoteInfinityProfile)
                ?? profiles[0];

            // The dashboard is intentionally not a profile manager. It shows
            // only the one live connection selected by the operator.
            _dbProfileCombo.Items.Add(selected);
            _dbListBox.Items.Add(new DatabaseListItem(selected));
            _dbProfileCombo.SelectedItem = selected;
            _dbListBox.SelectedIndex = 0;

            if (!string.Equals(_settings.ActiveDatabaseProfileName, selected.ProfileName, StringComparison.OrdinalIgnoreCase))
            {
                _settings.ActiveDatabaseProfileName = selected.ProfileName;
                _settingsStore.Save(_settings);
            }

            UpdateActiveDbHint(selected);
            RefreshConnectionSummary();
        }
        finally
        {
            _suppressDbListEvents = false;
        }
    }

    private void DeduplicateProfiles()
    {
        var all = _profileStore.GetAll().ToList();
        if (all.Count <= 1)
        {
            return;
        }

        var keepIds = all
            .GroupBy(
                profile =>
                    $"{profile.ProfileName.Trim()}|{profile.ServerName.Trim()}|{profile.DatabaseName.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                return group
                    .OrderByDescending(IsLocalMarketingProfile)
                    .ThenByDescending(profile => profile.IsEnabled)
                    .ThenBy(profile => profile.Id)
                    .First()
                    .Id;
            })
            .ToHashSet();

        foreach (var profile in all.Where(profile => !keepIds.Contains(profile.Id)))
        {
            _profileStore.Delete(profile.Id);
        }
    }

    private void RefreshConnectionSummary()
    {
        var activeName = _settings.ActiveDatabaseProfileName?.Trim();
        var active = string.IsNullOrWhiteSpace(activeName)
            ? null
            : _profileStore.GetAll().FirstOrDefault(profile =>
                profile.IsEnabled &&
                string.Equals(profile.ProfileName, activeName, StringComparison.OrdinalIgnoreCase));

        if (active is null)
        {
            _phoneDbStatusLabel.Text = "لا يوجد اتصال نشط — اختر اتصالاً محلياً أو شبكياً.";
            _phoneDbStatusLabel.ForeColor = UITheme.TextMuted;
            return;
        }

        var connectionType = active.ConnectionKind == DatabaseConnectionKind.Network
            ? "شبكي"
            : "محلي";
        _phoneDbStatusLabel.Text =
            $"الاتصال النشط: {SystemDisplayName(active)} · {connectionType} · {active.ServerName}";
        _phoneDbStatusLabel.ForeColor = Color.FromArgb(22, 101, 52);
    }

    private bool UsesActiveMarketingSnapshotRoute()
    {
        var activeName = _settings.ActiveDatabaseProfileName?.Trim();
        if (string.IsNullOrWhiteSpace(activeName))
        {
            return false;
        }

        var active = _profileStore.GetAll().FirstOrDefault(profile =>
            profile.IsEnabled &&
            string.Equals(profile.ProfileName, activeName, StringComparison.OrdinalIgnoreCase));
        if (active is null || !IsMarketingProfile(active))
        {
            return false;
        }

        var snapshotName = _settings.SnapshotMarketingProfileName?.Trim() ?? "Marketing";
        return string.Equals(active.ProfileName, snapshotName, StringComparison.OrdinalIgnoreCase);
    }

    private void NormalizeLegacyConnectionKinds()
    {
        foreach (var profile in _profileStore.GetAll())
        {
            if (profile.ConnectionKind == DatabaseConnectionKind.Local &&
                !ActivitySnapshotSyncService.IsLocalServer(profile.ServerName))
            {
                profile.ConnectionKind = DatabaseConnectionKind.Network;
                _profileStore.Save(profile);
            }
        }
    }

    private static bool IsPharmacyProfile(DatabaseProfile profile) =>
        IsMarketingProfile(profile) || IsInfinityProfile(profile);

    private static bool IsPharmacyDatabaseName(string databaseName) =>
        string.Equals(databaseName, "Marketing", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(databaseName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase);

    private static bool IsMarketingProfile(DatabaseProfile profile) =>
        string.Equals(profile.DatabaseName, "Marketing", StringComparison.OrdinalIgnoreCase)
        || string.Equals(profile.ProfileName, "Marketing", StringComparison.OrdinalIgnoreCase)
        || profile.ProfileName.StartsWith("Marketing", StringComparison.OrdinalIgnoreCase);

    private static bool IsInfinityProfile(DatabaseProfile profile) =>
        string.Equals(profile.DatabaseName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase)
        || string.Equals(profile.ProfileName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase)
        || profile.ProfileName.StartsWith("InfinityRetailDB", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalMarketingProfile(DatabaseProfile profile) =>
        IsMarketingProfile(profile)
        && !IsRemotePharmacyProfile(profile)
        && ActivitySnapshotSyncService.IsLocalServer(profile.ServerName);

    private static bool IsRemoteMarketingProfile(DatabaseProfile profile) =>
        IsMarketingProfile(profile) && IsRemotePharmacyProfile(profile);

    private static bool IsLocalInfinityProfile(DatabaseProfile profile) =>
        IsInfinityProfile(profile)
        && !IsRemotePharmacyProfile(profile)
        && ActivitySnapshotSyncService.IsLocalServer(profile.ServerName);

    private static bool IsRemoteInfinityProfile(DatabaseProfile profile) =>
        IsInfinityProfile(profile) && IsRemotePharmacyProfile(profile);

    private static bool IsRemotePharmacyProfile(DatabaseProfile profile) =>
        profile.ConnectionKind == DatabaseConnectionKind.Network
        || profile.ProfileName.EndsWith("__remote", StringComparison.OrdinalIgnoreCase)
        || !ActivitySnapshotSyncService.IsLocalServer(profile.ServerName);

    private static string SystemDisplayName(DatabaseProfile profile) =>
        IsInfinityProfile(profile) ? "إنفينيتي" : "أبوغريس";

    private static bool IsCanonicalPhoneProfile(DatabaseProfile profile) =>
        string.Equals(profile.ProfileName, "Marketing", StringComparison.OrdinalIgnoreCase)
        || string.Equals(profile.ProfileName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase);

    private static bool IsSnapshotProfile(DatabaseProfile profile) =>
        IsRemotePharmacyProfile(profile);

    private static int ProfileSortKey(DatabaseProfile profile)
    {
        if (IsLocalMarketingProfile(profile))
        {
            return 0;
        }

        if (IsRemoteMarketingProfile(profile))
        {
            return 1;
        }

        if (IsLocalInfinityProfile(profile))
        {
            return 2;
        }

        if (IsRemoteInfinityProfile(profile))
        {
            return 3;
        }

        return 4;
    }

    private sealed class DatabaseListItem
    {
        public DatabaseListItem(DatabaseProfile profile)
        {
            Profile = profile;
        }

        public DatabaseProfile Profile { get; }

        public override string ToString()
        {
            var role = Profile.ConnectionKind == DatabaseConnectionKind.Network ? "شبكي" : "محلي";
            var system = SystemDisplayName(Profile);
            return $"{system}  ·  {role}  ·  {Profile.ServerName}";
        }
    }

    private void OpenLogsFolder()
    {
        LabPaths.EnsureLogsDirectory();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = LabPaths.LogsDirectory,
            UseShellExecute = true,
        });
    }

    private void OpenTestConsole()
    {
        var form = _serviceProvider.GetRequiredService<TestConsoleForm>();
        form.ShowLastActivity = UpdateLastActivity;
        form.Show(this);
    }

    private void OpenProfiles()
    {
        using var form = _serviceProvider.GetRequiredService<DatabaseProfilesForm>();
        form.ShowDialog(this);
        ReloadDbProfileCombo();
        RefreshStatusLabels();
    }

    private void UpdateTransportModeFromUi()
    {
        // Operator UI always uses the cloud tunnel.
        _settings.TransportMode = TransportMode.SupabaseTunnel;
        _transportCombo.SelectedIndex = 2;
        _settingsStore.Save(_settings);
        RefreshStatusLabels();
    }

    public void UpdateLastActivity(string requestSummary, string? errorMessage)
    {
        _lastRequestLabel.Text = requestSummary;
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            _lastErrorLabel.Text = errorMessage;
        }

        RefreshStatusLabels();
    }

    private void RefreshStatusLabels()
    {
        if (_dbListBox.Items.Count == 0 && _dbProfileCombo.Items.Count == 0)
        {
            ReloadDbProfileCombo();
        }

        _bridgeStatusLabel.Text = _bridgeRunning
            ? "Connected / متصل — يعمل في الخلفية"
            : "Stopped / متوقف";
        if (_bridgeRunning)
        {
            _connectionPillLabel.Text = "● الجسر متصل — يعمل في الخلفية";
            _connectionPillLabel.ForeColor = Color.FromArgb(22, 163, 74);
            _changeWatchStatusLabel.Text = _lastChangeWatchSummary;
            _domainWatchStatusLabel.Text = _lastDomainWatchSummary;
        }
        else
        {
            _connectionPillLabel.Text = "● الجسر غير متصل";
            _connectionPillLabel.ForeColor = UITheme.ErrorColor;
            _changeWatchStatusLabel.Text = "المراقبة: متوقفة";
            _changeWatchStatusLabel.ForeColor = UITheme.TextMuted;
            _domainWatchStatusLabel.Text = "مراقبة الدلتا: متوقفة";
            _domainWatchStatusLabel.ForeColor = UITheme.TextMuted;
        }

        RefreshConnectionSummary();

        _databaseStatusLabel.Text =
            $"{_profileStore.GetAll().Count} profile(s)" +
            (_lastDatabaseTestSummary is null ? string.Empty : $" — {_lastDatabaseTestSummary}");
        _transportModeLabel.Text = _settings.TransportMode.ToString();
        _tunnelIdLabel.Text = string.IsNullOrWhiteSpace(_settings.TunnelId)
            ? "(لم يُسجّل بعد)"
            : _settings.TunnelId;
        _pairingCodeLabel.Text = string.IsNullOrWhiteSpace(_settings.LastPairingCode)
            ? "(اضغط تحديث الرمز)"
            : _settings.LastPairingCode;
        _pairingExpiresLabel.Text = "ينتهي: " + FormatPairingExpiry(_settings.LastPairingExpiresAtUtc);
        UpdatePairingQr();
        _activeQueriesLabel.Text = _activeRequestTracker.ActiveCount.ToString();

        if (_settings.TransportMode == TransportMode.SupabaseTunnel)
        {
            var pollStatus = _supabaseTransport.LastPollStatus;
            var pollAt = _supabaseTransport.LastPollAtUtc;
            _lastPollStatusLabel.Text = pollStatus is null
                ? "بانتظار الاتصال بالسحابة…"
                : pollAt is null
                    ? pollStatus
                    : $"{pollStatus} ({pollAt:HH:mm:ss} UTC)";
        }
        else
        {
            _lastPollStatusLabel.Text = "-";
        }
    }

    private static string FormatPairingExpiry(string? rawUtc)
    {
        if (string.IsNullOrWhiteSpace(rawUtc))
        {
            return "-";
        }

        if (!DateTime.TryParse(rawUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            return rawUtc;
        }

        var local = parsed.ToLocalTime();
        return $"{local:yyyy-MM-dd HH:mm} ({(parsed.ToUniversalTime() > DateTime.UtcNow ? "ساري" : "منتهي")})";
    }
}
