using System.Diagnostics;
using System.Drawing.Drawing2D;
using ImmichUploaderApp.Models;
using ImmichUploaderApp.Services;

namespace ImmichUploaderApp;

public sealed class ActivityPanelForm : Form
{
    private readonly UploadWatcherService _watcher;
    private readonly PhotoSyncService _photoSync;
    private readonly AppConfig _config;
    private readonly Action _openSettings;
    private readonly Palette _palette;

    private Label _statusLabel = null!;
    private Label _downloadStatusLabel = null!, _uploadSummaryLabel = null!, _downloadSummaryLabel = null!;
    private FlatProgressBar _downloadProgressBar = null!;
    private Label _failureLabel = null!;
    private FlatProgressBar _uploadProgressBar = null!;
    private DoubleBufferedFlowLayoutPanel _recentList = null!;
    private Label _storageLabel = null!;
    private FlatProgressBar _storageProgressBar = null!;
    private IReadOnlyList<RecentUpload> _lastUploads = Array.Empty<RecentUpload>();
    private IReadOnlyList<RecentDownload> _lastDownloads = Array.Empty<RecentDownload>();
    private IReadOnlyList<RecentDeletion> _lastDeletions = Array.Empty<RecentDeletion>();

    private Label _activityTitle = null!, _uploadBadge = null!, _downloadBadge = null!;
    private Button _pauseButton = null!;
    private readonly List<Button> _navigation = new();
    private int _activityFilter;
    private bool _rendering;

    public ActivityPanelForm(UploadWatcherService watcher, PhotoSyncService photoSync, AppConfig config, Action openSettings)
    {
        _watcher = watcher;
        _photoSync = photoSync;
        _config = config;
        _openSettings = openSettings;
        _palette = ThemeService.Resolve(config.Theme);

        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9.5f);
        Text = Loc.T("app.name");
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = false;
        MinimumSize = new Size(840, 620);
        var workArea = Screen.FromControl(this).WorkingArea;
        ClientSize = new Size(Math.Min(1080, workArea.Width - 60), Math.Min(760, workArea.Height - 80));
        KeyPreview = true;
        BackColor = _palette.Background;

        BuildLayout();
        HandleCreated += (_, _) => ThemeService.ApplyTitleBarTheme(this, _palette.IsDark);

