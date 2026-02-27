using System.Drawing;
using System.Drawing.Drawing2D;
using Comprimer.Models;
using Comprimer.Services;

namespace Comprimer.Forms;

/// <summary>
/// Main settings window for Comprimer — modern single-page layout.
/// </summary>
public sealed class SettingsForm : Form
{
    // Colors
    private static readonly Color BgColor = Color.FromArgb(243, 243, 243);
    private static readonly Color CardColor = Color.White;
    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
    private static readonly Color AccentHover = Color.FromArgb(0, 95, 184);
    private static readonly Color TextColor = Color.FromArgb(24, 24, 24);
    private static readonly Color DimText = Color.FromArgb(110, 110, 110);
    private static readonly Color BorderColor = Color.FromArgb(220, 220, 220);
    private static readonly Color GreenColor = Color.FromArgb(16, 124, 16);
    private static readonly Color RedColor = Color.FromArgb(196, 43, 28);
    private static readonly Color ChipBg = Color.FromArgb(232, 242, 252);
    private static readonly Color DangerBg = Color.FromArgb(253, 231, 233);
    private static readonly Color DangerText = Color.FromArgb(196, 43, 28);

    private readonly ConfigService _config;
    private readonly ExecutableService _exe;
    private readonly RegistryService _registry;
    private AppSettings _settings;

    // Install
    private Label _lblInstallStatus = null!;
    private Button _btnToggleInstall = null!;

    // Options
    private CheckBox _chkNested = null!;
    private CheckBox _chkOverwrite = null!;
    private ComboBox _cboMode = null!;

    // Sizes
    private FlowLayoutPanel _sizesFlow = null!;
    private NumericUpDown _nudNewSize = null!;

    // Format ops
    private CheckBox _chkJpgDown = null!;
    private CheckBox _chkJpgWebP = null!;
    private CheckBox _chkPngDown = null!;
    private CheckBox _chkPngWebP = null!;
    private CheckBox _chkPngJpg = null!;
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

    public SettingsForm()
    {
        _config = new ConfigService();
        _exe = new ExecutableService();
        _registry = new RegistryService();
        _settings = _config.Load();

        BuildUI();
        LoadSettings();
        RefreshAll();
    }

