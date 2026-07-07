using System.Drawing;
using System.Drawing.Drawing2D;
using Comprimer.Models;
using Comprimer.Services;

namespace Comprimer.Forms;

/// <summary>
/// Main settings window for Comprimer — modern single-page layout with FR/EN toggle.
/// </summary>
public sealed class SettingsForm : Form
{
    // Colors
    private static readonly Color BgColor = Color.FromArgb(250, 250, 250);
    private static readonly Color CardColor = Color.White;
    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
    private static readonly Color AccentHover = Color.FromArgb(0, 103, 181);
    private static readonly Color TextColor = Color.FromArgb(32, 32, 32);
    private static readonly Color DimText = Color.FromArgb(120, 120, 120);
    private static readonly Color BorderColor = Color.FromArgb(230, 230, 230);
    private static readonly Color ShadowColor = Color.FromArgb(235, 235, 235);
    private static readonly Color GreenColor = Color.FromArgb(16, 124, 16);
    private static readonly Color RedColor = Color.FromArgb(196, 43, 28);
    private static readonly Color ChipBg = Color.FromArgb(239, 246, 252);
    private static readonly Color DangerBg = Color.FromArgb(253, 231, 233);
    private static readonly Color DangerText = Color.FromArgb(196, 43, 28);
    private static readonly Color DangerHover = Color.FromArgb(241, 210, 213);

    private readonly ConfigService _config;
    private readonly ExecutableService _exe;
    private readonly RegistryService _registry;
    private AppSettings _settings;
    private bool _isFr;

    // Scrollable content
    private Panel _scroll = null!;

    // Install
    private Label _lblInstallStatus = null!;
    private RoundedButton _btnToggleInstall = null!;
    private RoundedButton _btnApply = null!;
    private Label _lblUpdateHint = null!;

    // Options
    private CheckBox _chkNested = null!;
    private CheckBox _chkOverwrite = null!;
    private ComboBox _cboMode = null!;
    private Label _lblModeLabel = null!;

    // Sizes
    private FlowLayoutPanel _sizesFlow = null!;
    private NumericUpDown _nudNewSize = null!;

    // Format ops
    private CheckBox _chkJpgDown = null!;
    private CheckBox _chkJpgWebP = null!;
    private CheckBox _chkJpgMoz = null!;
    private CheckBox _chkPngDown = null!;
    private CheckBox _chkPngWebP = null!;
    private CheckBox _chkPngJpg = null!;
    private CheckBox _chkPngOpt = null!;
    private CheckBox _chkWebPDown = null!;

    // Tools
    private Label _lblPngquantStatus = null!;
    private TextBox _txtPngquant = null!;
    private Button _btnBrowsePngquant = null!;
    private Label _lblCwebpStatus = null!;
    private TextBox _txtCwebp = null!;
    private Button _btnBrowseCwebp = null!;
    private Label _lblCjpegStatus = null!;
    private TextBox _txtCjpeg = null!;
    private Button _btnBrowseCjpeg = null!;

    // Header / language
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private LinkLabel _lnkLang = null!;

    // Section labels (for translation)
    private Label _lblSecExplorer = null!;
    private Label _lblSecOptions = null!;
    private Label _lblSecSizes = null!;
    private Label _lblSecFormats = null!;
    private Label _lblSecTools = null!;

    public SettingsForm()
    {
        _config = new ConfigService();
        _exe = new ExecutableService();
        _registry = new RegistryService();
        _settings = _config.Load();
        _isFr = _settings.Language == "fr";

        BuildUI();
        LoadSettings();
        RefreshAll();
    }

