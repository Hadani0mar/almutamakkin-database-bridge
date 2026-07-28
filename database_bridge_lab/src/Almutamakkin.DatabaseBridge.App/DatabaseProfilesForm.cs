using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Infrastructure;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.App;

public sealed class DatabaseProfilesForm : Form
{
    private readonly IDatabaseProfileStore _profileStore;
    private readonly IDatabaseConnectionTester _connectionTester;
    private readonly ISecretProtector _secretProtector;
    private readonly ISqlServerDiscovery _discovery;

    private readonly ListView _profilesList = new()
    {
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        Height = 140,
        Dock = DockStyle.Top,
    };

    private readonly ComboBox _serverCombo = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 360 };
    private readonly Button _discoverServersButton = new() { Text = "اكتشاف السيرفرات", Width = 130 };
    private readonly ComboBox _authModeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _userNameBox = new() { Width = 260 };
    private readonly TextBox _passwordBox = new() { Width = 260, UseSystemPasswordChar = true };
    private readonly Button _loadDatabasesButton = new() { Text = "جلب قواعد البيانات", Width = 150 };
    private readonly ComboBox _databaseCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly TextBox _profileNameBox = new() { Width = 260 };
    private readonly Label _hintLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(480, 0),
        ForeColor = Color.DimGray,
        Text =
            "اختر السيرفر المحلي أو اكتب IP بعيد (Tailscale مثل 100.x.x.x) ثم اجلب القواعد. اسم الملف يُملأ تلقائياً ليتوافق مع تطبيق المتمكن (Marketing / InfinityRetailDB).",
    };
    private readonly CheckBox _encryptConnectionCheck = new() { Text = "Encrypt", AutoSize = true };
    private readonly CheckBox _trustServerCertificateCheck = new()
    {
        Text = "Trust Server Certificate",
        AutoSize = true,
        Checked = true,
    };
    private readonly ComboBox _permissionCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly NumericUpDown _timeoutNumeric = new() { Minimum = 1, Maximum = 600, Value = 30, Width = 120 };
    private readonly NumericUpDown _maxRowsNumeric = new() { Minimum = 1, Maximum = 30000, Value = 1000, Width = 120 };
    private readonly CheckBox _enabledCheck = new() { Text = "مفعّل", AutoSize = true, Checked = true };
    private readonly Label _statusLabel = new()
    {
        AutoSize = true,
        ForeColor = Color.DarkSlateGray,
        Text = "جاهز",
    };

    private Guid? _selectedProfileId;
    private string? _existingEncryptedPassword;
    private bool _loadingDatabases;

    public DatabaseProfilesForm(
        IDatabaseProfileStore profileStore,
        IDatabaseConnectionTester connectionTester,
        ISecretProtector secretProtector,
        ISqlServerDiscovery discovery)
    {
        _profileStore = profileStore;
        _connectionTester = connectionTester;
        _secretProtector = secretProtector;
        _discovery = discovery;

        InitializeComponent();
        WireEvents();
        ReloadProfiles();
        DiscoverServers();
        ClearEditor(keepServers: true);
    }

    private void InitializeComponent()
    {
        Text = "ملفات الاتصال — اكتشاف تلقائي";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 620);
        Font = new Font("Segoe UI", 9F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        _profilesList.Columns.Add("الملف", 150);
        _profilesList.Columns.Add("السيرفر", 150);
        _profilesList.Columns.Add("القاعدة", 160);
        _profilesList.Columns.Add("الصلاحية", 100);
        _profilesList.Columns.Add("مفعّل", 60);

        _authModeCombo.Items.AddRange(["Windows Authentication", "SQL Authentication"]);
        _permissionCombo.Items.AddRange(["ReadOnly", "ReadWrite", "FullAccess", "Custom"]);

        var editorPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoScroll = true,
        };
        editorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        editorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var serverRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        serverRow.Controls.Add(_serverCombo);
        serverRow.Controls.Add(_discoverServersButton);

        var dbRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        dbRow.Controls.Add(_databaseCombo);
        dbRow.Controls.Add(_loadDatabasesButton);

        var options = new FlowLayoutPanel { AutoSize = true };
        options.Controls.Add(_encryptConnectionCheck);
        options.Controls.Add(_trustServerCertificateCheck);

        AddEditorRow(editorPanel, 0, "السيرفر / IP", serverRow);
        AddEditorRow(editorPanel, 1, "المصادقة", _authModeCombo);
        AddEditorRow(editorPanel, 2, "المستخدم", _userNameBox);
        AddEditorRow(editorPanel, 3, "كلمة المرور", _passwordBox);
        AddEditorRow(editorPanel, 4, "القواعد", dbRow);
        AddEditorRow(editorPanel, 5, "اسم الملف للجسر", _profileNameBox);
        AddEditorRow(editorPanel, 6, string.Empty, _hintLabel);
        AddEditorRow(editorPanel, 7, "خيارات", options);
        AddEditorRow(editorPanel, 8, "الصلاحية", _permissionCombo);
        AddEditorRow(editorPanel, 9, "المهلة (ث)", _timeoutNumeric);
        AddEditorRow(editorPanel, 10, "أقصى صفوف", _maxRowsNumeric);
        AddEditorRow(editorPanel, 11, "الحالة", _enabledCheck);
        AddEditorRow(editorPanel, 12, string.Empty, _statusLabel);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
            FlowDirection = FlowDirection.RightToLeft,
        };

        var newButton = new Button { Text = "جديد", Width = 90 };
        var saveButton = new Button { Text = "حفظ", Width = 90 };
        var testButton = new Button { Text = "اختبار الاتصال", Width = 120 };
        var deleteButton = new Button { Text = "حذف", Width = 90 };
        var duplicateButton = new Button { Text = "نسخ", Width = 90 };

        newButton.Click += (_, _) => ClearEditor(keepServers: true);
        saveButton.Click += (_, _) => SaveProfile();
        testButton.Click += async (_, _) => await TestConnectionAsync();
        deleteButton.Click += (_, _) => DeleteSelectedProfile();
        duplicateButton.Click += (_, _) => DuplicateSelectedProfile();

        buttons.Controls.AddRange([newButton, saveButton, testButton, deleteButton, duplicateButton]);

        Controls.Add(editorPanel);
        Controls.Add(buttons);
        Controls.Add(_profilesList);
        UITheme.ApplyTheme(this);
    }

    private static void AddEditorRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (!string.IsNullOrWhiteSpace(label))
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        }

        panel.Controls.Add(control, 1, row);
    }

    private void WireEvents()
    {
        _profilesList.SelectedIndexChanged += (_, _) => LoadSelectedProfile();
        _authModeCombo.SelectedIndexChanged += (_, _) => UpdateAuthFields();
        _discoverServersButton.Click += (_, _) => DiscoverServers();
        _loadDatabasesButton.Click += async (_, _) => await LoadDatabasesAsync();
        _databaseCombo.SelectedIndexChanged += (_, _) => OnDatabaseSelected();
    }

    private void DiscoverServers()
    {
        try
        {
            var selected = _serverCombo.Text;
            var instances = _discovery.DiscoverLocalInstances();
            _serverCombo.Items.Clear();
            foreach (var instance in instances)
            {
                _serverCombo.Items.Add(instance);
            }

            _serverCombo.DisplayMember = nameof(SqlServerInstanceInfo.DisplayName);
            _serverCombo.ValueMember = nameof(SqlServerInstanceInfo.DataSource);

            if (_serverCombo.Items.Count > 0)
            {
                var matchIndex = -1;
                for (var i = 0; i < _serverCombo.Items.Count; i++)
                {
                    if (_serverCombo.Items[i] is SqlServerInstanceInfo info &&
                        (string.Equals(info.DataSource, selected, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(info.DisplayName, selected, StringComparison.OrdinalIgnoreCase)))
                    {
                        matchIndex = i;
                        break;
                    }
                }

                _serverCombo.SelectedIndex = matchIndex >= 0 ? matchIndex : 0;
            }

            _statusLabel.Text = $"تم اكتشاف {instances.Count} سيرفر/نسخة محلية.";
            _statusLabel.ForeColor = Color.DarkGreen;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = SensitiveDataSanitizer.Sanitize(ex.Message);
            _statusLabel.ForeColor = Color.Firebrick;
        }
    }

    private async Task LoadDatabasesAsync()
    {
        if (_loadingDatabases)
        {
            return;
        }

        _loadingDatabases = true;
        _loadDatabasesButton.Enabled = false;
        _statusLabel.Text = "جاري جلب القواعد...";
        _statusLabel.ForeColor = Color.DarkSlateGray;

        try
        {
            var dataSource = ResolveSelectedDataSource();
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                MessageBox.Show(this, "اختر سيرفر أولاً.", "القواعد", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var isSqlAuth = _authModeCombo.SelectedIndex == 1;
            var plainPassword = isSqlAuth ? _passwordBox.Text : null;
            if (isSqlAuth && string.IsNullOrWhiteSpace(_userNameBox.Text))
            {
                MessageBox.Show(this, "أدخل اسم المستخدم لمصادقة SQL.", "القواعد", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var databases = await _discovery.ListDatabasesAsync(
                dataSource,
                isSqlAuth ? SqlAuthenticationMode.SqlAuthentication : SqlAuthenticationMode.WindowsAuthentication,
                isSqlAuth ? _userNameBox.Text.Trim() : null,
                plainPassword,
                _trustServerCertificateCheck.Checked,
                _encryptConnectionCheck.Checked,
                CancellationToken.None);

            _databaseCombo.Items.Clear();
            foreach (var database in databases)
            {
                _databaseCombo.Items.Add(database);
            }

            _databaseCombo.DisplayMember = nameof(SqlDatabaseInfo.Name);

            if (_databaseCombo.Items.Count > 0)
            {
                // Prefer pharmacy databases when present.
                var preferred = databases
                    .Select((db, index) => (db, index))
                    .FirstOrDefault(item =>
                        item.db.Name.Equals("InfinityRetailDB", StringComparison.OrdinalIgnoreCase) ||
                        item.db.Name.StartsWith("Marketing", StringComparison.OrdinalIgnoreCase));

                _databaseCombo.SelectedIndex = preferred.db is null ? 0 : preferred.index;
            }

            _statusLabel.Text = $"تم جلب {databases.Count} قاعدة.";
            _statusLabel.ForeColor = Color.DarkGreen;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "فشل جلب القواعد.";
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
            _loadingDatabases = false;
            _loadDatabasesButton.Enabled = true;
        }
    }

    private void OnDatabaseSelected()
    {
        if (_databaseCombo.SelectedItem is not SqlDatabaseInfo database)
        {
            return;
        }

        var suggested = database.CompatibilityHint ?? SqlServerDiscoveryService.SuggestBridgeProfileName(database.Name);
        if (string.IsNullOrWhiteSpace(_profileNameBox.Text) ||
            _profileNameBox.Text.Equals(database.Name, StringComparison.OrdinalIgnoreCase) ||
            _profileNameBox.Text is "Marketing" or "InfinityRetailDB")
        {
            _profileNameBox.Text = suggested;
        }
        else if (_selectedProfileId is null)
        {
            _profileNameBox.Text = suggested;
        }

        _hintLabel.Text =
            $"القاعدة الفعلية: {database.Name}\nاسم الملف للجسر (يستخدمه التطبيق): {suggested}";
    }

    private string ResolveSelectedDataSource()
    {
        if (_serverCombo.SelectedItem is SqlServerInstanceInfo instance)
        {
            return instance.DataSource;
        }

        return SqlConnectionStringBuilderService.NormalizeDataSource(_serverCombo.Text);
    }

    private void ReloadProfiles()
    {
        _profilesList.Items.Clear();
        foreach (var profile in _profileStore.GetAll().OrderBy(item => item.ProfileName))
        {
            var item = new ListViewItem(profile.ProfileName);
            item.SubItems.Add(profile.ServerName);
            item.SubItems.Add(profile.DatabaseName);
            item.SubItems.Add(profile.PermissionLevel.ToString());
            item.SubItems.Add(profile.IsEnabled ? "نعم" : "لا");
            item.Tag = profile.Id;
            _profilesList.Items.Add(item);
        }
    }

    private void ClearEditor(bool keepServers)
    {
        _selectedProfileId = null;
        _existingEncryptedPassword = null;
        _profileNameBox.Text = string.Empty;
        if (!keepServers)
        {
            _serverCombo.Items.Clear();
            _serverCombo.Text = "localhost";
        }

        _databaseCombo.Items.Clear();
        _authModeCombo.SelectedIndex = 0;
        _userNameBox.Text = string.Empty;
        _passwordBox.Text = string.Empty;
        _encryptConnectionCheck.Checked = false;
        _trustServerCertificateCheck.Checked = true;
        _permissionCombo.SelectedIndex = 0;
        _timeoutNumeric.Value = 30;
        _maxRowsNumeric.Value = 1000;
        _enabledCheck.Checked = true;
        _hintLabel.Text =
            "اختر السيرفر ثم اجلب القواعد واختر القاعدة. اسم الملف يُملأ تلقائياً ليتوافق مع تطبيق المتمكن.";
        _statusLabel.Text = "جاهز";
        _statusLabel.ForeColor = Color.DarkSlateGray;
        UpdateAuthFields();
    }

    private void LoadSelectedProfile()
    {
        if (_profilesList.SelectedItems.Count == 0)
        {
            return;
        }

        var profileId = (Guid)_profilesList.SelectedItems[0].Tag!;
        var profile = _profileStore.GetById(profileId);
        if (profile is null)
        {
            return;
        }

        _selectedProfileId = profile.Id;
        _existingEncryptedPassword = profile.EncryptedPassword;
        _profileNameBox.Text = profile.ProfileName;
        _serverCombo.Text = profile.ServerName;
        EnsureDatabaseItem(profile.DatabaseName);
        _databaseCombo.Text = profile.DatabaseName;
        _authModeCombo.SelectedIndex = profile.AuthenticationMode == SqlAuthenticationMode.SqlAuthentication ? 1 : 0;
        _userNameBox.Text = profile.UserName ?? string.Empty;
        _passwordBox.Text = string.Empty;
        _encryptConnectionCheck.Checked = profile.EncryptConnection;
        _trustServerCertificateCheck.Checked = profile.TrustServerCertificate;
        _permissionCombo.SelectedItem = profile.PermissionLevel.ToString();
        _timeoutNumeric.Value = Math.Clamp(profile.CommandTimeoutSeconds, 1, 600);
        _maxRowsNumeric.Value = Math.Clamp(profile.MaximumRows, 1, 30000);
        _enabledCheck.Checked = profile.IsEnabled;
        UpdateAuthFields();
    }

    private void EnsureDatabaseItem(string databaseName)
    {
        foreach (var item in _databaseCombo.Items)
        {
            if (item is SqlDatabaseInfo info &&
                info.Name.Equals(databaseName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        _databaseCombo.Items.Add(new SqlDatabaseInfo(
            databaseName,
            SqlServerDiscoveryService.SuggestBridgeProfileName(databaseName)));
    }

    private void DuplicateSelectedProfile()
    {
        LoadSelectedProfile();
        _selectedProfileId = null;
        _existingEncryptedPassword = null;
        _profileNameBox.Text += " Copy";
    }

    private void DeleteSelectedProfile()
    {
        if (_selectedProfileId is null)
        {
            MessageBox.Show(this, "اختر ملفاً أولاً.", "حذف", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this, "حذف الملف المحدد؟", "حذف", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) !=
            DialogResult.Yes)
        {
            return;
        }

        _profileStore.Delete(_selectedProfileId.Value);
        ReloadProfiles();
        ClearEditor(keepServers: true);
    }

    private void SaveProfile()
    {
        try
        {
            var profile = BuildProfileFromEditor();
            _profileStore.Save(profile);
            ReloadProfiles();
            _passwordBox.Clear();
            _existingEncryptedPassword = profile.EncryptedPassword;
            _selectedProfileId = profile.Id;
            MessageBox.Show(this, "تم الحفظ.", "حفظ", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, SensitiveDataSanitizer.Sanitize(ex.Message), "حفظ", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            var profile = BuildProfileFromEditor();
            var result = await _connectionTester.TestAsync(profile, CancellationToken.None);
            MessageBox.Show(
                this,
                result.Success
                    ? $"تم الاتصال بنجاح.\n{result.ServerName} / {result.DatabaseName}\n{result.LoginName}"
                    : $"{result.Message}\n{result.Details}",
                "اختبار الاتصال",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, SensitiveDataSanitizer.Sanitize(ex.Message), "اختبار الاتصال",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private DatabaseProfile BuildProfileFromEditor()
    {
        var dataSource = ResolveSelectedDataSource();
        var databaseName = _databaseCombo.SelectedItem is SqlDatabaseInfo info
            ? info.Name
            : _databaseCombo.Text.Trim();

        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new InvalidOperationException("اختر السيرفر.");
        }

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("اختر القاعدة.");
        }

        if (string.IsNullOrWhiteSpace(_profileNameBox.Text))
        {
            _profileNameBox.Text = SqlServerDiscoveryService.SuggestBridgeProfileName(databaseName);
        }

        var isSqlAuth = _authModeCombo.SelectedIndex == 1;
        var encryptedPassword = _existingEncryptedPassword;

        if (isSqlAuth && !string.IsNullOrEmpty(_passwordBox.Text))
        {
            encryptedPassword = _secretProtector.Protect(_passwordBox.Text);
        }
        else if (isSqlAuth && string.IsNullOrWhiteSpace(encryptedPassword))
        {
            throw new InvalidOperationException("مصادقة SQL تحتاج كلمة مرور عند أول حفظ.");
        }

        if (_permissionCombo.SelectedItem?.ToString() == "FullAccess" &&
            MessageBox.Show(
                this,
                "FullAccess يسمح بأوامر حساسة. المتابعة؟",
                "تحذير الصلاحية",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            throw new InvalidOperationException("تم إلغاء الحفظ.");
        }

        Enum.TryParse<SqlPermissionLevel>(_permissionCombo.SelectedItem?.ToString(), out var permissionLevel);

        return new DatabaseProfile
        {
            Id = _selectedProfileId ?? Guid.Empty,
            ProfileName = _profileNameBox.Text.Trim(),
            ServerName = SqlConnectionStringBuilderService.NormalizeDataSource(dataSource),
            DatabaseName = databaseName,
            AuthenticationMode = isSqlAuth
                ? SqlAuthenticationMode.SqlAuthentication
                : SqlAuthenticationMode.WindowsAuthentication,
            UserName = isSqlAuth ? _userNameBox.Text.Trim() : null,
            EncryptedPassword = isSqlAuth ? encryptedPassword : null,
            EncryptConnection = _encryptConnectionCheck.Checked,
            TrustServerCertificate = _trustServerCertificateCheck.Checked,
            PermissionLevel = permissionLevel == 0 ? SqlPermissionLevel.ReadOnly : permissionLevel,
            CommandTimeoutSeconds = (int)_timeoutNumeric.Value,
            MaximumRows = (int)_maxRowsNumeric.Value,
            IsEnabled = _enabledCheck.Checked,
        };
    }

    private void UpdateAuthFields()
    {
        var isSqlAuth = _authModeCombo.SelectedIndex == 1;
        _userNameBox.Enabled = isSqlAuth;
        _passwordBox.Enabled = isSqlAuth;
    }
}
