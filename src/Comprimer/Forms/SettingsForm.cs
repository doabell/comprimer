using System.Diagnostics;
using Comprimer.Models;
using Comprimer.Services;

namespace Comprimer.Forms;

/// <summary>
/// Main settings window for Comprimer.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly ConfigService _config;
    private readonly ExecutableService _exe;
    private readonly RegistryService _registry;
    private AppSettings _settings;

    // Operations tab controls
    private CheckBox _chkNestedMenu = null!;
    private CheckBox _chkOverwrite = null!;
    private NumericUpDown _nudDownscaleSize = null!;
    private ListBox _lstSizes = null!;
    private Button _btnAddSize = null!;
    private Button _btnRemoveSize = null!;

    // JPG
    private CheckBox _chkJpgDownscale = null!;
    private CheckBox _chkJpgToWebP = null!;

    // PNG
    private CheckBox _chkPngDownscale = null!;
    private CheckBox _chkPngToWebP = null!;
    private CheckBox _chkPngToJpg = null!;

    // WebP
    private CheckBox _chkWebPDownscale = null!;

    // Executables tab controls
    private Label _lblPngquantStatus = null!;
    private TextBox _txtPngquantPath = null!;
    private Label _lblImg2WebPStatus = null!;
    private TextBox _txtImg2WebPPath = null!;
    private Label _lblCjpegStatus = null!;
    private TextBox _txtCjpegPath = null!;

    // Explorer buttons
    private Button _btnAddToExplorer = null!;
    private Button _btnRemoveFromExplorer = null!;

    public SettingsForm()
    {
        _config = new ConfigService();
        _exe = new ExecutableService();
        _registry = new RegistryService();
        _settings = _config.Load();

        InitializeComponent();
        LoadSettings();
        RefreshExecutableStatus();
    }

    private void InitializeComponent()
    {
        Text = "Comprimer Settings";
        Size = new Size(520, 580);
        MinimumSize = new Size(480, 540);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(8, 4),
        };

        tabControl.TabPages.Add(CreateOperationsTab());
        tabControl.TabPages.Add(CreateExecutablesTab());

        // Bottom panel with explorer buttons
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            Padding = new Padding(12, 8, 12, 8),
        };

        _btnAddToExplorer = new Button
        {
            Text = "Add to Explorer",
            Size = new Size(140, 36),
            Location = new Point(12, 12),
            FlatStyle = FlatStyle.System,
        };
        _btnAddToExplorer.Click += BtnAddToExplorer_Click;

        _btnRemoveFromExplorer = new Button
        {
            Text = "Remove from Explorer",
            Size = new Size(160, 36),
            Location = new Point(164, 12),
            FlatStyle = FlatStyle.System,
        };
        _btnRemoveFromExplorer.Click += BtnRemoveFromExplorer_Click;

        var btnSave = new Button
        {
            Text = "Save",
            Size = new Size(80, 36),
            Location = new Point(400, 12),
            FlatStyle = FlatStyle.System,
        };
        btnSave.Click += BtnSave_Click;

        bottomPanel.Controls.AddRange([_btnAddToExplorer, _btnRemoveFromExplorer, btnSave]);

        Controls.Add(tabControl);
        Controls.Add(bottomPanel);
    }

    private TabPage CreateOperationsTab()
    {
        var tab = new TabPage("Operations") { Padding = new Padding(12) };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int y = 8;

        // Menu style
        var grpMenu = CreateGroupBox("Menu Style", 8, ref y, 460, 80);
        _chkNestedMenu = new CheckBox { Text = "Nested submenu (under \"Comprimer\")", Location = new Point(16, 24), AutoSize = true };
        _chkOverwrite = new CheckBox { Text = "Overwrite original file (no suffix)", Location = new Point(16, 48), AutoSize = true };
        grpMenu.Controls.AddRange([_chkNestedMenu, _chkOverwrite]);
        panel.Controls.Add(grpMenu);

        // Downscale sizes
        var grpSizes = CreateGroupBox("Downscale Sizes", 8, ref y, 460, 100);
        _lstSizes = new ListBox { Location = new Point(16, 24), Size = new Size(180, 60) };
        _nudDownscaleSize = new NumericUpDown
        {
            Location = new Point(210, 24),
            Size = new Size(80, 28),
            Minimum = 100,
            Maximum = 10000,
            Value = 1000,
        };
        _btnAddSize = new Button { Text = "Add", Location = new Point(300, 23), Size = new Size(60, 28), FlatStyle = FlatStyle.System };
        _btnAddSize.Click += BtnAddSize_Click;
        _btnRemoveSize = new Button { Text = "Remove", Location = new Point(370, 23), Size = new Size(70, 28), FlatStyle = FlatStyle.System };
        _btnRemoveSize.Click += BtnRemoveSize_Click;
        grpSizes.Controls.AddRange([_lstSizes, _nudDownscaleSize, _btnAddSize, _btnRemoveSize]);
        panel.Controls.Add(grpSizes);

        // JPG/JPEG
        var grpJpg = CreateGroupBox("JPG / JPEG", 8, ref y, 460, 80);
        _chkJpgDownscale = new CheckBox { Text = "Downscale", Location = new Point(16, 24), AutoSize = true };
        _chkJpgToWebP = new CheckBox { Text = "Convert to WebP", Location = new Point(16, 48), AutoSize = true };
        grpJpg.Controls.AddRange([_chkJpgDownscale, _chkJpgToWebP]);
        panel.Controls.Add(grpJpg);

        // PNG
        var grpPng = CreateGroupBox("PNG", 8, ref y, 460, 104);
        _chkPngDownscale = new CheckBox { Text = "Downscale", Location = new Point(16, 24), AutoSize = true };
        _chkPngToWebP = new CheckBox { Text = "Convert to WebP", Location = new Point(16, 48), AutoSize = true };
        _chkPngToJpg = new CheckBox { Text = "Convert to JPG (mozjpeg)", Location = new Point(16, 72), AutoSize = true };
        grpPng.Controls.AddRange([_chkPngDownscale, _chkPngToWebP, _chkPngToJpg]);
        panel.Controls.Add(grpPng);

        // WebP
        var grpWebP = CreateGroupBox("WebP", 8, ref y, 460, 56);
        _chkWebPDownscale = new CheckBox { Text = "Downscale", Location = new Point(16, 24), AutoSize = true };
        grpWebP.Controls.Add(_chkWebPDownscale);
        panel.Controls.Add(grpWebP);

        tab.Controls.Add(panel);
        return tab;
    }

    private TabPage CreateExecutablesTab()
    {
        var tab = new TabPage("Executables") { Padding = new Padding(12) };
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int y = 8;

        // pngquant
        var grpPngquant = CreateGroupBox("pngquant", 8, ref y, 460, 80);
        _lblPngquantStatus = new Label { Location = new Point(16, 28), AutoSize = true };
        _txtPngquantPath = new TextBox { Location = new Point(16, 50), Size = new Size(340, 24) };
        var btnBrowsePngquant = new Button { Text = "...", Location = new Point(366, 49), Size = new Size(36, 26), FlatStyle = FlatStyle.System };
        btnBrowsePngquant.Click += (_, _) => BrowseExecutable(_txtPngquantPath);
        grpPngquant.Controls.AddRange([_lblPngquantStatus, _txtPngquantPath, btnBrowsePngquant]);
        panel.Controls.Add(grpPngquant);

        // img2webp / cwebp
        var grpImg2WebP = CreateGroupBox("img2webp / cwebp", 8, ref y, 460, 80);
        _lblImg2WebPStatus = new Label { Location = new Point(16, 28), AutoSize = true };
        _txtImg2WebPPath = new TextBox { Location = new Point(16, 50), Size = new Size(340, 24) };
        var btnBrowseImg2WebP = new Button { Text = "...", Location = new Point(366, 49), Size = new Size(36, 26), FlatStyle = FlatStyle.System };
        btnBrowseImg2WebP.Click += (_, _) => BrowseExecutable(_txtImg2WebPPath);
        grpImg2WebP.Controls.AddRange([_lblImg2WebPStatus, _txtImg2WebPPath, btnBrowseImg2WebP]);
        panel.Controls.Add(grpImg2WebP);

        // cjpeg (mozjpeg)
        var grpCjpeg = CreateGroupBox("cjpeg (mozjpeg)", 8, ref y, 460, 80);
        _lblCjpegStatus = new Label { Location = new Point(16, 28), AutoSize = true };
        _txtCjpegPath = new TextBox { Location = new Point(16, 50), Size = new Size(340, 24) };
        var btnBrowseCjpeg = new Button { Text = "...", Location = new Point(366, 49), Size = new Size(36, 26), FlatStyle = FlatStyle.System };
        btnBrowseCjpeg.Click += (_, _) => BrowseExecutable(_txtCjpegPath);
        grpCjpeg.Controls.AddRange([_lblCjpegStatus, _txtCjpegPath, btnBrowseCjpeg]);
        panel.Controls.Add(grpCjpeg);

        tab.Controls.Add(panel);
        return tab;
    }

    private static GroupBox CreateGroupBox(string title, int x, ref int y, int width, int height)
    {
        var grp = new GroupBox
        {
            Text = title,
            Location = new Point(x, y),
            Size = new Size(width, height),
        };
        y += height + 8;
        return grp;
    }

    private void LoadSettings()
    {
        _chkNestedMenu.Checked = _settings.NestedMenu;
        _chkOverwrite.Checked = _settings.OverwriteOriginal;

        _lstSizes.Items.Clear();
        foreach (var size in _settings.AvailableSizes)
            _lstSizes.Items.Add($"{size}px");

        _chkJpgDownscale.Checked = _settings.Jpg.Downscale;
        _chkJpgToWebP.Checked = _settings.Jpg.ConvertToWebP;

        _chkPngDownscale.Checked = _settings.Png.Downscale;
        _chkPngToWebP.Checked = _settings.Png.ConvertToWebP;
        _chkPngToJpg.Checked = _settings.Png.ConvertToJpg;

        _chkWebPDownscale.Checked = _settings.WebP.Downscale;

        _txtPngquantPath.Text = _settings.Executables.PngquantPath ?? "";
        _txtImg2WebPPath.Text = _settings.Executables.Img2WebPPath ?? "";
        _txtCjpegPath.Text = _settings.Executables.CjpegPath ?? "";
    }

    private void SaveSettings()
    {
        _settings.NestedMenu = _chkNestedMenu.Checked;
        _settings.OverwriteOriginal = _chkOverwrite.Checked;

        _settings.AvailableSizes.Clear();
        foreach (var item in _lstSizes.Items)
        {
            var text = item.ToString()!.Replace("px", "");
            if (int.TryParse(text, out var size))
                _settings.AvailableSizes.Add(size);
        }
        if (_settings.AvailableSizes.Count == 0)
            _settings.AvailableSizes.Add(1000);

        _settings.Jpg.Downscale = _chkJpgDownscale.Checked;
        _settings.Jpg.ConvertToWebP = _chkJpgToWebP.Checked;

        _settings.Png.Downscale = _chkPngDownscale.Checked;
        _settings.Png.ConvertToWebP = _chkPngToWebP.Checked;
        _settings.Png.ConvertToJpg = _chkPngToJpg.Checked;

        _settings.WebP.Downscale = _chkWebPDownscale.Checked;

        var pngquantPath = _txtPngquantPath.Text.Trim();
        _settings.Executables.PngquantPath = pngquantPath.Length > 0 ? pngquantPath : null;

        var img2webpPath = _txtImg2WebPPath.Text.Trim();
        _settings.Executables.Img2WebPPath = img2webpPath.Length > 0 ? img2webpPath : null;

        var cjpegPath = _txtCjpegPath.Text.Trim();
        _settings.Executables.CjpegPath = cjpegPath.Length > 0 ? cjpegPath : null;

        _config.Save(_settings);
    }

    private void RefreshExecutableStatus()
    {
        SetExecutableStatus(_lblPngquantStatus, _txtPngquantPath, "pngquant",
            _settings.Executables.PngquantPath);
        SetExecutableStatus(_lblImg2WebPStatus, _txtImg2WebPPath, "cwebp",
            _settings.Executables.Img2WebPPath);
        SetExecutableStatus(_lblCjpegStatus, _txtCjpegPath, "cjpeg",
            _settings.Executables.CjpegPath);
    }

    private void SetExecutableStatus(Label statusLabel, TextBox pathBox, string exeName, string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            statusLabel.Text = "✓ Custom path configured";
            statusLabel.ForeColor = Color.Green;
            pathBox.Enabled = true;
        }
        else if (_exe.IsInPath(exeName))
        {
            statusLabel.Text = "✓ Detected in PATH";
            statusLabel.ForeColor = Color.Green;
            pathBox.Enabled = false;
            pathBox.Text = "Detected in PATH";
        }
        else
        {
            statusLabel.Text = "✗ Not found — enter path below";
            statusLabel.ForeColor = Color.Red;
            pathBox.Enabled = true;
        }
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
        var size = (int)_nudDownscaleSize.Value;
        var label = $"{size}px";
        if (!_lstSizes.Items.Contains(label))
        {
            _lstSizes.Items.Add(label);
        }
    }

    private void BtnRemoveSize_Click(object? sender, EventArgs e)
    {
        if (_lstSizes.SelectedIndex >= 0)
            _lstSizes.Items.RemoveAt(_lstSizes.SelectedIndex);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        SaveSettings();
        RefreshExecutableStatus();
        MessageBox.Show("Settings saved.", "Comprimer", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BtnAddToExplorer_Click(object? sender, EventArgs e)
    {
        SaveSettings();
        var exePath = Application.ExecutablePath;
        _registry.Register(_settings, exePath);
        MessageBox.Show(
            "Context menu entries added to Explorer.\nYou may need to restart Explorer for changes to take effect.",
            "Comprimer", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BtnRemoveFromExplorer_Click(object? sender, EventArgs e)
    {
        _registry.Unregister();
        MessageBox.Show(
            "Context menu entries removed from Explorer.",
            "Comprimer", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
