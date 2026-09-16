using System.Text.Json;

namespace EveLocalMon.Core.Tests.Support;

public static class FixtureLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string FixturesDir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string TemplatesDir => Path.Combine(FixturesDir, "templates");

    public static string Path_(params string[] parts) => System.IO.Path.Combine([FixturesDir, .. parts]);

    public static IEnumerable<string> AllFixtureFiles() => Directory.EnumerateFiles(FixturesDir, "*.json");

    public static FixtureDto Load(string fixturePath)
    {
        FixtureDto? fixture = JsonSerializer.Deserialize<FixtureDto>(File.ReadAllText(fixturePath), JsonOptions);
        return fixture ?? throw new InvalidDataException($"Could not parse fixture '{fixturePath}'.");
    }
}