    private static Font F(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    private void BuildUI()
    {
        Text = "Comprimer";
        ClientSize = new Size(510, 680);
        MinimumSize = new Size(510, 620);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = BgColor;
        AutoScaleMode = AutoScaleMode.Dpi;

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        Controls.Add(scroll);

        int y = 20;
        int w = 470;
        int left = 20;

        // ─── Header ─────────────────────────────────────────
        scroll.Controls.Add(new Label
        {
            Text = "Comprimer",
            Font = F(20, FontStyle.Bold),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(left, y),
        });
        y += 38;
        scroll.Controls.Add(new Label
        {
            Text = "Image compression & conversion for Explorer",
            Font = F(9),
            ForeColor = DimText,
            AutoSize = true,
            Location = new Point(left + 2, y),
        });
        y += 30;

        // ─── 1. Explorer Integration ────────────────────────
        y = Section(scroll, "Explorer Integration", y, left);
        var card1 = Card(scroll, ref y, left, w, 60);

        _lblInstallStatus = new Label { Font = F(9), AutoSize = true, Location = new Point(16, 10) };
        card1.Controls.Add(_lblInstallStatus);

        _btnToggleInstall = Btn("", 16, 32, 210, 22);
        _btnToggleInstall.Click += BtnToggleInstall_Click;
        card1.Controls.Add(_btnToggleInstall);

        // ─── 2. Options ─────────────────────────────────────
        y = Section(scroll, "Options", y, left);
        var card2 = Card(scroll, ref y, left, w, 76);

        _chkNested = Chk("Nested submenu", 16, 12);
        _chkOverwrite = Chk("Overwrite originals", 16, 38);
        card2.Controls.Add(_chkNested);
        card2.Controls.Add(_chkOverwrite);

        card2.Controls.Add(new Label
        {
            Text = "Downscale limit:",
            Font = F(8.5f),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(230, 14),
        });
        _cboMode = new ComboBox
        {
            Font = F(8.5f),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(338, 10),
            Size = new Size(120, 24),
        };
        _cboMode.Items.AddRange(["Longest side", "Width only", "Height only"]);
        _cboMode.SelectedIndex = 0;
        card2.Controls.Add(_cboMode);

        // ─── 3. Downscale Sizes ─────────────────────────────
        y = Section(scroll, "Downscale Sizes (px)", y, left);
        var card3 = Card(scroll, ref y, left, w, 50);

        _sizesFlow = new FlowLayoutPanel
        {
            Location = new Point(10, 8),
            Size = new Size(340, 34),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
        };
        card3.Controls.Add(_sizesFlow);

        _nudNewSize = new NumericUpDown
        {
            Font = F(8.5f),
            Location = new Point(360, 12),
            Size = new Size(64, 22),
            Minimum = 50,
            Maximum = 10000,
            Value = 1024,
            BorderStyle = BorderStyle.FixedSingle,
        };
        card3.Controls.Add(_nudNewSize);

        var btnAdd = Btn("+", 428, 11, 28, 24);
        btnAdd.Font = F(11, FontStyle.Bold);
        btnAdd.Click += BtnAddSize_Click;
        card3.Controls.Add(btnAdd);

        // ─── 4. Format Operations ───────────────────────────
        y = Section(scroll, "Format Operations", y, left);
        var card4 = Card(scroll, ref y, left, w, 110);

        // Column headers
        card4.Controls.Add(Lbl("Format", F(8, FontStyle.Bold), DimText, 16, 8));
        card4.Controls.Add(Lbl("Downscale", F(8, FontStyle.Bold), DimText, 120, 8));
        card4.Controls.Add(Lbl("→ WebP", F(8, FontStyle.Bold), DimText, 240, 8));
        card4.Controls.Add(Lbl("→ JPG", F(8, FontStyle.Bold), DimText, 360, 8));

        int ry = 30;
        card4.Controls.Add(Lbl("JPG", F(8.5f, FontStyle.Bold), TextColor, 16, ry + 2));
        _chkJpgDown = Chk("", 140, ry); card4.Controls.Add(_chkJpgDown);
        _chkJpgWebP = Chk("", 260, ry); card4.Controls.Add(_chkJpgWebP);

        ry += 26;
        card4.Controls.Add(Lbl("PNG", F(8.5f, FontStyle.Bold), TextColor, 16, ry + 2));
        _chkPngDown = Chk("", 140, ry); card4.Controls.Add(_chkPngDown);
        _chkPngWebP = Chk("", 260, ry); card4.Controls.Add(_chkPngWebP);
        _chkPngJpg = Chk("", 380, ry); card4.Controls.Add(_chkPngJpg);

        ry += 26;
        card4.Controls.Add(Lbl("WebP", F(8.5f, FontStyle.Bold), TextColor, 16, ry + 2));
        _chkWebPDown = Chk("", 140, ry); card4.Controls.Add(_chkWebPDown);

        // ─── 5. External Tools ──────────────────────────────
        y = Section(scroll, "External Tools", y, left);
        var card5 = Card(scroll, ref y, left, w, 126);

        int ty = 10;
        AddToolRow(card5, "pngquant", ref ty, out _lblPngquantStatus, out _txtPngquant, out _btnBrowsePngquant);
        AddToolRow(card5, "cwebp", ref ty, out _lblCwebpStatus, out _txtCwebp, out _btnBrowseCwebp);
        AddToolRow(card5, "cjpeg", ref ty, out _lblCjpegStatus, out _txtCjpeg, out _btnBrowseCjpeg, "cjpeg from mozjpeg");
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

    private int Section(Control parent, string title, int y, int left)
    {
        y += 6;
        parent.Controls.Add(new Label
        {
            Text = title.ToUpperInvariant(),
            Font = F(8, FontStyle.Bold),
            ForeColor = DimText,
            AutoSize = true,
            Location = new Point(left + 4, y),
        });
        return y + 18;
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
        y += h + 6;
        return p;
    }

    private static CheckBox Chk(string text, int x, int y) => new()
    {
        Text = text,
        Font = F(8.5f),
        ForeColor = TextColor,
        AutoSize = true,
        Location = new Point(x, y),
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
            Location = new Point(190, y + 1),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Custom path (optional)",
        };
        parent.Controls.Add(pathBox);

        browse = new Button
        {
            Text = "…",
            Font = F(8),
            Size = new Size(28, 22),
            Location = new Point(406, y + 1),
            FlatStyle = FlatStyle.Flat,
            ForeColor = DimText,
            BackColor = CardColor,
            Cursor = Cursors.Hand,
        };
        browse.FlatAppearance.BorderColor = BorderColor;
        var target = pathBox;
        browse.Click += (_, _) => BrowseExe(target);
        parent.Controls.Add(browse);

        y += 36;
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
        _chkPngDown.Checked = _settings.Png.Downscale;
        _chkPngWebP.Checked = _settings.Png.ConvertToWebP;
        _chkPngJpg.Checked = _settings.Png.ConvertToJpg;
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

        _settings.AvailableSizes.Clear();
        foreach (Control c in _sizesFlow.Controls)
            if (c is SizeChip chip) _settings.AvailableSizes.Add(chip.SizeValue);
        if (_settings.AvailableSizes.Count == 0)
            _settings.AvailableSizes.Add(1024);

        _settings.Jpg.Downscale = _chkJpgDown.Checked;
        _settings.Jpg.ConvertToWebP = _chkJpgWebP.Checked;
        _settings.Png.Downscale = _chkPngDown.Checked;
        _settings.Png.ConvertToWebP = _chkPngWebP.Checked;
        _settings.Png.ConvertToJpg = _chkPngJpg.Checked;
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
        _lblInstallStatus.Text = installed ? "●  Context menu is active" : "○  Not installed";
        _lblInstallStatus.ForeColor = installed ? GreenColor : DimText;

        if (installed)
        {
            _btnToggleInstall.Text = "Remove from Explorer";
            _btnToggleInstall.ForeColor = DangerText;
            _btnToggleInstall.BackColor = DangerBg;
            _btnToggleInstall.FlatAppearance.BorderColor = DangerText;
        }
        else
        {
            _btnToggleInstall.Text = "Add to Explorer";
            _btnToggleInstall.ForeColor = Color.White;
            _btnToggleInstall.BackColor = AccentColor;
            _btnToggleInstall.FlatAppearance.BorderColor = AccentColor;
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
            pathBox.PlaceholderText = "Custom path set";
        }
        else if (_exe.IsInPath(exeName))
        {
            status.Text = "✓ PATH";
            status.ForeColor = GreenColor;
            pathBox.PlaceholderText = "Using PATH (override optional)";
        }
        else
        {
            status.Text = "✗ Missing";
            status.ForeColor = RedColor;
            pathBox.PlaceholderText = "Path to executable…";
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

    private void BrowseExe(TextBox target)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
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

        public SizeChip(int size)
        {
            SizeValue = size;
            Size = new Size(84, 26);
            Margin = new Padding(2, 3, 2, 3);
            BackColor = ChipBg;
            Cursor = Cursors.Default;

            Controls.Add(new Label
            {
                Text = $"{size}",
                Font = new("Segoe UI", 8.25f),
                ForeColor = AccentColor,
                AutoSize = false,
                Size = new Size(54, 20),
                Location = new Point(8, 3),
                TextAlign = ContentAlignment.MiddleLeft,
            });

            var x = new Label
            {
                Text = "×",
                Font = new("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(140, 140, 140),
                AutoSize = false,
                Size = new Size(18, 20),
                Location = new Point(62, 3),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
            };
            x.Click += (_, _) => RemoveClicked?.Invoke(this, EventArgs.Empty);
            x.MouseEnter += (_, _) => x.ForeColor = RedColor;
            x.MouseLeave += (_, _) => x.ForeColor = Color.FromArgb(140, 140, 140);
            Controls.Add(x);
        }
    }
}