        _watcher.ActivityChanged += OnWatcherActivityChanged;
        _photoSync.ActivityChanged += OnPhotoSyncActivityChanged;
        Load += OnLoadAsync;
        FormClosed += (_, _) =>
        {
            _watcher.ActivityChanged -= OnWatcherActivityChanged;
            _photoSync.ActivityChanged -= OnPhotoSyncActivityChanged;
            _rendering = true;
            ClearActivityRows();
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Button MakeButton(string text, Action action, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(108, 38), Padding = new Padding(12, 5, 12, 5),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? _palette.Accent : _palette.ControlBackground,
            ForeColor = primary ? (_palette.IsDark ? _palette.Background : Color.White) : _palette.Text,
            Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0), AccessibleName = text,
        };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = _palette.ControlBorder;
        button.Click += (_, _) => action();
        return button;
    }

    private Label MakeText(string text, float size = 9.5f, bool bold = false, bool muted = false) => new()
    {
        Text = text, AutoSize = true, Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = muted ? _palette.TextMuted : _palette.Text, Margin = new Padding(0, 0, 0, 8),
    };

    private void BuildLayout()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), BackColor = _palette.Background };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(shell);

        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(20, 28, 16, 20), Margin = new Padding(0), BackColor = _palette.ControlBackground };
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        shell.Controls.Add(sidebar, 0, 0);
        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        brand.Controls.Add(MakeText("immich", 25, true), 0, 0);
        brand.Controls.Add(MakeText("UPLOADER  /  " + UpdateService.CurrentVersion.ToString(3), 8.5f, muted: true), 0, 1);
        sidebar.Controls.Add(brand, 0, 0);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        var titles = new[] { Loc.T("design.overview"), Loc.T("design.uploads"), Loc.T("design.downloads"), Loc.T("design.deletions") };
        for (var i = 0; i < titles.Length; i++)
        {
            var index = i;
            var button = MakeButton(titles[i], () => SelectActivity(index));
            button.AutoSize = false; button.Size = new Size(180, 44); button.TextAlign = ContentAlignment.MiddleLeft;
            button.FlatAppearance.BorderSize = 0; button.Margin = new Padding(0, 0, 0, 6);
            _navigation.Add(button); nav.Controls.Add(button);
        }
        sidebar.Controls.Add(nav, 0, 1);
        var links = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 22) };
        links.Controls.Add(MakeButton(Loc.T("tray.openServer"), () => OnOpenServerClicked(this, EventArgs.Empty)));
        links.Controls.Add(MakeButton(Loc.T("panel.settingsButton"), () => _openSettings()));
        sidebar.Controls.Add(links, 0, 3);
        var storage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        storage.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        storage.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        storage.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        storage.Controls.Add(MakeText(Loc.T("panel.storageHeader"), 8.5f, true, true), 0, 0);
        _storageProgressBar = new FlatProgressBar { Dock = DockStyle.Top, Height = 6, TrackColor = _palette.Track, FillColor = _palette.Accent, Margin = new Padding(0) };
        _storageLabel = MakeText(Loc.T("panel.loading"), 9, muted: true);
        _storageLabel.AutoSize = false;
        storage.Controls.Add(_storageProgressBar, 0, 1); storage.Controls.Add(_storageLabel, 0, 2);
        sidebar.Controls.Add(storage, 0, 4);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(28, 28, 28, 20), Margin = new Padding(0) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 222));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(content, 1, 0);
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        heading.Controls.Add(MakeText(Loc.T("design.overview"), 23, true), 0, 0);
        var server = Uri.TryCreate(_config.ServerUrl, UriKind.Absolute, out var uri) ? uri.Host : Loc.T("design.noServer");
        heading.Controls.Add(MakeText(Loc.T("design.subtitle", server), 9.5f, muted: true), 0, 1);
        content.Controls.Add(heading, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        actions.Controls.Add(MakeButton(Loc.T("activity.check"), () => { _watcher.ScanNow(); _photoSync.ScanNow(); }, true));
        _pauseButton = MakeButton(Loc.T("tray.pause"), () => { if (_watcher.IsPaused) _watcher.Resume(); else _watcher.Pause(); });
        actions.Controls.Add(_pauseButton);
        content.Controls.Add(actions, 0, 1);
        var transfers = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 16) };
        transfers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); transfers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        transfers.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        transfers.Controls.Add(BuildTransferCard(true), 0, 0); transfers.Controls.Add(BuildTransferCard(false), 1, 0);
        content.Controls.Add(transfers, 0, 2);
        _failureLabel = new Label { Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(650, 0), ForeColor = _palette.IsDark ? Color.Salmon : Color.Firebrick, Visible = false, Margin = new Padding(0, 0, 0, 10) };
        content.Controls.Add(_failureLabel, 0, 3);
        var history = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        history.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        history.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); history.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _activityTitle = MakeText(Loc.T("design.activity"), 13, true);
        history.Controls.Add(_activityTitle, 0, 0);
        _recentList = new DoubleBufferedFlowLayoutPanel(_palette.IsDark) { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = _palette.Background, Margin = new Padding(0) };
        _recentList.SizeChanged += (_, _) => { if (IsHandleCreated) RenderActivity(); };
        history.Controls.Add(_recentList, 0, 1);
        content.Controls.Add(history, 0, 4);
        SelectActivity(0);
    }

    private Control BuildTransferCard(bool upload)
    {
        var surface = new SurfacePanel(_palette) { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = upload ? new Padding(0, 0, 8, 0) : new Padding(8, 0, 0, 0) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = _palette.ControlBackground, Margin = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 18)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(MakeText(upload ? Loc.T("activity.upload") : Loc.T("activity.download"), 9.5f, true), 0, 0);
        var badge = MakeText(Loc.T("design.waiting"), 8.5f, true, true); header.Controls.Add(badge, 1, 0);
        var status = MakeText(Loc.T("activity.waiting"), 11, true); status.AutoSize = false; status.AutoEllipsis = true;
        var bar = new FlatProgressBar { Dock = DockStyle.Top, Height = 7, TrackColor = _palette.Track, FillColor = upload ? _palette.Accent : (_palette.IsDark ? Color.FromArgb(85, 211, 181) : Color.FromArgb(20, 151, 122)), Margin = new Padding(0, 4, 0, 4) };
        var summary = MakeText("", 8.5f, muted: true); summary.AutoSize = false; summary.AutoEllipsis = true;
        table.Controls.Add(header, 0, 0); table.Controls.Add(status, 0, 1); table.Controls.Add(bar, 0, 2); table.Controls.Add(summary, 0, 3);
        surface.Controls.Add(table);
        if (upload) { _statusLabel = status; _uploadProgressBar = bar; _uploadSummaryLabel = summary; _uploadBadge = badge; }
        else { _downloadStatusLabel = status; _downloadProgressBar = bar; _downloadSummaryLabel = summary; _downloadBadge = badge; }
        return surface;
    }

    private void SelectActivity(int filter)
    {
        _activityFilter = filter;
        for (var i = 0; i < _navigation.Count; i++)
        {
            _navigation[i].BackColor = i == filter ? _palette.Track : _palette.ControlBackground;
            _navigation[i].ForeColor = i == filter ? _palette.Accent : _palette.TextMuted;
        }
        _activityTitle.Text = Loc.T(filter switch { 1 => "design.uploads", 2 => "design.downloads", 3 => "design.deletions", _ => "design.activity" });
        RenderActivity();
    }

    private sealed class SurfacePanel : Panel
    {
        private readonly Palette _colors;
        public SurfacePanel(Palette colors) { _colors = colors; BackColor = colors.Background; DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 14);
            using var brush = new SolidBrush(_colors.ControlBackground); e.Graphics.FillPath(brush, path);
            using var pen = new Pen(_colors.Border); e.Graphics.DrawPath(pen, path);
        }
    }

    private void OnOpenServerClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_config.ServerUrl)) return;

        // ServerUrl is the API base (".../api"); the web UI lives at the same host without it.
        var url = _config.ServerUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) url = url[..^4];

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Log($"VAROITUS: palvelimen avaaminen epaonnistui: {ex.Message}");
        }
    }

    private void OnWatcherActivityChanged(WatcherActivitySnapshot snapshot)
    {
        RunOnUiThread(() =>
        {
            if (IsDisposed) return;

            var state = snapshot.CurrentFileName is { } file ? Loc.T("status.uploading", file) + (snapshot.CurrentFileProgressPercent is { } value ? $" ({value:0}%)" : "")
                : _watcher.IsPaused ? Loc.T("status.paused")
                : !_watcher.IsRunning ? snapshot.StatusText
                : snapshot.IsScanning ? Loc.T("activity.scanning", snapshot.FilesChecked)
                : snapshot.ScanError is { } error ? Loc.T("activity.error", error)
                : snapshot.QueueCount > 0 ? Loc.T("status.queued", snapshot.QueueCount)
                : snapshot.LastScanAtLocal is null ? Loc.T("activity.waiting") : Loc.T("activity.idle");
            _statusLabel.Text = state;
            _uploadBadge.Text = Loc.T(!_watcher.IsRunning ? "status.stopped" : snapshot.CurrentFileName is not null ? "design.active" : _watcher.IsPaused ? "design.paused" : snapshot.ScanError is not null ? "design.attention" : snapshot.IsScanning ? "design.checking" : snapshot.LastScanAtLocal is not null ? "design.ready" : "design.waiting");
            _uploadBadge.ForeColor = snapshot.ScanError is not null ? Color.Coral : _palette.Accent;
            _pauseButton.Text = Loc.T(_watcher.IsPaused ? "tray.resume" : "tray.pause");
            _pauseButton.Enabled = _watcher.IsRunning;
            _uploadSummaryLabel.Text = Loc.T("activity.queue", ScanSummary(snapshot.FilesChecked, snapshot.LastScanAtLocal), snapshot.QueueCount) + (snapshot.ScanError is { } scanError && snapshot.IsScanning ? "\n" + Loc.T("activity.error", scanError) : "");
            var failure = snapshot.RecentFailures.FirstOrDefault();
            _failureLabel.Visible = failure is not null;
            _failureLabel.Text = failure is null ? string.Empty : $"{failure.FileName}: {failure.Message}";

            if (snapshot.CurrentFileProgressPercent is { } percent)
            {
                _uploadProgressBar.Visible = true;
                _uploadProgressBar.Value = Math.Clamp((int)Math.Round(percent), 0, 100);
            }
            else
            {
                _uploadProgressBar.Visible = false;
            }

            if (!_lastUploads.SequenceEqual(snapshot.RecentUploads)) { _lastUploads = snapshot.RecentUploads; RenderActivity(); }
        });
    }

    private void OnPhotoSyncActivityChanged(PhotoSyncActivitySnapshot snapshot)
    {
        RunOnUiThread(() =>
        {
            if (IsDisposed) return;
            var state = !_config.SyncEnabled ? Loc.T("activity.disabled")
                : !_photoSync.IsRunning ? snapshot.StatusText
                : snapshot.CurrentFileName is { } file ? file + (snapshot.ProgressPercent is { } percent ? $" ({percent:0}%)" : $" ({FormatBytes(snapshot.BytesTransferred)})")
                : snapshot.IsScanning ? Loc.T("activity.scanning", snapshot.FilesChecked)
                : snapshot.ScanError is { } error ? Loc.T("activity.error", error)
                : snapshot.LastScanAtLocal is null ? Loc.T("activity.waiting") : Loc.T("activity.idle");
            _downloadStatusLabel.Text = state;
            _downloadBadge.Text = Loc.T(!_config.SyncEnabled ? "activity.disabled" : !_photoSync.IsRunning ? "status.stopped" : snapshot.CurrentFileName is not null ? "design.active" : snapshot.ScanError is not null ? "design.attention" : snapshot.IsScanning ? "design.checking" : snapshot.LastScanAtLocal is not null ? "design.ready" : "design.waiting");
            _downloadBadge.ForeColor = snapshot.ScanError is not null ? Color.Coral : _palette.Accent;
            _downloadSummaryLabel.Text = ScanSummary(snapshot.FilesChecked, snapshot.LastScanAtLocal) + "\n" + Loc.T("activity.received", snapshot.FilesTransferred) + (snapshot.ScanError is { } scanError && snapshot.IsScanning ? "\n" + Loc.T("activity.error", scanError) : "");
            _downloadProgressBar.Visible = snapshot.ProgressPercent is not null;
            _downloadProgressBar.Value = snapshot.ProgressPercent is { } progress ? (int)Math.Round(progress) : 0;
            if (!_lastDownloads.SequenceEqual(snapshot.RecentDownloads) || !_lastDeletions.SequenceEqual(snapshot.RecentDeletions))
            {
                _lastDownloads = snapshot.RecentDownloads;
                _lastDeletions = snapshot.RecentDeletions;
                RenderActivity();
            }
        });
    }

    private static string ScanSummary(int count, DateTime? at) => at is { } time ? Loc.T("activity.summary", count, time) : Loc.T("activity.scanning", count);

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { /* Form closed between the checks. */ }
        }
        else
        {
            action();
        }
    }

    private const int ThumbnailBoxSize = 60;
    private int Scale(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96d);

    private sealed record TimelineItem(string Name, DateTime At, long Size, byte[]? Thumbnail, string? Path, int Kind, string? Reason = null);

    private void RenderActivity()
    {
        if (_rendering) return;
        _rendering = true;
        var scroll = _recentList.AutoScrollPosition;
        _recentList.SuspendLayout();
        try
        {
            ClearActivityRows();
            var rowWidth = Math.Max(200, _recentList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            var items = _lastUploads.Select(u => new TimelineItem(u.FileName, u.UploadedAtLocal, u.SizeBytes, u.ThumbnailPng, u.FullPath, 1))
                .Concat(_lastDownloads.Select(d => new TimelineItem(d.FileName, d.DownloadedAtLocal, d.SizeBytes, d.ThumbnailPng, d.FullPath, 2)))
                .Concat(_lastDeletions.Select(d => new TimelineItem(d.FileName, d.DeletedAtLocal, 0, null, null, 3, d.Reason)))
                .Where(item => _activityFilter == 0 || item.Kind == _activityFilter).OrderByDescending(item => item.At).ToArray();
            if (items.Length == 0)
            {
                var empty = new SurfacePanel(_palette) { Width = rowWidth, Height = Scale(170), Margin = new Padding(0, 4, 0, 0), Padding = new Padding(24) };
                var title = new Label { Text = Loc.T("design.emptyTitle"), Dock = DockStyle.Top, Height = 36, ForeColor = _palette.Text, BackColor = _palette.ControlBackground, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
                var hint = new Label { Text = Loc.T("design.emptyHint"), Dock = DockStyle.Fill, ForeColor = _palette.TextMuted, BackColor = _palette.ControlBackground, Padding = new Padding(0, 16, 0, 0) };
                empty.Controls.Add(hint); empty.Controls.Add(title); _recentList.Controls.Add(empty);
            }
            foreach (var item in items)
            {
                var row = item.Kind == 3 ? BuildDeletionRow(item.Name, item.Reason!, item.At, rowWidth) : BuildActivityRow(item.Name, item.At, item.Thumbnail, item.Path, rowWidth);
                row.AccessibleName = item.Name;
                row.AccessibleDescription = item.Path ?? item.Reason;
                if (item.Kind != 3)
                {
                    var metadata = row.Controls.OfType<Label>().Last();
                    metadata.Text = Loc.T(item.Kind == 1 ? "design.sent" : "design.received") + "   ·   " + FormatBytes(item.Size) + "   ·   " + FormatRelativeTime(item.At);
                }
                _recentList.Controls.Add(row);
            }
            _activityTitle.Text = Loc.T(_activityFilter switch { 1 => "design.uploads", 2 => "design.downloads", 3 => "design.deletions", _ => "design.activity" }) + $"  ·  {items.Length}";
        }
        finally
        {
            _recentList.ResumeLayout();
            _recentList.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
            _rendering = false;
        }
    }

    /// PictureBox doesn't dispose an Image assigned via its Image property when the control
    /// itself is disposed (Controls.Clear() does not dispose controls or their images) - without
    /// this, every re-render (a scan tick, a new upload, a Settings save...) leaks a GDI bitmap
    /// per visible thumbnail, and the panel gets progressively choppier to scroll over time.
    private void ClearActivityRows()
    {
        foreach (Control control in _recentList.Controls.Cast<Control>().ToArray())
        {
            foreach (Control child in control.Controls)
            {
                if (child is PictureBox { Image: { } image } pictureBox)
                {
                    pictureBox.Image = null;
                    image.Dispose();
                }
            }
            control.Dispose();
        }
        _recentList.Controls.Clear();
    }

    private Control BuildActivityRow(string fileName, DateTime atLocal, byte[]? thumbnailPng, string? fullPath, int rowWidth)
    {
        var row = new SurfacePanel(_palette)
        {
            Width = rowWidth,
            Height = Scale(ThumbnailBoxSize + 24),
            Margin = new Padding(0, 0, 0, 10),
            BackColor = _palette.Background,
        };

        var thumbBox = new PictureBox
        {
            Location = new Point(Scale(12), Scale(12)),
            Size = new Size(Scale(ThumbnailBoxSize), Scale(ThumbnailBoxSize)),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = _palette.ThumbPlaceholder,
        };
        if (thumbnailPng is { Length: > 0 } png)
        {
            try
            {
                using var ms = new MemoryStream(png);
                using var source = Image.FromStream(ms);
                thumbBox.Image = new Bitmap(source);
            }
            catch (ArgumentException) { /* Keep the placeholder when a saved preview cannot be decoded. */ }
        }

        var textLeft = Scale(ThumbnailBoxSize + 28);
        var textWidth = Math.Max(60, rowWidth - textLeft - Scale(16));
        var nameLabel = new Label
        {
            Text = fileName,
            Location = new Point(textLeft, Scale(17)),
            Size = new Size(textWidth, Scale(24)),
            ForeColor = _palette.Text,
            BackColor = _palette.ControlBackground,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            AutoEllipsis = true,
        };
        var timeLabel = new Label
        {
            Text = FormatRelativeTime(atLocal),
            Location = new Point(textLeft, Scale(45)),
            Size = new Size(textWidth, Scale(22)),
            ForeColor = _palette.TextMuted,
            BackColor = _palette.ControlBackground,
            Font = new Font("Segoe UI", 9f),
        };

        row.Controls.Add(thumbBox);
        row.Controls.Add(nameLabel);
        row.Controls.Add(timeLabel);

        // Every row is clickable. Rows persisted by an older version have no stored path, so the file
        // is looked up by name in the configured folders when clicked (see OpenActivityFileAsync).
        var parts = new Control[] { row, thumbBox, nameLabel, timeLabel };
        foreach (var control in parts)
        {
            control.Cursor = Cursors.Hand;
            control.Click += (_, _) => _ = OpenActivityFileAsync(fileName, fullPath);
            // Hover highlight so it is obvious the row can be clicked. Child controls fire their own
            // enter/leave, so only un-highlight once the pointer has really left the whole row.
            control.MouseEnter += (_, _) => nameLabel.ForeColor = _palette.Accent;
            control.MouseLeave += (_, _) =>
            {
                if (!row.ClientRectangle.Contains(row.PointToClient(Cursor.Position))) nameLabel.ForeColor = _palette.Text;
            };
        }
        return row;
    }

    // Same media types the uploader handles. File names of downloaded assets come from the server
    // (possibly another user's shared album), so anything else - e.g. a "photo.cmd" - must never be
    // handed to ShellExecute from a click.
    private static readonly HashSet<string> OpenableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".gif", ".bmp", ".tiff", ".webp", ".dng", ".cr2", ".nef", ".arw",
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".m4v", ".3gp",
    };

    /// Uses the stored full path when there is one; otherwise (rows saved before paths were recorded)
    /// searches the sync and watched folders for a file with that name. The search runs off the UI
    /// thread since those folders can hold thousands of files.
    private async Task OpenActivityFileAsync(string fileName, string? fullPath)
    {
        try
        {
            var path = !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath) ? fullPath : await Task.Run(() => FindByName(fileName));
            if (path is null) { AppLogger.Log($"VAROITUS: tiedostoa '{fileName}' ei loydy avattavaksi."); return; }
            OpenFile(path);
        }
        catch (Exception ex) { AppLogger.Log($"VAROITUS: tiedoston '{fileName}' avaaminen epaonnistui: {ex.Message}"); }
    }

    private string? FindByName(string fileName)
    {
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        var roots = new[] { _config.SyncPhotoFolder, _config.SyncVideoFolder }
            .Concat(_config.Directories)
            .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            try
            {
                var match = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (match is not null) return match;
            }
            catch (Exception ex) { AppLogger.Log($"VAROITUS: kansion '{root}' haku epaonnistui: {ex.Message}"); }
        }
        return null;
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (!OpenableExtensions.Contains(Path.GetExtension(path))) { AppLogger.Log($"VAROITUS: tiedostotyyppia ei avata: '{path}'."); return; }
            if (!File.Exists(path)) { AppLogger.Log($"VAROITUS: tiedostoa '{path}' ei loydy avattavaksi."); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { AppLogger.Log($"VAROITUS: tiedoston '{path}' avaaminen epaonnistui: {ex.Message}"); }
    }

    /// No thumbnail (the file is gone by the time this renders, whichever side deleted it first)
    /// - just the filename, why it was deleted, and when, in the same two-line row shape as the
    /// upload/download rows in the combined timeline.
    private Control BuildDeletionRow(string fileName, string reason, DateTime atLocal, int rowWidth)
    {
        var row = new SurfacePanel(_palette)
        {
            Width = rowWidth,
            Height = Scale(66),
            Margin = new Padding(0, 0, 0, 10),
            BackColor = _palette.Background,
        };

        var nameLabel = new Label
        {
            Text = fileName,
            Location = new Point(Scale(16), Scale(10)),
            Size = new Size(rowWidth - Scale(32), Scale(20)),
            ForeColor = _palette.Text,
            BackColor = _palette.ControlBackground,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            AutoEllipsis = true,
        };
        var reasonLabel = new Label
        {
            Text = $"{reason} · {FormatRelativeTime(atLocal)}",
            Location = new Point(Scale(16), Scale(35)),
            Size = new Size(rowWidth - Scale(32), Scale(20)),
            ForeColor = _palette.TextMuted,
            BackColor = _palette.ControlBackground,
            Font = new Font("Segoe UI", 9f),
            AutoEllipsis = true,
        };

        row.Controls.Add(nameLabel);
        row.Controls.Add(reasonLabel);
        return row;
    }

    private async void OnLoadAsync(object? sender, EventArgs e)
    {
        // Populated here (post-handle-creation), not the constructor: a ListView's Items added
        // before its native handle exists do not reliably show once the handle is later created.
        OnWatcherActivityChanged(_watcher.GetCurrentSnapshot());
        OnPhotoSyncActivityChanged(_photoSync.GetCurrentSnapshot());
        RenderActivity();

        try
        {
            var stats = await _watcher.TryGetStorageStatsAsync(CancellationToken.None);
            if (IsDisposed) return;

            if (stats is null)
            {
                _storageLabel.Text = Loc.T("panel.storageUnavailable");
                _storageProgressBar.Visible = false;
                return;
            }

            _storageProgressBar.Visible = true;
            _storageProgressBar.Value = Math.Clamp((int)Math.Round(stats.DiskUsagePercentage), 0, 100);
            _storageLabel.Text = Loc.T("panel.storageUsed", FormatBytes(stats.DiskUseRaw), FormatBytes(stats.DiskSizeRaw));
        }
        catch
        {
            if (IsDisposed) return;
            _storageLabel.Text = Loc.T("panel.storageUnavailable");
            _storageProgressBar.Visible = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double gb = 1024d * 1024 * 1024;
        const double mb = 1024d * 1024;
        if (bytes >= gb) return $"{bytes / gb:0.0} GB";
        if (bytes >= mb) return $"{bytes / mb:0.0} MB";
        return $"{bytes / 1024.0:0.0} KB";
    }

    private static string FormatRelativeTime(DateTime local)
    {
        var span = DateTime.Now - local;
        if (span.TotalSeconds < 60) return Loc.T("time.justNow");
        if (span.TotalMinutes < 60) return Loc.T("time.minutesAgo", (int)span.TotalMinutes);
        if (span.TotalHours < 24) return Loc.T("time.hoursAgo", (int)span.TotalHours);
        return Loc.T("time.daysAgo", (int)span.TotalDays);
    }

    /// FlowLayoutPanel.DoubleBuffered is protected and off by default, which tears/flickers
    /// visibly while scrolling a panel with this many image-heavy child controls. Its scrollbars
    /// are also plain native Win32 ones - always light/white - which look wrong against the dark
    /// palette; SetWindowTheme("DarkMode_Explorer") is the same undocumented-but-widely-used trick
    /// other dark-themed WinForms/Win32 apps use to get the OS's own dark scrollbar skin instead
    /// of drawing a custom scrollbar from scratch.
    private sealed class DoubleBufferedFlowLayoutPanel : FlowLayoutPanel
    {
        private readonly bool _isDark;

        public DoubleBufferedFlowLayoutPanel(bool isDark = false)
        {
            _isDark = isDark;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_isDark) SetWindowTheme(Handle, "DarkMode_Explorer", null);
        }
    }

    private sealed class FlatProgressBar : Panel
    {
        private int _value;

        public FlatProgressBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color FillColor { get; set; } = Color.FromArgb(0, 150, 140);

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color TrackColor { get => BackColor; set => BackColor = value; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Value
        {
            get => _value;
            set
            {
                var clamped = Math.Clamp(value, 0, 100);
                if (clamped == _value) return;
                _value = clamped;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_value <= 0) return;
            var fillWidth = (int)Math.Round(Width * (_value / 100.0));
            using var brush = new SolidBrush(FillColor);
            e.Graphics.FillRectangle(brush, 0, 0, fillWidth, Height);
        }
    }
}
