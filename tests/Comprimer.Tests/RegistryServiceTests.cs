using Comprimer.Models;
using Comprimer.Services;
using Xunit;

namespace Comprimer.Tests;

public class RegistryServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Registration_PreservesChosenComprimerPathAcrossRunningCopies(bool hasSavedPath)
    {
        var selected = Path.Combine(Path.GetTempPath(), $"selected-comprimer-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(selected, "path selection fixture");
            var settings = new AppSettings { ComprimerPath = hasSavedPath ? selected : null };
            var result = RegistryService.GetRegistrationPath(settings, "C:\\new-copy\\Comprimer.exe", selected);
            Assert.Equal(selected, result);
        }
        finally { File.Delete(selected); }
    }

    [Fact]
    public void Registration_UsesRunningCopyOnlyWhenNoPathHasBeenChosen()
    {
        var current = Path.Combine(Path.GetTempPath(), $"current-comprimer-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(current, "fixture");
            Assert.Equal(current, RegistryService.GetRegistrationPath(new AppSettings(), current, null));
        }
        finally { File.Delete(current); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\missing-comprimer-358e5c\\Comprimer.exe")]
    public void Registration_RejectsInvalidSavedPathInsteadOfSwitchingCopies(string selected)
    {
        var current = Path.Combine(Path.GetTempPath(), $"current-comprimer-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(current, "fixture");
            var settings = new AppSettings { ComprimerPath = selected };
            Assert.Throws<FileNotFoundException>(() => RegistryService.GetRegistrationPath(settings, current, current));
        }
        finally { File.Delete(current); }
    }

    [Fact]
    public void AutoEntries_CoverAllSourcesAndEverySize()
    {
        var settings = new AppSettings { AvailableSizes = [512, 1024, 512] };
        var entries = RegistryService.BuildMenuEntries(settings).Where(entry => entry.Id.StartsWith("Auto")).ToArray();
        Assert.Equal(3, entries.Length);
        Assert.Equal(["--auto", "--auto 512", "--auto 1024"], entries.Select(entry => entry.Command));
        Assert.All(entries, entry => Assert.Equal([".jpg", ".jpeg", ".png", ".webp"], entry.Extensions));
    }

    [Fact]
    public void Auto_CanBeDisabledIndependently()
    {
        var entries = RegistryService.BuildMenuEntries(new AppSettings { AutoMode = false });
        Assert.DoesNotContain(entries, entry => entry.Command == "--auto");
        Assert.Contains(entries, entry => entry.Command == "--auto 512");
        Assert.Contains(entries, entry => entry.Command == "--to-webp");
        Assert.Contains(entries, entry => entry.Command == "--downscale 512");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void AutoAndSizeActions_AreIndependent(bool original, bool autoResize, bool resize)
    {
        var settings = new AppSettings
        {
            AutoMode = original,
            SizeActions = new()
            {
                [512] = new SizeActionSettings { AutoResize = autoResize, Resize = resize },
                [1024] = new SizeActionSettings(),
            },
        };
        var entries = RegistryService.BuildMenuEntries(settings);
        Assert.Equal(original, entries.Any(entry => entry.Command == "--auto"));
        Assert.Equal(autoResize, entries.Any(entry => entry.Command == "--auto 512"));
        Assert.Equal(resize ? 3 : 0, entries.Count(entry => entry.Command == "--downscale 512"));
        Assert.Single(entries, entry => entry.Command == "--auto 1024");
        Assert.Equal(3, entries.Count(entry => entry.Command == "--downscale 1024"));
    }

    [Theory]
    [InlineData("en", "Auto + resize", "Resize only")]
    [InlineData("fr", "Auto + réduire", "Réduire seulement")]
    public void ResizeAndAuto_ShareCustomSizesWithDistinctLabels(string language, string autoLabel, string resizeLabel)
    {
        var settings = new AppSettings { Language = language, AvailableSizes = [640, 1536] };
        var entries = RegistryService.BuildMenuEntries(settings);
        foreach (var size in settings.AvailableSizes)
        {
            var auto = Assert.Single(entries, entry => entry.Command == $"--auto {size}");
            Assert.Equal($"{autoLabel} {size}px", auto.Label);
            var resize = entries.Where(entry => entry.Command == $"--downscale {size}").ToArray();
            Assert.Equal(3, resize.Length);
            Assert.All(resize, entry => Assert.Equal($"{resizeLabel} {size}px", entry.Label));
            Assert.Equal(auto.Extensions, resize.SelectMany(entry => entry.Extensions));
        }
        Assert.DoesNotContain(entries, entry => entry.Command.EndsWith(" 512") || entry.Command.EndsWith(" 1024"));
    }

    [Fact]
    public void Auto_IsAvailableWithoutIndividualActionsAndIsLocalized()
    {
        var settings = new AppSettings { Jpg = new(), Png = new(), WebP = new(), Language = "fr" };
        var entries = RegistryService.BuildMenuEntries(settings);
        Assert.Equal(3, entries.Count);
        Assert.All(entries, entry => Assert.StartsWith("--auto", entry.Command));
        Assert.Contains("taille d'origine", entries[0].Label);
    }
}
