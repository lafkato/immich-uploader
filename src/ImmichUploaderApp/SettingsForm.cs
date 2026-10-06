using System.Diagnostics;
using ImmichUploaderApp.Models;
using ImmichUploaderApp.Services;

namespace ImmichUploaderApp;

public sealed class SettingsForm : Form
{
    private readonly AppConfig _initialConfig;
    private readonly Palette _palette;
    private readonly HashSet<string> _originalExcludeSet;
    private readonly List<string> _directories;
    private readonly Action _onExitRequested;

    private readonly TextBox _txtServerUrl = new();
    private readonly TextBox _txtApiKey = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _chkShowKey = new() { Text = Loc.T("settings.showKey") };
    private readonly TextBox _txtAlbumName = new();
    private readonly TextBox _txtDeviceName = new();
    private readonly ListBox _lstDirectories = new();
    private readonly TreeView _treeExclusions = new() { CheckBoxes = true };
    private bool _suppressTreeCheckEvents;
    private readonly CheckBox _chkStartWithWindows = new() { Text = Loc.T("settings.startWithWindows") };
    private readonly Label _lblTestResult = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly ComboBox _cmbTheme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly ComboBox _cmbLanguage = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly UpdateService _updateService = new();
    private readonly Label _lblCurrentVersion = new() { AutoSize = true };
    private readonly Label _lblUpdateResult = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly Button _btnDownloadUpdate = new() { Text = Loc.T("settings.downloadAndInstall"), AutoSize = true, Visible = false, Margin = new Padding(0, 8, 0, 0) };
    private UpdateCheckResult? _pendingUpdate;
    private readonly CheckBox _chkSyncEnabled = new() { Text = Loc.T("sync.enable") };
    private readonly TextBox _txtSyncPhotoFolder = new() { ReadOnly = true };
    private readonly TextBox _txtSyncVideoFolder = new() { ReadOnly = true };
    private readonly RadioButton _rdoSyncThumbnail = new() { Text = Loc.T("sync.modeThumbnail") };
    private readonly RadioButton _rdoSyncOriginal = new() { Text = Loc.T("sync.modeOriginal") };
    private readonly CheckBox _chkSyncDeleteRemote = new() { Text = Loc.T("sync.deleteRemoteOnLocalDelete") };
    private readonly CheckBox _chkSyncOrganizeByAlbum = new() { Text = Loc.T("sync.organizeByAlbum") };

    public AppConfig? ResultConfig { get; private set; }

    public SettingsForm(AppConfig config, Action? onExitRequested = null)
    {
        _initialConfig = config;
        _onExitRequested = onExitRequested ?? Application.Exit;
        _palette = ThemeService.Resolve(config.Theme);
        _directories = new List<string>(config.Directories);
        _originalExcludeSet = new HashSet<string>(config.ExcludeDirectories, StringComparer.OrdinalIgnoreCase);

        // Font-pohjainen skaalaus on WinFormsin oma oletus (sama minka Designer generoi
        // joka lomakkeelle) - asetetaan se eksplisiittisesti koska lomake rakennetaan
        // koodissa ilman Designeria, jolloin oletusarvo ei muuten periydy oikein.
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9.5f);

        Text = Loc.T("settings.title");
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = _palette.Background;
        ForeColor = _palette.Text;
        HandleCreated += (_, _) => ThemeService.ApplyTitleBarTheme(this, _palette.IsDark);

        BuildLayout();
        LoadFromConfig();
        RefreshExclusionTree();

