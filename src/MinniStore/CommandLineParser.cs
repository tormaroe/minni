namespace MinniStore;

public static class CommandLineParser
{
    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--port")
            {
                if (i + 1 >= args.Length)
                {
                    options.ErrorMessage = "Missing value for --port";
                    return options;
                }
                var portStr = args[++i];
                if (!int.TryParse(portStr, out var port) || port < 1 || port > 65535)
                {
                    options.ErrorMessage = $"Invalid port number: {portStr}. Must be between 1 and 65535.";
                    return options;
                }
                options.Port = port;
            }
            else if (arg == "--db")
            {
                if (i + 1 >= args.Length)
                {
                    options.ErrorMessage = "Missing value for --db";
                    return options;
                }
                var dbPath = args[++i];
                if (string.IsNullOrWhiteSpace(dbPath))
                {
                    options.ErrorMessage = "Database file path cannot be empty.";
                    return options;
                }
                options.DbPath = dbPath;
            }
            else if (arg == "--help" || arg == "-h")
            {
                options.ShowHelp = true;
            }
            else if (arg.StartsWith("--environment=") || 
                     arg.StartsWith("--contentRoot=") || 
                     arg.StartsWith("--applicationName="))
            {
                // Ignore WebApplicationFactory injected options
                continue;
            }
            else
            {
                options.ErrorMessage = $"Unknown argument: {arg}";
                return options;
            }
        }
        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: minni [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  --port <port>    The port to listen on (default: 25000)");
        Console.WriteLine("  --db <path>      The path to the database file (default: minni.db)");
        Console.WriteLine("  --help, -h       Show help details");
    }
}
