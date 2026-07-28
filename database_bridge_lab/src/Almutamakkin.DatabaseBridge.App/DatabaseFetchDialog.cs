using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Infrastructure;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.App;

public sealed class DatabaseFetchDialog : Form
{
    private readonly ISqlServerDiscovery _discovery;

    private readonly ComboBox _serverCombo = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 320 };
    private readonly Button _discoverServersButton = new() { Text = "اكتشاف السيرفرات", Width = 130 };
    private readonly Button _loadDatabasesButton = new() { Text = "جلب قواعد البيانات", Width = 150 };
    private readonly CheckedListBox _databasesList = new() { CheckOnClick = true, Width = 520, Height = 280 };
    private readonly Label _statusLabel = new() { AutoSize = true, ForeColor = Color.DarkSlateGray };
    private bool _loading;

    public IReadOnlyList<DatabaseFetchSelection> SelectedDatabases { get; private set; } =
        Array.Empty<DatabaseFetchSelection>();

    public DatabaseFetchDialog(ISqlServerDiscovery discovery)
    {
        _discovery = discovery;
        InitializeComponent();
        DiscoverServers();
    }

    private void InitializeComponent()
    {
        Text = "جلب قواعد البيانات من الجهاز";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 470);
        Font = new Font("Segoe UI", 9F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Text =
                "يُجلب فقط ما هو موجود فعلاً على SQL Server المحلي. اختر القواعد التي تريد تسجيلها كملفات اتصال للجسر.",
        };

        var serverRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        serverRow.Controls.Add(_serverCombo);
        serverRow.Controls.Add(_discoverServersButton);
        serverRow.Controls.Add(_loadDatabasesButton);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
            FlowDirection = FlowDirection.RightToLeft,
        };
        var okButton = new Button { Text = "إضافة المحدد", DialogResult = DialogResult.OK, Width = 120 };
        var cancelButton = new Button { Text = "إلغاء", DialogResult = DialogResult.Cancel, Width = 90 };
        buttons.Controls.AddRange([okButton, cancelButton]);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16),
            RowCount = 5,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(intro, 0, 0);
        layout.Controls.Add(serverRow, 0, 1);
        layout.Controls.Add(
            new Label { Text = "القواعد المكتشفة", AutoSize = true, Padding = new Padding(0, 8, 0, 4) },
            0,
            2);
        layout.Controls.Add(_databasesList, 0, 3);
        layout.Controls.Add(_statusLabel, 0, 4);

        Controls.Add(layout);
        Controls.Add(buttons);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        _discoverServersButton.Click += (_, _) => DiscoverServers();
        _loadDatabasesButton.Click += async (_, _) => await LoadDatabasesAsync();
        okButton.Click += (_, _) => OnConfirm();

        UITheme.ApplyTheme(this);
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
        if (_loading)
        {
            return;
        }

        _loading = true;
        _loadDatabasesButton.Enabled = false;
        _statusLabel.Text = "جاري جلب القواعد من السيرفر...";
        _statusLabel.ForeColor = Color.DarkSlateGray;

        try
        {
            var dataSource = ResolveSelectedDataSource();
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                MessageBox.Show(this, "اختر سيرفر أولاً.", "جلب القواعد", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var databases = await _discovery.ListDatabasesAsync(
                dataSource,
                SqlAuthenticationMode.WindowsAuthentication,
                userName: null,
                plainPassword: null,
                trustServerCertificate: true,
                encryptConnection: false,
                CancellationToken.None);

            _databasesList.Items.Clear();
            foreach (var database in databases)
            {
                var profileName = database.CompatibilityHint ??
                                  SqlServerDiscoveryService.SuggestBridgeProfileName(database.Name);
                var item = new DatabaseFetchListItem(database.Name, profileName);
                var index = _databasesList.Items.Add(item);
                if (IsPharmacyDatabase(database.Name))
                {
                    _databasesList.SetItemChecked(index, true);
                }
            }

            _statusLabel.Text = databases.Count == 0
                ? "لم يُعثر على قواعد متاحة على هذا السيرفر."
                : $"تم جلب {databases.Count} قاعدة من السيرفر (كل القواعد المتاحة).";
            _statusLabel.ForeColor = databases.Count == 0 ? Color.DarkOrange : Color.DarkGreen;
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
            _loading = false;
            _loadDatabasesButton.Enabled = true;
        }
    }

    private void OnConfirm()
    {
        var dataSource = ResolveSelectedDataSource();
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            MessageBox.Show(this, "اختر سيرفر أولاً.", "جلب القواعد", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                database.ProfileName));
        }

        if (selections.Count == 0)
        {
            MessageBox.Show(this, "اختر قاعدة واحدة على الأقل.", "جلب القواعد", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.None;
            return;
        }

        SelectedDatabases = selections;
    }

    private static bool IsPharmacyDatabase(string databaseName) =>
        string.Equals(databaseName, "InfinityRetailDB", StringComparison.OrdinalIgnoreCase) ||
        databaseName.StartsWith("Marketing", StringComparison.OrdinalIgnoreCase);

    private string ResolveSelectedDataSource()
    {
        if (_serverCombo.SelectedItem is SqlServerInstanceInfo instance)
        {
            return instance.DataSource;
        }

        return SqlConnectionStringBuilderService.NormalizeDataSource(_serverCombo.Text);
    }

    private sealed record DatabaseFetchListItem(string DatabaseName, string ProfileName)
    {
        public override string ToString() =>
            $"{DatabaseName}  →  ملف الجسر: {ProfileName}";
    }
}

public sealed record DatabaseFetchSelection(
    string ServerName,
    string DatabaseName,
    string ProfileName,
    SqlAuthenticationMode AuthenticationMode = SqlAuthenticationMode.WindowsAuthentication,
    string? UserName = null,
    string? PlainPassword = null);
