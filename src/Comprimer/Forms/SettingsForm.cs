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
    private static readonly Color BgColor = Color.FromArgb(250, 250, 250);
    private static readonly Color CardColor = Color.White;
    private static readonly Color AccentColor = Color.FromArgb(0, 120, 212);
    private static readonly Color TextColor = Color.FromArgb(26, 26, 26);
    private static readonly Color SecondaryText = Color.FromArgb(96, 96, 96);
    private static readonly Color BorderColor = Color.FromArgb(228, 228, 228);
    private static readonly Color SuccessColor = Color.FromArgb(16, 124, 16);
    private static readonly Color ErrorColor = Color.FromArgb(209, 52, 56);

    // Fonts
    private static readonly Font TitleFont = new("Segoe UI", 18f, FontStyle.Bold);
    private static readonly Font SubtitleFont = new("Segoe UI", 9.5f, FontStyle.Regular);
    private static readonly Font SectionFont = new("Segoe UI", 10f, FontStyle.Bold);
    private static readonly Font BodyFont = new("Segoe UI", 9.5f, FontStyle.Regular);
    private static readonly Font SmallFont = new("Segoe UI", 8.5f, FontStyle.Regular);

    private readonly ConfigService _config;
    private readonly ExecutableService _exe;
    private readonly RegistryService _registry;
    private AppSettings _settings;

    // Context Menu section
    private Label _lblInstallStatus = null!;
    private Button _btnExplorer = null!;
    private CheckBox _chkNestedMenu = null!;
    private CheckBox _chkOverwrite = null!;

    // Downscale sizes
    private FlowLayoutPanel _sizesPanel = null!;
    private NumericUpDown _nudSize = null!;

    // Format operations
    private CheckBox _chkJpgDownscale = null!;
    private CheckBox _chkJpgToWebP = null!;
    private CheckBox _chkPngDownscale = null!;
    private CheckBox _chkPngToWebP = null!;
    private CheckBox _chkPngToJpg = null!;
    private CheckBox _chkWebPDownscale = null!;

    // Executables
    private Label _lblPngquantStatus = null!;
    private TextBox _txtPngquantPath = null!;
    private Button _btnBrowsePngquant = null!;
    private Label _lblCwebpStatus = null!;
    private TextBox _txtCwebpPath = null!;
    private Button _btnBrowseCwebp = null!;
    private Label _lblCjpegStatus = null!;
    private TextBox _txtCjpegPath = null!;
    private Button _btnBrowseCjpeg = null!;

    public SettingsForm()
    {
        _config = new ConfigService();
        _exe = new ExecutableService();
        _registry = new RegistryService();
        _settings = _config.Load();

        InitializeComponent();
        LoadSettings();
        RefreshStatus();
    }

    private void InitializeComponent()
    {
        Text = "Comprimer";
        Size = new Size(500, 680);
        MinimumSize = new Size(480, 600);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = BgColor;

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(24, 16, 24, 16),
        };

        int y = 16;

        // Title
        var lblTitle = new Label
        {
            Text = "Comprimer",
            Font = TitleFont,
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(24, y),
        };
        scroll.Controls.Add(lblTitle);
        y += 36;

        var lblSubtitle = new Label
        {
            Text = "Image compression & conversion for Windows Explorer",
            Font = SubtitleFont,
            ForeColor = SecondaryText,
            AutoSize = true,
            Location = new Point(26, y),
        };
        scroll.Controls.Add(lblSubtitle);
        y += 32;

        // — Context Menu Card —
        y = AddSectionHeader(scroll, "CONTEXT MENU", y);
        var cardCtx = CreateCard(scroll, ref y, 120);

        _lblInstallStatus = new Label
        {
            Font = BodyFont,
            AutoSize = true,
            Location = new Point(16, 14),
        };
        cardCtx.Controls.Add(_lblInstallStatus);

        _btnExplorer = new Button
        {
            Font = BodyFont,
            Size = new Size(200, 32),
            Location = new Point(16, 40),
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = AccentColor,
            Cursor = Cursors.Hand,
        };
        _btnExplorer.FlatAppearance.BorderSize = 0;
        _btnExplorer.Click += BtnExplorer_Click;
        cardCtx.Controls.Add(_btnExplorer);

        _chkNestedMenu = CreateCheckBox("Use nested submenu", 16, 82);
        _chkOverwrite = CreateCheckBox("Overwrite original files", 220, 82);
        cardCtx.Controls.AddRange([_chkNestedMenu, _chkOverwrite]);

        // — Downscale Sizes Card —
        y = AddSectionHeader(scroll, "DOWNSCALE SIZES", y);
        var cardSizes = CreateCard(scroll, ref y, 72);

        _sizesPanel = new FlowLayoutPanel
        {
            Location = new Point(12, 10),
            Size = new Size(300, 50),
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = false,
            WrapContents = true,
        };
        cardSizes.Controls.Add(_sizesPanel);

        _nudSize = new NumericUpDown
        {
            Font = BodyFont,
            Location = new Point(320, 12),
            Size = new Size(70, 28),
            Minimum = 100,
            Maximum = 10000,
            Value = 1000,
            BorderStyle = BorderStyle.FixedSingle,
        };
        cardSizes.Controls.Add(_nudSize);

        var btnAdd = new Button
        {
            Text = "+",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Location = new Point(394, 10),
            Size = new Size(32, 30),
            FlatStyle = FlatStyle.Flat,
            ForeColor = AccentColor,
            BackColor = CardColor,
            Cursor = Cursors.Hand,
        };
        btnAdd.FlatAppearance.BorderColor = AccentColor;
        btnAdd.Click += BtnAddSize_Click;
        cardSizes.Controls.Add(btnAdd);

        // — Operations Card —
        y = AddSectionHeader(scroll, "OPERATIONS", y);
        var cardOps = CreateCard(scroll, ref y, 152);

        int opY = 12;
        AddFormatRow(cardOps, "JPG / JPEG", ref opY, out _chkJpgDownscale, out _chkJpgToWebP, out _);
        AddFormatRow(cardOps, "PNG", ref opY, out _chkPngDownscale, out _chkPngToWebP, out _chkPngToJpg);
        AddFormatRow(cardOps, "WebP", ref opY, out _chkWebPDownscale, out _, out _);

        // — External Tools Card —
        y = AddSectionHeader(scroll, "EXTERNAL TOOLS", y);
        var cardTools = CreateCard(scroll, ref y, 138);

        int toolY = 12;
        AddToolRow(cardTools, "pngquant", ref toolY, out _lblPngquantStatus, out _txtPngquantPath, out _btnBrowsePngquant);
        AddToolRow(cardTools, "cwebp", ref toolY, out _lblCwebpStatus, out _txtCwebpPath, out _btnBrowseCwebp);
        AddToolRow(cardTools, "cjpeg", ref toolY, out _lblCjpegStatus, out _txtCjpegPath, out _btnBrowseCjpeg);

        Controls.Add(scroll);
    }

    private int AddSectionHeader(Control parent, string title, int y)
    {
        y += 8;
        var lbl = new Label
        {
            Text = title,
            Font = SectionFont,
            ForeColor = SecondaryText,
            AutoSize = true,
            Location = new Point(28, y),
        };
        parent.Controls.Add(lbl);
        return y + 22;
    }

    private Panel CreateCard(Control parent, ref int y, int height)
    {
        var card = new RoundedPanel
        {
            Location = new Point(24, y),
            Size = new Size(430, height),
            BackColor = CardColor,
            BorderColor = BorderColor,
        };
        parent.Controls.Add(card);
        y += height + 8;
        return card;
    }

    private CheckBox CreateCheckBox(string text, int x, int y)
    {
        return new CheckBox
        {
            Text = text,
            Font = SmallFont,
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(x, y),
        };
    }

    private void AddFormatRow(Control parent, string format, ref int y, out CheckBox chkDown, out CheckBox chkWebP, out CheckBox chkJpg)
    {
        var lblFmt = new Label
        {
            Text = format,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = TextColor,
            Size = new Size(80, 20),
            Location = new Point(16, y + 2),
        };
        parent.Controls.Add(lblFmt);

        chkDown = new CheckBox
        {
            Text = "Downscale",
            Font = SmallFont,
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(100, y),
        };
        parent.Controls.Add(chkDown);

        chkWebP = null!;
        chkJpg = null!;

        if (format != "WebP")
        {
            chkWebP = new CheckBox
            {
                Text = "→ WebP",
                Font = SmallFont,
                ForeColor = TextColor,
                AutoSize = true,
                Location = new Point(210, y),
            };
            parent.Controls.Add(chkWebP);
        }

        if (format == "PNG")
        {
            chkJpg = new CheckBox
            {
                Text = "→ JPG",
                Font = SmallFont,
                ForeColor = TextColor,
                AutoSize = true,
                Location = new Point(310, y),
            };
            parent.Controls.Add(chkJpg);
        }

        y += 40;
    }

    private void AddToolRow(Control parent, string name, ref int y, out Label status, out TextBox pathBox, out Button browse)
    {
        var lbl = new Label
        {
            Text = name,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = TextColor,
            Size = new Size(70, 20),
            Location = new Point(16, y + 2),
        };
        parent.Controls.Add(lbl);

        status = new Label
        {
            Font = SmallFont,
            AutoSize = true,
            Location = new Point(90, y + 3),
        };
        parent.Controls.Add(status);

        pathBox = new TextBox
        {
            Font = SmallFont,
            Size = new Size(200, 24),
            Location = new Point(200, y),
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false,
        };
        parent.Controls.Add(pathBox);

        browse = new Button
        {
            Text = "Browse…",
            Font = SmallFont,
            Size = new Size(64, 24),
            Location = new Point(350, y),
            FlatStyle = FlatStyle.Flat,
            ForeColor = AccentColor,
            BackColor = CardColor,
            Cursor = Cursors.Hand,
            Visible = false,
        };
        browse.FlatAppearance.BorderColor = BorderColor;
        var target = pathBox;
        browse.Click += (_, _) => BrowseExecutable(target);
        parent.Controls.Add(browse);

        y += 38;
    }

    private void LoadSettings()
    {
        _chkNestedMenu.Checked = _settings.NestedMenu;
        _chkOverwrite.Checked = _settings.OverwriteOriginal;

        RefreshSizeTags();

        _chkJpgDownscale.Checked = _settings.Jpg.Downscale;
        if (_chkJpgToWebP != null) _chkJpgToWebP.Checked = _settings.Jpg.ConvertToWebP;

        _chkPngDownscale.Checked = _settings.Png.Downscale;
        if (_chkPngToWebP != null) _chkPngToWebP.Checked = _settings.Png.ConvertToWebP;
        if (_chkPngToJpg != null) _chkPngToJpg.Checked = _settings.Png.ConvertToJpg;

        _chkWebPDownscale.Checked = _settings.WebP.Downscale;

        _txtPngquantPath.Text = _settings.Executables.PngquantPath ?? "";
        _txtCwebpPath.Text = _settings.Executables.Img2WebPPath ?? "";
        _txtCjpegPath.Text = _settings.Executables.CjpegPath ?? "";
    }

    private void SaveSettings()
    {
        _settings.NestedMenu = _chkNestedMenu.Checked;
        _settings.OverwriteOriginal = _chkOverwrite.Checked;

        _settings.AvailableSizes.Clear();
        foreach (Control c in _sizesPanel.Controls)
        {
            if (c is SizeTag tag)
                _settings.AvailableSizes.Add(tag.SizeValue);
        }
        if (_settings.AvailableSizes.Count == 0)
            _settings.AvailableSizes.Add(1000);

        _settings.Jpg.Downscale = _chkJpgDownscale.Checked;
        _settings.Jpg.ConvertToWebP = _chkJpgToWebP?.Checked ?? false;

        _settings.Png.Downscale = _chkPngDownscale.Checked;
        _settings.Png.ConvertToWebP = _chkPngToWebP?.Checked ?? false;
        _settings.Png.ConvertToJpg = _chkPngToJpg?.Checked ?? false;

        _settings.WebP.Downscale = _chkWebPDownscale.Checked;

        var pngquantPath = _txtPngquantPath.Text.Trim();
        _settings.Executables.PngquantPath = pngquantPath.Length > 0 ? pngquantPath : null;

        var cwebpPath = _txtCwebpPath.Text.Trim();
        _settings.Executables.Img2WebPPath = cwebpPath.Length > 0 ? cwebpPath : null;

        var cjpegPath = _txtCjpegPath.Text.Trim();
        _settings.Executables.CjpegPath = cjpegPath.Length > 0 ? cjpegPath : null;

        _config.Save(_settings);
    }

    private void RefreshStatus()
    {
        var installed = _registry.IsRegistered();
        _lblInstallStatus.Text = installed ? "●  Installed in Explorer" : "○  Not installed";
        _lblInstallStatus.ForeColor = installed ? SuccessColor : SecondaryText;
        _btnExplorer.Text = installed ? "Remove from Explorer" : "Add to Explorer";
        _btnExplorer.BackColor = installed ? Color.FromArgb(220, 220, 220) : AccentColor;
        _btnExplorer.ForeColor = installed ? TextColor : Color.White;

        RefreshToolStatus(_lblPngquantStatus, _txtPngquantPath, _btnBrowsePngquant, "pngquant", _settings.Executables.PngquantPath);
        RefreshToolStatus(_lblCwebpStatus, _txtCwebpPath, _btnBrowseCwebp, "cwebp", _settings.Executables.Img2WebPPath);
        RefreshToolStatus(_lblCjpegStatus, _txtCjpegPath, _btnBrowseCjpeg, "cjpeg", _settings.Executables.CjpegPath);
    }

    private void RefreshToolStatus(Label status, TextBox pathBox, Button browse, string exeName, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            status.Text = "✓ Custom path set";
            status.ForeColor = SuccessColor;
            pathBox.Visible = false;
            browse.Visible = false;
        }
        else if (_exe.IsInPath(exeName))
        {
            status.Text = "✓ Found in PATH";
            status.ForeColor = SuccessColor;
            pathBox.Visible = false;
            browse.Visible = false;
        }
        else
        {
            status.Text = "✗ Not found";
            status.ForeColor = ErrorColor;
            pathBox.Visible = true;
            browse.Visible = true;
        }
    }

    private void RefreshSizeTags()
    {
        _sizesPanel.Controls.Clear();
        foreach (var size in _settings.AvailableSizes)
            AddSizeTag(size);
    }

    private void AddSizeTag(int size)
    {
        var tag = new SizeTag(size, SmallFont, AccentColor, BorderColor);
        tag.RemoveClicked += (_, _) =>
        {
            _sizesPanel.Controls.Remove(tag);
            tag.Dispose();
        };
        _sizesPanel.Controls.Add(tag);
    }

    private void BrowseExecutable(TextBox targetBox)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            targetBox.Text = dlg.FileName;
        }
    }

    private void BtnAddSize_Click(object? sender, EventArgs e)
    {
        var size = (int)_nudSize.Value;
        // Don't add duplicates
        foreach (Control c in _sizesPanel.Controls)
            if (c is SizeTag t && t.SizeValue == size) return;
        AddSizeTag(size);
    }

    private void BtnExplorer_Click(object? sender, EventArgs e)
    {
        var installed = _registry.IsRegistered();
        if (installed)
        {
            _registry.Unregister();
        }
        else
        {
            SaveSettings();
            _registry.Register(_settings, Application.ExecutablePath);
        }
        RefreshStatus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        base.OnFormClosing(e);
    }

    /// <summary>
    /// Custom panel with rounded corners and border.
    /// </summary>
    private sealed class RoundedPanel : Panel
    {
        public Color BorderColor { get; set; } = Color.FromArgb(228, 228, 228);
        private const int Radius = 8;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = GetRoundedPath(rect, Radius);
            using var brush = new SolidBrush(BackColor);
            using var pen = new Pen(BorderColor, 1f);

            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        private static GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// A removable size tag chip (e.g. "1000px ×").
    /// </summary>
    private sealed class SizeTag : UserControl
    {
        public int SizeValue { get; }
        public event EventHandler? RemoveClicked;

        public SizeTag(int size, Font font, Color accent, Color border)
        {
            SizeValue = size;
            Size = new Size(86, 26);
            Margin = new Padding(2);
            BackColor = Color.FromArgb(240, 247, 255);
            Cursor = Cursors.Default;

            var lbl = new Label
            {
                Text = $"{size}px",
                Font = font,
                ForeColor = accent,
                AutoSize = false,
                Size = new Size(56, 20),
                Location = new Point(6, 3),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            Controls.Add(lbl);

            var btn = new Label
            {
                Text = "×",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 160),
                AutoSize = false,
                Size = new Size(20, 20),
                Location = new Point(62, 3),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
            };
            btn.Click += (_, _) => RemoveClicked?.Invoke(this, EventArgs.Empty);
            btn.MouseEnter += (_, _) => btn.ForeColor = Color.FromArgb(209, 52, 56);
            btn.MouseLeave += (_, _) => btn.ForeColor = Color.FromArgb(160, 160, 160);
            Controls.Add(btn);
        }
    }
}
