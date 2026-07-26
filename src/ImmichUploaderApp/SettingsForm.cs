using ImmichUploaderApp.Models;
using ImmichUploaderApp.Services;

namespace ImmichUploaderApp;

public sealed class SettingsForm : Form
{
    private readonly AppConfig _initialConfig;
    private readonly Palette _palette;
    private readonly HashSet<string> _originalExcludeSet;
    private readonly List<string> _directories;

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

    public AppConfig? ResultConfig { get; private set; }

    public SettingsForm(AppConfig config)
    {
        _initialConfig = config;
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
        MinimumSize = new Size(480, 320);
        MaximumSize = new Size(
            Math.Min(720, workingArea.Width - 40),
            Math.Max(300, workingArea.Height - 40));
        ClientSize = new Size(
            Math.Min(560, MaximumSize.Width),
            Math.Min(700, MaximumSize.Height));
    }

    private const int ContentWidth = 500;

    private void BuildLayout()
    {
        // Painikkeet AutoSize+MinimumSize -yhdistelmalla: teksti ei koskaan leikkaudu,
        // riippumatta fontin koosta, kielesta tai DPI-skaalauksesta.
        var btnCancel = MakeDialogButton(Loc.T("settings.cancel"));
        btnCancel.DialogResult = DialogResult.Cancel;
        var btnSave = MakeDialogButton(Loc.T("settings.save"));
        btnSave.Click += OnSaveClicked;

        // Alaosan Tallenna/Peruuta-painikkeet omaan Dock=Bottom-paneeliin niin ne
        // pysyvat aina nakyvissa ja erillaan vieritettavasta sisallosta.
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

        // Paakontit: pystysuuntainen, tayttaa lomakkeen ja vierittaa itse jos sisalto ei mahdu -
        // ei AutoSizea (se kasvattaisi aina koko sisallon mukaan eika vierittaisi koskaan).
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12),
            BackColor = _palette.Background,
        };

        // Ulkoasu ja kieli ensimmaisena: yleisia sovellusasetuksia, ei palvelinkohtaisia -
        // ja aina nakyvissa ilman vierittamista, koska nailla on tapana jaada piiloon listan
        // hankalasti loytyvaan pohjaan.
        content.Controls.Add(MakeLabel(Loc.T("settings.themeLabel"), new Padding(0, 6, 0, 2)));
        StyleComboBox(_cmbTheme);
        _cmbTheme.Items.AddRange(new object[] { Loc.T("settings.themeSystem"), Loc.T("settings.themeLight"), Loc.T("settings.themeDark") });
        content.Controls.Add(_cmbTheme);

        content.Controls.Add(MakeLabel(Loc.T("settings.languageLabel"), new Padding(0, 10, 0, 2)));
        StyleComboBox(_cmbLanguage);
        foreach (var (_, name) in Loc.SupportedLanguages) _cmbLanguage.Items.Add(name);
        content.Controls.Add(_cmbLanguage);

        content.Controls.Add(new Panel { Width = ContentWidth, Height = 1, BackColor = _palette.Divider, Margin = new Padding(0, 16, 0, 4) });

        content.Controls.Add(MakeLabel(Loc.T("settings.serverUrlLabel"), new Padding(0, 6, 0, 2)));
        StyleTextBox(_txtServerUrl);
        _txtServerUrl.Width = ContentWidth;
        content.Controls.Add(_txtServerUrl);

        content.Controls.Add(MakeLabel(Loc.T("settings.apiKeyLabel"), new Padding(0, 10, 0, 2)));
        var apiKeyPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0), BackColor = _palette.Background };
        StyleTextBox(_txtApiKey);
        _txtApiKey.Width = 380;
        _chkShowKey.AutoSize = true;
        StyleCheckBox(_chkShowKey);
        _chkShowKey.CheckedChanged += (_, _) => _txtApiKey.UseSystemPasswordChar = !_chkShowKey.Checked;
        apiKeyPanel.Controls.Add(_txtApiKey);
        apiKeyPanel.Controls.Add(_chkShowKey);
        content.Controls.Add(apiKeyPanel);

        var testPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 0), BackColor = _palette.Background };
        var btnTest = StyleButton(new Button { Text = Loc.T("settings.testConnection"), AutoSize = true });
        btnTest.Click += OnTestConnectionClicked;
        testPanel.Controls.Add(btnTest);
        _lblTestResult.Margin = new Padding(10, 6, 0, 0);
        _lblTestResult.ForeColor = _palette.Text;
        testPanel.Controls.Add(_lblTestResult);
        content.Controls.Add(testPanel);

        content.Controls.Add(MakeLabel(Loc.T("settings.albumNameLabel"), new Padding(0, 14, 0, 2)));
        StyleTextBox(_txtAlbumName);
        _txtAlbumName.Width = 300;
        content.Controls.Add(_txtAlbumName);

        content.Controls.Add(MakeLabel(Loc.T("settings.deviceNameLabel"), new Padding(0, 10, 0, 2)));
        StyleTextBox(_txtDeviceName);
        _txtDeviceName.Width = 300;
        content.Controls.Add(_txtDeviceName);

        content.Controls.Add(MakeLabel(Loc.T("settings.watchedFoldersLabel"), new Padding(0, 14, 0, 2)));
        StyleListBox(_lstDirectories);
        _lstDirectories.Width = ContentWidth;
        _lstDirectories.Height = 80;
        content.Controls.Add(_lstDirectories);

        var dirButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 0), BackColor = _palette.Background };
        var btnAddDir = StyleButton(new Button { Text = Loc.T("settings.addFolder"), AutoSize = true });
        btnAddDir.Click += OnAddDirectoryClicked;
        var btnRemoveDir = StyleButton(new Button { Text = Loc.T("settings.removeSelected"), AutoSize = true });
        btnRemoveDir.Click += OnRemoveDirectoryClicked;
        dirButtons.Controls.Add(btnAddDir);
        dirButtons.Controls.Add(btnRemoveDir);
        content.Controls.Add(dirButtons);

        content.Controls.Add(MakeLabel(Loc.T("settings.exclusionsLabel"), new Padding(0, 14, 0, 2), ContentWidth));
        StyleTreeView(_treeExclusions);
        _treeExclusions.Width = ContentWidth;
        _treeExclusions.Height = 220;
        _treeExclusions.BeforeCheck += OnTreeBeforeCheck;
        _treeExclusions.AfterCheck += OnTreeAfterCheck;
        _treeExclusions.BeforeExpand += OnTreeBeforeExpand;
        content.Controls.Add(_treeExclusions);

        _chkStartWithWindows.AutoSize = true;
        _chkStartWithWindows.Margin = new Padding(0, 16, 0, 4);
        StyleCheckBox(_chkStartWithWindows);
        content.Controls.Add(_chkStartWithWindows);

        // Dock-jarjestys on tarkea: Bottom-ankkuroitu palkki pitaa lisata ENNEN
        // Fill-ankkuroitua sisaltoa, muuten Fill peittaa sen.
        Controls.Add(actionBar);
        Controls.Add(content);
        AcceptButton = null;
        CancelButton = btnCancel;
    }

    private Label MakeLabel(string text, Padding margin, int maxWidth = 0) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = margin,
        ForeColor = _palette.Text,
        MaximumSize = maxWidth > 0 ? new Size(maxWidth, 0) : Size.Empty,
    };

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
}
