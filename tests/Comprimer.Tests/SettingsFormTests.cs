using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Comprimer.Forms;
using Comprimer.Models;
using Comprimer.Services;
using Xunit;

namespace Comprimer.Tests;

public class SettingsFormTests
{
    [Fact]
    public void LanguageSwitch_PreservesLiveEditsAndScrollWithoutRebuildingOrReadingRegistry()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var file = Path.Combine(Path.GetTempPath(), $"comprimer-language-{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigService(file);
                config.Save(new AppSettings
                {
                    ComprimerPath = "",
                    Executables = new ExecutableSettings
                    {
                        PngquantPath = "", CjpegPath = "", Img2WebPPath = "", DwebpPath = "",
                    },
                });
                var registry = new FakeRegistry();
                using var form = new SettingsForm(config, registry)
                {
                    StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000),
                    ShowInTaskbar = false, Opacity = 0,
                };
                form.Show();
                Application.DoEvents();
                T Find<T>(string name) where T : Control => Assert.IsAssignableFrom<T>(Assert.Single(form.Controls.Find(name, true)));

                const string selectedPath = @"C:\My chosen encoders\pngquant.exe";
                var path = Find<TextBox>("Path.pngquant");
                path.Text = selectedPath;
                path.Select(3, 6);
                Find<NumericUpDown>("QualityValue.PNG").Value = 71;
                Find<NumericUpDown>("MinimumPngQuality").Value = 42;
                Find<NumericUpDown>("QualityValue.JPG").Value = 77;
                Find<TrackBar>("Quality.WebP").Value = 64;
                Find<ComboBox>("ResizeDimension").SelectedIndex = 2;
                Find<NumericUpDown>("NewSize").Value = 1536;
                Find<CheckBox>("AutoMode").Checked = false;
                Find<CheckBox>("AutoResize.512").Checked = false;
                Find<CheckBox>("Resize.1024").Checked = false;
                Find<CheckBox>("JPG.1").Checked = false;
                var page = Find<Panel>("SettingsPage");
                page.AutoScrollPosition = new Point(0, 260);
                Application.DoEvents();
                var scroll = page.AutoScrollPosition;
                Assert.True(scroll.Y < 0);
                var controls = Descendants(form).ToArray();
                var handles = controls.Where(control => control.IsHandleCreated).Select(control => (control, control.Handle)).ToArray();
                int reads = registry.Reads;

                foreach (var expectedLanguage in new[] { "fr", "en", "fr" })
                {
                    Find<Button>("Language").PerformClick();
                    Application.DoEvents();
                    Assert.Equal(controls, Descendants(form));
                    Assert.All(handles, pair => Assert.Equal(pair.Handle, pair.control.Handle));
                    Assert.Equal(reads, registry.Reads);
                    Assert.Equal(scroll, page.AutoScrollPosition);
                    Assert.False(page.HorizontalScroll.Visible);
                    Assert.Equal(selectedPath, path.Text);
                    Assert.Equal(3, path.SelectionStart);
                    Assert.Equal(6, path.SelectionLength);
                    Assert.Equal(2, Find<ComboBox>("ResizeDimension").SelectedIndex);
                    Assert.Equal(1536, Find<NumericUpDown>("NewSize").Value);
                    Assert.Equal(expectedLanguage == "fr" ? "English" : "Français", Find<Button>("Language").Text);
                    Assert.Contains(expectedLanguage == "fr" ? "Réduire" : "Resize", Find<Label>("SharedSizes").Text);
                    Assert.Equal(71, Find<NumericUpDown>("QualityValue.PNG").Value);
                    Assert.Equal(42, Find<NumericUpDown>("MinimumPngQuality").Value);
                    Assert.Equal(77, Find<NumericUpDown>("QualityValue.JPG").Value);
                    Assert.Equal(64, Find<TrackBar>("Quality.WebP").Value);
                    Assert.False(Find<CheckBox>("AutoMode").Checked);
                    Assert.False(Find<CheckBox>("AutoResize.512").Checked);
                    Assert.True(Find<CheckBox>("AutoResize.1024").Checked);
                    Assert.True(Find<CheckBox>("Resize.512").Checked);
                    Assert.False(Find<CheckBox>("Resize.1024").Checked);
                    Assert.False(Find<CheckBox>("JPG.1").Checked);
                }

                Find<Button>("SaveChanges").PerformClick();
                var saved = config.Load();
                Assert.Equal("fr", saved.Language);
                Assert.Equal(selectedPath, saved.Executables.PngquantPath);
                Assert.Equal(DownscaleMode.Height, saved.DownscaleMode);
                Assert.Equal([512, 1024], saved.AvailableSizes);
                Assert.Equal(71, saved.Encoders.PngQuality);
                Assert.Equal(42, saved.Encoders.PngMinQuality);
                Assert.Equal(77, saved.Encoders.JpgQuality);
                Assert.Equal(64, saved.Encoders.WebPQuality);
                Assert.False(saved.AutoMode);
                Assert.False(saved.GetSizeActions(512).AutoResize);
                Assert.True(saved.GetSizeActions(1024).AutoResize);
                Assert.True(saved.GetSizeActions(512).Resize);
                Assert.False(saved.GetSizeActions(1024).Resize);
                Assert.False(saved.Jpg.Downscale);
                Assert.True(saved.Png.Downscale);

                // Adding/removing a preset must leave the other choices intact.
                Find<Button>("AddSize").PerformClick();
                Find<CheckBox>("AutoResize.1536").Checked = false;
                Find<Button>("RemoveSize.512").PerformClick();
                Assert.Empty(form.Controls.Find("AutoResize.512", true));
                Assert.True(Find<CheckBox>("AutoResize.1024").Checked);
                Assert.False(Find<CheckBox>("Resize.1024").Checked);
                Find<Button>("Language").PerformClick();
                Assert.Equal("Auto + resize 1536px", Find<CheckBox>("AutoResize.1536").AccessibleName);
                Assert.False(Find<CheckBox>("AutoResize.1536").Checked);
                Find<Button>("SaveChanges").PerformClick();
                saved = config.Load();
                Assert.Equal([1024, 1536], saved.AvailableSizes);
                Assert.False(saved.SizeActions.ContainsKey(512));
                Assert.True(saved.GetSizeActions(1024).AutoResize);
                Assert.False(saved.GetSizeActions(1024).Resize);
                Assert.False(saved.GetSizeActions(1536).AutoResize);
                Assert.True(saved.GetSizeActions(1536).Resize);
                var entries = RegistryService.BuildMenuEntries(saved);
                Assert.DoesNotContain(entries, entry => entry.Command == "--auto" || entry.Command.EndsWith(" 512"));
                Assert.Contains(entries, entry => entry.Command == "--auto 1024");
                Assert.DoesNotContain(entries, entry => entry.Command == "--downscale 1024" || entry.Command == "--auto 1536");
                Assert.Contains(entries, entry => entry.Command == "--downscale 1536");
            }
            catch (Exception ex) { failure = ex; }
            finally { File.Delete(file); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class FakeRegistry : IRegistryService
    {
        public int Reads { get; private set; }
        public bool IsRegistered() { Reads++; return false; }
        public string? GetRegisteredExePath() { Reads++; return null; }
        public void Register(AppSettings settings, string currentExePath) => throw new InvalidOperationException("Unexpected registration");
        public void Unregister() => throw new InvalidOperationException("Unexpected unregistration");
    }
}