        // Sizing: the content panel below is Dock=Fill + AutoScroll (not AutoSize - AutoSize on
        // a scrollable panel always grows to fit everything, which defeats the scrollbar). So
        // the window gets a sensible default size instead of auto-fitting to content height,
        // clamped to the working area; anything that doesn't fit scrolls within the panel.
        var workingArea = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(860, 620);
        ClientSize = new Size(Math.Min(1000, workingArea.Width - 60), Math.Min(740, workingArea.Height - 80));

    }

    private readonly List<Button> _tabHeaders = new();
    private readonly List<Panel> _tabPages = new();
    private Label _pageTitle = null!, _pageHint = null!;
    private int _selectedTabIndex;
    private readonly string[] _hints = { "design.generalHint", "design.serverHint", "design.foldersHint", "design.downloadsHint", "design.updatesHint" };

    private void BuildLayout()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(shell);
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = _palette.ControlBackground, Padding = new Padding(20, 28, 16, 20), Margin = new Padding(0) };
        navigation.Controls.Add(new Label { Text = "immich", AutoSize = true, Font = new Font("Segoe UI", 25, FontStyle.Bold), ForeColor = _palette.Text, Margin = new Padding(0, 0, 0, 8) });
        navigation.Controls.Add(new Label { Text = Loc.T("panel.settingsButton"), AutoSize = true, ForeColor = _palette.TextMuted, Margin = new Padding(0, 0, 0, 28) });
        shell.Controls.Add(navigation, 0, 0);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(28, 28, 28, 20), Margin = new Padding(0), BackColor = _palette.Background };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        shell.Controls.Add(content, 1, 0);
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _pageTitle = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 23, FontStyle.Bold), ForeColor = _palette.Text, Margin = new Padding(0) };
        _pageHint = new Label { Dock = DockStyle.Fill, ForeColor = _palette.TextMuted, Margin = new Padding(0, 6, 0, 0) };
        heading.Controls.Add(_pageTitle, 0, 0); heading.Controls.Add(_pageHint, 0, 1); content.Controls.Add(heading, 0, 0);
        var host = new Panel { Dock = DockStyle.Fill, BackColor = _palette.Background, Margin = new Padding(0) };
        content.Controls.Add(host, 0, 1);
        AddTab(navigation, host, Loc.T("settings.tabGeneral"), BuildGeneralTab);
        AddTab(navigation, host, Loc.T("settings.tabServer"), BuildServerTab);
        AddTab(navigation, host, Loc.T("settings.tabFolders"), BuildFoldersTab);
        AddTab(navigation, host, Loc.T("sync.tabTitle"), BuildDownloadsTab);
        AddTab(navigation, host, Loc.T("settings.updatesLabel"), BuildUpdatesTab);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 14, 0, 0), Margin = new Padding(0) };
        var cancel = StyleButton(MakeDialogButton(Loc.T("settings.cancel"))); cancel.DialogResult = DialogResult.Cancel;
        var save = StyleButton(MakeDialogButton(Loc.T("settings.save")));
        save.BackColor = _palette.Accent; save.ForeColor = _palette.IsDark ? _palette.Background : Color.White; save.FlatAppearance.BorderSize = 0;
        save.Click += OnSaveClicked; actions.Controls.Add(save); actions.Controls.Add(cancel);
        content.Controls.Add(actions, 0, 2);
        CancelButton = cancel;
        SelectTab(_initialConfig.IsConfigured ? 0 : 1);
    }

    private void AddTab(FlowLayoutPanel navigation, Panel host, string title, Action<Panel> build)
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = _palette.Background, AutoScroll = true };
        host.Controls.Add(page); build(page); _tabPages.Add(page);
        var index = _tabHeaders.Count;
        var button = new Button { Text = title, Size = new Size(180, 44), FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 4, 0), Margin = new Padding(0, 0, 0, 6), Cursor = Cursors.Hand, AccessibleName = title };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => SelectTab(index);
        navigation.Controls.Add(button); _tabHeaders.Add(button);
    }

    private void SelectTab(int index)
    {
        _selectedTabIndex = index;
        _pageTitle.Text = _tabHeaders[index].Text;
        _pageHint.Text = Loc.T(_hints[index]);
        for (var i = 0; i < _tabPages.Count; i++)
        {
            _tabPages[i].Visible = i == index;
            _tabHeaders[i].BackColor = i == index ? _palette.Track : _palette.ControlBackground;
            _tabHeaders[i].ForeColor = i == index ? _palette.Accent : _palette.TextMuted;
        }
        _tabPages[index].BringToFront();
    }

    private void BuildGeneralTab(Panel page)
    {
        var table = CreateTabTable();
        page.Controls.Add(table);

        AddRow(table, MakeLabel(Loc.T("settings.themeLabel"), new Padding(0, 0, 0, 2)));
        StyleComboBox(_cmbTheme);
        _cmbTheme.Items.AddRange(new object[] { Loc.T("settings.themeSystem"), Loc.T("settings.themeLight"), Loc.T("settings.themeDark") });
        AddRow(table, _cmbTheme);

        AddRow(table, MakeLabel(Loc.T("settings.languageLabel"), new Padding(0, 14, 0, 2)));
        StyleComboBox(_cmbLanguage);
        foreach (var (_, name) in Loc.SupportedLanguages) _cmbLanguage.Items.Add(name);
        AddRow(table, _cmbLanguage);

        _chkStartWithWindows.AutoSize = true;
        _chkStartWithWindows.Margin = new Padding(0, 18, 0, 4);
        StyleCheckBox(_chkStartWithWindows);
        AddRow(table, _chkStartWithWindows);
    }

    private void BuildServerTab(Panel page)
    {
        var table = CreateTabTable();
        page.Controls.Add(table);

        AddRow(table, MakeLabel(Loc.T("settings.serverUrlLabel"), new Padding(0, 0, 0, 2), wrapHeight: 32));
        StyleTextBox(_txtServerUrl);
        _txtServerUrl.Dock = DockStyle.Top;
        AddRow(table, _txtServerUrl);

        AddRow(table, MakeLabel(Loc.T("settings.apiKeyLabel"), new Padding(0, 12, 0, 2)));
        var apiKeyPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0), BackColor = _palette.Background };
        StyleTextBox(_txtApiKey);
        _txtApiKey.Width = 300;
        _chkShowKey.AutoSize = true;
        StyleCheckBox(_chkShowKey);
        _chkShowKey.CheckedChanged += (_, _) => _txtApiKey.UseSystemPasswordChar = !_chkShowKey.Checked;
        apiKeyPanel.Controls.Add(_txtApiKey);
        apiKeyPanel.Controls.Add(_chkShowKey);
        AddRow(table, apiKeyPanel);

        var testPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 8, 0, 0), BackColor = _palette.Background };
        var btnTest = StyleButton(new Button { Text = Loc.T("settings.testConnection"), AutoSize = true });
        btnTest.Click += OnTestConnectionClicked;
        testPanel.Controls.Add(btnTest);
        _lblTestResult.Margin = new Padding(10, 6, 0, 0);
        _lblTestResult.ForeColor = _palette.Text;
        testPanel.Controls.Add(_lblTestResult);
        AddRow(table, testPanel);

        AddRow(table, MakeLabel(Loc.T("settings.albumNameLabel"), new Padding(0, 18, 0, 2)));
        StyleTextBox(_txtAlbumName);
        _txtAlbumName.Width = 300;
        AddRow(table, _txtAlbumName);

        AddRow(table, MakeLabel(Loc.T("settings.deviceNameLabel"), new Padding(0, 12, 0, 2)));
        StyleTextBox(_txtDeviceName);
        _txtDeviceName.Width = 300;
        AddRow(table, _txtDeviceName);
    }

    private void BuildFoldersTab(Panel page)
    {
        var table = CreateTabTable(true);
        page.Controls.Add(table);

        AddRow(table, MakeLabel(Loc.T("settings.watchedFoldersLabel"), new Padding(0, 0, 0, 2)));

        StyleListBox(_lstDirectories);
        _lstDirectories.Dock = DockStyle.Fill;
        AddRow(table, _lstDirectories, percentHeight: 30);

        var dirButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 0), BackColor = _palette.Background };
        var btnAddDir = StyleButton(new Button { Text = Loc.T("settings.addFolder"), AutoSize = true });
        btnAddDir.Click += OnAddDirectoryClicked;
        var btnRemoveDir = StyleButton(new Button { Text = Loc.T("settings.removeSelected"), AutoSize = true });
        btnRemoveDir.Click += OnRemoveDirectoryClicked;
        dirButtons.Controls.Add(btnAddDir);
        dirButtons.Controls.Add(btnRemoveDir);
        AddRow(table, dirButtons);

        AddRow(table, MakeLabel(Loc.T("settings.exclusionsLabel"), new Padding(0, 14, 0, 2), wrapHeight: 40));

        StyleTreeView(_treeExclusions);
        _treeExclusions.Dock = DockStyle.Fill;
        _treeExclusions.BeforeCheck += OnTreeBeforeCheck;
        _treeExclusions.AfterCheck += OnTreeAfterCheck;
        _treeExclusions.BeforeExpand += OnTreeBeforeExpand;
        AddRow(table, _treeExclusions, percentHeight: 70);
    }

    private void BuildDownloadsTab(Panel page)
    {
        var table = CreateTabTable();
        page.Controls.Add(table);

        _chkSyncEnabled.AutoSize = true;
        StyleCheckBox(_chkSyncEnabled);
        AddRow(table, _chkSyncEnabled);

        AddRow(table, MakeLabel(Loc.T("sync.photoFolderLabel"), new Padding(0, 14, 0, 2)));
        var photoFolderPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0), BackColor = _palette.Background };
        StyleTextBox(_txtSyncPhotoFolder);
        _txtSyncPhotoFolder.Width = 300;
        var btnChoosePhotoFolder = StyleButton(new Button { Text = Loc.T("sync.chooseFolder"), AutoSize = true });
        btnChoosePhotoFolder.Click += (_, _) => ChooseSyncFolder(_txtSyncPhotoFolder);
        photoFolderPanel.Controls.Add(_txtSyncPhotoFolder);
        photoFolderPanel.Controls.Add(btnChoosePhotoFolder);
        AddRow(table, photoFolderPanel);

        AddRow(table, MakeLabel(Loc.T("sync.videoFolderLabel"), new Padding(0, 12, 0, 2)));
        var videoFolderPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0), BackColor = _palette.Background };
        StyleTextBox(_txtSyncVideoFolder);
        _txtSyncVideoFolder.Width = 300;
        var btnChooseVideoFolder = StyleButton(new Button { Text = Loc.T("sync.chooseFolder"), AutoSize = true });
        btnChooseVideoFolder.Click += (_, _) => ChooseSyncFolder(_txtSyncVideoFolder);
        videoFolderPanel.Controls.Add(_txtSyncVideoFolder);
        videoFolderPanel.Controls.Add(btnChooseVideoFolder);
        AddRow(table, videoFolderPanel);

        AddRow(table, MakeLabel(Loc.T("sync.modeLabel"), new Padding(0, 14, 0, 2)));
        var modePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0), BackColor = _palette.Background };
        _rdoSyncThumbnail.AutoSize = true;
        _rdoSyncThumbnail.Margin = new Padding(0, 0, 16, 0);
        _rdoSyncOriginal.AutoSize = true;
        StyleRadioButton(_rdoSyncThumbnail);
        StyleRadioButton(_rdoSyncOriginal);
        modePanel.Controls.Add(_rdoSyncThumbnail);
        modePanel.Controls.Add(_rdoSyncOriginal);
        AddRow(table, modePanel);

        _chkSyncDeleteRemote.AutoSize = true;
        _chkSyncDeleteRemote.Margin = new Padding(0, 14, 0, 2);
        StyleCheckBox(_chkSyncDeleteRemote);
        AddRow(table, _chkSyncDeleteRemote);

        _chkSyncOrganizeByAlbum.AutoSize = true;
        _chkSyncOrganizeByAlbum.Margin = new Padding(0, 6, 0, 2);
        StyleCheckBox(_chkSyncOrganizeByAlbum);
        AddRow(table, _chkSyncOrganizeByAlbum);

        AddRow(table, MakeLabel(Loc.T("sync.info"), new Padding(0, 16, 0, 2), wrapHeight: 48));
    }

    private void BuildUpdatesTab(Panel page)
    {
        var table = CreateTabTable();
        page.Controls.Add(table);

        _lblCurrentVersion.ForeColor = _palette.Text;
        _lblCurrentVersion.Text = Loc.T("settings.currentVersion", UpdateService.CurrentVersion.ToString(3));
        AddRow(table, _lblCurrentVersion);

        var updatePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 10, 0, 0), BackColor = _palette.Background };
        var btnCheckUpdate = StyleButton(new Button { Text = Loc.T("settings.checkForUpdates"), AutoSize = true });
        btnCheckUpdate.Click += OnCheckForUpdatesClicked;
        updatePanel.Controls.Add(btnCheckUpdate);
        _lblUpdateResult.Margin = new Padding(10, 6, 0, 0);
        _lblUpdateResult.ForeColor = _palette.Text;
        updatePanel.Controls.Add(_lblUpdateResult);
        AddRow(table, updatePanel);

        StyleButton(_btnDownloadUpdate);
        _btnDownloadUpdate.Click += OnDownloadAndInstallClicked;
        AddRow(table, _btnDownloadUpdate);
    }

    /// Jokainen valilehti saa oman, itsenaisen TableLayoutPanelin: yksi Percent(100)-sarake
    /// pitaa huolen etta rivit tayttavat aina valilehden todellisen leveyden riippumatta
    /// ikkunan koosta, fontin skaalauksesta tai kielen tekstien pituudesta - toisin kuin
    /// vanha versio jossa kontrollien leveys oli kovakoodattu eika reagoinut mihinkaan.
    private TableLayoutPanel CreateTabTable(bool fill = false)
    {
        var table = new TableLayoutPanel
        {
            Dock = fill ? DockStyle.Fill : DockStyle.Top,
            AutoSize = !fill,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            AutoScroll = false,
            Padding = new Padding(0, 10, 0, 10),
            BackColor = _palette.Background,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        table.RowCount = 0;
        return table;
    }

    /// percentHeight=null -> rivi mitoittuu kontrollin oman koon mukaan (labelit, tekstikentat).
    /// percentHeight annettuna -> rivi saa osuuden jaljella olevasta korkeudesta ja kasvaa/kutistuu
    /// ikkunan mukana (kansiolista, poissulkupuu) - kontrollilla pitaa olla Dock=Fill.
    private static void AddRow(TableLayoutPanel table, Control control, float? percentHeight = null)
    {
        var rowIndex = table.RowCount++;
        table.RowStyles.Add(percentHeight is { } pct ? new RowStyle(SizeType.Percent, pct) : new RowStyle(SizeType.AutoSize));
        if (control is FlowLayoutPanel)
        {
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }
        table.Controls.Add(control, 0, rowIndex);
    }

    /// wrapHeight=0 -> yksirivinen otsikkolabel, kutistuu tekstin mukaan.
    /// wrapHeight>0 -> rivittyva kuvausteksti: AutoSize=false + Dock=Top rivittaa tekstin aina
    /// oikein valilehden senhetkiseen todelliseen leveyteen, myos ikkunan koon muuttuessa -
    /// toisin kuin AutoSize+kiintea MaximumSize, joka ei seuraisi ikkunan kokoa jalkikateen.
    private Label MakeLabel(string text, Padding margin, int wrapHeight = 0) => wrapHeight > 0
        ? new Label { Text = text, AutoSize = false, Dock = DockStyle.Top, Height = wrapHeight, Margin = margin, ForeColor = _palette.Text }
        : new Label { Text = text, AutoSize = true, Margin = margin, ForeColor = _palette.Text };

    private void StyleTextBox(TextBox t)
    {
        t.BackColor = _palette.ControlBackground;
        t.ForeColor = _palette.Text;
        t.BorderStyle = BorderStyle.FixedSingle;
    }

    private void StyleListBox(ListBox l)
    {
        l.BackColor = _palette.ControlBackground;
        l.ForeColor = _palette.Text;
        l.BorderStyle = BorderStyle.FixedSingle;
    }

    private void StyleTreeView(TreeView t)
    {
        t.BackColor = _palette.ControlBackground;
        t.ForeColor = _palette.Text;
        t.BorderStyle = BorderStyle.FixedSingle;
    }

    private void StyleCheckBox(CheckBox c)
    {
        c.ForeColor = _palette.Text;
        c.BackColor = _palette.Background;
        c.UseVisualStyleBackColor = false;
    }

    private void StyleRadioButton(RadioButton r)
    {
        r.ForeColor = _palette.Text;
        r.BackColor = _palette.Background;
        r.UseVisualStyleBackColor = false;
    }

    private Button StyleButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = _palette.ControlBackground;
        b.ForeColor = _palette.Text;
        b.FlatAppearance.BorderColor = _palette.ControlBorder;
        b.FlatAppearance.MouseOverBackColor = _palette.Divider;
        return b;
    }

    private void StyleComboBox(ComboBox combo)
    {
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = _palette.ControlBackground;
        combo.ForeColor = _palette.Text;
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.ItemHeight = Math.Max(combo.ItemHeight, 18);
        combo.DrawItem += (s, e) =>
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var bgBrush = new SolidBrush(selected ? _palette.Divider : _palette.ControlBackground);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString() ?? string.Empty, e.Font ?? combo.Font, e.Bounds, _palette.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        };
    }

    private static Button MakeDialogButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(112, 38),
        Padding = new Padding(10, 4, 10, 4),
    };

    private void LoadFromConfig()
    {
        _txtServerUrl.Text = _initialConfig.ServerUrl;
        _txtApiKey.Text = _initialConfig.ApiKey;
        _txtAlbumName.Text = _initialConfig.AlbumName;
        _txtDeviceName.Text = _initialConfig.DeviceName;
        _chkStartWithWindows.Checked = AutoStartService.IsEnabled();

        _cmbTheme.SelectedIndex = _initialConfig.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        var languageIndex = Array.FindIndex(Loc.SupportedLanguages, l => l.Code == _initialConfig.Language);
        _cmbLanguage.SelectedIndex = Math.Max(0, languageIndex);

        _lstDirectories.Items.Clear();
        foreach (var dir in _directories) _lstDirectories.Items.Add(dir);

        _chkSyncEnabled.Checked = _initialConfig.SyncEnabled;
        _txtSyncPhotoFolder.Text = _initialConfig.SyncPhotoFolder;
        _txtSyncVideoFolder.Text = _initialConfig.SyncVideoFolder;
        if (_initialConfig.SyncMode == "Original") _rdoSyncOriginal.Checked = true;
        else _rdoSyncThumbnail.Checked = true;
        _chkSyncDeleteRemote.Checked = _initialConfig.SyncDeleteRemoteOnLocalDelete;
        _chkSyncOrganizeByAlbum.Checked = _initialConfig.SyncOrganizeByAlbum;
    }

    private void OnAddDirectoryClicked(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = Loc.T("settings.chooseFolderDialog") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var path = dialog.SelectedPath;
        if (_directories.Any(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, Loc.T("settings.folderAlreadyListed"), Loc.T("app.name"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _directories.Add(path);
        _lstDirectories.Items.Add(path);
        RefreshExclusionTree();
    }

    private void ChooseSyncFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog { Description = Loc.T("sync.chooseFolderDialog") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        target.Text = dialog.SelectedPath;
    }

    /// Estaa latauskansion paallekkaisuuden minka tahansa tarkkailtavan (lataus-Immichiin)
    /// kansion kanssa - muuten jokainen ladattu tiedosto tunnistettaisiin heti uutena
    /// paikallisena tiedostona ja ladattaisiin takaisin Immichiin, loputtomassa silmukassa.
    private bool IsSyncFolderOverlappingWatched(string syncFolder)
    {
        var normalizedSync = NormalizeFolderPath(syncFolder);
        foreach (var dir in _directories)
        {
            var normalizedDir = NormalizeFolderPath(dir);
            if (string.Equals(normalizedSync, normalizedDir, StringComparison.OrdinalIgnoreCase)) return true;
            if (normalizedSync.StartsWith(normalizedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            if (normalizedDir.StartsWith(normalizedSync + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string NormalizeFolderPath(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    private void OnRemoveDirectoryClicked(object? sender, EventArgs e)
    {
        if (_lstDirectories.SelectedItem is not string selected) return;
        _directories.Remove(selected);
        _lstDirectories.Items.Remove(selected);
        RefreshExclusionTree();
    }

    // Tagina kaytetaan null-merkkia lapsettomalla "..." -placeholder-solmulla, jotta
    // laiska lataus (BeforeExpand) tunnistaa milloin oikeat alikansiot pitaa hakea.
    private static readonly object LazyPlaceholderTag = new();

    private void RefreshExclusionTree()
    {
        _suppressTreeCheckEvents = true;
        try
        {
            _treeExclusions.Nodes.Clear();

            foreach (var dir in _directories)
            {
                var rootNode = new TreeNode(dir) { Tag = dir, Checked = true };
                _treeExclusions.Nodes.Add(rootNode);
                PopulateChildNodes(rootNode, dir, parentExcluded: false);
                rootNode.Expand();
            }
        }
        finally
        {
            _suppressTreeCheckEvents = false;
        }
    }

    /// Lisaa yhden tason alikansiot annetun solmun alle. Jokainen alikansio, jolla itsellaan
    /// on viela alikansioita, saa nayta placeholder-lapsen jotta puu voi laajentua rajattomasti
    /// - oikeat alikansiot haetaan levylta vasta kun kayttaja avaa kyseisen solmun.
    private void PopulateChildNodes(TreeNode parentNode, string path, bool parentExcluded)
    {
        List<string> subDirs;
        try
        {
            subDirs = Directory.GetDirectories(path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            subDirs = new List<string>();
        }

        foreach (var subDir in subDirs)
        {
            var isChecked = parentExcluded ? false : !_originalExcludeSet.Contains(subDir);
            var childNode = new TreeNode(Path.GetFileName(subDir)) { Tag = subDir, Checked = isChecked };
            parentNode.Nodes.Add(childNode);

            if (HasAnySubDirectory(subDir))
            {
                childNode.Nodes.Add(new TreeNode("...") { Tag = LazyPlaceholderTag });
            }
        }
    }

    private static bool HasAnySubDirectory(string path)
    {
        try { return Directory.EnumerateDirectories(path).Any(); }
        catch { return false; }
    }

    private void OnTreeBeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        var node = e.Node;
        if (node is null || node.Tag is not string path) return;

        // Placeholder-lapsi paikalla -> alikansioita ei ole viela oikeasti haettu levylta.
        if (node.Nodes.Count == 1 && node.Nodes[0].Tag == LazyPlaceholderTag)
        {
            _suppressTreeCheckEvents = true;
            try
            {
                node.Nodes.Clear();
                PopulateChildNodes(node, path, parentExcluded: !node.Checked);
            }
            finally
            {
                _suppressTreeCheckEvents = false;
            }
        }
    }

    private void OnTreeBeforeCheck(object? sender, TreeViewCancelEventArgs e)
    {
        // Juurikansiot (tarkkailtavat kansiot itse) eivat ole poissuljettavissa, vain niiden alikansiot.
        if (e.Node?.Level == 0) e.Cancel = true;
    }

    private void OnTreeAfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (_suppressTreeCheckEvents) return;
        if (e.Node is null) return;

        // Rasti periytyy alaspain (jo ladatuille lapsille) - poissuljettu kansio poissulkee
        // aina koko sisaltonsa, joten UI:n on nayttettava tama selkeasti.
        _suppressTreeCheckEvents = true;
        try
        {
            if (e.Node.Tag is string changedPath)
            {
                _originalExcludeSet.RemoveWhere(p => string.Equals(p, changedPath, StringComparison.OrdinalIgnoreCase) || p.StartsWith(changedPath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                if (!e.Node.Checked) _originalExcludeSet.Add(changedPath);
            }
            SetDescendantsChecked(e.Node, e.Node.Checked);
        }
        finally
        {
            _suppressTreeCheckEvents = false;
        }
    }

    private static void SetDescendantsChecked(TreeNode node, bool isChecked)
    {
        foreach (TreeNode child in node.Nodes)
        {
            if (child.Tag == LazyPlaceholderTag) continue;
            child.Checked = isChecked;
            SetDescendantsChecked(child, isChecked);
        }
    }

    /// Keraa kaikkien rastittamattomien solmujen polut koko (ladatun) puun syvyydelta.
    /// Ei-avattuja (laiskasti lataamattomia) alipuita ei kayda lapi, mutta se on turvallista:
    /// jos yla-kansio on jo poissuljettujen listalla, taustapalvelu sulkee sen koko sisallon
    /// polkuetuliitteen perusteella riippumatta siita onko lapsia yksilollisesti listattu.
    private static void CollectUncheckedPaths(TreeNode node, List<string> results)
    {
        foreach (TreeNode child in node.Nodes)
        {
            if (child.Tag is not string path) continue;
            if (!child.Checked) results.Add(path);
            CollectUncheckedPaths(child, results);
        }
    }

    private async void OnTestConnectionClicked(object? sender, EventArgs e)
    {
        var serverUrl = _txtServerUrl.Text.Trim();
        var apiKey = _txtApiKey.Text.Trim();

        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            _lblTestResult.ForeColor = Color.DarkOrange;
            _lblTestResult.Text = Loc.T("settings.testMissingFields");
            return;
        }

        if (!ImmichClient.IsValidServerUrl(serverUrl))
        {
            _lblTestResult.ForeColor = Color.Firebrick;
            _lblTestResult.Text = "Palvelimen osoitteen tulee olla http- tai https-osoite.";
            return;
        }

        serverUrl = ImmichClient.NormalizeServerUrl(serverUrl);
        _txtServerUrl.Text = serverUrl;

        _lblTestResult.ForeColor = _palette.Text;
        _lblTestResult.Text = Loc.T("settings.testing");

        try
        {
            using var client = new ImmichClient(serverUrl, apiKey);
            var albums = await client.GetAlbumsAsync();
            _lblTestResult.ForeColor = Color.SeaGreen;
            _lblTestResult.Text = Loc.T("settings.testOk", albums.Count);
        }
        catch (Exception ex)
        {
            _lblTestResult.ForeColor = Color.Firebrick;
            _lblTestResult.Text = Loc.T("settings.testError", ex.Message);
        }
    }

    private List<string> BuildExclusions()
    {
        var results = _originalExcludeSet.Where(p => _directories.Any(d => p.StartsWith(d.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (TreeNode node in _treeExclusions.Nodes) CollectUncheckedPaths(node, results);
        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        var serverUrl = _txtServerUrl.Text.Trim();
        var apiKey = _txtApiKey.Text.Trim();

        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(this, Loc.T("settings.missingRequiredFields"), Loc.T("app.name"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!ImmichClient.IsValidServerUrl(serverUrl))
        {
            MessageBox.Show(this, "Palvelimen osoitteen tulee olla http- tai https-osoite.", Loc.T("app.name"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        serverUrl = ImmichClient.NormalizeServerUrl(serverUrl);
        _txtServerUrl.Text = serverUrl;

        if (_directories.Count == 0)
        {
            MessageBox.Show(this, Loc.T("settings.noFolders"), Loc.T("app.name"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var syncPhotoFolder = _txtSyncPhotoFolder.Text.Trim();
        var syncVideoFolder = _txtSyncVideoFolder.Text.Trim();
        if (_chkSyncEnabled.Checked)
        {
            if (string.IsNullOrWhiteSpace(syncPhotoFolder) || string.IsNullOrWhiteSpace(syncVideoFolder))
            {
                MessageBox.Show(this, Loc.T("sync.missingFolder"), Loc.T("app.name"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Overlap is safe now (downloaded files are marked "known" in the shared upload
            // history, so the watcher never re-uploads them - see PhotoSyncService), but it still
            // mixes small preview files in with the user's real originals if they're not careful,
            // so ask rather than silently allowing or silently blocking.
            if (IsSyncFolderOverlappingWatched(syncPhotoFolder) || IsSyncFolderOverlappingWatched(syncVideoFolder))
            {
                var proceed = MessageBox.Show(this, Loc.T("sync.folderOverlapsWatched"), Loc.T("app.name"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (proceed != DialogResult.Yes) return;
            }
        }

        var excludeDirectories = BuildExclusions();

        var theme = _cmbTheme.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        var languageIndex = Math.Max(0, _cmbLanguage.SelectedIndex);
        var language = Loc.SupportedLanguages[languageIndex].Code;

        var updated = new AppConfig
        {
            ServerUrl = serverUrl,
            ApiKey = apiKey,
            AlbumName = _txtAlbumName.Text.Trim(),
            DeviceName = string.IsNullOrWhiteSpace(_txtDeviceName.Text) ? Environment.MachineName : _txtDeviceName.Text.Trim(),
            DeviceId = _initialConfig.DeviceId,
            Directories = _directories.ToList(),
            ExcludeDirectories = excludeDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Theme = theme,
            Language = language,
            SyncEnabled = _chkSyncEnabled.Checked,
            SyncPhotoFolder = syncPhotoFolder,
            SyncVideoFolder = syncVideoFolder,
            SyncMode = _rdoSyncOriginal.Checked ? "Original" : "Thumbnail",
            SyncDeleteRemoteOnLocalDelete = _chkSyncDeleteRemote.Checked,
            SyncOrganizeByAlbum = _chkSyncOrganizeByAlbum.Checked,
        };

        if (_chkStartWithWindows.Checked) AutoStartService.Enable();
        else AutoStartService.Disable();

        ResultConfig = updated;
        DialogResult = DialogResult.OK;
        Close();
    }

    private async void OnCheckForUpdatesClicked(object? sender, EventArgs e)
    {
        _btnDownloadUpdate.Visible = false;
        _pendingUpdate = null;
        _lblUpdateResult.ForeColor = _palette.Text;
        _lblUpdateResult.Text = Loc.T("settings.checkingForUpdates");

        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            if (UpdateService.IsNewer(result.LatestVersion, UpdateService.CurrentVersion))
            {
                _pendingUpdate = result;
                _lblUpdateResult.ForeColor = Color.SeaGreen;
                _lblUpdateResult.Text = Loc.T("settings.updateAvailable", result.TagName);
                _btnDownloadUpdate.Visible = result.DownloadUrl is not null;
            }
            else
            {
                _lblUpdateResult.ForeColor = _palette.Text;
                _lblUpdateResult.Text = Loc.T("settings.upToDate");
            }
        }
        catch (Exception ex)
        {
            _lblUpdateResult.ForeColor = Color.Firebrick;
            _lblUpdateResult.Text = Loc.T("settings.updateCheckFailed", ex.Message);
        }
    }

    private async void OnDownloadAndInstallClicked(object? sender, EventArgs e)
    {
        if (_pendingUpdate is not { DownloadUrl: { } downloadUrl } pendingUpdate) return;

        var confirm = MessageBox.Show(this, Loc.T("settings.updateInstallConfirm"), Loc.T("app.name"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _btnDownloadUpdate.Enabled = false;
        var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        string installerPath;

        try
        {
            installerPath = await _updateService.DownloadInstallerAsync(downloadUrl, fileName, (done, total) =>
            {
                var percent = total > 0 ? (int)(done * 100 / total) : 0;
                _lblUpdateResult.ForeColor = _palette.Text;
                _lblUpdateResult.Text = Loc.T("settings.downloadingUpdate", percent);
            }, expectedDigest: pendingUpdate.Digest, expectedSize: pendingUpdate.Size);
        }
        catch (Exception ex)
        {
            _lblUpdateResult.ForeColor = Color.Firebrick;
            AppLogger.Log($"UPDATE DOWNLOAD FAILED: {ex}");
            _lblUpdateResult.Text = Loc.T("settings.updateDownloadFailed", ex.Message);
            _btnDownloadUpdate.Enabled = true;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            _onExitRequested();
        }
        catch (Exception)
        {
            // Windows Defender/SmartScreen not infrequently quarantines unsigned, self-contained
            // .NET executables as a false positive (e.g. "Trojan:Win32/Wacatac.B!ml") right as
            // they're launched, even though the download itself succeeded moments earlier - a
            // known limitation of this project's unsigned build, not something this code can fix
            // (see the "Reduce antivirus false positives" commit). Point at the release page
            // instead of surfacing the raw exception, so the user has somewhere to go.
            _lblUpdateResult.ForeColor = Color.Firebrick;
            _lblUpdateResult.Text = Loc.T("settings.updateLaunchBlocked");
            _btnDownloadUpdate.Enabled = true;

            if (pendingUpdate.ReleaseUrl is { } releaseUrl)
            {
                try { Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true }); }
                catch { /* best effort */ }
            }
        }
    }
}