    private static Font F(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    // ── Localization ────────────────────────────────────────
    private string T(string en, string fr) => _isFr ? fr : en;

    private void BuildUI()
    {
        Text = "Comprimer";
        ClientSize = new Size(620, 820);
        MinimumSize = new Size(580, 720);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = BgColor;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(0);

        _scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = BgColor,
            Padding = new Padding(28, 24, 28, 24),
        };
        Controls.Add(_scroll);

        int y = 8;
        int w = _scroll.ClientSize.Width - 56;
        int left = 28;

        // ─── Header ─────────────────────────────────────────
        _lblTitle = new Label
        {
            Text = "Comprimer",
            Font = F(26, FontStyle.Bold),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(left, y),
        };
        _scroll.Controls.Add(_lblTitle);

        _lnkLang = new LinkLabel
        {
            Text = T("En français", "En anglais"),
            Font = F(9.5f),
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Location = new Point(left + w - 110, y + 12),
            LinkColor = AccentColor,
            ActiveLinkColor = AccentHover,
            VisitedLinkColor = AccentColor,
        };
        _lnkLang.Click += LnkLang_Click;
        _scroll.Controls.Add(_lnkLang);

        y += 50;
        _lblSubtitle = new Label
        {
            Text = T("Image compression & conversion for Explorer",
                     "Compression et conversion d'images pour l'Explorateur"),
            Font = F(10),
            ForeColor = DimText,
            AutoSize = true,
            Location = new Point(left + 2, y),
        };
        _scroll.Controls.Add(_lblSubtitle);
        y += 42;

        // ─── 1. Explorer Integration ────────────────────────
        _lblSecExplorer = SectionLabel(_scroll, T("Explorer Integration", "Intégration Explorer"), ref y, left);
        var card1 = Card(_scroll, ref y, left, w, 110);

        _lblInstallStatus = new Label
        {
            Font = F(10, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 18),
        };
        card1.Controls.Add(_lblInstallStatus);

        _lblUpdateHint = new Label
        {
            Font = F(9),
            ForeColor = RedColor,
            AutoSize = true,
            Location = new Point(20, 44),
            Visible = false,
        };
        card1.Controls.Add(_lblUpdateHint);

        var btnPanel = new Panel
        {
            Location = new Point(20, 68),
            Size = new Size(w - 56, 32),
            BackColor = Color.Transparent,
        };
        card1.Controls.Add(btnPanel);

        _btnToggleInstall = new RoundedButton
        {
            Text = "",
            Font = F(9),
            Size = new Size(180, 32),
            Location = new Point(0, 0),
            Cursor = Cursors.Hand,
        };
        _btnToggleInstall.Click += BtnToggleInstall_Click;
        btnPanel.Controls.Add(_btnToggleInstall);

        _btnApply = new RoundedButton
        {
            Text = T("Apply", "Appliquer"),
            Font = F(9),
            Size = new Size(110, 32),
            Location = new Point(194, 0),
            Cursor = Cursors.Hand,
            BackColor = CardColor,
            ForeColor = AccentColor,
            BorderColor = AccentColor,
            HoverBackColor = Color.FromArgb(245, 250, 255),
        };
        _btnApply.Click += BtnApply_Click;
        btnPanel.Controls.Add(_btnApply);

        // ─── 2. Options ─────────────────────────────────────
        _lblSecOptions = SectionLabel(_scroll, T("Options", "Options"), ref y, left);
        var card2 = Card(_scroll, ref y, left, w, 104);

        var optionsTable = new TableLayoutPanel
        {
            Location = new Point(20, 16),
            Size = new Size(w - 56, 72),
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
        };
        optionsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        optionsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        optionsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        optionsTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        card2.Controls.Add(optionsTable);

        _chkNested = Chk(T("Nested submenu", "Sous-menu imbriqué"));
        _chkOverwrite = Chk(T("Overwrite originals", "Écraser les originaux"));
        optionsTable.Controls.Add(_chkNested, 0, 0);
        optionsTable.Controls.Add(_chkOverwrite, 0, 1);

        var modePanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0, 2, 0, 0),
            AutoSize = true,
            BackColor = Color.Transparent,
        };
        _lblModeLabel = new Label
        {
            Text = T("Downscale limit:", "Limite de réduction :"),
            Font = F(9.5f),
            ForeColor = TextColor,
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 0),
        };
        _cboMode = new ComboBox
        {
            Font = F(9.5f),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 140,
            Height = 24,
            Margin = new Padding(0),
        };
        _cboMode.Items.AddRange([
            T("Longest side", "Plus long côté"),
            T("Width only", "Largeur seule"),
            T("Height only", "Hauteur seule"),
        ]);
        _cboMode.SelectedIndex = 0;
        modePanel.Controls.Add(_lblModeLabel);
        modePanel.Controls.Add(_cboMode);
        optionsTable.Controls.Add(modePanel, 1, 0);
        optionsTable.SetRowSpan(modePanel, 2);

        // ─── 3. Downscale Sizes ─────────────────────────────
        _lblSecSizes = SectionLabel(_scroll, T("Downscale Sizes (px)", "Tailles de réduction (px)"), ref y, left);
        var card3 = Card(_scroll, ref y, left, w, 72);

        _sizesFlow = new FlowLayoutPanel
        {
            Location = new Point(16, 14),
            Size = new Size(w - 190, 44),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
        };
        card3.Controls.Add(_sizesFlow);

        _nudNewSize = new NumericUpDown
        {
            Font = F(9.5f),
            Location = new Point(w - 160, 16),
            Size = new Size(72, 24),
            Minimum = 50,
            Maximum = 10000,
            Value = 1024,
            BorderStyle = BorderStyle.FixedSingle,
        };
        card3.Controls.Add(_nudNewSize);

        var btnAdd = new RoundedButton
        {
            Text = "+",
            Font = F(13, FontStyle.Bold),
            Size = new Size(36, 30),
            Location = new Point(w - 80, 13),
            Cursor = Cursors.Hand,
            BackColor = AccentColor,
            ForeColor = Color.White,
            BorderColor = AccentColor,
            HoverBackColor = AccentHover,
        };
        btnAdd.Click += BtnAddSize_Click;
        card3.Controls.Add(btnAdd);

        // ─── 4. Format Operations ───────────────────────────
        _lblSecFormats = SectionLabel(_scroll, T("Format Operations", "Opérations par format"), ref y, left);
        var card4 = Card(_scroll, ref y, left, w, 168);

        var formatGrid = new TableLayoutPanel
        {
            Location = new Point(20, 16),
            Size = new Size(w - 56, 136),
            ColumnCount = 5,
            RowCount = 4,
            BackColor = Color.Transparent,
        };
        formatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
        formatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19.5f));
        formatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19.5f));
        formatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19.5f));
        formatGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19.5f));
        for (int i = 0; i < 4; i++)
            formatGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
        card4.Controls.Add(formatGrid);

        // Header row
        formatGrid.Controls.Add(GridHeader(T("Format", "Format")), 0, 0);
        formatGrid.Controls.Add(GridHeader(T("Downscale", "Réduire")), 1, 0);
        formatGrid.Controls.Add(GridHeader(T("Optimize", "Optimiser")), 2, 0);
        formatGrid.Controls.Add(GridHeader("→ WebP"), 3, 0);
        formatGrid.Controls.Add(GridHeader("→ JPG"), 4, 0);

        // JPG row
        formatGrid.Controls.Add(GridLabel("JPG"), 0, 1);
        _chkJpgDown = GridChk(); formatGrid.Controls.Add(_chkJpgDown, 1, 1);
        _chkJpgMoz = GridChk(); formatGrid.Controls.Add(_chkJpgMoz, 2, 1);
        _chkJpgWebP = GridChk(); formatGrid.Controls.Add(_chkJpgWebP, 3, 1);

        // PNG row
        formatGrid.Controls.Add(GridLabel("PNG"), 0, 2);
        _chkPngDown = GridChk(); formatGrid.Controls.Add(_chkPngDown, 1, 2);
        _chkPngOpt = GridChk(); formatGrid.Controls.Add(_chkPngOpt, 2, 2);
        _chkPngWebP = GridChk(); formatGrid.Controls.Add(_chkPngWebP, 3, 2);
        _chkPngJpg = GridChk(); formatGrid.Controls.Add(_chkPngJpg, 4, 2);

        // WebP row
        formatGrid.Controls.Add(GridLabel("WebP"), 0, 3);
        _chkWebPDown = GridChk(); formatGrid.Controls.Add(_chkWebPDown, 1, 3);

        // ─── 5. External Tools ──────────────────────────────
        _lblSecTools = SectionLabel(_scroll, T("External Tools", "Outils externes"), ref y, left);
        var card5 = Card(_scroll, ref y, left, w, 182);

        int ty = 16;
        AddToolRow(card5, "pngquant", w, ref ty, out _lblPngquantStatus, out _txtPngquant, out _btnBrowsePngquant);
        AddToolRow(card5, "cwebp", w, ref ty, out _lblCwebpStatus, out _txtCwebp, out _btnBrowseCwebp);
        AddToolRow(card5, "cjpeg", w, ref ty, out _lblCjpegStatus, out _txtCjpeg, out _btnBrowseCjpeg, T("cjpeg from mozjpeg", "cjpeg de mozjpeg"));

        // Footer
        y += 16;
        var footer = new Label
        {
            Text = T("Changes are saved automatically when you close this window.",
                     "Les modifications sont enregistrées automatiquement à la fermeture."),
            Font = F(8.5f),
            ForeColor = DimText,
            AutoSize = true,
            Location = new Point(left + 4, y),
        };
        _scroll.Controls.Add(footer);
    }

    // ── UI Helpers ──────────────────────────────────────────

    private static Label Lbl(string text, Font font, Color color, int x, int y) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        AutoSize = true,
        Location = new Point(x, y),
    };

    private Label SectionLabel(Control parent, string title, ref int y, int left)
    {
        y += 14;
        var container = new Panel
        {
            Location = new Point(left + 4, y),
            Size = new Size(200, 22),
            BackColor = Color.Transparent,
        };

        var lbl = new Label
        {
            Text = title.ToUpperInvariant(),
            Font = F(8.5f, FontStyle.Bold),
            ForeColor = DimText,
            AutoSize = true,
            Location = new Point(0, 0),
        };
        container.Controls.Add(lbl);

        var line = new Panel
        {
            BackColor = AccentColor,
            Size = new Size(28, 3),
            Location = new Point(0, 16),
        };
        container.Controls.Add(line);

        parent.Controls.Add(container);
        y += 28;
        return lbl;
    }

    private static RoundedPanel Card(Control parent, ref int y, int left, int w, int h)
    {
        var p = new RoundedPanel
        {
            Location = new Point(left, y),
            Size = new Size(w, h),
            BackColor = CardColor,
            BorderColor = BorderColor,
            ShadowColor = ShadowColor,
        };
        parent.Controls.Add(p);
        y += h + 14;
        return p;
    }

    private static CheckBox Chk(string text) => new()
    {
        Text = text,
        Font = F(9.5f),
        ForeColor = TextColor,
        AutoSize = true,
        Margin = new Padding(0, 2, 0, 0),
        BackColor = Color.Transparent,
    };

    private static CheckBox GridChk() => new()
    {
        AutoSize = true,
        Margin = new Padding(0, 4, 0, 0),
        BackColor = Color.Transparent,
    };

    private static Label GridHeader(string text) => new()
    {
        Text = text,
        Font = F(9, FontStyle.Bold),
        ForeColor = DimText,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 4),
        BackColor = Color.Transparent,
    };

    private static Label GridLabel(string text) => new()
    {
        Text = text,
        Font = F(9.5f, FontStyle.Bold),
        ForeColor = TextColor,
        AutoSize = true,
        Margin = new Padding(0, 4, 0, 0),
        BackColor = Color.Transparent,
    };

    private void AddToolRow(Control parent, string name, int cardWidth, ref int y, out Label status, out TextBox pathBox, out Button browse, string? tooltip = null)
    {
        var row = new Panel
        {
            Location = new Point(20, y),
            Size = new Size(cardWidth - 56, 42),
            BackColor = Color.Transparent,
        };

        var lbl = new Label
        {
            Text = name,
            Font = F(9.5f, FontStyle.Bold),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(0, 10),
            BackColor = Color.Transparent,
        };
        row.Controls.Add(lbl);
        if (tooltip != null)
        {
            var tip = new ToolTip();
            tip.SetToolTip(lbl, tooltip);
        }

        status = new Label
        {
            Font = F(9, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(90, 11),
            BackColor = Color.Transparent,
        };
        row.Controls.Add(status);

        browse = new RoundedButton
        {
            Text = "…",
            Font = F(10),
            Size = new Size(32, 30),
            Location = new Point(row.Width - 32, 6),
            Cursor = Cursors.Hand,
            BackColor = CardColor,
            ForeColor = DimText,
            BorderColor = BorderColor,
            HoverBackColor = Color.FromArgb(245, 245, 245),
        };
        browse.FlatAppearance.BorderColor = BorderColor;
        var target = pathBox = new TextBox
        {
            Font = F(9),
            Size = new Size(row.Width - 140, 24),
            Location = new Point(142, 9),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = T("Custom path (optional)", "Chemin personnalisé (optionnel)"),
        };
        browse.Click += (_, _) => BrowseExe(target);
        row.Controls.Add(pathBox);
        row.Controls.Add(browse);

        parent.Controls.Add(row);
        y += 48;
    }

    // ── Data Load / Save ────────────────────────────────────

    private void LoadSettings()
    {
        _chkNested.Checked = _settings.NestedMenu;
        _chkOverwrite.Checked = _settings.OverwriteOriginal;
        _cboMode.SelectedIndex = (int)_settings.DownscaleMode;

        RefreshSizeTags();

        _chkJpgDown.Checked = _settings.Jpg.Downscale;
        _chkJpgWebP.Checked = _settings.Jpg.ConvertToWebP;
        _chkJpgMoz.Checked = _settings.Jpg.Optimize;
        _chkPngDown.Checked = _settings.Png.Downscale;
        _chkPngWebP.Checked = _settings.Png.ConvertToWebP;
        _chkPngJpg.Checked = _settings.Png.ConvertToJpg;
        _chkPngOpt.Checked = _settings.Png.Optimize;
        _chkWebPDown.Checked = _settings.WebP.Downscale;

        _txtPngquant.Text = _settings.Executables.PngquantPath ?? "";
        _txtCwebp.Text = _settings.Executables.Img2WebPPath ?? "";
        _txtCjpeg.Text = _settings.Executables.CjpegPath ?? "";
    }

    private void SaveSettings()
    {
        _settings.NestedMenu = _chkNested.Checked;
        _settings.OverwriteOriginal = _chkOverwrite.Checked;
        _settings.DownscaleMode = (DownscaleMode)_cboMode.SelectedIndex;
        _settings.Language = _isFr ? "fr" : "en";

        _settings.AvailableSizes.Clear();
        foreach (Control c in _sizesFlow.Controls)
            if (c is SizeChip chip) _settings.AvailableSizes.Add(chip.SizeValue);
        if (_settings.AvailableSizes.Count == 0)
            _settings.AvailableSizes.Add(1024);

        _settings.Jpg.Downscale = _chkJpgDown.Checked;
        _settings.Jpg.ConvertToWebP = _chkJpgWebP.Checked;
        _settings.Jpg.Optimize = _chkJpgMoz.Checked;
        _settings.Png.Downscale = _chkPngDown.Checked;
        _settings.Png.ConvertToWebP = _chkPngWebP.Checked;
        _settings.Png.ConvertToJpg = _chkPngJpg.Checked;
        _settings.Png.Optimize = _chkPngOpt.Checked;
        _settings.WebP.Downscale = _chkWebPDown.Checked;

        var pp = _txtPngquant.Text.Trim();
        _settings.Executables.PngquantPath = pp.Length > 0 ? pp : null;
        var cp = _txtCwebp.Text.Trim();
        _settings.Executables.Img2WebPPath = cp.Length > 0 ? cp : null;
        var jp = _txtCjpeg.Text.Trim();
        _settings.Executables.CjpegPath = jp.Length > 0 ? jp : null;

        _config.Save(_settings);
    }

    // ── Refresh ─────────────────────────────────────────────

    private void RefreshAll()
    {
        var installed = _registry.IsRegistered();
        _lblInstallStatus.Text = installed
            ? T("●  Context menu is active", "●  Menu contextuel actif")
            : T("○  Not installed", "○  Non installé");
        _lblInstallStatus.ForeColor = installed ? GreenColor : DimText;

        if (installed)
        {
            _btnToggleInstall.Text = T("Remove from Explorer", "Retirer de l'Explorateur");
            _btnToggleInstall.BackColor = DangerBg;
            _btnToggleInstall.ForeColor = DangerText;
            _btnToggleInstall.BorderColor = DangerText;
            _btnToggleInstall.HoverBackColor = DangerHover;
        }
        else
        {
            _btnToggleInstall.Text = T("Add to Explorer", "Ajouter à l'Explorateur");
            _btnToggleInstall.BackColor = AccentColor;
            _btnToggleInstall.ForeColor = Color.White;
            _btnToggleInstall.BorderColor = AccentColor;
            _btnToggleInstall.HoverBackColor = AccentHover;
        }

        // Apply button styling
        _btnApply.Text = T("Apply", "Appliquer");
        _btnApply.Visible = installed;

        // Check if registered path differs from current exe
        _lblUpdateHint.Visible = false;
        if (installed)
        {
            var regPath = _registry.GetRegisteredExePath();
            var curPath = Application.ExecutablePath;
            if (regPath != null && !string.Equals(regPath, curPath, StringComparison.OrdinalIgnoreCase))
            {
                _lblUpdateHint.Text = T(
                    "⚠ Registered path differs — click Apply to update",
                    "⚠ Chemin enregistré différent — cliquez Appliquer pour mettre à jour");
                _lblUpdateHint.Visible = true;
            }
        }

        RefreshToolRow(_lblPngquantStatus, _txtPngquant, "pngquant", _settings.Executables.PngquantPath);
        RefreshToolRow(_lblCwebpStatus, _txtCwebp, "cwebp", _settings.Executables.Img2WebPPath);
        RefreshToolRow(_lblCjpegStatus, _txtCjpeg, "cjpeg", _settings.Executables.CjpegPath);
    }

    private void RefreshToolRow(Label status, TextBox pathBox, string exeName, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            status.Text = "✓ Custom";
            status.ForeColor = GreenColor;
            pathBox.PlaceholderText = T("Custom path set", "Chemin personnalisé défini");
        }
        else if (_exe.IsInPath(exeName))
        {
            status.Text = "✓ PATH";
            status.ForeColor = GreenColor;
            pathBox.PlaceholderText = T("Using PATH (override optional)", "Utilise PATH (remplacement optionnel)");
        }
        else
        {
            status.Text = T("✗ Missing", "✗ Absent");
            status.ForeColor = RedColor;
            pathBox.PlaceholderText = T("Path to executable…", "Chemin vers l'exécutable…");
        }
    }

    private void RefreshSizeTags()
    {
        _sizesFlow.Controls.Clear();
        foreach (var s in _settings.AvailableSizes)
            AddSizeChip(s);
    }

    private void AddSizeChip(int size)
    {
        var chip = new SizeChip(size);
        chip.RemoveClicked += (_, _) =>
        {
            _sizesFlow.Controls.Remove(chip);
            chip.Dispose();
        };
        _sizesFlow.Controls.Add(chip);
    }

    // ── Events ──────────────────────────────────────────────

    private void BtnAddSize_Click(object? sender, EventArgs e)
    {
        var size = (int)_nudNewSize.Value;
        foreach (Control c in _sizesFlow.Controls)
            if (c is SizeChip t && t.SizeValue == size) return;
        AddSizeChip(size);
    }

    private void BtnToggleInstall_Click(object? sender, EventArgs e)
    {
        if (_registry.IsRegistered())
        {
            _registry.Unregister();
        }
        else
        {
            SaveSettings();
            _registry.Register(_settings, Application.ExecutablePath);
        }
        RefreshAll();
    }

    private void BtnApply_Click(object? sender, EventArgs e)
    {
        SaveSettings();
        _registry.Register(_settings, Application.ExecutablePath);
        RefreshAll();
    }

    private void LnkLang_Click(object? sender, EventArgs e)
    {
        _isFr = !_isFr;
        SaveSettings();
        // Re-register context menus with updated language if installed
        if (_registry.IsRegistered())
            _registry.Register(_settings, Application.ExecutablePath);
        // Rebuild UI with new language
        Controls.Clear();
        BuildUI();
        LoadSettings();
        RefreshAll();
    }

    private void BrowseExe(TextBox target)
    {
        using var dlg = new OpenFileDialog
        {
            Title = T("Select executable", "Sélectionner l'exécutable"),
            Filter = T("Executables (*.exe)|*.exe|All files (*.*)|*.*",
                       "Exécutables (*.exe)|*.exe|Tous les fichiers (*.*)|*.*"),
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            target.Text = dlg.FileName;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        base.OnFormClosing(e);
    }

    // ── Inner Controls ──────────────────────────────────────

    private sealed class RoundedPanel : Panel
    {
        public Color BorderColor { get; set; } = Color.FromArgb(220, 220, 220);
        public Color ShadowColor { get; set; } = Color.FromArgb(235, 235, 235);
        private const int R = 8;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Shadow
            var shadowRect = new Rectangle(1, 2, Width - 3, Height - 4);
            using var shadowPath = RPath(shadowRect, R);
            using var shadowBrush = new SolidBrush(ShadowColor);
            g.FillPath(shadowBrush, shadowPath);

            // Card
            var rect = new Rectangle(0, 0, Width - 2, Height - 2);
            using var path = RPath(rect, R);
            using var brush = new SolidBrush(BackColor);
            using var pen = new Pen(BorderColor, 1f);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        private static GraphicsPath RPath(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    private sealed class RoundedButton : Button
    {
        public Color BorderColor { get; set; } = Color.FromArgb(0, 120, 212);
        public Color HoverBackColor { get; set; } = Color.FromArgb(0, 103, 181);
        private Color _normalBackColor;
        private const int R = 6;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.FromArgb(0, 120, 212);
            ForeColor = Color.White;
            _normalBackColor = BackColor;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            MouseEnter += (_, _) => { _normalBackColor = BackColor; BackColor = HoverBackColor; };
            MouseLeave += (_, _) => BackColor = _normalBackColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RPath(rect, R);
            using var brush = new SolidBrush(BackColor);
            using var pen = new Pen(BorderColor, 1f);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, Text, Font, rect, ForeColor, flags);
        }

        private static GraphicsPath RPath(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    private sealed class SizeChip : UserControl
    {
        public int SizeValue { get; }
        public event EventHandler? RemoveClicked;

        public SizeChip(int size)
        {
            SizeValue = size;
            Size = new Size(88, 30);
            Margin = new Padding(3, 4, 3, 4);
            BackColor = ChipBg;
            Cursor = Cursors.Default;
            DoubleBuffered = true;

            Controls.Add(new Label
            {
                Text = $"{size}px",
                Font = new("Segoe UI", 8.5f),
                ForeColor = AccentColor,
                AutoSize = false,
                Size = new Size(58, 22),
                Location = new Point(8, 4),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            });

            var x = new Label
            {
                Text = "×",
                Font = new("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 140, 140),
                AutoSize = false,
                Size = new Size(20, 22),
                Location = new Point(64, 4),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent,
            };
            x.Click += (_, _) => RemoveClicked?.Invoke(this, EventArgs.Empty);
            x.MouseEnter += (_, _) => x.ForeColor = RedColor;
            x.MouseLeave += (_, _) => x.ForeColor = Color.FromArgb(140, 140, 140);
            Controls.Add(x);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, 6);
            using var brush = new SolidBrush(BackColor);
            g.FillPath(brush, path);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
