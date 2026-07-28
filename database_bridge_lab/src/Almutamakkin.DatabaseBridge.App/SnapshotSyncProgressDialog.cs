using Almutamakkin.DatabaseBridge.Infrastructure.Snapshots;

namespace Almutamakkin.DatabaseBridge.App;

public sealed class SnapshotSyncProgressDialog : Form
{
    private readonly Func<IProgress<SnapshotSyncProgress>, CancellationToken, Task<IReadOnlyList<SnapshotJobResult>>> _run;
    private readonly Panel _rowsHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly ProgressBar _overallBar = new()
    {
        Dock = DockStyle.Top,
        Height = 18,
        Style = ProgressBarStyle.Continuous,
        Minimum = 0,
    };
    private readonly Label _summaryLabel = new()
    {
        Dock = DockStyle.Top,
        Height = 36,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
    };
    private readonly Button _closeButton = new()
    {
        Text = "إغلاق",
        Width = 120,
        Height = 36,
        Enabled = false,
        DialogResult = DialogResult.OK,
    };
    private readonly System.Windows.Forms.Timer _tickTimer = new() { Interval = 250 };

    private readonly Dictionary<string, SnapshotRowUi> _rows = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeSnapshotType;
    private DateTime _activeStartedUtc;
    private int _activeEstimatedSeconds;
    private int _completedCount;
    private int _totalCount;
    private bool _started;

    public IReadOnlyList<SnapshotJobResult> Results { get; private set; } = Array.Empty<SnapshotJobResult>();

    public SnapshotSyncProgressDialog(
        Func<IProgress<SnapshotSyncProgress>, CancellationToken, Task<IReadOnlyList<SnapshotJobResult>>> run)
    {
        _run = run;
        InitializeComponent();
        Shown += OnShownAsync;
        FormClosing += OnFormClosing;
        _tickTimer.Tick += (_, _) => RefreshActiveRemaining();
        _closeButton.Click += (_, _) => Close();
    }

