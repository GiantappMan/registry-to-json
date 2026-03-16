using RegistryToJson.Core;

namespace RegistryToJson;

internal static class Program
{
    private static int Main(string[] args)
    {
        var options = CommandLineOptions.Parse(args);
        if (!options.IsValid(out var validationMessage))
        {
            Console.WriteLine(validationMessage);
            return 1;
        }

        var snapshotService = new RegistrySnapshotService();
        var watchService = new RegistryWatchService(snapshotService, new RegistryDiffService());

        if (options.Watch)
        {
            while (true)
            {
                if (!Export(snapshotService, options))
                {
                    return 1;
                }

                var watchResult = watchService.Refresh(options.RegistryPath!, baselineSnapshot: null);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Snapshot refreshed: {watchResult.CurrentSnapshot.SourcePath}");
                Thread.Sleep(TimeSpan.FromSeconds(options.IntervalSeconds));
            }
        }

        if (!Export(snapshotService, options))
        {
            return 1;
        }

        Console.WriteLine($"Successfully exported the registry to {options.OutputFilePath}");
        return 0;
    }

    private static bool Export(RegistrySnapshotService snapshotService, CommandLineOptions options)
    {
        try
        {
            snapshotService.Export(new ExportRequest
            {
                RegistryPath = options.RegistryPath!,
                OutputFilePath = options.OutputFilePath!,
            });
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred while exporting the registry: {ex.Message}");
            return false;
        }
    }
}

internal sealed class CommandLineOptions
{
    public string? RegistryPath { get; private set; }

    public string? OutputFilePath { get; private set; }

    public bool Watch { get; private set; }

    public int IntervalSeconds { get; private set; } = 1;

    public static CommandLineOptions Parse(string[] args)
    {
        var options = new CommandLineOptions();
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "-r" when index + 1 < args.Length:
                    options.RegistryPath = args[++index];
                    break;
                case "-o" when index + 1 < args.Length:
                    options.OutputFilePath = args[++index];
                    break;
                case "-watch":
                    options.Watch = true;
                    break;
                case "-interval" when index + 1 < args.Length && int.TryParse(args[index + 1], out var seconds) && seconds > 0:
                    options.IntervalSeconds = seconds;
                    index++;
                    break;
            }
        }

        return options;
    }

    public bool IsValid(out string message)
    {
        if (string.IsNullOrWhiteSpace(RegistryPath))
        {
            message = "Please specify the registry path using the -r option.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(OutputFilePath))
        {
            message = "Please specify the output file path using the -o option.";
            return false;
        }

        message = string.Empty;
        return true;
    }
}