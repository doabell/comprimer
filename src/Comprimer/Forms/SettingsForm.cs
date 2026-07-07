using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Comprimer.Models;
using Comprimer.Services;

namespace Comprimer.Forms;

/// <summary>
/// Main settings window for Comprimer — modern single-page layout with FR/EN toggle.
/// </summary>
public sealed class SettingsForm : Form
{
    // Light theme
    private static readonly Color LightBg = Color.FromArgb(243, 243, 243);
    private static readonly Color LightCard = Color.White;
    private static readonly Color LightText = Color.FromArgb(24, 24, 24);
    private static readonly Color LightDim = Color.FromArgb(110, 110, 110);
    private static readonly Color LightBorder = Color.FromArgb(220, 220, 220);
    private static readonly Color LightInputBg = Color.White;
    private static readonly Color LightChipBg = Color.FromArgb(232, 242, 252);
    private static readonly Color LightDangerBg = Color.FromArgb(253, 231, 233);

    // Dark theme
    private static readonly Color DarkBg = Color.FromArgb(32, 32, 32);
    private static readonly Color DarkCard = Color.FromArgb(45, 45, 45);
    private static readonly Color DarkText = Color.FromArgb(240, 240, 240);
    private static readonly Color DarkDim = Color.FromArgb(160, 160, 160);
    private static readonly Color DarkBorder = Color.FromArgb(70, 70, 70);
    private static readonly Color DarkInputBg = Color.FromArgb(55, 55, 55);
    private static readonly Color DarkChipBg = Color.FromArgb(35, 60, 85);
    private static readonly Color DarkDangerBg = Color.FromArgb(80, 35, 38);

    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
    private static readonly Color GreenColor = Color.FromArgb(16, 124, 16);
    private static readonly Color RedColor = Color.FromArgb(196, 43, 28);
    private static readonly Color DangerText = Color.FromArgb(196, 43, 28);

    private bool _isDark;
    private Color BgColor => _isDark ? DarkBg : LightBg;
    private Color CardColor => _isDark ? DarkCard : LightCard;
    private Color TextColor => _isDark ? DarkText : LightText;
    private Color DimText => _isDark ? DarkDim : LightDim;
    private Color BorderColor => _isDark ? DarkBorder : LightBorder;
    private Color InputBg => _isDark ? DarkInputBg : LightInputBg;
    private Color ChipBg => _isDark ? DarkChipBg : LightChipBg;
    private Color DangerBg => _isDark ? DarkDangerBg : LightDangerBg;

    private readonly ConfigService _config;
    private readonly ExecutableService _exe;
    private readonly RegistryService _registry;
    private AppSettings _settings;
    private bool _isFr;

    // Scrollable content
    private Panel _scroll = null!;

    // Install
    private Label _lblInstallStatus = null!;
    private Button _btnToggleInstall = null!;
    private Button _btnApply = null!;
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

    // Format grid headers
    private Label _lblColFormat = null!;
    private Label _lblColDown = null!;
    private Label _lblColWebP = null!;
    private Label _lblColJpg = null!;
    private Label _lblColOpt = null!;

    public SettingsForm()
    {
        _config = new ConfigService();
        _exe = new ExecutableService();
        _registry = new RegistryService();
        _settings = _config.Load();
        _isFr = _settings.Language == "fr";
        _isDark = _settings.DarkMode;

        BuildUI();
        LoadSettings();
        RefreshAll();
    }

