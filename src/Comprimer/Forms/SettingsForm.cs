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
    private bool _isDark;

    // Light theme
    private static readonly Color LightBg = Color.FromArgb(243, 243, 243);
    private static readonly Color LightCard = Color.White;
    private static readonly Color LightText = Color.FromArgb(24, 24, 24);
    private static readonly Color LightDim = Color.FromArgb(110, 110, 110);
    private static readonly Color LightBorder = Color.FromArgb(220, 220, 220);
    private static readonly Color LightInputBg = Color.White;
    private static readonly Color LightInputBorder = Color.FromArgb(200, 200, 200);

    // Dark theme
    private static readonly Color DarkBg = Color.FromArgb(32, 32, 32);
    private static readonly Color DarkCard = Color.FromArgb(45, 45, 45);
    private static readonly Color DarkText = Color.FromArgb(240, 240, 240);
    private static readonly Color DarkDim = Color.FromArgb(160, 160, 160);
    private static readonly Color DarkBorder = Color.FromArgb(70, 70, 70);
    private static readonly Color DarkInputBg = Color.FromArgb(55, 55, 55);
    private static readonly Color DarkInputBorder = Color.FromArgb(90, 90, 90);

    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
    private static readonly Color GreenColor = Color.FromArgb(16, 124, 16);
    private static readonly Color RedColor = Color.FromArgb(196, 43, 28);
    private static readonly Color ChipBgLight = Color.FromArgb(232, 242, 252);
    private static readonly Color ChipBgDark = Color.FromArgb(35, 60, 85);
    private static readonly Color DangerBgLight = Color.FromArgb(253, 231, 233);
    private static readonly Color DangerBgDark = Color.FromArgb(80, 35, 38);

    private Color BgColor => _isDark ? DarkBg : LightBg;
    private Color CardColor => _isDark ? DarkCard : LightCard;
    private Color TextColor => _isDark ? DarkText : LightText;
    private Color DimText => _isDark ? DarkDim : LightDim;
    private Color BorderColor => _isDark ? DarkBorder : LightBorder;
    private Color InputBg => _isDark ? DarkInputBg : LightInputBg;
    private Color InputBorder => _isDark ? DarkInputBorder : LightInputBorder;
    private Color ChipBg => _isDark ? ChipBgDark : ChipBgLight;
    private Color DangerBg => _isDark ? DangerBgDark : DangerBgLight;

    private readonly ConfigService _config;
    private readonly ExecutableService _exe;
    private readonly RegistryService _registry;
    private AppSettings _settings;
    private bool _isFr;

    private Panel _scroll = null!;

    private Label _lblInstallStatus = null!;
    private Button _btnToggleInstall = null!;
    private Button _btnApply = null!;
    private Label _lblUpdateHint = null!;

    private CheckBox _chkNested = null!;
    private CheckBox _chkOverwrite = null!;
    private ComboBox _cboMode = null!;
    private Label _lblModeLabel = null!;

    private FlowLayoutPanel _sizesFlow = null!;
    private NumericUpDown _nudNewSize = null!;

    private CheckBox _chkJpgDown = null!;
    private CheckBox _chkJpgWebP = null!;
    private CheckBox _chkJpgMoz = null!;
    private CheckBox _chkPngDown = null!;
    private CheckBox _chkPngWebP = null!;
    private CheckBox _chkPngJpg = null!;
    private CheckBox _chkPngOpt = null!;
    private CheckBox _chkWebPDown = null!;

    private Label _lblPngquantStatus = null!;
    private TextBox _txtPngquant = null!;
    private Button _btnBrowsePngquant = null!;
    private Label _lblCwebpStatus = null!;
    private TextBox _txtCwebp = null!;
    private Button _btnBrowseCwebp = null!;
    private Label _lblCjpegStatus = null!;
    private TextBox _txtCjpeg = null!;
    private Button _btnBrowseCjpeg = null!;

    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private LinkLabel _lnkLang = null!;
    private Button _btnTheme = null!;

    private Label _lblSecExplorer = null!;
    private Label _lblSecOptions = null!;
    private Label _lblSecSizes = null!;
    private Label _lblSecFormats = null!;
    private Label _lblSecTools = null!;

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

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BuildUI();
        LoadSettings();
        RefreshAll();
    }

    private static Font F(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

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

    private void BuildUI()
    {
        Text = "Comprimer";
        ClientSize = new Size(560, 740);
        MinimumSize = new Size(520, 700);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = BgColor;
        AutoScaleMode = AutoScaleMode.Dpi;

        _scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = BgColor,
        };
        Controls.Add(_scroll);

        int y = 18;
        int w = 520;
        int left = 20;

        // Header
        _lblTitle = new Label
        {
            Text = "Comprimer",
            Font = F(22, FontStyle.Bold),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(left, y),
        };
        _scroll.Controls.Add(_lblTitle);

        _btnTheme = new Button
        {
            Text = _isDark ? "☀" : "☾",
            Font = F(10),
            Size = new Size(28, 28),
            Location = new Point(w - 96, y),
            FlatStyle = FlatStyle.Flat,
            ForeColor = DimText,
            BackColor = CardColor,
            Cursor = Cursors.Hand,
        };
        _btnTheme.FlatAppearance.BorderColor = BorderColor;
        _btnTheme.Click += BtnTheme_Click;
        _scroll.Controls.Add(_btnTheme);

        _lnkLang = new LinkLabel
        {
            Text = T("En français", "En anglais"),
            Font = F(9.5f),
            AutoSize = true,
            Location = new Point(w - 58, y + 6),
            LinkColor = AccentColor,
            ActiveLinkColor = AccentColor,
        };
        _lnkLang.Click += LnkLang_Click;
        _scroll.Controls.Add(_lnkLang);

        y += 44;
        _lblSubtitle = new Label
        {
            Text = T("Image compression & conversion for Explorer",
                     "Compression et conversion d'images pour l'Explorateur"),
            Font = F(9.5f),
            ForeColor = DimText,
            AutoSize = true,
            Location = new Point(left + 2, y),
        };
        _scroll.Controls.Add(_lblSubtitle);
        y += 36;

        // 1. Explorer Integration
        _lblSecExplorer = SectionLabel(_scroll, T("Explorer Integration", "Intégration Explorer"), ref y, left);
        var card1 = Card(_scroll, ref y, left, w, 96);

        _lblInstallStatus = new Label
        {
            Font = F(9.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 14),
        };
        card1.Controls.Add(_lblInstallStatus);

        _lblUpdateHint = new Label
        {
            Font = F(8.5f),
            ForeColor = RedColor,
            AutoSize = true,
            Location = new Point(16, 34),
            Visible = false,
        };
        card1.Controls.Add(_lblUpdateHint);

        _btnToggleInstall = Btn("", 16, 58, 200, 28);
        _btnToggleInstall.Click += BtnToggleInstall_Click;
        card1.Controls.Add(_btnToggleInstall);

        _btnApply = Btn(T("Apply", "Appliquer"), 226, 58, 100, 28);
        _btnApply.Click += BtnApply_Click;
        card1.Controls.Add(_btnApply);

        // 2. Options
        _lblSecOptions = SectionLabel(_scroll, T("Options", "Options"), ref y, left);
        var card2 = Card(_scroll, ref y, left, w, 86);

        _chkNested = Chk(T("Nested submenu", "Sous-menu imbriqué"), 16, 14);
        _chkOverwrite = Chk(T("Overwrite originals", "Écraser les originaux"), 16, 44);
        card2.Controls.Add(_chkNested);
        card2.Controls.Add(_chkOverwrite);

        _lblModeLabel = Lbl(T("Downscale limit:", "Limite de réduction :"), F(9), TextColor, 240, 18);
        card2.Controls.Add(_lblModeLabel);
        _cboMode = new ComboBox
        {
            Font = F(9),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(355, 14),
            Size = new Size(150, 24),
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

        // 3. Downscale Sizes
        _lblSecSizes = SectionLabel(_scroll, T("Downscale Sizes (px)", "Tailles de réduction (px)"), ref y, left);
        var card3 = Card(_scroll, ref y, left, w, 60);

        _sizesFlow = new FlowLayoutPanel
        {
            Location = new Point(12, 12),
            Size = new Size(380, 36),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
        };
        card3.Controls.Add(_sizesFlow);

        _nudNewSize = new NumericUpDown
        {
            Font = F(9),
            Location = new Point(404, 14),
            Size = new Size(68, 24),
            Minimum = 50,
            Maximum = 10000,
            Value = 1024,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = InputBg,
            ForeColor = TextColor,
        };
        card3.Controls.Add(_nudNewSize);

        var btnAdd = Btn("+", 480, 13, 28, 28);
        btnAdd.Font = F(12, FontStyle.Bold);
        btnAdd.Click += BtnAddSize_Click;
        card3.Controls.Add(btnAdd);

        // 4. Format Operations
        _lblSecFormats = SectionLabel(_scroll, T("Format Operations", "Opérations par format"), ref y, left);
        var card4 = Card(_scroll, ref y, left, w, 132);

        _lblColFormat = Lbl(T("Format", "Format"), F(8.5f, FontStyle.Bold), DimText, 16, 12);
        _lblColDown = Lbl(T("Downscale", "Réduire"), F(8.5f, FontStyle.Bold), DimText, 100, 12);
        _lblColOpt = Lbl(T("Optimize", "Optimiser"), F(8.5f, FontStyle.Bold), DimText, 200, 12);
        _lblColWebP = Lbl("→ WebP", F(8.5f, FontStyle.Bold), DimText, 310, 12);
        _lblColJpg = Lbl("→ JPG", F(8.5f, FontStyle.Bold), DimText, 420, 12);
        card4.Controls.Add(_lblColFormat);
        card4.Controls.Add(_lblColDown);
        card4.Controls.Add(_lblColOpt);
        card4.Controls.Add(_lblColWebP);
        card4.Controls.Add(_lblColJpg);

        int ry = 40;
        card4.Controls.Add(Lbl("JPG", F(9, FontStyle.Bold), TextColor, 16, ry + 2));
        _chkJpgDown = Chk("", 120, ry); card4.Controls.Add(_chkJpgDown);
        _chkJpgMoz = Chk("", 220, ry); card4.Controls.Add(_chkJpgMoz);
        _chkJpgWebP = Chk("", 330, ry); card4.Controls.Add(_chkJpgWebP);

        ry += 30;
        card4.Controls.Add(Lbl("PNG", F(9, FontStyle.Bold), TextColor, 16, ry + 2));
        _chkPngDown = Chk("", 120, ry); card4.Controls.Add(_chkPngDown);
        _chkPngOpt = Chk("", 220, ry); card4.Controls.Add(_chkPngOpt);
        _chkPngWebP = Chk("", 330, ry); card4.Controls.Add(_chkPngWebP);
        _chkPngJpg = Chk("", 440, ry); card4.Controls.Add(_chkPngJpg);

        ry += 30;
        card4.Controls.Add(Lbl("WebP", F(9, FontStyle.Bold), TextColor, 16, ry + 2));
        _chkWebPDown = Chk("", 120, ry); card4.Controls.Add(_chkWebPDown);

        // 5. External Tools
        _lblSecTools = SectionLabel(_scroll, T("External Tools", "Outils externes"), ref y, left);
        var card5 = Card(_scroll, ref y, left, w, 138);

        int ty = 14;
        AddToolRow(card5, "pngquant", ref ty, out _lblPngquantStatus, out _txtPngquant, out _btnBrowsePngquant);
        AddToolRow(card5, "cwebp", ref ty, out _lblCwebpStatus, out _txtCwebp, out _btnBrowseCwebp);
        AddToolRow(card5, "cjpeg", ref ty, out _lblCjpegStatus, out _txtCjpeg, out _btnBrowseCjpeg, T("cjpeg from mozjpeg", "cjpeg de mozjpeg"));
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
                    if (lbl == _lblInstallStatus)
                        break;
                    if (lbl.ForeColor == (_isDark ? LightText : DarkText) || lbl.ForeColor == (_isDark ? LightDim : DarkDim))
                        lbl.ForeColor = lbl.ForeColor == (_isDark ? LightText : DarkText) ? TextColor : DimText;
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
                case Button btn:
                    if (btn == _btnTheme)
                    {
                        btn.BackColor = CardColor;
                        btn.ForeColor = DimText;
                        btn.FlatAppearance.BorderColor = BorderColor;
                    }
                    break;
            }
        }

        foreach (RoundedPanel card in _scroll.Controls.OfType<RoundedPanel>())
        {
            card.BackColor = CardColor;
            card.BorderColor = BorderColor;
        }

        _lblTitle.ForeColor = TextColor;
        _lblSubtitle.ForeColor = DimText;
        _lblModeLabel.ForeColor = TextColor;
        _lblColFormat.ForeColor = DimText;
        _lblColDown.ForeColor = DimText;
        _lblColOpt.ForeColor = DimText;
        _lblColWebP.ForeColor = DimText;
        _lblColJpg.ForeColor = DimText;

        foreach (Control c in _sizesFlow.Controls)
        {
            if (c is SizeChip chip)
            {
                chip.BackColor = ChipBg;
                chip.ForeColor = AccentColor;
            }
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

    private Label SectionLabel(Control parent, string title, ref int y, int left)
    {
        y += 10;
        var lbl = new Label
        {
            Text = title.ToUpperInvariant(),
            Font = F(8.5f, FontStyle.Bold),
            ForeColor = DimText,
            AutoSize = true,
            Location = new Point(left + 4, y),
        };
        parent.Controls.Add(lbl);
        y += 22;
        return lbl;
    }

    private static RoundedPanel Card(Control parent, ref int y, int left, int w, int h)
    {
        var p = new RoundedPanel
        {
            Location = new Point(left, y),
            Size = new Size(w, h),
            BackColor = Color.White,
            BorderColor = Color.FromArgb(220, 220, 220),
        };
        parent.Controls.Add(p);
        y += h + 10;
        return p;
    }

    private static Label Lbl(string text, Font font, Color color, int x, int y) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        AutoSize = true,
        Location = new Point(x, y),
        BackColor = Color.Transparent,
    };

    private CheckBox Chk(string text, int x, int y)
    {
        var chk = new CheckBox
        {
            Text = text,
            Font = F(9),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(x, y),
            BackColor = Color.Transparent,
        };
        if (_isDark)
            chk.ForeColor = TextColor;
        return chk;
    }

    private Button Btn(string text, int x, int y, int w, int h)
    {
        var b = new Button
        {
            Text = text,
            Font = new("Segoe UI", 9f),
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
        var lbl = Lbl(name, F(9, FontStyle.Bold), TextColor, 16, y + 4);
        parent.Controls.Add(lbl);
        if (tooltip != null)
        {
            var tip = new ToolTip();
            tip.SetToolTip(lbl, tooltip);
        }

        status = new Label
        {
            Font = F(8.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(100, y + 5),
            BackColor = Color.Transparent,
        };
        parent.Controls.Add(status);

        pathBox = new TextBox
        {
            Font = F(8.5f),
            Size = new Size(240, 22),
            Location = new Point(196, y + 2),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = T("Custom path (optional)", "Chemin personnalisé (optionnel)"),
            BackColor = InputBg,
            ForeColor = TextColor,
        };
        parent.Controls.Add(pathBox);

        browse = new Button
        {
            Text = "…",
            Font = F(9),
            Size = new Size(28, 24),
            Location = new Point(444, y + 2),
            FlatStyle = FlatStyle.Flat,
            ForeColor = DimText,
            BackColor = CardColor,
            Cursor = Cursors.Hand,
        };
        browse.FlatAppearance.BorderColor = BorderColor;
        var target = pathBox;
        browse.Click += (_, _) => BrowseExe(target);
        parent.Controls.Add(browse);

        y += 40;
    }

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
        _settings.DarkMode = _isDark;

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

        _btnApply.Text = T("Apply", "Appliquer");
        _btnApply.Visible = installed;

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
        var chip = new SizeChip(size, _isDark);
        chip.RemoveClicked += (_, _) =>
        {
            _sizesFlow.Controls.Remove(chip);
            chip.Dispose();
        };
        _sizesFlow.Controls.Add(chip);
    }

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

    private void BtnTheme_Click(object? sender, EventArgs e)
    {
        _isDark = !_isDark;
        _btnTheme.Text = _isDark ? "☀" : "☾";
        ApplyTheme();
    }

    private void LnkLang_Click(object? sender, EventArgs e)
    {
        _isFr = !_isFr;
        SaveSettings();
        if (_registry.IsRegistered())
            _registry.Register(_settings, Application.ExecutablePath);

        SuspendLayout();
        _scroll.Controls.Clear();
        BuildUI();
        LoadSettings();
        RefreshAll();
        ResumeLayout(true);
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
            Size = new Size(88, 28);
            Margin = new Padding(3, 3, 3, 3);
            BackColor = dark ? ChipBgDark : ChipBgLight;
            ForeColor = AccentColor;
            Cursor = Cursors.Default;

            Controls.Add(new Label
            {
                Text = $"{size}px",
                Font = new("Segoe UI", 8.5f),
                ForeColor = AccentColor,
                AutoSize = false,
                Size = new Size(58, 22),
                Location = new Point(8, 3),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
            });

            var x = new Label
            {
                Text = "×",
                Font = new("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(140, 140, 140),
                AutoSize = false,
                Size = new Size(20, 22),
                Location = new Point(64, 3),
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
