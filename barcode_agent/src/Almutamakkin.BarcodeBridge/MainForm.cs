using System.ComponentModel;
using System.Drawing.Printing;
using System.Net;
using Almutamakkin.BarcodeBridge.Configuration;
using Almutamakkin.BarcodeBridge.Diagnostics;
using Almutamakkin.BarcodeBridge.Logging;
using Almutamakkin.BarcodeBridge.Networking;
using Almutamakkin.BarcodeBridge.Pairing;
using Almutamakkin.BarcodeBridge.Server;
using Almutamakkin.BarcodeBridge.Windows;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Almutamakkin.BarcodeBridge;

public sealed class MainForm : Form
{
    private readonly EncryptedSettingsStore _store;
    private readonly BridgeLogHub _logs;
    private readonly BridgeServerController _server;
    private BridgeSettings _settings;
    private IPAddress? _lanAddress;
    private bool _operationInProgress;
    private bool _allowClose;

    private readonly TextBox _sqlServer = new();
    private readonly TextBox _database = new();
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();
    private readonly ComboBox _printer = new();
    private readonly NumericUpDown _port = new();
    private readonly CheckBox _runAtStartup = new();
    private readonly Button _saveButton = new();
    private readonly Button _databaseTestButton = new();
    private readonly Button _printerTestButton = new();
    private readonly Button _firewallButton = new();
    private readonly Button _rotateKeyButton = new();
    private readonly Button _serverButton = new();
    private readonly Label _serverStatus = new();
    private readonly Label _lanStatus = new();
    private readonly TextBox _pairingCode = new();
    private readonly Button _copyPairingButton = new();
    private readonly PictureBox _qrPicture = new();
    private readonly RichTextBox _logBox = new();
    private readonly bool _embedded;
    private readonly NotifyIcon _trayIcon = new();
    private readonly System.Windows.Forms.Timer _networkTimer = new() { Interval = 15_000 };
    private readonly List<Control> _settingsControls = [];

    public MainForm(
        EncryptedSettingsStore store,
        BridgeLogHub logs,
        BridgeServerController server,
        bool startMinimized,
        bool embedded = false)
    {
        _store = store;
        _logs = logs;
        _server = server;
        _embedded = embedded;
        _settings = store.LoadOrCreate();

        InitializeWindow();
        BuildInterface();
        PopulatePrinters();
        LoadSettingsIntoControls();
        SubscribeEvents();
        RefreshLanAddress(restartServer: false);
        foreach (var entry in _logs.Snapshot()) AppendLog(entry);

        if (embedded)
        {
            Shown += async (_, _) =>
            {
                if (!_server.IsRunning)
                {
                    await StartServerAsync();
                }
            };
            return;
        }

        if (startMinimized)
        {
            Shown += async (_, _) =>
            {
                await StartServerAsync();
                if (_server.IsRunning) HideToTray(showBalloon: false);
            };
        }
    }

