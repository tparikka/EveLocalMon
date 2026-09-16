using Avalonia;

namespace EveLocalMon.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<EveLocalMonApp>()
            .UsePlatformDetect()
            .LogToTrace();
}
