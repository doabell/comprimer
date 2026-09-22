using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Comprimer.Models;
using Comprimer.Services;

namespace Comprimer.Forms;

public sealed class SettingsForm : Form
{
    private static readonly Color Background = Color.FromArgb(19, 23, 30);
    private static readonly Color Surface = Color.FromArgb(28, 34, 44);
    private static readonly Color Input = Color.FromArgb(36, 44, 57);
    private static readonly Color Border = Color.FromArgb(49, 59, 74);
    private static readonly Color Ink = Color.FromArgb(235, 240, 247);
    private static readonly Color Muted = Color.FromArgb(155, 169, 189);
    private static readonly Color Accent = Color.FromArgb(104, 213, 198);
    private static readonly Color Success = Color.FromArgb(119, 214, 170);
    private static readonly Color Warning = Color.FromArgb(244, 193, 120);
    private readonly ConfigService _config;
    private readonly ExecutableService _exe = new();
    private readonly IRegistryService _registry;
    private readonly AppSettings _settings;
    private readonly ToolTip _pathTips = new();
    private readonly List<Action> _pathRefresh = [];
    private readonly List<Action> _translations = [];
    private readonly List<Control> _initialLayout = [];
    private Label _saveStatus = null!;
    private Label _explorerStatus = null!;
    private Label _explorerHint = null!;
    private Button _install = null!;
    private TableLayoutPanel _sizes = null!;
    private NumericUpDown _newSize = null!;
    private Button _addSize = null!;
    private Panel _page = null!;
    private bool _installed;
    private string? _registeredPath;
    private bool _dirty;
    private bool _building;
    private bool IsFr => _settings.Language == "fr";
    private string T(string en, string fr) => IsFr ? fr : en;
    private static Font F(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    public SettingsForm() : this(new ConfigService()) { }
    internal SettingsForm(ConfigService config) : this(config, new RegistryService()) { }

    internal SettingsForm(ConfigService config, IRegistryService registry)
    {
        SuspendLayout();
        _config = config;
        _registry = registry;
        _settings = config.Load();
        // Migrate old implicit paths once. Saved choices, including cleared paths, stay fixed.
        var paths = _settings.Executables;
        _dirty = _settings.ComprimerPath == null || paths.PngquantPath == null || paths.CjpegPath == null ||
            paths.Img2WebPPath == null || paths.DwebpPath == null;
        _settings.ComprimerPath ??= _registry.GetRegisteredExePath() ?? Application.ExecutablePath;
        paths.PngquantPath ??= _exe.FindInPath("pngquant") ?? "";
        paths.CjpegPath ??= _exe.FindInPath("cjpeg") ?? "";
        paths.Img2WebPPath ??= _exe.FindInPath("cwebp") ?? "";
        paths.DwebpPath ??= _exe.DetectWebPDecoder(paths.Img2WebPPath) ?? "";

        Text = "Comprimer";
        Font = F(9.5f);
        ForeColor = Ink;
        BackColor = Background;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(860, 780);
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        using var iconStream = typeof(SettingsForm).Assembly.GetManifestResourceStream("Comprimer.Resources.app.ico");
        if (iconStream != null) Icon = new Icon(iconStream);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BuildUI();
        ResumeLayout(true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.FromControl(this).WorkingArea;
        var available = new Size(Math.Max(1, area.Width - 48), Math.Max(1, area.Height - 48));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width), Math.Min(MinimumSize.Height, available.Height));
        Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
        if (StartPosition == FormStartPosition.CenterScreen)
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!value || Visible || _page == null)
        {
            base.SetVisibleCore(value);
            return;
        }

