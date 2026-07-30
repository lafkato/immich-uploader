using System.Diagnostics;
using ImmichUploaderApp.Models;
using ImmichUploaderApp.Services;

namespace ImmichUploaderApp;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _pauseResumeItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _openServerItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _scanNowItem;
    private readonly ToolStripMenuItem _donateItem;
    private readonly ToolStripMenuItem _viewLogItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ConfigService _configService = new();
    private readonly UploadWatcherService _watcher = new();

    private AppConfig _config;
    private ActivityPanelForm? _activityPanel;

    public TrayApplicationContext()
    {
        _config = _configService.LoadOrCreate();
        Loc.Language = _config.Language;

        _statusItem = new ToolStripMenuItem(Loc.T("tray.statusFormat", Loc.T("status.stopped"))) { Enabled = false };
        _pauseResumeItem = new ToolStripMenuItem(Loc.T("tray.pause"), null, OnPauseResumeClicked);
        _openServerItem = new ToolStripMenuItem(Loc.T("tray.openServer"), null, OnOpenServerClicked);
        _settingsItem = new ToolStripMenuItem(Loc.T("tray.settings"), null, OnSettingsClicked);
        _scanNowItem = new ToolStripMenuItem(GetScanNowText(), null, (_, _) => _watcher.ScanNow());
        _donateItem = new ToolStripMenuItem(DonationService.MenuText, null, (_, _) => DonationService.ShowPrompt());
        _viewLogItem = new ToolStripMenuItem(Loc.T("tray.viewLog"), null, OnViewLogClicked);
        _exitItem = new ToolStripMenuItem(Loc.T("tray.exit"), null, OnExitClicked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_openServerItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(_pauseResumeItem);
        menu.Items.Add(_scanNowItem);
        menu.Items.Add(_donateItem);
        menu.Items.Add(_viewLogItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        // Luetaan ikoni suoraan exe:n omasta resurssista (ApplicationIcon-buildiasetus upottaa
        // sen sinne), ei erillisesta tiedostosta - nain se toimii myos yhden tiedoston
        // (PublishSingleFile) julkaisussa, jossa erillisia Resources-tiedostoja ei valttamatta ole mukana.
        var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        _trayIcon = new NotifyIcon
        {
            Icon = icon,
            Text = Loc.T("app.name"),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.MouseClick += OnTrayIconMouseClick;

        _watcher.ActivityChanged += OnWatcherActivityChanged;

        if (!_config.IsConfigured || Environment.GetEnvironmentVariable("IMMICH_FORCE_SETTINGS") == "1")
        {
            ShowSettings(forceOpen: true);
        }
        else
        {
            _ = StartWatcherAsync();
        }
    }

    private async Task StartWatcherAsync()
    {
        try
        {
            await _watcher.StartAsync(_config);
        }
        catch (Exception ex)
        {
            AppLogger.LogFatal(ex);
            _trayIcon.ShowBalloonTip(5000, Loc.T("app.name"), Loc.T("status.startFailed", ex.Message), ToolTipIcon.Error);
        }
    }

    private void OnWatcherActivityChanged(WatcherActivitySnapshot snapshot)
    {
        if (_statusItem.GetCurrentParent()?.InvokeRequired == true)
        {
            _statusItem.GetCurrentParent()!.BeginInvoke(new Action(() => UpdateStatusText(snapshot.StatusText)));
        }
        else
        {
            UpdateStatusText(snapshot.StatusText);
        }
    }

    private void UpdateStatusText(string status)
    {
        _statusItem.Text = Loc.T("tray.statusFormat", status);
        _pauseResumeItem.Text = _watcher.IsPaused ? Loc.T("tray.resume") : Loc.T("tray.pause");
    }

    private void RefreshLocalizedUi()
    {
        _trayIcon.Text = Loc.T("app.name");
        _openServerItem.Text = Loc.T("tray.openServer");
        _settingsItem.Text = Loc.T("tray.settings");
        _scanNowItem.Text = GetScanNowText();
        _donateItem.Text = DonationService.MenuText;
        _viewLogItem.Text = Loc.T("tray.viewLog");
        _exitItem.Text = Loc.T("tray.exit");
        UpdateStatusText(_watcher.GetCurrentSnapshot().StatusText);
    }

    private static string GetScanNowText() => Loc.Language switch
    {
        "en" => "Scan now",
        "sv" => "Skanna nu",
        "de" => "Jetzt scannen",
        _ => "Skannaa nyt",
    };

    private void OnTrayIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) ShowActivityPanel();
    }

    private void ShowActivityPanel()
    {
        if (_activityPanel is { IsDisposed: false })
        {
            _activityPanel.Close();
            return;
        }

        _activityPanel = new ActivityPanelForm(_watcher, _config, () => ShowSettings(forceOpen: false));
        _activityPanel.FormClosed += (_, _) => _activityPanel = null;
        _activityPanel.Show();
        _activityPanel.Activate();
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

    private void OnSettingsClicked(object? sender, EventArgs e) => ShowSettings(forceOpen: false);

    private void ShowSettings(bool forceOpen)
    {
        using var form = new SettingsForm(_config, onExitRequested: () => _ = ExitApplicationAsync());
        var result = form.ShowDialog();
        if (result == DialogResult.OK && form.ResultConfig is not null)
        {
            _config = form.ResultConfig;
            Loc.Language = _config.Language;
            _configService.Save(_config);
            RefreshLocalizedUi();
            _ = StartWatcherAsync();
        }
        else if (forceOpen && !_config.IsConfigured)
        {
            // Kayttaja peruutti ensimmaisen asetusikkunan ilman konfiguraatiota - sovellus jaa
            // odottamaan valikon kautta avattavaa asetusikkunaa, ei kaynnisty tarkkailua.
        }
    }

    private void OnPauseResumeClicked(object? sender, EventArgs e)
    {
        // Pause()/Resume() already raise the correct localized status themselves.
        if (_watcher.IsPaused) _watcher.Resume();
        else _watcher.Pause();
    }

    private void OnViewLogClicked(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ConfigService.LogDir);
            var logPath = Path.Combine(ConfigService.LogDir, "app.log");
            if (!File.Exists(logPath)) File.WriteAllText(logPath, string.Empty);
            Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("tray.logOpenFailed", ex.Message), Loc.T("app.name"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnExitClicked(object? sender, EventArgs e) => await ExitApplicationAsync();

    private async Task ExitApplicationAsync()
    {
        _trayIcon.Visible = false;
        await _watcher.StopAsync();
        _trayIcon.Dispose();
        Application.Exit();
    }
}