    private void InitializeComponent()
    {
        Text = "تقدم مزامنة اللقطات";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 480);
        Font = new Font("Segoe UI", 9.5F);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        BackColor = UITheme.BackgroundColor;
        ForeColor = UITheme.TextColor;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttons.Controls.Add(_closeButton);

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(12, 10, 12, 0),
            Text = "مزامنة لقطات أبوغريس من القاعدة البعيدة",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
        };

        var barHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(12, 8, 12, 8),
        };
        _overallBar.Dock = DockStyle.Fill;
        barHost.Controls.Add(_overallBar);

        _summaryLabel.Padding = new Padding(12, 0, 12, 0);
        _summaryLabel.Text = "جاري التحضير…";
        _summaryLabel.ForeColor = UITheme.TextMuted;

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
        _rowsHost.BackColor = UITheme.PanelColor;
        _rowsHost.BorderStyle = BorderStyle.FixedSingle;
        body.Controls.Add(_rowsHost);

        Controls.Add(body);
        Controls.Add(_summaryLabel);
        Controls.Add(barHost);
        Controls.Add(header);
        Controls.Add(buttons);

        AcceptButton = _closeButton;
        CancelButton = _closeButton;
    }

    private async void OnShownAsync(object? sender, EventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        BuildRows(ActivitySnapshotSyncService.MarketingFirstWaveJobs);
        _tickTimer.Start();

        var progress = new Progress<SnapshotSyncProgress>(ApplyProgress);
        try
        {
            Results = await _run(progress, CancellationToken.None).ConfigureAwait(true);
            var anyFailed = Results.Any(result => !result.Success);
            _summaryLabel.ForeColor = anyFailed ? UITheme.ErrorColor : UITheme.SuccessColor;
            _summaryLabel.Text = anyFailed
                ? "اكتملت المزامنة مع أخطاء — راجع الصفوف أدناه."
                : "اكتملت مزامنة كل اللقطات بنجاح.";
            if (_overallBar.Maximum > 0)
            {
                _overallBar.Value = _overallBar.Maximum;
            }
        }
        catch (Exception ex)
        {
            _summaryLabel.ForeColor = UITheme.ErrorColor;
            _summaryLabel.Text = ex.Message;
        }
        finally
        {
            _activeSnapshotType = null;
            _tickTimer.Stop();
            _closeButton.Enabled = true;
            _closeButton.Focus();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_closeButton.Enabled)
        {
            e.Cancel = true;
        }
    }

    private void ApplyProgress(SnapshotSyncProgress update)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyProgress(update));
            return;
        }

        switch (update.Phase)
        {
            case SnapshotSyncPhase.Planned when update.Jobs is not null:
                BuildRows(update.Jobs);
                break;

            case SnapshotSyncPhase.Started when update.SnapshotType is not null:
                _activeSnapshotType = update.SnapshotType;
                _activeStartedUtc = DateTime.UtcNow;
                _activeEstimatedSeconds = Math.Max(1, update.EstimatedSeconds);
                if (_rows.TryGetValue(update.SnapshotType, out var running))
                {
                    running.MarkRunning(update.DisplayName ?? update.SnapshotType);
                }

                _summaryLabel.ForeColor = UITheme.AccentColor;
                _summaryLabel.Text = $"جاري: {update.DisplayName ?? update.SnapshotType}";
                RefreshActiveRemaining();
                break;

            case SnapshotSyncPhase.Completed when update.SnapshotType is not null:
                if (_rows.TryGetValue(update.SnapshotType, out var done))
                {
                    done.MarkCompleted(
                        update.Success,
                        update.RowCount,
                        update.Message,
                        update.Elapsed ?? TimeSpan.Zero);
                }

                if (string.Equals(_activeSnapshotType, update.SnapshotType, StringComparison.OrdinalIgnoreCase))
                {
                    _activeSnapshotType = null;
                }

                _completedCount = Math.Min(_totalCount, _completedCount + 1);
                if (_overallBar.Maximum > 0)
                {
                    _overallBar.Value = Math.Min(_overallBar.Maximum, _completedCount);
                }

                break;

            case SnapshotSyncPhase.WaveCompleted:
                _summaryLabel.ForeColor = update.Success ? UITheme.SuccessColor : UITheme.ErrorColor;
                if (!string.IsNullOrWhiteSpace(update.Message))
                {
                    _summaryLabel.Text = update.Message;
                }

                break;
        }
    }

    private void BuildRows(IReadOnlyList<SnapshotSyncJobPlan> jobs)
    {
        _rowsHost.SuspendLayout();
        _rowsHost.Controls.Clear();
        _rows.Clear();
        _completedCount = 0;
        _totalCount = jobs.Count;
        _overallBar.Maximum = Math.Max(1, jobs.Count);
        _overallBar.Value = 0;

        // Bottom-up docking so first job appears at top.
        for (var i = jobs.Count - 1; i >= 0; i--)
        {
            var job = jobs[i];
            var row = new SnapshotRowUi(job);
            _rows[job.SnapshotType] = row;
            row.Panel.Dock = DockStyle.Top;
            _rowsHost.Controls.Add(row.Panel);
        }

        _rowsHost.ResumeLayout();
        _summaryLabel.Text = $"بانتظار المزامنة — {jobs.Count} لقطات";
    }

    private void RefreshActiveRemaining()
    {
        if (_activeSnapshotType is null ||
            !_rows.TryGetValue(_activeSnapshotType, out var row))
        {
            return;
        }

        var elapsed = DateTime.UtcNow - _activeStartedUtc;
        var remaining = TimeSpan.FromSeconds(_activeEstimatedSeconds) - elapsed;
        row.UpdateRemaining(elapsed, remaining);
    }

    private sealed class SnapshotRowUi
    {
        private readonly Label _statusLabel;
        private readonly Label _nameLabel;
        private readonly Label _timeLabel;
        private readonly string _displayName;

        public Panel Panel { get; }

        public SnapshotRowUi(SnapshotSyncJobPlan job)
        {
            _displayName = job.DisplayName;
            Panel = new Panel
            {
                Height = 44,
                Padding = new Padding(10, 6, 10, 6),
                BackColor = UITheme.PanelColor,
            };

            _statusLabel = new Label
            {
                AutoSize = false,
                Width = 36,
                Dock = DockStyle.Right,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                Text = "○",
                ForeColor = UITheme.TextMuted,
            };
            _nameLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Text = job.DisplayName,
            };
            _timeLabel = new Label
            {
                AutoSize = false,
                Width = 200,
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F),
                ForeColor = UITheme.TextMuted,
                Text = $"تقدير ~{FormatSeconds(job.EstimatedSeconds)}",
            };

            var divider = new Panel
            {
                Height = 1,
                Dock = DockStyle.Bottom,
                BackColor = UITheme.BorderColor,
            };

            Panel.Controls.Add(_nameLabel);
            Panel.Controls.Add(_timeLabel);
            Panel.Controls.Add(_statusLabel);
            Panel.Controls.Add(divider);
        }

        public void MarkRunning(string displayName)
        {
            _statusLabel.Text = "…";
            _statusLabel.ForeColor = UITheme.AccentColor;
            _nameLabel.Text = displayName;
            _nameLabel.ForeColor = UITheme.AccentColor;
            _timeLabel.ForeColor = UITheme.AccentColor;
            _timeLabel.Text = "جاري المزامنة…";
        }

        public void UpdateRemaining(TimeSpan elapsed, TimeSpan remaining)
        {
            if (remaining > TimeSpan.Zero)
            {
                _timeLabel.Text =
                    $"متبقي ~{FormatSeconds((int)Math.Ceiling(remaining.TotalSeconds))} · مرّ {FormatSeconds((int)elapsed.TotalSeconds)}";
            }
            else
            {
                var over = (int)Math.Ceiling((-remaining).TotalSeconds);
                _timeLabel.Text =
                    over <= 0
                        ? $"جاري… · مرّ {FormatSeconds((int)elapsed.TotalSeconds)}"
                        : $"تجاوز التقدير (+{FormatSeconds(over)}) · مرّ {FormatSeconds((int)elapsed.TotalSeconds)}";
            }
        }

        public void MarkCompleted(bool success, int rowCount, string? message, TimeSpan elapsed)
        {
            if (success)
            {
                _statusLabel.Text = "✓";
                _statusLabel.ForeColor = UITheme.SuccessColor;
                _nameLabel.ForeColor = UITheme.SuccessColor;
                _timeLabel.ForeColor = UITheme.SuccessColor;
                _timeLabel.Text = $"تم ({rowCount} صف) · {FormatSeconds((int)Math.Ceiling(elapsed.TotalSeconds))}";
            }
            else
            {
                _statusLabel.Text = "✗";
                _statusLabel.ForeColor = UITheme.ErrorColor;
                _nameLabel.ForeColor = UITheme.ErrorColor;
                _timeLabel.ForeColor = UITheme.ErrorColor;
                var detail = string.IsNullOrWhiteSpace(message) ? "فشلت" : TrimMessage(message!);
                _timeLabel.Text = $"{detail} · {FormatSeconds((int)Math.Ceiling(elapsed.TotalSeconds))}";
            }

            _nameLabel.Text = _displayName;
        }

        private static string TrimMessage(string message)
        {
            var oneLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return oneLine.Length <= 42 ? oneLine : oneLine[..39] + "…";
        }

        private static string FormatSeconds(int totalSeconds)
        {
            totalSeconds = Math.Max(0, totalSeconds);
            if (totalSeconds < 60)
            {
                return $"{totalSeconds} ث";
            }

            var minutes = totalSeconds / 60;
            var seconds = totalSeconds % 60;
            return seconds == 0 ? $"{minutes} د" : $"{minutes} د {seconds} ث";
        }
    }
}
