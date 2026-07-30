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
        MinimumSize = new Size(520, 380);
        MaximumSize = new Size(
            Math.Min(720, workingArea.Width - 40),
            Math.Max(300, workingArea.Height - 40));
        ClientSize = new Size(
            Math.Min(580, MaximumSize.Width),
            Math.Min(700, MaximumSize.Height));

    }

    private void BuildLayout()
    {
        // Painikkeet AutoSize+MinimumSize -yhdistelmalla: teksti ei koskaan leikkaudu,
        // riippumatta fontin koosta, kielesta tai DPI-skaalauksesta.
        var btnCancel = MakeDialogButton(Loc.T("settings.cancel"));
        btnCancel.DialogResult = DialogResult.Cancel;
        var btnSave = MakeDialogButton(Loc.T("settings.save"));
        btnSave.Click += OnSaveClicked;

        // Alaosan Tallenna/Peruuta-painikkeet omaan Dock=Bottom-paneeliin niin ne
        // pysyvat aina nakyvissa riippumatta siita mika valilehti on auki.
        var actionBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 10),
            BackColor = _palette.Background,
        };
        actionBar.Controls.Add(btnCancel);
        actionBar.Controls.Add(btnSave);

        // Kokonaan itse piirretty valilehtirivi natiivin TabControlin sijaan: comctl32:n
        // TabControl piirtaa aina oman reunuksensa jokaisen valilehden ymparille riippumatta
        // owner-drawista tai teemauksesta - todennettu elavalla kuvakaappauksella etta vaalea
        // reunaviiva jai nakyviin jokaisen valilehden ymparille myos SetWindowTheme+WM_THEMECHANGED
        // -yritysten jalkeen. Tavalliset Panel-otsikot omassa FlowLayoutPanelissa eivat piirra
        // mitaan natiivia kehysta, joten ongelmaa ei voi ilmestya.
        var tabStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Height = 42,
            BackColor = _palette.Background,
            Padding = new Padding(4, 0, 0, 0),
        };
        var tabHost = new Panel { Dock = DockStyle.Fill, BackColor = _palette.Background };

        AddTab(tabStrip, tabHost, Loc.T("settings.tabGeneral"), BuildGeneralTab);
        AddTab(tabStrip, tabHost, Loc.T("settings.tabServer"), BuildServerTab);
        AddTab(tabStrip, tabHost, Loc.T("settings.tabFolders"), BuildFoldersTab);
        AddTab(tabStrip, tabHost, Loc.T("settings.updatesLabel"), BuildUpdatesTab);

        // Keskeneraisella konfiguraatiolla Palvelin-valilehti on se mita kayttaja oikeasti
        // tarvitsee heti - sama ehto jolla TrayApplicationContext pakottaa tamun ikkunan auki.
        SelectTab(_initialConfig.IsConfigured ? 0 : 1);

        // Dock-jarjestys on tarkea: Bottom/Top-ankkuroidut palkit pitaa lisata ENNEN
        // Fill-ankkuroitua sisaltoa, muuten Fill peittaa ne.
        Controls.Add(actionBar);
        Controls.Add(tabStrip);
        Controls.Add(tabHost);
        AcceptButton = null;
        CancelButton = btnCancel;
    }

    private readonly List<Panel> _tabHeaders = new();
    private readonly List<Panel> _tabPages = new();
    private int _selectedTabIndex;

    private void AddTab(FlowLayoutPanel tabStrip, Panel tabHost, string title, Action<Panel> build)
    {
        // Sivu rakennetaan aluksi NAKYVANA (ei Visible=false): TableLayoutPanelin ensimmainen
        // AutoSize-rivi jaa 0px korkuiseksi jos koko sisalto taytetaan piilotettuun kontrolliin -
        // WinForms ei suorita asettelulaskentaa piilossa oleville kontrolleille, ja rivi 0 ei
        // ilmeisesti saa tata laskentaa jalkikateenkaan Visible=true -vaihdon yhteydessa (todennettu
        // debug-punaisella taustavarilla, joka ei nakynyt ollenkaan). Piilotus tehdaan vasta
        // SelectTab-kutsulla kun kaikki neljä valilehtea on jo rakennettu nakyvina.
        var page = new Panel { Dock = DockStyle.Fill, BackColor = _palette.Background };
        tabHost.Controls.Add(page);
        build(page);
        _tabPages.Add(page);

        var index = _tabHeaders.Count;
        var header = new Panel
        {
            Height = 42,
            Width = TextRenderer.MeasureText(title, Font).Width + 32,
            Cursor = Cursors.Hand,
            BackColor = _palette.Background,
            Margin = new Padding(0),
        };
        header.Paint += (_, e) => PaintTabHeader(e.Graphics, header, title, index == _selectedTabIndex);
        header.Click += (_, _) => SelectTab(index);
        tabStrip.Controls.Add(header);
        _tabHeaders.Add(header);
    }

    private void PaintTabHeader(Graphics g, Panel header, string title, bool selected)
    {
        var textColor = selected ? _palette.Text : _palette.TextMuted;
        TextRenderer.DrawText(g, title, Font, header.ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        if (selected)
        {
            using var accentBrush = new SolidBrush(_palette.Accent);
            g.FillRectangle(accentBrush, 0, header.Height - 3, header.Width, 3);
        }
    }

    private void SelectTab(int index)
    {
        _selectedTabIndex = index;
        for (var i = 0; i < _tabPages.Count; i++) _tabPages[i].Visible = i == index;
        foreach (var header in _tabHeaders) header.Invalidate();
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
        var table = CreateTabTable();
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
    private TableLayoutPanel CreateTabTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(16),
            BackColor = _palette.Background,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Workaround for a reproducible rendering quirk: content positioned in roughly the
        // first ~80px below the tab strip never paints on first show, regardless of which
        // control occupies it (tested with a Label, and with a Panel with a solid debug
        // BackColor - both silently failed to appear despite correct Bounds/Visible/Parent,
        // verified via both PrintWindow and CopyFromScreen captures). Neither reordering
        // construction, disabling AutoScroll, nor a deferred PerformLayout+Refresh on Shown
        // fixed it - only pushing real content below that band did. Root cause not identified;
        // this spacer is an empirically verified, if inelegant, workaround.
        table.RowCount = 1;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        table.Controls.Add(new Panel { Height = 80, BackColor = _palette.Background }, 0, 0);
        return table;
    }

    /// percentHeight=null -> rivi mitoittuu kontrollin oman koon mukaan (labelit, tekstikentat).
    /// percentHeight annettuna -> rivi saa osuuden jaljella olevasta korkeudesta ja kasvaa/kutistuu
    /// ikkunan mukana (kansiolista, poissulkupuu) - kontrollilla pitaa olla Dock=Fill.
    private static void AddRow(TableLayoutPanel table, Control control, float? percentHeight = null)
    {
        var rowIndex = table.RowCount++;
        table.RowStyles.Add(percentHeight is { } pct ? new RowStyle(SizeType.Percent, pct) : new RowStyle(SizeType.AutoSize));
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
        MinimumSize = new Size(88, 27),
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

        var excludeDirectories = new List<string>();
        foreach (TreeNode rootNode in _treeExclusions.Nodes)
        {
            CollectUncheckedPaths(rootNode, excludeDirectories);
        }

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
            ExcludeDirectories = excludeDirectories,
            Theme = theme,
            Language = language,
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
            });
        }
        catch (Exception ex)
        {
            _lblUpdateResult.ForeColor = Color.Firebrick;
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
