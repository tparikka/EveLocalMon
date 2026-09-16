namespace EveLocalMon.Capture.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string[] rest = args[1..];
        return args[0] switch
        {
            "capture" => CaptureCommand.Run(rest),
            "crop" => CropCommand.Run(rest),
            "read" => ReadCommand.Run(rest),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            Usage:
              evelocalmon-capture capture --region x,y,w,h --out <dir> [--interval <sec>=2] [--count <n>]
              evelocalmon-capture crop <src.png> x,y,w,h <out.png>
              evelocalmon-capture read <src.png> <fixture.json> <templatesDir>
            """);
    }
}
