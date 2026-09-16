using EveLocalMon.Core.Settings;

namespace EveLocalMon.App.Services;

/// <summary>
/// Copies the icon/digit templates shipped next to the exe into the user's data directory the
/// first time the app runs, so M4's "teach it this unknown icon" has a writable home and the
/// shipped defaults can still be updated by a new build without clobbering user additions.
/// </summary>
public static class TemplateSeeder
{
    public static string EnsureSeeded()
    {
        string target = SettingsPaths.TemplatesDirectory;
        string source = Path.Combine(AppContext.BaseDirectory, "assets", "templates");

        if (!Directory.Exists(source))
        {
            return Directory.Exists(target) ? target : source;
        }

        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string destination = Path.Combine(target, relative);
            if (File.Exists(destination))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return target;
    }
}
