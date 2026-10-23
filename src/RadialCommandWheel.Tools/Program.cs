namespace RadialCommandWheel.Tools;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "generate-icon" => GenerateIconTool.Run(args[1..]),
                "hold-hotkey" => HoldHotkeyTool.Run(args[1..]),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("RadialCommandWheel.Tools");
        Console.WriteLine("  generate-icon [--output <path>]");
        Console.WriteLine("  hold-hotkey [--marker <path>] [--seconds <1-3600>]");
    }
}