        // Native control creation changes preferred sizes; settle them together.
        var controls = Descendants(this).Prepend(this).ToArray();
        foreach (var control in controls) control.SuspendLayout();
        try { base.SetVisibleCore(value); }
        finally
        {
            foreach (var control in controls.Reverse()) control.ResumeLayout(true);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Environment.OSVersion.Version.Build >= 18985)
        {
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        }
    }

    private void BuildUI()
    {
        _building = true;
        var shell = DeferLayout(new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty });
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = DeferLayout(new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3,
            Padding = new Padding(24, 16, 24, 12), Margin = Padding.Empty,
        });
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        using var logoStream = typeof(SettingsForm).Assembly.GetManifestResourceStream("Comprimer.Resources.icon-C.png")!;
        using var sourceLogo = Image.FromStream(logoStream);
        var logo = new PictureBox
        {
            Image = new Bitmap(sourceLogo), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 14, 0), AccessibleName = "Comprimer logo", TabStop = false,
        };
        logo.Disposed += (_, _) => logo.Image?.Dispose();
        header.Controls.Add(logo, 0, 0);
        var title = Stack();
        Add(title, Label("Comprimer", 20, Ink, FontStyle.Bold));
        header.Controls.Add(title, 1, 0);
        var language = ActionButton("Français", "English");
        language.Name = "Language";
        language.Anchor = AnchorStyles.Right;
        language.Click += (_, _) => SwitchLanguage();
        header.Controls.Add(language, 2, 0);
        shell.Controls.Add(header, 0, 0);

        _page = DeferLayout(new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty, Name = "SettingsPage" });
        var body = Stack(24);
        body.Padding = new Padding(24, 0, 24, 8);
        body.Dock = DockStyle.Top;
        _page.Controls.Add(body);
        shell.Controls.Add(_page, 0, 1);
        BuildExplorer(body);
        BuildEncoders(body);

        var footer = DeferLayout(new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(24, 14, 24, 14),
            BackColor = Surface, Margin = Padding.Empty,
        });
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _saveStatus = Label("", 9, Muted);
        _saveStatus.Anchor = AnchorStyles.Left;
        footer.Controls.Add(_saveStatus, 0, 0);
        var save = ActionButton("Save changes", "Enregistrer", primary: true);
        save.Name = "SaveChanges";
        save.Click += (_, _) => Save();
        footer.Controls.Add(save, 1, 0);
        shell.Controls.Add(footer, 0, 2);
        AcceptButton = save;
        _translations.Add(RenderStatus);
        _translations.Add(TranslateSizeNames);
        _building = false;
        RefreshStatus();
        Controls.Add(shell);
        // Layout each complete panel once, with its ancestors still suspended.
        foreach (var control in _initialLayout.AsEnumerable().Reverse()) control.ResumeLayout(true);
        _initialLayout.Clear();
    }

    private void BuildExplorer(TableLayoutPanel body)
    {
        var card = Card(body, "Explorer shortcuts", "Raccourcis de l'Explorateur");
        card.Name = "ExplorerShortcuts";
        var installation = DeferLayout(new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = Padding.Empty });
        installation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        installation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _explorerStatus = Label("", 9, Success);
        _explorerStatus.Anchor = AnchorStyles.Left;
        installation.Controls.Add(_explorerStatus, 0, 0);
        _install = Button("");
        _install.Name = "ExplorerInstallation";
        _install.Click += (_, _) =>
        {
            try
            {
                if (_registry.IsRegistered()) _registry.Unregister();
                else
                {
                    ValidateComprimerPath();
                    _config.Save(_settings);
                    _registry.Register(_settings, Application.ExecutablePath);
                    _dirty = false;
                }
                RefreshStatus();
            }
            catch (Exception ex) { ShowError(ex); }
        };
        installation.Controls.Add(_install, 1, 0);
        Add(card, installation);
        AddPath(card, "Comprimer", "Comprimer executable", _settings.ComprimerPath!,
            value => _settings.ComprimerPath = value, () => ExecutableService.ExistingPath(Application.ExecutablePath), "Exécutable Comprimer");
        _explorerHint = Description("");
        _explorerHint.Name = "RegisteredPath";
        Add(card, _explorerHint);

        var menuOptions = Flow();
        var nested = Option("Group in a submenu", "Regrouper dans un sous-menu", _settings.NestedMenu, value => _settings.NestedMenu = value);
        nested.Margin = new Padding(0, 4, 28, 4);
        menuOptions.Controls.Add(nested);
        menuOptions.Controls.Add(Option("Overwrite existing files", "Écraser les fichiers existants", _settings.OverwriteOriginal, value => _settings.OverwriteOriginal = value));
        Add(card, menuOptions);
        var auto = Option("Auto (original size)", "Auto (taille d'origine)", _settings.AutoMode, value => _settings.AutoMode = value);
        auto.Name = "AutoMode";
        Translate(() => _pathTips.SetToolTip(auto, T("PNG<JPG: PNG · otherwise: JPG+WebP", "PNG<JPG: PNG · sinon: JPG+WebP")));
        Add(card, auto);
        Add(card, Caption("Chooses PNG or JPG+WebP", "Choisit PNG ou JPG+WebP"));
        BuildResize(card);

        Add(card, Caption("Format actions", "Actions par format", 10, Ink, FontStyle.Bold));
        var grid = DeferLayout(new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
            ColumnCount = 4, RowCount = 3, Margin = new Padding(0, 2, 0, 2), Name = "FormatActions",
        });
        for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        for (int i = 0; i < 3; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label[] headers = [Caption("Source", "Source", 8.5f), Caption("Optimize", "Optimiser", 8.5f),
            Label("→ WebP", 8.5f, Muted), Label("→ JPG", 8.5f, Muted)];
        for (int i = 0; i < headers.Length; i++) grid.Controls.Add(headers[i], i, 0);
        AddFormatRow(grid, 1, "JPG", _settings.Jpg, true, true, false);
        AddFormatRow(grid, 2, "PNG", _settings.Png, true, true, true);
        Add(card, grid);
    }

    private void AddFormatRow(TableLayoutPanel grid, int row, string name, FormatSettings settings, bool optimize, bool webp, bool jpg)
    {
        var label = Label(name, 9, Ink, FontStyle.Bold);
        label.Margin = new Padding(0, 6, 0, 6);
        grid.Controls.Add(label, 0, row);
        void Cell(int column, bool supported, bool value, string action, string french, Action<bool> update)
        {
            if (supported)
            {
                var check = Check("", value, update);
                check.Name = $"{name}.{column + 1}";
                Translate(() => check.AccessibleName = $"{name}: {T(action, french)}");
                check.Anchor = AnchorStyles.Left;
                grid.Controls.Add(check, column, row);
            }
            else
            {
                var dash = Label("—", 10, Muted);
                dash.Anchor = AnchorStyles.Left;
                grid.Controls.Add(dash, column, row);
            }
        }
        Cell(1, optimize, settings.Optimize, "Optimize", "Optimiser", value => settings.Optimize = value);
        Cell(2, webp, settings.ConvertToWebP, "WebP", "WebP", value => settings.ConvertToWebP = value);
        Cell(3, jpg, settings.ConvertToJpg, "JPG", "JPG", value => settings.ConvertToJpg = value);
    }

    private void BuildResize(TableLayoutPanel card)
    {
        var sharedSizes = Caption("Sizes (Auto + Resize)", "Tailles (Auto + Réduire)", 10, Ink, FontStyle.Bold);
        sharedSizes.Name = "SharedSizes";
        Add(card, sharedSizes);
        var row = Flow();
        var mode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed,
            BackColor = Input, ForeColor = Ink, Width = 180, Name = "ResizeDimension",
            Margin = new Padding(0, 4, 16, 0),
        };
        mode.Items.AddRange([T("Longest side", "Plus long côté"), T("Width", "Largeur"), T("Height", "Hauteur")]);
        Translate(() => mode.AccessibleName = T("Resize dimension", "Dimension à réduire"));
        _translations.Add(() =>
        {
            int selected = mode.SelectedIndex;
            mode.BeginUpdate();
            mode.Items[0] = T("Longest side", "Plus long côté");
            mode.Items[1] = T("Width", "Largeur");
            mode.Items[2] = T("Height", "Hauteur");
            mode.SelectedIndex = selected;
            mode.EndUpdate();
        });
        mode.DrawItem += (_, e) =>
        {
            using var brush = new SolidBrush(e.State.HasFlag(DrawItemState.Selected) ? Border : Input);
            e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, mode.Items[e.Index]?.ToString(), mode.Font,
                e.Bounds, Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        };
        mode.SelectedIndex = Math.Clamp((int)_settings.DownscaleMode, 0, 2);
        mode.SelectedIndexChanged += (_, _) =>
        {
            if (_building) return;
            _settings.DownscaleMode = (DownscaleMode)mode.SelectedIndex;
            Changed();
        };
        row.Controls.Add(mode);
        var size = _newSize = Number(1024, 50, 10000);
        size.Name = "NewSize";
        Translate(() => size.AccessibleName = T("New size in pixels", "Nouvelle taille en pixels"));
        row.Controls.Add(size);
        row.Controls.Add(Label("px", 9, Muted));
        var add = _addSize = ActionButton("Add size", "Ajouter");
        add.Name = "AddSize";
        size.ValueChanged += (_, _) => add.Enabled = !_settings.AvailableSizes.Contains((int)size.Value);
        add.Click += (_, _) =>
        {
            if (_settings.AvailableSizes.Contains((int)size.Value)) return;
            _settings.AvailableSizes.Add((int)size.Value);
            _settings.AvailableSizes.Sort();
            RefreshSizes();
            Changed();
            var added = _sizes.Controls.Find($"AutoResize.{(int)size.Value}", false).Single();
            added.Focus();
            _page.ScrollControlIntoView(added);
        };
        row.Controls.Add(add);
        Add(card, row);
        _sizes = DeferLayout(new TableLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Top, ColumnCount = 4, RowCount = 1,
            Margin = new Padding(0, 8, 0, 8), Name = "SizeShortcuts",
        });
        _sizes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _sizes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37.5f));
        _sizes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37.5f));
        _sizes.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _sizes.Controls.Add(Caption("Size", "Taille", 8.5f), 0, 0);
        _sizes.Controls.Add(Caption("Auto + resize", "Auto + réduire", 8.5f), 1, 0);
        var resizeHeader = Caption("Resize only", "Réduire seulement", 8.5f);
        _sizes.Controls.Add(resizeHeader, 2, 0);
        Translate(() => _pathTips.SetToolTip(resizeHeader, T("Keeps source format", "Conserve le format source")));
        Add(card, _sizes);
        RefreshSizes();

        var sources = Flow();
        var sourceLabel = Caption("Resize sources", "Sources à réduire");
        sourceLabel.Margin = new Padding(0, 6, 20, 8);
        sources.Controls.Add(sourceLabel);
        foreach (var (name, format) in new[] { ("JPG", _settings.Jpg), ("PNG", _settings.Png), ("WebP", _settings.WebP) })
        {
            var check = Check(name, format.Downscale, value => format.Downscale = value);
            check.Name = $"{name}.1";
            check.Margin = new Padding(0, 6, 24, 8);
            Translate(() => check.AccessibleName = T($"Resize {name} sources", $"Réduire les sources {name}"));
            sources.Controls.Add(check);
        }
        Add(card, sources);
    }

    private void BuildEncoders(TableLayoutPanel body)
    {
        var card = Card(body, "Encoders & quality", "Encodeurs et qualité");
        card.Name = "Encoders";
        var paths = _settings.Executables;
        AddPath(card, "pngquant", "PNG · pngquant", paths.PngquantPath!, value => paths.PngquantPath = value, () => _exe.FindInPath("pngquant"));
        var pngQuality = QualityRow(card, "PNG", _settings.Encoders.PngQuality, value => _settings.Encoders.PngQuality = value);
        var minRow = Flow();
        minRow.Controls.Add(Caption("Minimum PNG quality", "Qualité PNG minimale"));
        var pngMin = Number(_settings.Encoders.EffectivePngMinQuality, 0, _settings.Encoders.PngQuality);
        pngMin.Name = "MinimumPngQuality";
        Translate(() => pngMin.AccessibleName = T("Minimum PNG quality", "Qualité PNG minimale"));
        Translate(() => _pathTips.SetToolTip(pngMin, T("Below minimum: lossless PNG", "Repli : PNG sans perte")));
        pngMin.ValueChanged += (_, _) => { _settings.Encoders.PngMinQuality = (int)pngMin.Value; Changed(); };
        pngQuality.ValueChanged += (_, _) => pngMin.Maximum = pngQuality.Value;
        minRow.Controls.Add(pngMin);
        Add(card, minRow);
        AddPath(card, "cjpeg", "JPG · mozjpeg (cjpeg)", paths.CjpegPath!, value => paths.CjpegPath = value, () => _exe.FindInPath("cjpeg"));
        QualityRow(card, "JPG", _settings.Encoders.JpgQuality, value => _settings.Encoders.JpgQuality = value);
        AddPath(card, "cwebp", "WebP · cwebp", paths.Img2WebPPath!, value => paths.Img2WebPPath = value, () => _exe.FindInPath("cwebp"));
        QualityRow(card, "WebP", _settings.Encoders.WebPQuality, value => _settings.Encoders.WebPQuality = value);
        AddPath(card, "dwebp", "WebP decoder · dwebp", paths.DwebpPath!, value => paths.DwebpPath = value,
            () => _exe.DetectWebPDecoder(paths.Img2WebPPath), "Décodeur WebP · dwebp");
    }

    private void AddPath(TableLayoutPanel parent, string name, string title, string path, Action<string> update, Func<string?> detectPath, string? frenchTitle = null)
    {
        var block = Stack();
        block.Margin = new Padding(0, 10, 0, 2);
        var heading = DeferLayout(new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = Padding.Empty });
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titleLabel = Caption(title, frenchTitle ?? title, 9, Ink, FontStyle.Bold);
        heading.Controls.Add(titleLabel, 0, 0);
        var status = Label("", 8.5f, Muted);
        status.Name = $"Status.{name}";
        heading.Controls.Add(status, 1, 0);
        Add(block, heading);
        var row = DeferLayout(new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, Margin = Padding.Empty });
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var input = new TextBox
        {
            Text = path,
            Name = $"Path.{name}", AccessibleName = $"{name} path", BorderStyle = BorderStyle.FixedSingle,
            BackColor = Input, ForeColor = Ink, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 10, 0),
        };
        Translate(() => input.PlaceholderText = T("Executable path", "Chemin de l'exécutable"));
        string pathState = "";
        void RenderPathStatus()
        {
            status.Text = pathState switch
            {
                "ready" => T("Ready", "Prêt"),
                "empty" => T("Not set", "Non défini"),
                "undetected" => T("Not detected", "Non détecté"),
                _ => T("Path not found", "Chemin introuvable"),
            };
            status.ForeColor = pathState == "ready" ? Success : Warning;
        }
        _translations.Add(RenderPathStatus);
        void Refresh()
        {
            bool exists = ExecutableService.ExistingPath(input.Text) != null;
            pathState = exists ? "ready" : string.IsNullOrWhiteSpace(input.Text) ? "empty" : "missing";
            RenderPathStatus();
            _pathTips.SetToolTip(input, input.Text);
        }
        _pathRefresh.Add(Refresh);
        input.TextChanged += (_, _) => { update(input.Text.Trim().Trim('"')); Changed(); Refresh(); };
        row.Controls.Add(input, 0, 0);
        var detect = ActionButton("Detect", "Détecter");
        detect.Name = $"Detect.{name}";
        Translate(() => detect.AccessibleName = $"{name}: {detect.Text}");
        detect.Click += (_, _) =>
        {
            var found = detectPath();
            if (found != null) { input.Text = found; Refresh(); }
            else { pathState = "undetected"; RenderPathStatus(); }
        };
        row.Controls.Add(detect, 1, 0);
        var browse = ActionButton("Browse…", "Parcourir…");
        browse.Name = $"Browse.{name}";
        Translate(() => browse.AccessibleName = $"{name}: {browse.Text}");
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = titleLabel.Text, Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*" };
            if (ExecutableService.ExistingPath(input.Text) is string current) dialog.FileName = current;
            if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.FileName;
        };
        row.Controls.Add(browse, 2, 0);
        Add(block, row);
        Add(parent, block);
    }


    private NumericUpDown QualityRow(TableLayoutPanel parent, string name, int value, Action<int> update)
    {
        var row = DeferLayout(new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 3, Margin = new Padding(0, 8, 0, 4) });
        row.RowCount = 1;
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var label = Caption("Quality", "Qualité", 9, Ink);
        label.Anchor = AnchorStyles.Left;
        row.Controls.Add(label, 0, 0);
        var slider = new QualityTrackBar { Minimum = 0, Maximum = 100, Value = value, TickStyle = TickStyle.None,
            AutoSize = false, Height = 32, Dock = DockStyle.Fill, SmallChange = 1, LargeChange = 5,
            Name = $"Quality.{name}", Margin = new Padding(0, 0, 12, 0) };
        Translate(() => slider.AccessibleName = T($"{name} quality", $"Qualité {name}"));
        row.Controls.Add(slider, 1, 0);
        var number = Number(value, 0, 100);
        number.Name = $"QualityValue.{name}";
        Translate(() => number.AccessibleName = T($"{name} quality value", $"Valeur de qualité {name}"));
        number.Anchor = AnchorStyles.Right;
        row.Controls.Add(number, 2, 0);
        slider.ValueChanged += (_, _) => number.Value = slider.Value;
        number.ValueChanged += (_, _) => { slider.Value = (int)number.Value; update((int)number.Value); Changed(); };
        Add(parent, row);
        return number;
    }

    private void RefreshSizes()
    {
        Padding ScalePadding(Padding value) => new(LogicalToDeviceUnits(value.Left), LogicalToDeviceUnits(value.Top),
            LogicalToDeviceUnits(value.Right), LogicalToDeviceUnits(value.Bottom));
        void AddCell(Control control, int column, int row)
        {
            // New rows need the same spacing as controls scaled when the window opened.
            if (IsHandleCreated)
            {
                control.Margin = ScalePadding(control.Margin);
                control.Padding = ScalePadding(control.Padding);
                control.MinimumSize = LogicalToDeviceUnits(control.MinimumSize);
            }
            _sizes.Controls.Add(control, column, row);
        }
        _sizes.SuspendLayout();
        foreach (var control in _sizes.Controls.Cast<Control>().Where(control => _sizes.GetRow(control) > 0).ToArray())
            control.Dispose();
        _sizes.RowCount = 1;
        _sizes.RowStyles.Clear();
        _sizes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var sizes = _settings.AvailableSizes.Where(size => size > 0).Distinct().Order().ToArray();
        foreach (var size in sizes)
        {
            int row = _sizes.RowCount++;
            _sizes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = Label($"{size} px", 9, Ink);
            label.Anchor = AnchorStyles.Left;
            AddCell(label, 0, row);
            var actions = _settings.GetSizeActions(size);
            var auto = Check("", actions.AutoResize, value => actions.AutoResize = value);
            auto.Name = $"AutoResize.{size}";
            auto.Tag = size;
            auto.Anchor = AnchorStyles.Left;
            AddCell(auto, 1, row);
            var resize = Check("", actions.Resize, value => actions.Resize = value);
            resize.Name = $"Resize.{size}";
            resize.Tag = size;
            resize.Anchor = AnchorStyles.Left;
            AddCell(resize, 2, row);
            var remove = Button("×");
            remove.Name = $"RemoveSize.{size}";
            remove.Tag = size;
            remove.ForeColor = Muted;
            remove.Margin = new Padding(0, 2, 0, 2);
            remove.Click += (_, _) =>
            {
                if (_settings.AvailableSizes.Distinct().Count() <= 1) return;
                _settings.AvailableSizes.RemoveAll(value => value == size);
                _settings.SizeActions.Remove(size);
                RefreshSizes();
                Changed();
            };
            remove.Enabled = sizes.Length > 1;
            AddCell(remove, 3, row);
        }
        TranslateSizeNames();
        _addSize.Enabled = !_settings.AvailableSizes.Contains((int)_newSize.Value);
        _sizes.ResumeLayout(true);
    }

    private void TranslateSizeNames()
    {
        foreach (Control control in _sizes.Controls)
        {
            if (control.Tag is not int size) continue;
            control.AccessibleName = control switch
            {
                System.Windows.Forms.Button => T($"Remove {size}px preset", $"Supprimer la taille {size}px"),
                CheckBox when _sizes.GetColumn(control) == 1 => T($"Auto + resize {size}px", $"Auto + réduire {size}px"),
                _ => T($"Resize only {size}px", $"Réduire seulement {size}px"),
            };
            _pathTips.SetToolTip(control, control.AccessibleName);
        }
    }

    private void SwitchLanguage()
    {
        var controls = Descendants(this).Prepend(this).ToArray();
        var scroll = _page.AutoScrollPosition;
        foreach (var control in controls) control.SuspendLayout();
        _building = true;
        try
        {
            _settings.Language = IsFr ? "en" : "fr";
            _dirty = true;
            foreach (var translate in _translations) translate();
        }
        finally
        {
            _building = false;
            // Resume children first while their parents still defer layout.
            foreach (var control in controls.Reverse()) control.ResumeLayout(true);
            _page.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void Translate(Action action)
    {
        _translations.Add(action);
        action();
    }

    private TControl Localize<TControl>(TControl control, string english, string french) where TControl : Control
    {
        Translate(() => control.Text = T(english, french));
        return control;
    }

    private Label Caption(string english, string french, float size = 9, Color? color = null, FontStyle style = FontStyle.Regular) =>
        Localize(Label("", size, color ?? Muted, style), english, french);

    private Button ActionButton(string english, string french, bool primary = false) => Localize(Button("", primary), english, french);

    private CheckBox Option(string english, string french, bool value, Action<bool> update) => Localize(Check("", value, update), english, french);

    private void Changed()
    {
        if (_building) return;
        _dirty = true;
        _saveStatus.Text = T("Unsaved changes", "Modifications non enregistrées");
        _saveStatus.ForeColor = Warning;
    }

    private void Save()
    {
        try
        {
            if (_registry.IsRegistered()) ValidateComprimerPath();
            _config.Save(_settings);
            if (_registry.IsRegistered()) _registry.Register(_settings, Application.ExecutablePath);
            _dirty = false;
            RefreshStatus();
            _saveStatus.Text = T("Changes saved", "Modifications enregistrées");
            _saveStatus.ForeColor = Success;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ValidateComprimerPath() => RegistryService.GetRegistrationPath(_settings, Application.ExecutablePath, _registry.GetRegisteredExePath());

    private void RefreshStatus()
    {
        _installed = _registry.IsRegistered();
        _registeredPath = _installed ? _registry.GetRegisteredExePath() : null;
        RenderStatus();
        foreach (var refresh in _pathRefresh) refresh();
    }

    private void RenderStatus()
    {
        _explorerStatus.Text = _installed ? T("● Enabled", "● Activé") : T("● Disabled", "● Désactivé");
        _explorerStatus.ForeColor = _installed ? Success : Muted;
        _install.Text = _installed ? T("Remove from Explorer", "Retirer de l'Explorateur") : T("Add to Explorer", "Ajouter à l'Explorateur");
        _explorerHint.Text = _registeredPath ?? "";
        _explorerHint.Visible = _registeredPath != null && !string.Equals(_registeredPath, _settings.ComprimerPath, StringComparison.OrdinalIgnoreCase);
        _saveStatus.Text = _dirty ? T("Unsaved changes", "Modifications non enregistrées") : T("Autosaves on close", "Enregistrement à la fermeture");
        _saveStatus.ForeColor = _dirty ? Warning : Muted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _pathTips.Dispose();
        base.Dispose(disposing);
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, T("Could not save changes", "Échec de l'enregistrement"), MessageBoxButtons.OK, MessageBoxIcon.Error);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty) { Save(); if (_dirty) e.Cancel = true; }
        base.OnFormClosing(e);
    }

    private TControl DeferLayout<TControl>(TControl control) where TControl : Control
    {
        control.SuspendLayout();
        _initialLayout.Add(control);
        return control;
    }

    private TableLayoutPanel Stack(int padding = 0)
    {
        var panel = DeferLayout(new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
            Padding = new Padding(padding), Margin = Padding.Empty,
        });
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private static void Add(TableLayoutPanel parent, Control control)
    {
        int row = parent.RowCount++;
        parent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parent.Controls.Add(control, 0, row);
        control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
    }

    private static Label Label(string text, float size, Color color, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text, AutoSize = true, Font = F(size, style), ForeColor = color,
        Margin = new Padding(0, 4, 0, 8), UseMnemonic = false,
    };

    private static Label Description(string text) => Label(text, 9, Muted);

    private TableLayoutPanel Card(TableLayoutPanel body, string title, string frenchTitle)
    {
        var card = Stack(18);
        card.BackColor = Surface;
        card.Dock = DockStyle.Top;
        card.Margin = new Padding(0, 0, 0, 16);
        Add(card, Caption(title, frenchTitle, 12, Ink, FontStyle.Bold));
        Add(body, card);
        return card;
    }

    private FlowLayoutPanel Flow() => DeferLayout(new FlowLayoutPanel
    {
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
        WrapContents = true, Margin = new Padding(0, 4, 0, 4),
    });

    private static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 36), Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Input,
            ForeColor = primary ? Background : Ink, Font = F(9, primary ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0), UseMnemonic = false,
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(163, 199, 255) : Border;
        button.Paint += (_, e) =>
        {
            if (button.Enabled) return;
            var bounds = Rectangle.Inflate(button.ClientRectangle, -1, -1);
            using var background = new SolidBrush(button.BackColor);
            e.Graphics.FillRectangle(background, bounds);
            TextRenderer.DrawText(e.Graphics, button.Text, button.Font, bounds, Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        return button;
    }

    private static NumericUpDown Number(int value, int min, int max) => new()
    {
        Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 74,
        BackColor = Input, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(0, 4, 10, 4), TextAlign = HorizontalAlignment.Center,
    };

    private CheckBox Check(string text, bool value, Action<bool> update)
    {
        var check = new ThemedCheckBox { Text = text, Checked = value, AutoSize = true, ForeColor = Ink,
            Margin = new Padding(0, 6, 0, 8), Cursor = Cursors.Hand };
        check.CheckedChanged += (_, _) => { update(check.Checked); Changed(); };
        return check;
    }

    private sealed class ThemedCheckBox : CheckBox
    {
        public ThemedCheckBox() => SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        public override Size GetPreferredSize(Size proposedSize)
        {
            int box = LogicalToDeviceUnits(20);
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size(box + (Text.Length == 0 ? 0 : LogicalToDeviceUnits(10) + text.Width), Math.Max(box + 4, text.Height + 4));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            int size = LogicalToDeviceUnits(17);
            var box = new Rectangle(1, (Height - size) / 2, size, size);
            using var fill = new SolidBrush(Checked ? Accent : Input);
            using var border = new Pen(Checked ? Accent : Muted);
            e.Graphics.FillRectangle(fill, box);
            e.Graphics.DrawRectangle(border, box);
            if (Checked)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Background, LogicalToDeviceUnits(2));
                e.Graphics.DrawLines(pen, new Point[] { new Point(box.Left + size / 5, box.Top + size / 2),
                    new Point(box.Left + size * 2 / 5, box.Top + size * 3 / 4), new Point(box.Left + size * 4 / 5, box.Top + size / 4) });
            }
            var textBounds = new Rectangle(size + LogicalToDeviceUnits(10), 0, Math.Max(0, Width - size - LogicalToDeviceUnits(10)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Ink, BackColor);
        }
    }

    private sealed class QualityTrackBar : TrackBar
    {
        public QualityTrackBar() => SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        public override Size GetPreferredSize(Size proposedSize) => new(120, LogicalToDeviceUnits(32));

        protected override void OnValueChanged(EventArgs e) { base.OnValueChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            Capture = true;
            SetFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture && e.Button == MouseButtons.Left) SetFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }

        private void SetFromMouse(int x)
        {
            int inset = LogicalToDeviceUnits(10);
            Value = Math.Clamp((int)Math.Round((double)(x - inset) / Math.Max(1, Width - inset * 2) * (Maximum - Minimum)) + Minimum, Minimum, Maximum);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int inset = LogicalToDeviceUnits(10);
            int middle = Height / 2;
            int position = inset + (int)((double)(Value - Minimum) / Math.Max(1, Maximum - Minimum) * (Width - inset * 2));
            using var rail = new Pen(Border, LogicalToDeviceUnits(4)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var fill = new Pen(Accent, LogicalToDeviceUnits(4)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLine(rail, inset, middle, Width - inset, middle);
            e.Graphics.DrawLine(fill, inset, middle, position, middle);
            int radius = LogicalToDeviceUnits(6);
            using var thumb = new SolidBrush(Ink);
            e.Graphics.FillEllipse(thumb, position - radius, middle - radius, radius * 2, radius * 2);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Ink, BackColor);
        }
    }
}