    private static Font F(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    // ── Localization ────────────────────────────────────────
    private string T(string en, string fr) => _isFr ? fr : en;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private void SetTitleBarDark()
    {
        if (Environment.OSVersion.Version.Build >= 22000)
        {
            int value = _isDark ? 1 : 0;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetTitleBarDark();
    }

    private void ApplyTheme()
    {
        BackColor = BgColor;
        _scroll.BackColor = BgColor;
        SetTitleBarDark();

        foreach (Control c in GetAllControls(_scroll))
        {
            switch (c)
            {
                case Label lbl:
                    lbl.BackColor = Color.Transparent;
                    if (lbl == _lblInstallStatus) break;
                    if (lbl == _lblTitle || lbl == _lblModeLabel || lbl == _lblColFormat || lbl == _lblColDown || lbl == _lblColOpt || lbl == _lblColWebP || lbl == _lblColJpg)
                    {
                        lbl.ForeColor = lbl == _lblTitle ? TextColor : DimText;
                        if (lbl == _lblModeLabel) lbl.ForeColor = TextColor;
                    }
                    else if (lbl != _lblUpdateHint)
                    {
                        lbl.ForeColor = TextColor;
                    }
                    break;
                case CheckBox chk:
                    chk.ForeColor = TextColor;
                    chk.BackColor = Color.Transparent;
                    break;
                case TextBox txt:
                    txt.BackColor = InputBg;
                    txt.ForeColor = TextColor;
                    break;
                case ComboBox cbo:
                    cbo.BackColor = InputBg;
                    cbo.ForeColor = TextColor;
                    break;
                case NumericUpDown nud:
                    nud.BackColor = InputBg;
                    nud.ForeColor = TextColor;
                    break;
                case RoundedPanel card:
                    card.BackColor = CardColor;
                    card.BorderColor = BorderColor;
                    break;
            }
        }

        foreach (SizeChip chip in _sizesFlow.Controls.OfType<SizeChip>())
        {
            chip.BackColor = ChipBg;
        }

        RefreshAll();
    }

    private static IEnumerable<Control> GetAllControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control child in GetAllControls(c))
                yield return child;
        }
    }

    private void BuildUI()
    {
        Text = "Comprimer";
        ClientSize = new Size(520, 780);
        MinimumSize = new Size(520, 700);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = BgColor;
        AutoScaleMode = AutoScaleMode.Dpi;

        _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BgColor };
        Controls.Add(_scroll);

        int y = 18;
        int w = 480;
        int left = 20;

        // ─── Header ─────────────────────────────────────────
        _lblTitle = new Label
        {
            Text = "Comprimer",
            Font = F(20, FontStyle.Bold),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(left, y),
        };
        _scroll.Controls.Add(_lblTitle);

        var btnTheme = new Button
        {
            Text = _isDark ? "☀" : "☾",
            Font = F(9),
            Size = new Size(26, 26),
            Location = new Point(w - 40, y + 2),
            FlatStyle = FlatStyle.Flat,
            ForeColor = DimText,
            BackColor = CardColor,
            Cursor = Cursors.Hand,
        };
        btnTheme.FlatAppearance.BorderColor = BorderColor;
        btnTheme.Click += BtnTheme_Click;
        _scroll.Controls.Add(btnTheme);

        _lnkLang = new LinkLabel
        {
            Text = T("En français", "En anglais"),
            Font = F(9),
            AutoSize = true,
            Location = new Point(w - 10, y + 6),
            LinkColor = AccentColor,
            ActiveLinkColor = AccentColor,
        };
        _lnkLang.Click += LnkLang_Click;
        _scroll.Controls.Add(_lnkLang);

        y += 38;
        _lblSubtitle = new Label
        {
            Text = T("Image compression & conversion for Explorer",
                     "Compression et conversion d'images pour l'Explorateur"),
            Font = F(9),
            ForeColor = DimText,
            AutoSize = true,
            Location = new Point(left + 2, y),
        };
        _scroll.Controls.Add(_lblSubtitle);
        y += 32;

        // ─── 1. Explorer Integration ────────────────────────
        _lblSecExplorer = SectionLabel(_scroll, T("Explorer Integration", "Intégration Explorer"), ref y, left);
        var card1 = Card(_scroll, ref y, left, w, 86);

        _lblInstallStatus = new Label { Font = F(9), AutoSize = true, Location = new Point(16, 10) };
        card1.Controls.Add(_lblInstallStatus);

        _lblUpdateHint = new Label { Font = F(8), ForeColor = DimText, AutoSize = true, Location = new Point(16, 30), Visible = false };
        card1.Controls.Add(_lblUpdateHint);

        _btnToggleInstall = Btn("", 16, 54, 200, 26);
        _btnToggleInstall.Click += BtnToggleInstall_Click;
        card1.Controls.Add(_btnToggleInstall);

        _btnApply = Btn(T("Apply", "Appliquer"), 224, 54, 100, 26);
        _btnApply.Click += BtnApply_Click;
        card1.Controls.Add(_btnApply);

        // ─── 2. Options ─────────────────────────────────────
        _lblSecOptions = SectionLabel(_scroll, T("Options", "Options"), ref y, left);
        var card2 = Card(_scroll, ref y, left, w, 80);

        _chkNested = Chk(T("Nested submenu", "Sous-menu imbriqué"), 16, 14);
        _chkOverwrite = Chk(T("Overwrite originals", "Écraser les originaux"), 16, 42);
        card2.Controls.Add(_chkNested);
        card2.Controls.Add(_chkOverwrite);

        _lblModeLabel = Lbl(T("Downscale limit:", "Limite de réduction :"), F(8.5f), TextColor, 240, 16);
        card2.Controls.Add(_lblModeLabel);
        _cboMode = new ComboBox
        {
            Font = F(8.5f),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(355, 12),
            Size = new Size(115, 24),
            BackColor = InputBg,
            ForeColor = TextColor,
        };
        _cboMode.Items.AddRange([
            T("Longest side", "Plus long côté"),
            T("Width only", "Largeur seule"),
            T("Height only", "Hauteur seule"),
        ]);
        _cboMode.SelectedIndex = 0;
        card2.Controls.Add(_cboMode);

        // ─── 3. Downscale Sizes ─────────────────────────────
        _lblSecSizes = SectionLabel(_scroll, T("Downscale Sizes (px)", "Tailles de réduction (px)"), ref y, left);
        var card3 = Card(_scroll, ref y, left, w, 52);

        _sizesFlow = new FlowLayoutPanel
        {
            Location = new Point(10, 10),
            Size = new Size(350, 34),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
        };
        card3.Controls.Add(_sizesFlow);

        _nudNewSize = new NumericUpDown
        {
            Font = F(8.5f),
            Location = new Point(370, 14),
            Size = new Size(64, 22),
            Minimum = 50,
            Maximum = 10000,
            Value = 1024,
            BorderStyle = BorderStyle.FixedSingle,
        };
        card3.Controls.Add(_nudNewSize);

        var btnAdd = Btn("+", 438, 13, 28, 24);
        btnAdd.Font = F(11, FontStyle.Bold);
        btnAdd.Click += BtnAddSize_Click;
        card3.Controls.Add(btnAdd);

        // ─── 4. Format Operations ───────────────────────────
        _lblSecFormats = SectionLabel(_scroll, T("Format Operations", "Opérations par format"), ref y, left);
        var card4 = Card(_scroll, ref y, left, w, 124);

        // Column headers
        _lblColFormat = Lbl(T("Format", "Format"), F(8, FontStyle.Bold), DimText, 16, 10);
        _lblColDown = Lbl(T("Downscale", "Réduire"), F(8, FontStyle.Bold), DimText, 100, 10);
        _lblColOpt = Lbl(T("Optimize", "Optimiser"), F(8, FontStyle.Bold), DimText, 195, 10);
        _lblColWebP = Lbl("→ WebP", F(8, FontStyle.Bold), DimText, 295, 10);
        _lblColJpg = Lbl("→ JPG", F(8, FontStyle.Bold), DimText, 395, 10);
        card4.Controls.Add(_lblColFormat);
        card4.Controls.Add(_lblColDown);
        card4.Controls.Add(_lblColOpt);
        card4.Controls.Add(_lblColWebP);
        card4.Controls.Add(_lblColJpg);

        int ry = 34;
        card4.Controls.Add(Lbl("JPG", F(8.5f, FontStyle.Bold), TextColor, 16, ry + 2));
        _chkJpgDown = Chk("", 120, ry); card4.Controls.Add(_chkJpgDown);
        _chkJpgMoz = Chk("", 215, ry); card4.Controls.Add(_chkJpgMoz);
        _chkJpgWebP = Chk("", 315, ry); card4.Controls.Add(_chkJpgWebP);

        ry += 28;
        card4.Controls.Add(Lbl("PNG", F(8.5f, FontStyle.Bold), TextColor, 16, ry + 2));
        _chkPngDown = Chk("", 120, ry); card4.Controls.Add(_chkPngDown);
        _chkPngOpt = Chk("", 215, ry); card4.Controls.Add(_chkPngOpt);
        _chkPngWebP = Chk("", 315, ry); card4.Controls.Add(_chkPngWebP);
        _chkPngJpg = Chk("", 415, ry); card4.Controls.Add(_chkPngJpg);

        ry += 28;
        card4.Controls.Add(Lbl("WebP", F(8.5f, FontStyle.Bold), TextColor, 16, ry + 2));
        _chkWebPDown = Chk("", 120, ry); card4.Controls.Add(_chkWebPDown);

        // ─── 5. External Tools ──────────────────────────────
        _lblSecTools = SectionLabel(_scroll, T("External Tools", "Outils externes"), ref y, left);
        var card5 = Card(_scroll, ref y, left, w, 130);

        int ty = 12;
        AddToolRow(card5, "pngquant", ref ty, out _lblPngquantStatus, out _txtPngquant, out _btnBrowsePngquant);
        AddToolRow(card5, "cwebp", ref ty, out _lblCwebpStatus, out _txtCwebp, out _btnBrowseCwebp);
        AddToolRow(card5, "cjpeg", ref ty, out _lblCjpegStatus, out _txtCjpeg, out _btnBrowseCjpeg, T("cjpeg from mozjpeg", "cjpeg de mozjpeg"));
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
        y += 10;
        var lbl = new Label
        {
            Text = title.ToUpperInvariant(),
            Font = F(8, FontStyle.Bold),
            ForeColor = DimText,
            AutoSize = true,
            Location = new Point(left + 4, y),
        };
        parent.Controls.Add(lbl);
        y += 20;
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
        };
        parent.Controls.Add(p);
        y += h + 8;
        return p;
    }

    private static CheckBox Chk(string text, int x, int y) => new()
    {
        Text = text,
        Font = F(8.5f),
        ForeColor = TextColor,
        AutoSize = true,
        Location = new Point(x, y),
        BackColor = Color.Transparent,
    };

    private static Button Btn(string text, int x, int y, int w, int h)
    {
        var b = new Button
        {
            Text = text,
            Font = new("Segoe UI", 8.5f),
            Location = new Point(x, y),
            Size = new Size(w, h),
            FlatStyle = FlatStyle.Flat,
            ForeColor = AccentColor,
            BackColor = CardColor,
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderColor = AccentColor;
        return b;
    }

    private void AddToolRow(Control parent, string name, ref int y, out Label status, out TextBox pathBox, out Button browse, string? tooltip = null)
    {
        var lbl = Lbl(name, F(8.5f, FontStyle.Bold), TextColor, 16, y + 3);
        parent.Controls.Add(lbl);
        if (tooltip != null)
        {
            var tip = new ToolTip();
            tip.SetToolTip(lbl, tooltip);
        }

        status = new Label { Font = F(8), AutoSize = true, Location = new Point(100, y + 4) };
        parent.Controls.Add(status);

        pathBox = new TextBox
        {
            Font = F(8),
            Size = new Size(210, 22),
            Location = new Point(196, y + 1),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = T("Custom path (optional)", "Chemin personnalisé (optionnel)"),
            BackColor = InputBg,
            ForeColor = TextColor,
        };
        parent.Controls.Add(pathBox);

        browse = new Button
        {
            Text = "…",
            Font = F(8),
            Size = new Size(28, 22),
            Location = new Point(412, y + 1),
            FlatStyle = FlatStyle.Flat,
            ForeColor = DimText,
            BackColor = CardColor,
            Cursor = Cursors.Hand,
        };
        browse.FlatAppearance.BorderColor = BorderColor;
        var target = pathBox;
        browse.Click += (_, _) => BrowseExe(target);
        parent.Controls.Add(browse);

        y += 38;
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
            _btnToggleInstall.ForeColor = DangerText;
            _btnToggleInstall.BackColor = DangerBg;
            _btnToggleInstall.FlatAppearance.BorderColor = DangerText;
        }
        else
        {
            _btnToggleInstall.Text = T("Add to Explorer", "Ajouter à l'Explorateur");
            _btnToggleInstall.ForeColor = Color.White;
            _btnToggleInstall.BackColor = AccentColor;
            _btnToggleInstall.FlatAppearance.BorderColor = AccentColor;
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
                _lblUpdateHint.ForeColor = RedColor;
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
        var chip = new SizeChip(size, _isDark);
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
        SuspendLayout();
        Controls.Clear();
        BuildUI();
        LoadSettings();
        RefreshAll();
        ResumeLayout(true);
    }

    private void BtnTheme_Click(object? sender, EventArgs e)
    {
        _isDark = !_isDark;
        SaveSettings();
        ApplyTheme();
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
        private const int R = 6;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
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

        public SizeChip(int size, bool dark)
        {
            SizeValue = size;
            Size = new Size(88, 26);
            Margin = new Padding(2, 3, 2, 3);
            BackColor = dark ? ChipBgDark : ChipBgLight;
            Cursor = Cursors.Default;

            Controls.Add(new Label
            {
                Text = $"{size}px",
                Font = new("Segoe UI", 8.25f),
                ForeColor = AccentColor,
                AutoSize = false,
                Size = new Size(58, 20),
                Location = new Point(8, 3),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            });

            var x = new Label
            {
                Text = "×",
                Font = new("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(140, 140, 140),
                AutoSize = false,
                Size = new Size(18, 20),
                Location = new Point(66, 3),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent,
            };
            x.Click += (_, _) => RemoveClicked?.Invoke(this, EventArgs.Empty);
            x.MouseEnter += (_, _) => x.ForeColor = RedColor;
            x.MouseLeave += (_, _) => x.ForeColor = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(140, 140, 140);
            Controls.Add(x);
        }
    }
}
