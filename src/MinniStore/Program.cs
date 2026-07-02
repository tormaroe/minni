using MinniStore.Storage;
using MinniStore.API;

namespace MinniStore;

public class Program
{
    public static int Main(string[] args)
    {
        var options = CommandLineParser.Parse(args);
        
        if (options.ShowHelp)
        {
            CommandLineParser.PrintHelp();
            return 0;
        }

        if (options.ErrorMessage != null)
        {
            Console.Error.WriteLine($"Error: {options.ErrorMessage}");
            CommandLineParser.PrintHelp();
            return 1;
        }

        try
        {
            var builder = WebApplication.CreateBuilder(args);
            
            // Configure Kestrel to use the parsed port
            builder.WebHost.ConfigureKestrel(kestrelOptions =>
            {
                kestrelOptions.ListenAnyIP(options.Port);
            });

            // Register parsed command-line options as a singleton
            builder.Services.AddSingleton(options);

            // Register storage engine
            builder.Services.AddSingleton<IStorageEngine, StorageEngine>();

            // Add standard console logging
            builder.Logging.AddConsole();

            var app = builder.Build();

            // Initialize the StorageEngine before running the web app
            var storageEngine = app.Services.GetRequiredService<IStorageEngine>();
            storageEngine.InitializeAsync().GetAwaiter().GetResult();

            app.MapGet("/", () => "Hello World!");
            app.MapStreamEndpoints();

            app.Run();
            return 0;
        }
        catch (Exception ex) when (ex.GetType().Name != "HostAbortedException")
        {
            Console.Error.WriteLine($"Application startup failed: {ex.Message}");
            return 1;
        }
    }
}
