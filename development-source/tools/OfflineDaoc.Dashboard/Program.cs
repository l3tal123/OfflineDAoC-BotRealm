namespace OfflineDaoc.Dashboard;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        DashboardOptions options;
        try
        {
            options = DashboardOptions.Parse(arguments);
        }
        catch (DashboardUsageException exception)
        {
            if (exception.Message.Length > 0)
                Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(DashboardOptions.Usage);
            return exception.Message.Length > 0 ? 2 : 0;
        }

        using var host = new DashboardHost(options);
        try
        {
            host.Start();
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException)
        {
            Console.Error.WriteLine($"Cannot listen on 127.0.0.1:{options.Port}: {exception.Message}");
            Console.Error.WriteLine("Is the dashboard already running? Close the other window, or pass --port <number>.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("  Offline DAoC BotRealm — web dashboard (preview)");
        Console.WriteLine();
        Console.WriteLine($"    Playable folder : {options.RuntimeDirectory}");
        Console.WriteLine($"    World database  : {options.DatabasePath}");
        Console.WriteLine($"    Server folder   : {options.ServerDirectory}");
        Console.WriteLine($"    Listening on    : {host.Url}");
        Console.WriteLine();
        Console.WriteLine("    Read-only. Close this window, press Ctrl+C, or type 'exit' to stop.");
        Console.WriteLine("    If the game server is not running, the saved roster is shown and the map has no live bots.");
        Console.WriteLine();

        if (options.OpenBrowser)
            TryOpenBrowser(host.Url);

        var stop = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, eventArguments) =>
        {
            eventArguments.Cancel = true;
            stop.Set();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Set();

        var reader = new Thread(() =>
        {
            try
            {
                while (Console.ReadLine() is { } line)
                {
                    string command = line.Trim();
                    if (command.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                        command.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                        command.Equals("q", StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // The console window went away: the process is being closed anyway.
            }

            stop.Set();
        })
        {
            IsBackground = true,
            Name = "dashboard console",
        };
        // A console window can be typed into; a redirected stdout/stderr (a script, a background job) cannot, and
        // its stdin reaches end-of-file immediately - that must not stop the dashboard.
        if (!Console.IsInputRedirected)
            reader.Start();

        stop.Wait();
        Console.WriteLine("  Dashboard stopped.");
        return 0;
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.WriteLine($"    Could not open a browser automatically: {exception.Message}");
            Console.WriteLine($"    Open {url} yourself.");
        }
    }
}
