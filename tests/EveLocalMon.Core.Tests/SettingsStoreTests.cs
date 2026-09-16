using EveLocalMon.Core.Imaging;
using EveLocalMon.Core.Reading;
using EveLocalMon.Core.Settings;

namespace EveLocalMon.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "EveLocalMonTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RoundTripsRegionAndCalibration()
    {
        var store = new SettingsStore(SettingsPath);
        var settings = new AppSettings
        {
            Region = new PixelRect(2200, 300, 140, 900),
            Calibration = new Calibration(
                new PixelRect(0, 24, 140, 876), IconX: 3, NameX: 18, NameRight: 130,
                HeaderCountRect: new PixelRect(20, 0, 30, 16)),
            PollIntervalMs = 250,
        };

        store.Save(settings);
        AppSettings loaded = store.Load(out string? warning);

        Assert.Null(warning);
        Assert.Equal(settings.Region, loaded.Region);
        Assert.Equal(settings.Calibration, loaded.Calibration);
        Assert.Equal(250, loaded.PollIntervalMs);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public void MissingFileGivesDefaultsWithoutComplaint()
    {
        AppSettings loaded = new SettingsStore(SettingsPath).Load(out string? warning);

        Assert.Null(warning);
        Assert.Null(loaded.Region);
        Assert.Equal(500, loaded.PollIntervalMs);
    }

    [Fact]
    public void CorruptFileDegradesToDefaultsRatherThanThrowing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");

        AppSettings loaded = new SettingsStore(SettingsPath).Load(out string? warning);

        Assert.NotNull(warning);
        Assert.Null(loaded.Region);
    }

    [Fact]
    public void UnknownFieldsAreIgnoredAndANewerSchemaWarns()
    {
        Directory.CreateDirectory(SettingsPath[..SettingsPath.LastIndexOf(Path.DirectorySeparatorChar)]);
        File.WriteAllText(SettingsPath, """
            { "schemaVersion": 99, "pollIntervalMs": 750, "somethingFromTheFuture": { "a": 1 } }
            """);

        AppSettings loaded = new SettingsStore(SettingsPath).Load(out string? warning);

        Assert.NotNull(warning);
        Assert.Equal(750, loaded.PollIntervalMs);
    }

    [Fact]
    public void SaveIsAtomicAndLeavesNoTempFile()
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(new AppSettings { PollIntervalMs = 100 });
        store.Save(new AppSettings { PollIntervalMs = 200 });

        Assert.Equal(200, store.Load().PollIntervalMs);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }
}
