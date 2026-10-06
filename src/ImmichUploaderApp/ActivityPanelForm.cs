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

    private const int CornerRadius = 10;

    public ActivityPanelForm(UploadWatcherService watcher, PhotoSyncService photoSync, AppConfig config, Action openSettings)
    {
        _watcher = watcher;
        _photoSync = photoSync;
        _config = config;
        _openSettings = openSettings;
        _palette = ThemeService.Resolve(config.Theme);

        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ClientSize = new Size(500, 680);
        KeyPreview = true;
        BackColor = _palette.Background;

        BuildLayout();
        ApplyRoundedRegion();
        PositionNearTray();

        _watcher.ActivityChanged += OnWatcherActivityChanged;
        _photoSync.ActivityChanged += OnPhotoSyncActivityChanged;
        Load += OnLoadAsync;
        FormClosed += (_, _) =>
        {
            _watcher.ActivityChanged -= OnWatcherActivityChanged;
            _photoSync.ActivityChanged -= OnPhotoSyncActivityChanged;
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    // Gives the borderless popup a soft native drop shadow, the same trick used by tooltips
    // and most tray flyouts, instead of it looking like a flat rectangle pasted on the desktop.
    protected override CreateParams CreateParams
    {
        get
        {
            const int csDropShadow = 0x00020000;
            var cp = base.CreateParams;
            cp.ClassStyle |= csDropShadow;
            return cp;
        }
    }

    private void ApplyRoundedRegion()
    {
        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        using var pen = new Pen(_palette.Border);
        e.Graphics.DrawPath(pen, path);
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

    private void BuildLayout()
    {
        var outer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _palette.Background,
            Padding = new Padding(14),
        };
        Controls.Add(outer);

        // TableLayoutPanel with one Percent(100) row for the recent-uploads list and AutoSize
        // rows for everything else: a fixed header/footer around one flexible middle region is
        // exactly what it's designed for, and unlike chained Dock=Top/Bottom/Fill siblings it
        // doesn't depend on Controls.Add ordering to compute the flexible row's size correctly.
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 13,
            BackColor = _palette.Background,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 13; i++)
        {
            table.RowStyles.Add(new RowStyle(i == 8 ? SizeType.Percent : SizeType.AutoSize, i == 8 ? 100 : 0));
        }
        outer.Controls.Add(table);

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            BackColor = _palette.Background,
        };
        var titleLabel = new Label
        {
            Text = Loc.T("app.name"),
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
            ForeColor = _palette.Text,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 10),
        };
        var openServerButton = new Button
        {
            Text = Loc.T("tray.openServer"),
            FlatStyle = FlatStyle.Flat,
            ForeColor = _palette.Accent,
            BackColor = _palette.Background,
            Cursor = Cursors.Hand,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(10, 0, 0, 10),
            Padding = new Padding(2, 0, 2, 0),
        };
        openServerButton.FlatAppearance.BorderSize = 0;
        openServerButton.FlatAppearance.MouseOverBackColor = _palette.Divider;
        openServerButton.FlatAppearance.MouseDownBackColor = _palette.Divider;
        openServerButton.Click += OnOpenServerClicked;

        var settingsButton = new Button
        {
            Text = Loc.T("panel.settingsButton"),
            FlatStyle = FlatStyle.Flat,
            ForeColor = _palette.Accent,
            BackColor = _palette.Background,
            Cursor = Cursors.Hand,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(6, 0, 0, 10),
            Padding = new Padding(2, 0, 2, 0),
        };
        settingsButton.FlatAppearance.BorderSize = 0;
        settingsButton.FlatAppearance.MouseOverBackColor = _palette.Divider;
        settingsButton.FlatAppearance.MouseDownBackColor = _palette.Divider;
        settingsButton.Click += (_, _) => { Close(); _openSettings(); };
        header.Controls.Add(titleLabel);
        header.Controls.Add(openServerButton);
        header.Controls.Add(settingsButton);
        var scanButton = new Button { Text = Loc.T("activity.check"), AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = _palette.Accent, BackColor = _palette.Background };
        scanButton.Click += (_, _) => { _watcher.ScanNow(); _photoSync.ScanNow(); };
        header.Controls.Add(scanButton);
        _uploadSummaryLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = _palette.TextMuted, Margin = new Padding(0, 0, 0, 12) };
        _downloadStatusLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = _palette.Text, Margin = new Padding(0, 0, 0, 6) };
        _downloadSummaryLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = _palette.TextMuted, Margin = new Padding(0, 0, 0, 12) };
        _downloadProgressBar = new FlatProgressBar { Dock = DockStyle.Fill, Height = 6, TrackColor = _palette.Track, FillColor = _palette.Accent, Visible = false };

        _statusLabel = new Label
        {
            Text = Loc.T("panel.loading"),
            ForeColor = _palette.Text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 6),
        };

        _uploadProgressBar = new FlatProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 4,
            TrackColor = _palette.Track,
            FillColor = _palette.Accent,
            Visible = false,
            Margin = new Padding(0, 0, 0, 10),
        };

        _failureLabel = new Label
        {
            ForeColor = Color.Firebrick,
            AutoEllipsis = true,
            AutoSize = false,
            Height = 34,
            Dock = DockStyle.Fill,
            Visible = false,
            Margin = new Padding(0, 0, 0, 6),
        };

        // Plain FlowLayoutPanel isn't double-buffered, which tears/flickers noticeably while
        // scrolling once there's enough content to need scrolling at all - now routinely the
        // case with two sections (uploads + downloads) instead of one. Same fix as
        // FlatProgressBar below: enable double buffering via the protected ControlStyles.
        _recentList = new DoubleBufferedFlowLayoutPanel(_palette.IsDark)
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = _palette.Background,
        };

        var separator1 = new Panel { Dock = DockStyle.Fill, Height = 1, BackColor = _palette.Divider, Margin = new Padding(0, 10, 0, 10) };

        var storageHeader = new Label
        {
            Text = Loc.T("panel.storageHeader"),
            Font = new Font(Font.FontFamily, 8f, FontStyle.Bold),
            ForeColor = _palette.TextMuted,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 6),
        };
        _storageProgressBar = new FlatProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 4,
            TrackColor = _palette.Track,
            FillColor = _palette.Accent,
            Margin = new Padding(0, 0, 0, 6),
        };
        _storageLabel = new Label
        {
            Text = Loc.T("panel.loading"),
            ForeColor = _palette.TextMuted,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 0),
        };

        table.Controls.Add(header, 0, 0);
        table.Controls.Add(_statusLabel, 0, 1);
        table.Controls.Add(_uploadProgressBar, 0, 2);
        table.Controls.Add(_uploadSummaryLabel, 0, 3);
        table.Controls.Add(_downloadStatusLabel, 0, 4);
        table.Controls.Add(_downloadProgressBar, 0, 5);
        table.Controls.Add(_downloadSummaryLabel, 0, 6);
        table.Controls.Add(_failureLabel, 0, 7);
        table.Controls.Add(_recentList, 0, 8);
        table.Controls.Add(separator1, 0, 9);
        table.Controls.Add(storageHeader, 0, 10);
        table.Controls.Add(_storageProgressBar, 0, 11);
        table.Controls.Add(_storageLabel, 0, 12);
    }

    private void PositionNearTray()
    {
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        var x = workArea.Right - Width - 8;
        var y = workArea.Bottom - Height - 8;
        Location = new Point(Math.Max(workArea.Left, x), Math.Max(workArea.Top, y));
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
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
            _statusLabel.Text = Loc.T("activity.upload") + "\n" + state;
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
            _downloadStatusLabel.Text = Loc.T("activity.download") + "\n" + state;
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

    private const int ThumbnailBoxSize = 42;

    /// Renders both directions into one scrollable region: uploads section, then downloads
    /// section, each with its own header - simpler and more robust to varying content amounts
    /// than splitting the flexible table row into two fixed halves.
    private void RenderActivity()
    {
        _recentList.SuspendLayout();
        ClearActivityRows();
        // Reserve the vertical scrollbar's width up front, even while it isn't visible yet: rows
        // get an explicit Width below (not Dock=Fill), so if it were sized to the current,
        // scrollbar-less ClientSize.Width and adding these rows is what makes the list tall enough
        // to need scrolling, the now-narrower post-scrollbar ClientSize.Width leaves every row
        // slightly too wide - which is exactly what was showing up as a spurious horizontal
        // scrollbar alongside the vertical one.
        var rowWidth = Math.Max(200, _recentList.ClientSize.Width - 4 - SystemInformation.VerticalScrollBarWidth);

        AddSectionHeader(Loc.T("panel.recentHeader"));
        if (_lastUploads.Count == 0)
        {
            AddEmptyLabel(Loc.T("panel.noUploads"));
        }
        else
        {
            AddActivityRows(_lastUploads.Select(u => (u.FileName, u.UploadedAtLocal, u.SizeBytes, u.ThumbnailPng, u.FullPath)), rowWidth);
        }

        AddSectionHeader(Loc.T("panel.downloadsHeader"), topMargin: 14);
        if (_lastDownloads.Count == 0)
        {
            AddEmptyLabel(Loc.T("panel.noDownloads"));
        }
        else
        {
            AddActivityRows(_lastDownloads.Select(d => (d.FileName, d.DownloadedAtLocal, d.SizeBytes, d.ThumbnailPng, d.FullPath)), rowWidth);
        }

        AddSectionHeader(Loc.T("panel.deletionsHeader"), topMargin: 14);
        if (_lastDeletions.Count == 0)
        {
            AddEmptyLabel(Loc.T("panel.noDeletions"));
        }
        else
        {
            var isFirst = true;
            foreach (var deletion in _lastDeletions)
            {
                if (!isFirst) _recentList.Controls.Add(new Panel { Width = rowWidth, Height = 1, BackColor = _palette.Divider, Margin = new Padding(0, 2, 0, 2) });
                isFirst = false;
                _recentList.Controls.Add(BuildDeletionRow(deletion.FileName, deletion.Reason, deletion.DeletedAtLocal, rowWidth));
            }
        }

        _recentList.ResumeLayout();
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

    private void AddSectionHeader(string text, int topMargin = 0)
    {
        _recentList.Controls.Add(new Label
        {
            Text = text,
            Font = new Font(Font.FontFamily, 8f, FontStyle.Bold),
            ForeColor = _palette.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, topMargin, 0, 6),
        });
    }

    private void AddEmptyLabel(string text)
    {
        _recentList.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = _palette.TextMuted,
            Margin = new Padding(2, 0, 2, 4),
        });
    }

    private void AddActivityRows(IEnumerable<(string FileName, DateTime AtLocal, long SizeBytes, byte[]? ThumbnailPng, string? FullPath)> items, int rowWidth)
    {
        var isFirst = true;
        foreach (var item in items)
        {
            if (!isFirst) _recentList.Controls.Add(new Panel { Width = rowWidth, Height = 1, BackColor = _palette.Divider, Margin = new Padding(0, 2, 0, 2) });
            isFirst = false;
            _recentList.Controls.Add(BuildActivityRow(item.FileName, item.AtLocal, item.ThumbnailPng, item.FullPath, rowWidth));
        }
    }

    private Control BuildActivityRow(string fileName, DateTime atLocal, byte[]? thumbnailPng, string? fullPath, int rowWidth)
    {
        var row = new Panel
        {
            Width = rowWidth,
            Height = ThumbnailBoxSize + 8,
            Margin = new Padding(0, 4, 0, 4),
            BackColor = _palette.Background,
        };

        var thumbBox = new PictureBox
        {
            Location = new Point(0, 4),
            Size = new Size(ThumbnailBoxSize, ThumbnailBoxSize),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = _palette.ThumbPlaceholder,
        };
        if (thumbnailPng is { Length: > 0 } png)
        {
            using var ms = new MemoryStream(png);
            thumbBox.Image = Image.FromStream(ms);
        }

        var textLeft = ThumbnailBoxSize + 10;
        var textWidth = Math.Max(60, rowWidth - textLeft);
        var nameLabel = new Label
        {
            Text = fileName,
            Location = new Point(textLeft, 6),
            Size = new Size(textWidth, 18),
            ForeColor = _palette.Text,
            AutoEllipsis = true,
        };
        var timeLabel = new Label
        {
            Text = FormatRelativeTime(atLocal),
            Location = new Point(textLeft, 25),
            Size = new Size(textWidth, 16),
            ForeColor = _palette.TextMuted,
            Font = new Font(Font.FontFamily, 8f),
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
            control.MouseEnter += (_, _) => row.BackColor = _palette.ControlBackground;
            control.MouseLeave += (_, _) =>
            {
                if (!row.ClientRectangle.Contains(row.PointToClient(Cursor.Position))) row.BackColor = _palette.Background;
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
    /// upload/download rows so the three sections read as one consistent list.
    private Control BuildDeletionRow(string fileName, string reason, DateTime atLocal, int rowWidth)
    {
        var row = new Panel
        {
            Width = rowWidth,
            Height = 40,
            Margin = new Padding(0, 4, 0, 4),
            BackColor = _palette.Background,
        };

        var nameLabel = new Label
        {
            Text = fileName,
            Location = new Point(0, 2),
            Size = new Size(rowWidth, 18),
            ForeColor = _palette.Text,
            AutoEllipsis = true,
        };
        var reasonLabel = new Label
        {
            Text = $"{reason} · {FormatRelativeTime(atLocal)}",
            Location = new Point(0, 21),
            Size = new Size(rowWidth, 16),
            ForeColor = _palette.TextMuted,
            Font = new Font(Font.FontFamily, 8f),
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
