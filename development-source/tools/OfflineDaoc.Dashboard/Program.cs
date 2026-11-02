using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace OfflineDaoc.Dashboard;

internal static class Program
{
    /// <summary>One launcher per install: the name carries a hash of the runtime folder, so a second install (a
    /// scratch copy used for testing, for example) can run its own launcher at the same time.</summary>
    private static string InstanceNameFor(DashboardOptions options) =>
        @"Local\OfflineDAoC.Launcher." +
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(options.RuntimeDirectory))))[..16];

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

        using var instance = new Mutex(true, InstanceNameFor(options), out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // One launcher per install: a second double-click just brings the dashboard back up.
            string? url = ReadRunningUrl(options.RuntimeDirectory);
            if (url != null)
            {
                Console.WriteLine($"The launcher is already running: {url}");
                if (options.OpenBrowser)
                    OpenBrowser(url);
            }
            else
            {
                Console.WriteLine("The launcher is already running in another window.");
            }

            return 0;
        }

        using var launcher = new LauncherHost(options);
        using var host = new DashboardHost(options, launcher);
        try
        {
            host.Start();
        }
        catch (Exception exception) when (exception is SocketException)
        {
            Console.Error.WriteLine($"Cannot listen on 127.0.0.1:{options.Port}: {exception.Message}");
            Console.Error.WriteLine("Is another launcher already using the port? Close that window, or pass --port <number>.");
            return 1;
        }

        WriteLockFile(options, host.Url);

        Console.WriteLine();
        Console.WriteLine($"  Offline DAoC BotRealm launcher {LauncherHost.DisplayVersion}");
        Console.WriteLine();
        Console.WriteLine($"    Playable folder : {options.RuntimeDirectory}");
        Console.WriteLine($"    World database  : {options.DatabasePath}");
        Console.WriteLine($"    Server          : {options.ServerDirectory}");
        Console.WriteLine($"    Dashboard       : {host.Url}");
        Console.WriteLine();
        Console.WriteLine("    The launcher is the dashboard: start the server, enter the realm and follow the world in the browser.");
        Console.WriteLine("    Closing this window stops the server. Leave it open while you play.");
        Console.WriteLine();

        if (options.OpenBrowser)
            OpenBrowser(host.Url);

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
            Name = "launcher console",
        };
        // A console window can be typed into; a redirected stdin (a script, a background job) reaches end-of-file at
        // once and must not stop the launcher.
        if (!Console.IsInputRedirected)
            reader.Start();

        stop.Wait();
        Console.WriteLine("  Stopping the launcher…");
        host.Dispose();
        launcher.Dispose();
        DeleteLockFile(options);
        Console.WriteLine("  Launcher stopped.");
        return 0;
    }

    private static string LockFilePath(DashboardOptions options) =>
        Path.Combine(options.RuntimeDirectory, "launcher-session.json");

    private static void WriteLockFile(DashboardOptions options, string url)
    {
        try
        {
            File.WriteAllText(LockFilePath(options), JsonSerializer.Serialize(new
            {
                url,
                port = options.Port,
                pid = Environment.ProcessId,
                startedUtc = DateTime.UtcNow,
            }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A read-only install still works: it only costs the "already running" shortcut.
        }
    }

    private static void DeleteLockFile(DashboardOptions options)
    {
        try
        {
            File.Delete(LockFilePath(options));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? ReadRunningUrl(string runtimeDirectory)
    {
        try
        {
            string path = Path.Combine(runtimeDirectory, "launcher-session.json");
            if (!File.Exists(path))
                return null;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("url", out JsonElement url) ? url.GetString() : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.WriteLine($"    Could not open a browser automatically: {exception.Message}");
            Console.WriteLine($"    Open {url} yourself.");
        }
    }
}