    private void InitializeWindow()
    {
        Text = "جسر طباعة الباركود — المتمكن";
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(880, 680);
        Size = new Size(980, 780);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(245, 247, 250);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = false;
        if (_embedded)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopLevel = false;
            Dock = DockStyle.Fill;
        }
    }

    private void BuildInterface()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 116, Padding = new Padding(24, 16, 24, 12), BackColor = Color.White };
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            RightToLeft = RightToLeft.No,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        var headerInfo = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            RightToLeft = RightToLeft.Yes,
            Margin = new Padding(0, 0, 12, 0),
            Padding = Padding.Empty
        };
        headerInfo.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        headerInfo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = "جسر طباعة الباركود",
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(25, 34, 51),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true
        };
        _serverStatus.Text = "●  الخادم متوقف";
        _serverStatus.ForeColor = Color.FromArgb(176, 55, 55);
        _serverStatus.AutoSize = true;
        _lanStatus.AutoSize = true;
        _lanStatus.ForeColor = Color.FromArgb(80, 90, 108);
        var statusFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            RightToLeft = RightToLeft.Yes,
            Padding = new Padding(0, 6, 0, 0),
            Margin = Padding.Empty
        };
        _serverStatus.Margin = new Padding(18, 4, 0, 4);
        _lanStatus.Margin = new Padding(18, 4, 0, 4);
        statusFlow.Controls.AddRange([_serverStatus, _lanStatus]);
        headerInfo.Controls.Add(title, 0, 0);
        headerInfo.Controls.Add(statusFlow, 0, 1);
        StylePrimaryButton(_serverButton, "تشغيل الخادم");
        _serverButton.AutoSize = false;
        _serverButton.Dock = DockStyle.Fill;
        _serverButton.Margin = new Padding(10, 18, 0, 18);
        headerLayout.Controls.Add(headerInfo, 0, 0);
        headerLayout.Controls.Add(_serverButton, 1, 0);
        header.Controls.Add(headerLayout);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(18, 8) };
        tabs.TabPages.Add(BuildSettingsTab());
        tabs.TabPages.Add(BuildPairingTab());
        tabs.TabPages.Add(BuildLogsTab());

        Controls.Add(tabs);
        Controls.Add(header);
        ConfigureTrayIcon();
    }

    private TabPage BuildSettingsTab()
    {
        var tab = new TabPage("الإعدادات") { BackColor = Color.FromArgb(245, 247, 250), Padding = new Padding(18) };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(18),
            BackColor = Color.White
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));

        ConfigureTextField(_sqlServer);
        ConfigureTextField(_database);
        ConfigureTextField(_username);
        ConfigureTextField(_password);
        _password.UseSystemPasswordChar = true;
        _printer.DropDownStyle = ComboBoxStyle.DropDownList;
        _printer.Dock = DockStyle.Fill;
        _printer.Height = 34;
        _port.Minimum = 1024;
        _port.Maximum = 65535;
        _port.Dock = DockStyle.Left;
        _port.Width = 180;
        _runAtStartup.Text = "تشغيل الخادم تلقائياً مع دخول ويندوز";
        _runAtStartup.AutoSize = true;

        AddSettingRow(table, "عنوان قاعدة البيانات", _sqlServer);
        AddSettingRow(table, "اسم القاعدة", _database);
        AddSettingRow(table, "اسم المستخدم", _username);
        AddSettingRow(table, "كلمة المرور", _password);
        AddSettingRow(table, "طابعة الباركود", _printer);
        AddSettingRow(table, "منفذ الاتصال", _port);
        AddSettingRow(table, "بدء التشغيل", _runAtStartup);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = false,
            Height = 112,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true,
            RightToLeft = RightToLeft.Yes,
            Padding = new Padding(0, 14, 0, 0),
            Margin = Padding.Empty
        };
        StylePrimaryButton(_saveButton, "حفظ الإعدادات");
        StyleSecondaryButton(_databaseTestButton, "اختبار قاعدة البيانات");
        StyleSecondaryButton(_printerTestButton, "فحص الطابعة");
        StyleSecondaryButton(_firewallButton, "إعداد جدار الحماية");
        StyleSecondaryButton(_rotateKeyButton, "تجديد مفتاح الربط");
        buttons.Controls.AddRange([_saveButton, _databaseTestButton, _printerTestButton, _firewallButton, _rotateKeyButton]);
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        table.Controls.Add(buttons, 0, table.RowCount);
        table.SetColumnSpan(buttons, 2);
        table.RowCount++;

        _settingsControls.AddRange([
            _sqlServer, _database, _username, _password, _printer, _port, _runAtStartup,
            _saveButton, _databaseTestButton, _printerTestButton, _rotateKeyButton
        ]);
        tab.Controls.Add(table);
        return tab;
    }

    private TabPage BuildPairingTab()
    {
        var tab = new TabPage("ربط التطبيق") { BackColor = Color.FromArgb(245, 247, 250), Padding = new Padding(18) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18),
            BackColor = Color.White
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        var qrPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
        _qrPicture.SizeMode = PictureBoxSizeMode.Zoom;
        _qrPicture.Dock = DockStyle.Fill;
        _qrPicture.BackColor = Color.White;
        qrPanel.Controls.Add(_qrPicture);

        var codePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(18) };
        codePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        codePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        codePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        codePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new Label
        {
            Text = "امسح الرمز من تطبيق المتمكن",
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(4, 4, 4, 12)
        };
        _pairingCode.Multiline = true;
        _pairingCode.ReadOnly = true;
        _pairingCode.ScrollBars = ScrollBars.Vertical;
        _pairingCode.Dock = DockStyle.Fill;
        _pairingCode.RightToLeft = RightToLeft.No;
        _pairingCode.Font = new Font("Consolas", 9F);
        _copyPairingButton.Text = "نسخ رمز الربط";
        _copyPairingButton.AutoSize = true;
        _copyPairingButton.Padding = new Padding(12, 7, 12, 7);
        var note = new Label
        {
            Text = "يحتوي الرمز على عنوان الجهاز ومفتاح وصول خاص. لا تشاركه خارج أجهزتك.",
            ForeColor = Color.FromArgb(96, 103, 118),
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(4, 12, 4, 4)
        };
        codePanel.Controls.Add(heading, 0, 0);
        codePanel.Controls.Add(_pairingCode, 0, 1);
        codePanel.Controls.Add(_copyPairingButton, 0, 2);
        codePanel.Controls.Add(note, 0, 3);

        layout.Controls.Add(qrPanel, 0, 0);
        layout.Controls.Add(codePanel, 1, 0);
        tab.Controls.Add(layout);
        return tab;
    }

    private TabPage BuildLogsTab()
    {
        var tab = new TabPage("السجل") { BackColor = Color.FromArgb(245, 247, 250), Padding = new Padding(18) };
        _logBox.Dock = DockStyle.Fill;
        _logBox.ReadOnly = true;
        _logBox.BackColor = Color.FromArgb(24, 29, 39);
        _logBox.ForeColor = Color.FromArgb(218, 226, 239);
        _logBox.Font = new Font("Consolas", 9.5F);
        _logBox.RightToLeft = RightToLeft.No;
        tab.Controls.Add(_logBox);
        return tab;
    }

    private void ConfigureTrayIcon()
    {
        if (_embedded)
        {
            _trayIcon.Visible = false;
            return;
        }

        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
        var open = new ToolStripMenuItem("فتح النافذة");
        var start = new ToolStripMenuItem("تشغيل الخادم");
        var stop = new ToolStripMenuItem("إيقاف الخادم");
        var exit = new ToolStripMenuItem("خروج من البرنامج");
        open.Click += (_, _) => RestoreFromTray();
        start.Click += async (_, _) => await StartServerAsync();
        stop.Click += async (_, _) => await StopServerAsync();
        exit.Click += async (_, _) => await ExitApplicationAsync();
        menu.Items.AddRange([open, new ToolStripSeparator(), start, stop, new ToolStripSeparator(), exit]);
        _trayIcon.Icon = Icon;
        _trayIcon.Text = "جسر طباعة الباركود";
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void SubscribeEvents()
    {
        _serverButton.Click += async (_, _) =>
        {
            if (_server.IsRunning) await StopServerAsync();
            else await StartServerAsync();
        };
        _saveButton.Click += (_, _) => SaveSettings(showSuccess: true);
        _databaseTestButton.Click += async (_, _) => await TestDatabaseAsync();
        _printerTestButton.Click += (_, _) => TestPrinter();
        _firewallButton.Click += async (_, _) => await ConfigureFirewallAsync();
        _rotateKeyButton.Click += (_, _) => RotateApiKey();
        _copyPairingButton.Click += (_, _) => CopyPairingCode();
        _logs.EntryAdded += OnLogEntryAdded;
        _networkTimer.Tick += async (_, _) => await RefreshLanAddressAsync();
        _networkTimer.Start();
        if (!_embedded)
        {
            Resize += (_, _) =>
            {
                if (WindowState == FormWindowState.Minimized) HideToTray(showBalloon: true);
            };
            FormClosing += OnFormClosing;
        }
    }

    private void PopulatePrinters()
    {
        _printer.Items.Clear();
        foreach (var name in BridgeDiagnostics.InstalledPrinters()) _printer.Items.Add(name);
    }

    private void LoadSettingsIntoControls()
    {
        _sqlServer.Text = _settings.SqlServer;
        _database.Text = _settings.Database;
        _username.Text = _settings.Username;
        _password.Text = _settings.Password;
        _port.Value = Math.Clamp(_settings.Port, (int)_port.Minimum, (int)_port.Maximum);
        _runAtStartup.Checked = StartupManager.IsEnabled() || _settings.RunAtStartup;
        var index = _printer.Items.Cast<string>().ToList().FindIndex(name =>
            string.Equals(name, _settings.PrinterName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _printer.SelectedIndex = index;
        else if (_printer.Items.Count == 1) _printer.SelectedIndex = 0;
    }

    private BridgeSettings ReadControls() => new()
    {
        SqlServer = _sqlServer.Text.Trim(),
        Database = _database.Text.Trim(),
        Username = _username.Text.Trim(),
        Password = _password.Text,
        PrinterName = _printer.SelectedItem?.ToString() ?? string.Empty,
        Port = Decimal.ToInt32(_port.Value),
        ApiKey = _settings.ApiKey,
        RunAtStartup = _runAtStartup.Checked
    };

    private bool SaveSettings(bool showSuccess)
    {
        var candidate = ReadControls();
        var errors = candidate.Validate();
        if (errors.Count != 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, errors), "راجع الإعدادات", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        try
        {
            _store.Save(candidate);
            StartupManager.SetEnabled(candidate.RunAtStartup);
            _settings = candidate;
            UpdatePairingCode();
            _logs.Add(LogLevel.Information, "تم حفظ الإعدادات بصورة مشفّرة للمستخدم الحالي.");
            if (showSuccess)
                MessageBox.Show("تم حفظ الإعدادات.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception exception)
        {
            _logs.Add(LogLevel.Error, "تعذر حفظ الإعدادات.", exception);
            MessageBox.Show(exception.Message, "تعذر الحفظ", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task StartServerAsync()
    {
        if (_operationInProgress || _server.IsRunning) return;
        if (!SaveSettings(showSuccess: false)) return;
        SetBusy(true);
        try
        {
            RefreshLanAddress(restartServer: false);
            await _server.StartAsync(_settings.Copy(), _lanAddress);
            SetRunningState(true);
        }
        catch (Exception exception)
        {
            _logs.Add(LogLevel.Error, "تعذر تشغيل الخادم.", exception);
            MessageBox.Show(exception.Message, "تعذر تشغيل الخادم", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetRunningState(false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StopServerAsync()
    {
        if (_operationInProgress || !_server.IsRunning) return;
        SetBusy(true);
        try
        {
            await _server.StopAsync();
            SetRunningState(false);
        }
        catch (Exception exception)
        {
            _logs.Add(LogLevel.Error, "تعذر إيقاف الخادم بصورة سليمة.", exception);
            MessageBox.Show(exception.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TestDatabaseAsync()
    {
        if (_operationInProgress) return;
        SetBusy(true);
        try
        {
            var candidate = ReadControls();
            var result = await BridgeDiagnostics.TestDatabaseAsync(candidate);
            if (result.Ready)
            {
                _logs.Add(LogLevel.Information, $"اتصال قاعدة البيانات ناجح. النشاط: {result.BusinessName ?? "غير محدد"}.");
                MessageBox.Show($"الاتصال ناجح.\nاسم النشاط: {result.BusinessName ?? "غير محدد"}", "قاعدة البيانات", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                _logs.Add(LogLevel.Warning, $"فشل اختبار قاعدة البيانات: {result.Error}");
                MessageBox.Show(result.Error, "فشل الاتصال", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void TestPrinter()
    {
        var result = BridgeDiagnostics.TestPrinter(_printer.SelectedItem?.ToString() ?? string.Empty);
        if (result.Ready)
        {
            _logs.Add(LogLevel.Information, $"الطابعة جاهزة دون تنفيذ طباعة. المهام المنتظرة: {result.QueuedJobs}.");
            MessageBox.Show("الطابعة موجودة وطابور ويندوز جاهز. لم يتم إرسال أي ملصق.", "فحص الطابعة", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            _logs.Add(LogLevel.Warning, $"فحص الطابعة غير جاهز: {result.Reason}");
            MessageBox.Show(result.Reason, "الطابعة غير جاهزة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ConfigureFirewallAsync()
    {
        if (_operationInProgress) return;
        var port = Decimal.ToInt32(_port.Value);
        SetBusy(true);
        try
        {
            var exitCode = await FirewallConfigurator.RunElevatedAsync(port);
            if (exitCode != 0) throw new InvalidOperationException($"فشل إعداد جدار الحماية. رمز الخطأ: {exitCode}");
            _logs.Add(LogLevel.Information, $"تم السماح بالمنفذ {port} لعناوين الشبكة المحلية الخاصة فقط.");
            MessageBox.Show("تم إعداد جدار الحماية لعناوين الشبكة المحلية الخاصة فقط.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            _logs.Add(LogLevel.Warning, "ألغى المستخدم طلب صلاحية المسؤول.");
        }
        catch (Exception exception)
        {
            _logs.Add(LogLevel.Error, "تعذر إعداد جدار الحماية.", exception);
            MessageBox.Show(exception.Message, "خطأ", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RotateApiKey()
    {
        if (MessageBox.Show(
                "سيحتاج تطبيق الهاتف إلى ربط جديد بعد تغيير المفتاح. هل تريد المتابعة؟",
                "تجديد مفتاح الربط",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _settings.ApiKey = ApiKeyGenerator.Generate();
        if (!SaveSettings(showSuccess: false)) return;
        _logs.Add(LogLevel.Information, "تم إنشاء مفتاح ربط جديد.");
        MessageBox.Show("تم إنشاء مفتاح جديد. اربط تطبيق الهاتف مرة أخرى.", "تم", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RefreshLanAddress(bool restartServer)
    {
        var detected = LanIpDetector.Detect();
        if (Equals(detected, _lanAddress)) return;
        _lanAddress = detected;
        _lanStatus.Text = detected is null ? "الشبكة: غير متصلة" : $"الشبكة: {detected}";
        _lanStatus.ForeColor = detected is null ? Color.FromArgb(176, 97, 39) : Color.FromArgb(42, 128, 86);
        UpdatePairingCode();
        if (restartServer && _server.IsRunning)
            _logs.Add(LogLevel.Information, "تغيّر عنوان الشبكة المحلية؛ سيتم تحديث رمز الربط.");
    }

    private Task RefreshLanAddressAsync()
    {
        if (_operationInProgress) return Task.CompletedTask;
        RefreshLanAddress(restartServer: false);
        return Task.CompletedTask;
    }

    private void UpdatePairingCode()
    {
        if (_lanAddress is null || string.IsNullOrWhiteSpace(_settings.PrinterName))
        {
            _pairingCode.Text = "وصّل الجهاز بشبكة Wi‑Fi أو Ethernet واختر الطابعة لإظهار رمز الربط.";
            _copyPairingButton.Enabled = false;
            var old = _qrPicture.Image;
            _qrPicture.Image = null;
            old?.Dispose();
            return;
        }

        var code = PairingCodeService.Create(_settings, _lanAddress, Environment.MachineName);
        _pairingCode.Text = code;
        _copyPairingButton.Enabled = true;
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new QRCode(qrData);
        var image = qrCode.GetGraphic(7, Color.Black, Color.White, drawQuietZones: true);
        var previous = _qrPicture.Image;
        _qrPicture.Image = image;
        previous?.Dispose();
    }

    private void CopyPairingCode()
    {
        if (!_pairingCode.Text.StartsWith(PairingCodeService.Prefix, StringComparison.Ordinal)) return;
        Clipboard.SetText(_pairingCode.Text);
        _logs.Add(LogLevel.Information, "تم نسخ رمز الربط.");
    }

    private void SetBusy(bool busy)
    {
        _operationInProgress = busy;
        UseWaitCursor = busy;
        _serverButton.Enabled = !busy;
        _firewallButton.Enabled = !busy;
    }

    private void SetRunningState(bool running)
    {
        _serverStatus.Text = running ? "●  الخادم يعمل" : "●  الخادم متوقف";
        _serverStatus.ForeColor = running ? Color.FromArgb(42, 128, 86) : Color.FromArgb(176, 55, 55);
        _serverButton.Text = running ? "إيقاف الخادم" : "تشغيل الخادم";
        foreach (var control in _settingsControls) control.Enabled = !running;
        _firewallButton.Enabled = true;
    }

    private void OnLogEntryAdded(BridgeLogEntry entry)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(() => AppendLog(entry));
        else AppendLog(entry);
    }

    private void AppendLog(BridgeLogEntry entry)
    {
        _logBox.AppendText(entry + Environment.NewLine);
        if (_logBox.TextLength > 120_000) _logBox.Text = _logBox.Text[^80_000..];
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void HideToTray(bool showBalloon)
    {
        if (_embedded) return;
        Hide();
        WindowState = FormWindowState.Normal;
        if (!showBalloon) return;
        _trayIcon.BalloonTipTitle = "جسر طباعة الباركود";
        _trayIcon.BalloonTipText = "البرنامج مستمر في العمل بجانب الساعة.";
        _trayIcon.ShowBalloonTip(2500);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private async Task ExitApplicationAsync()
    {
        if (MessageBox.Show(
                "سيتم إيقاف الخادم وإنهاء البرنامج. هل تريد المتابعة؟",
                "خروج من البرنامج",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
        _allowClose = true;
        try { await _server.StopAsync(); }
        finally
        {
            _trayIcon.Visible = false;
            Close();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose) return;
        if (eventArgs.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            _allowClose = true;
            _server.StopAsync().GetAwaiter().GetResult();
            return;
        }
        eventArgs.Cancel = true;
        HideToTray(showBalloon: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _networkTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _qrPicture.Image?.Dispose();
            _logs.EntryAdded -= OnLogEntryAdded;
        }
        base.Dispose(disposing);
    }

    private static void AddSettingRow(TableLayoutPanel table, string labelText, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = true,
            Padding = new Padding(4, 10, 4, 10)
        };
        control.Margin = new Padding(4, 7, 4, 7);
        table.Controls.Add(control, 0, row);
        table.Controls.Add(label, 1, row);
    }

    private static void ConfigureTextField(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Margin = new Padding(4, 7, 4, 7);
    }

    private static void StylePrimaryButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(16, 8, 16, 8);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Color.FromArgb(44, 97, 201);
        button.ForeColor = Color.White;
        button.Cursor = Cursors.Hand;
    }

    private static void StyleSecondaryButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(12, 7, 12, 7);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(190, 198, 212);
        button.BackColor = Color.White;
        button.ForeColor = Color.FromArgb(40, 48, 63);
        button.Cursor = Cursors.Hand;
    }
}
