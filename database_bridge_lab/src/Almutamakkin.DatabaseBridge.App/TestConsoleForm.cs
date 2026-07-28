using System.Diagnostics;
using System.Text.Json;
using Almutamakkin.DatabaseBridge.Core;
using Almutamakkin.DatabaseBridge.Protocol;

namespace Almutamakkin.DatabaseBridge.App;

public sealed class TestConsoleForm : Form
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly IActiveRequestTracker _activeRequestTracker;

    private readonly ComboBox _sampleCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly TextBox _requestBox = new() { Multiline = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
    private readonly TextBox _responseBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 9F) };
    private readonly Label _durationLabel = new() { AutoSize = true, Text = "Duration: -" };
    private readonly Button _executeButton = new() { Text = "Execute", Width = 100 };
    private readonly Button _copyButton = new() { Text = "Copy Response", Width = 120 };
    private readonly Button _cancelButton = new() { Text = "Cancel", Width = 100 };
    private CancellationTokenSource? _executionCts;
    private string? _lastRequestId;

    public Action<string, string?>? ShowLastActivity { get; set; }

    public TestConsoleForm(ICommandDispatcher dispatcher, IActiveRequestTracker activeRequestTracker)
    {
        _dispatcher = dispatcher;
        _activeRequestTracker = activeRequestTracker;
        InitializeComponent();
        WireEvents();
        LoadSample(0);
    }

    private void InitializeComponent()
    {
        Text = "Test Console — وحدة الاختبار";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 620);
        Font = new Font("Segoe UI", 9F);

        foreach (var sample in SampleCommands.All)
        {
            _sampleCombo.Items.Add(sample.Name);
        }

        if (_sampleCombo.Items.Count > 0)
        {
            _sampleCombo.SelectedIndex = 0;
        }

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(12, 8, 12, 8),
        };
        topPanel.Controls.Add(new Label { Text = "Sample:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        topPanel.Controls.Add(_sampleCombo);
        topPanel.Controls.Add(_executeButton);
        topPanel.Controls.Add(_cancelButton);
        topPanel.Controls.Add(_copyButton);
        topPanel.Controls.Add(_durationLabel);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 280,
        };

        split.Panel1.Controls.Add(new GroupBox { Text = "Request JSON", Dock = DockStyle.Fill, Padding = new Padding(8), Controls = { _requestBox } });
        split.Panel2.Controls.Add(new GroupBox { Text = "Response JSON", Dock = DockStyle.Fill, Padding = new Padding(8), Controls = { _responseBox } });

        Controls.Add(split);
        Controls.Add(topPanel);
        UITheme.ApplyTheme(this);
    }

    private void WireEvents()
    {
        _sampleCombo.SelectedIndexChanged += (_, _) => LoadSample(_sampleCombo.SelectedIndex);
        _executeButton.Click += async (_, _) => await ExecuteAsync();
        _copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_responseBox.Text))
            {
                Clipboard.SetText(_responseBox.Text);
            }
        };
        _cancelButton.Click += (_, _) => CancelCurrentExecution();
    }

    private void LoadSample(int index)
    {
        if (index < 0 || index >= SampleCommands.All.Count)
        {
            return;
        }

        _requestBox.Text = SampleCommands.All[index].Json;
    }

    private async Task ExecuteAsync()
    {
        _executeButton.Enabled = false;
        _cancelButton.Enabled = true;
        _executionCts = new CancellationTokenSource();
        _responseBox.Clear();
        _durationLabel.Text = "Duration: running...";

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var command = BridgeJson.DeserializeCommand(_requestBox.Text);
            if (command is null)
            {
                _responseBox.Text = "Invalid request JSON.";
                return;
            }

            _lastRequestId = command.RequestId;
            var response = await _dispatcher.DispatchAsync(command, _executionCts.Token);
            stopwatch.Stop();

            _responseBox.Text = JsonSerializer.Serialize(response, BridgeJson.Options);
            _durationLabel.Text = $"Duration: {stopwatch.ElapsedMilliseconds} ms";

            ShowLastActivity?.Invoke(
                $"{command.MessageType} / {command.RequestId} ({stopwatch.ElapsedMilliseconds} ms)",
                response.Success ? null : response.Error?.Message);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _responseBox.Text = "Execution cancelled.";
            _durationLabel.Text = $"Duration: {stopwatch.ElapsedMilliseconds} ms (cancelled)";
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _responseBox.Text = ex.ToString();
            _durationLabel.Text = $"Duration: {stopwatch.ElapsedMilliseconds} ms (error)";
            ShowLastActivity?.Invoke("Test console error", ex.Message);
        }
        finally
        {
            _executeButton.Enabled = true;
            _cancelButton.Enabled = false;
            _executionCts?.Dispose();
            _executionCts = null;
        }
    }

    private void CancelCurrentExecution()
    {
        _executionCts?.Cancel();

        if (!string.IsNullOrWhiteSpace(_lastRequestId))
        {
            _activeRequestTracker.TryCancel(_lastRequestId);
        }
    }
}
