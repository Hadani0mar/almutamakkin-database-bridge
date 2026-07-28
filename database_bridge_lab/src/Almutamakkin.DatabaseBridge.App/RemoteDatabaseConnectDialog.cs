using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Infrastructure;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.App;

/// <summary>
/// Connects to a remote SQL Server reachable over Tailscale (or any IP)
/// using SQL authentication, then registers selected databases as bridge profiles.
/// </summary>
public sealed class RemoteDatabaseConnectDialog : Form
{
    private readonly ISqlServerDiscovery _discovery;
    private readonly IDatabaseConnectionTester _connectionTester;
    private readonly ISecretProtector _secretProtector;

    private readonly TextBox _hostBox = new() { Width = 280 };
    private readonly TextBox _portBox = new() { Width = 80, Text = "1433" };
    private readonly TextBox _userBox = new() { Width = 220 };
    private readonly TextBox _passwordBox = new() { Width = 220, UseSystemPasswordChar = true };
    private readonly CheckBox _trustCertCheck = new()
    {
        Text = "الثقة بشهادة السيرفر (موصى به لـ Tailscale)",
        AutoSize = true,
        Checked = true,
    };
    private readonly Button _testButton = new() { Text = "اختبار الاتصال", Width = 130 };
    private readonly Button _loadDatabasesButton = new() { Text = "جلب قواعد البيانات", Width = 150 };
    private readonly CheckedListBox _databasesList = new() { CheckOnClick = true, Width = 540, Height = 240 };
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = Color.DarkSlateGray, MaximumSize = new Size(540, 0) };
    private bool _loading;

    public IReadOnlyList<DatabaseFetchSelection> SelectedDatabases { get; private set; } =
        Array.Empty<DatabaseFetchSelection>();

    public RemoteDatabaseConnectDialog(
        ISqlServerDiscovery discovery,
        IDatabaseConnectionTester connectionTester,
        ISecretProtector secretProtector)
    {
        _discovery = discovery;
        _connectionTester = connectionTester;
        _secretProtector = secretProtector;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "ربط قاعدة بعيدة (Tailscale / IP)";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(580, 560);
        Font = new Font("Segoe UI", 9F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            Text =
                "أدخل عنوان IP لجهاز SQL Server عبر Tailscale (غالباً يبدأ بـ 100.) أو أي مضيف بعيد، " +
                "ثم مستخدم SQL وكلمة المرور. بعدها اجلب القواعد واختر ما تريد تسجيله للجسر.",
        };

        var hostRow = BuildLabeledRow("عنوان IP / المضيف", _hostBox);
        var portRow = BuildLabeledRow("المنفذ", _portBox);
        var userRow = BuildLabeledRow("مستخدم القاعدة", _userBox);
        var passwordRow = BuildLabeledRow("كلمة السر", _passwordBox);

        var actionRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        actionRow.Controls.Add(_testButton);
        actionRow.Controls.Add(_loadDatabasesButton);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
            FlowDirection = FlowDirection.RightToLeft,
        };
        var okButton = new Button { Text = "حفظ المحدد", DialogResult = DialogResult.OK, Width = 120 };
        var cancelButton = new Button { Text = "إلغاء", DialogResult = DialogResult.Cancel, Width = 90 };
        buttons.Controls.AddRange([okButton, cancelButton]);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16),
            RowCount = 10,
        };
        for (var i = 0; i < 8; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(intro, 0, 0);
        layout.Controls.Add(hostRow, 0, 1);
        layout.Controls.Add(portRow, 0, 2);
        layout.Controls.Add(userRow, 0, 3);
        layout.Controls.Add(passwordRow, 0, 4);
        layout.Controls.Add(_trustCertCheck, 0, 5);
        layout.Controls.Add(actionRow, 0, 6);
        layout.Controls.Add(
            new Label { Text = "القواعد المكتشفة", AutoSize = true, Padding = new Padding(0, 8, 0, 4) },
            0,
            7);
        layout.Controls.Add(_databasesList, 0, 8);
        layout.Controls.Add(_statusLabel, 0, 9);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        _testButton.Click += async (_, _) => await TestConnectionAsync();
        _loadDatabasesButton.Click += async (_, _) => await LoadDatabasesAsync();
        okButton.Click += (_, _) => OnConfirm();

        UITheme.ApplyTheme(this);
        _statusLabel.Text = "مثال مضيف: 100.64.1.23   أو   100.64.1.23,1433";
    }

    private static FlowLayoutPanel BuildLabeledRow(string label, Control field)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 4),
        };
        row.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Width = 130,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 6, 8, 0),
        });
        row.Controls.Add(field);
        return row;
    }

    private async Task TestConnectionAsync()
    {
        if (!TryBuildProbeProfile(out var profile, out var error))
        {
            MessageBox.Show(this, error, "اختبار الاتصال", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _testButton.Enabled = false;
        _statusLabel.Text = "جاري اختبار الاتصال...";
        _statusLabel.ForeColor = Color.DarkSlateGray;
        try
        {
            // Probe master so we don't require a selected business database yet.
            profile.DatabaseName = "master";
            var result = await _connectionTester.TestAsync(profile, CancellationToken.None);
            _statusLabel.Text = result.Success
                ? $"الاتصال ناجح: {result.ServerName} كـ {result.LoginName}"
                : SensitiveDataSanitizer.Sanitize(result.Message);
            _statusLabel.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
            if (!result.Success)
            {
                MessageBox.Show(
                    this,
                    SensitiveDataSanitizer.Sanitize($"{result.Message}\n{result.Details}"),
                    "اختبار الاتصال",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "فشل اختبار الاتصال.";
            _statusLabel.ForeColor = Color.Firebrick;
            MessageBox.Show(
                this,
                SensitiveDataSanitizer.Sanitize(ex.Message),
                "اختبار الاتصال",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _testButton.Enabled = true;
        }
    }

    private async Task LoadDatabasesAsync()
    {
        if (_loading)
        {
            return;
        }

        if (!TryResolveCredentials(out var dataSource, out var userName, out var password, out var error))
        {
            MessageBox.Show(this, error, "جلب القواعد", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _loading = true;
        _loadDatabasesButton.Enabled = false;
        _statusLabel.Text = "جاري جلب القواعد من السيرفر البعيد...";
        _statusLabel.ForeColor = Color.DarkSlateGray;

        try
        {
            var databases = await _discovery.ListDatabasesAsync(
                dataSource,
                SqlAuthenticationMode.SqlAuthentication,
                userName,
                password,
                trustServerCertificate: _trustCertCheck.Checked,
                encryptConnection: false,
                CancellationToken.None);

            _databasesList.Items.Clear();
            foreach (var database in databases)
            {
                if (!IsPharmacyDatabase(database.Name))
                {
                    continue;
                }

                var profileName = database.CompatibilityHint ??
                                  SqlServerDiscoveryService.SuggestBridgeProfileName(database.Name);
                var item = new DatabaseFetchListItem(database.Name, profileName);
                var index = _databasesList.Items.Add(item);
                if (IsPharmacyDatabase(database.Name))
                {
                    _databasesList.SetItemChecked(index, true);
                }
            }

            var supportedCount = _databasesList.Items.Count;
            _statusLabel.Text = supportedCount == 0
                ? "لم يُعثر على قواعد متاحة على هذا السيرفر."
                : $"تم جلب {supportedCount} قاعدة مدعومة من السيرفر الشبكي.";
            _statusLabel.ForeColor = supportedCount == 0 ? Color.DarkOrange : Color.DarkGreen;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "فشل جلب القواعد البعيدة.";
            _statusLabel.ForeColor = Color.Firebrick;
            MessageBox.Show(
                this,
                SensitiveDataSanitizer.Sanitize(ex.Message),
                "جلب القواعد",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _loading = false;
            _loadDatabasesButton.Enabled = true;
        }
    }

    private void OnConfirm()
    {
        if (!TryResolveCredentials(out var dataSource, out var userName, out var password, out var error))
        {
            MessageBox.Show(this, error, "ربط قاعدة بعيدة", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        var selections = new List<DatabaseFetchSelection>();
        foreach (var item in _databasesList.CheckedItems)
        {
            if (item is not DatabaseFetchListItem database)
            {
                continue;
            }

            selections.Add(new DatabaseFetchSelection(
                SqlConnectionStringBuilderService.NormalizeDataSource(dataSource),
                database.DatabaseName,
                database.ProfileName,
                SqlAuthenticationMode.SqlAuthentication,
                userName,
                password));
        }

        if (selections.Count == 0)
        {
            MessageBox.Show(
                this,
                "اختر قاعدة واحدة على الأقل بعد جلب القائمة.",
                "ربط قاعدة بعيدة",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.None;
            return;
        }

        SelectedDatabases = selections;
    }

    private bool TryBuildProbeProfile(out DatabaseProfile profile, out string error)
    {
        profile = new DatabaseProfile();
        if (!TryResolveCredentials(out var dataSource, out var userName, out var password, out error))
        {
            return false;
        }

        // Password is DPAPI-protected so the shared tester can Unprotect it.
        profile = new DatabaseProfile
        {
            ProfileName = "remote-probe",
            ServerName = dataSource,
            DatabaseName = "master",
            AuthenticationMode = SqlAuthenticationMode.SqlAuthentication,
            UserName = userName,
            EncryptedPassword = _secretProtector.Protect(password),
            TrustServerCertificate = _trustCertCheck.Checked,
            EncryptConnection = false,
            IsEnabled = true,
            PermissionLevel = SqlPermissionLevel.ReadOnly,
            CommandTimeoutSeconds = 30,
            MaximumRows = 100,
        };
        return true;
    }

    private bool TryResolveCredentials(
        out string dataSource,
        out string userName,
        out string password,
        out string error)
    {
        dataSource = string.Empty;
        userName = string.Empty;
        password = string.Empty;
        error = string.Empty;

        var host = _hostBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            error = "أدخل عنوان IP أو اسم المضيف البعيد.";
            return false;
        }

        var portText = _portBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(portText))
        {
            portText = "1433";
        }

        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            error = "المنفذ يجب أن يكون رقماً بين 1 و 65535.";
            return false;
        }

        // If the user already typed host,port keep it; otherwise append the port field.
        dataSource = host.Contains(',', StringComparison.Ordinal)
            ? SqlConnectionStringBuilderService.NormalizeDataSource(host)
            : SqlConnectionStringBuilderService.NormalizeDataSource($"{host},{port}");

        userName = _userBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userName))
        {
            error = "أدخل مستخدم قاعدة البيانات.";
            return false;
        }

        password = _passwordBox.Text;
        if (string.IsNullOrEmpty(password))
        {
            error = "أدخل كلمة سر قاعدة البيانات.";
            return false;
        }

        return true;
    }

    private static bool IsPharmacyDatabase(string databaseName) =>
        string.Equals(databaseName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase) ||
        databaseName.StartsWith("Marketing", StringComparison.OrdinalIgnoreCase);

    private sealed record DatabaseFetchListItem(string DatabaseName, string ProfileName)
    {
        public override string ToString() =>
            $"{DatabaseName}  →  ملف الجسر: {ProfileName}";
    }
}
