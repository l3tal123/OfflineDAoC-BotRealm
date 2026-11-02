using System.Data.SQLite;
using System.Diagnostics;
using System.Net.NetworkInformation;
using OfflineDaoc.Launcher;

namespace OfflineDaoc.Dashboard;

/// <summary>
/// The launcher half of the program: it owns the game server process and the game client process, runs the True 1.65
/// pre-flight (mode check + content refresh) before a start, keeps the redirected server console, and reports all of
/// it as JSON for the web UI. It is the same behaviour the WinForms launcher has, without the WinForms.
/// </summary>
internal sealed class LauncherHost : IDisposable
{
    internal const string DisplayVersion = "0.3";
    private const int GamePort = 10300;

    private readonly string _root;
    private readonly string _installName;
    private readonly string _serverDirectory;
    private readonly string _serverExecutable;
    private readonly string _logsDirectory;
    private readonly string _database;
    private readonly LogBuffer _log = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _sync = new();

    private Process? _server;
    private RollingServerLog? _logWriter;
    private string _state = "Stopped";
    private DateTime? _startedUtc;
    private bool _disposed;
    private string _lastNotice = string.Empty;
    private bool _modeOk;
    private string _modeProblem = string.Empty;
    private DateTime _modeCheckedUtc = DateTime.MinValue;
    private NewClient? _client;
    private DateTime _clientCheckedUtc = DateTime.MinValue;
    private bool _refreshRunning;

    internal LauncherHost(DashboardOptions options)
    {
        _root = options.RuntimeDirectory;
        // A playable folder is named by the folder above runtime; the runtime folder itself is called "runtime".
        string folder = Path.GetFileName(_root.TrimEnd(Path.DirectorySeparatorChar));
        _installName = folder.Equals("runtime", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(Path.GetDirectoryName(_root.TrimEnd(Path.DirectorySeparatorChar))) ?? folder
            : folder;
        _serverDirectory = options.ServerDirectory;
        _serverExecutable = Path.Combine(_serverDirectory, "CoreServer.exe");
        _logsDirectory = Path.Combine(_root, "logs");
        _database = options.DatabasePath;
        _log.Add($"[launcher] Offline DAoC launcher {DisplayVersion} — {_root}");
    }

    internal IReadOnlyList<string> LogSince(long after, out long next) => _log.Since(after, out next);

    // ------------------------------------------------------------------ status

    internal object Status()
    {
        Process? server;
        string state;
        lock (_sync)
        {
            server = _server;
            state = _state;
        }

        bool listening = Listening();
        bool processAlive = server is { HasExited: false };
        if (state == "Starting" && listening)
            state = "Running";
        if (state is "Running" && !listening && !processAlive)
            state = "Stopped";
        if (state == "Starting" && server is { HasExited: true })
            state = "Stopped";

        double memoryMb = 0;
        double uptime = 0;
        int? pid = null;
        if (processAlive)
        {
            try
            {
                server!.Refresh();
                pid = server.Id;
                memoryMb = server.WorkingSet64 / 1024d / 1024d;
                if (_startedUtc is { } started)
                    uptime = Math.Max(0, (DateTime.UtcNow - started).TotalSeconds);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                pid = null;
            }
        }

        return new
        {
            state,
            ready = listening,
            listening,
            pid,
            memoryMb = Math.Round(memoryMb, 1),
            uptimeSeconds = Math.Round(uptime, 0),
            clientAvailable = Client() != null,
            serverPackagePresent = File.Exists(_serverExecutable),
            refreshRunning = _refreshRunning,
            version = DisplayVersion,
            install = _installName,
            modeOk = ModeOk(out string modeProblem),
            modeProblem,
            notice = _lastNotice,
        };
    }

    // ------------------------------------------------------------------ actions

    /// <summary>Validates, then starts the server in the background: the web UI polls the status and the log.</summary>
    internal ActionOutcome StartServer()
    {
        lock (_sync)
        {
            if (_server is { HasExited: false })
                return new ActionOutcome(false, "The server is already starting.");
            if (_state is "Starting" or "Stopping")
                return new ActionOutcome(false, $"The server is {_state.ToLowerInvariant()}.");
        }

        if (Listening())
            return new ActionOutcome(false, "Port 10300 is already in use: a server is running. Stop it before starting another.");
        using (Process? exact = FindExactServerProcess())
        {
            if (exact is not null)
                return new ActionOutcome(false, "A CoreServer process is already running from this folder.");
        }

        if (!File.Exists(_serverExecutable))
            return new ActionOutcome(false, "The compiled server package is missing (server\\CoreServer.exe).");

        if (!ModeOk(out string modeProblem))
            return new ActionOutcome(false, modeProblem);

        SetState("Starting");
        _refreshRunning = true;
        _ = Task.Run(RunStartAsync);
        return new ActionOutcome(true, "Starting the server. The log shows the startup and the content refresh.");
    }

    private async Task RunStartAsync()
    {
        Process? process = null;
        try
        {
            TrueMode.Mode mode = TrueMode.Read(_root);
            if (mode.Is165 && File.Exists(Path.Combine(_root, "true165", "true165-rules.json")))
            {
                try
                {
                    if (TrueMode.Refresh(_root, message => _log.Add("[launcher] " + message)))
                        _log.Add("[launcher] The True 1.65 world content was updated from the data files.");
                }
                catch (Exception exception)
                {
                    _log.Add("[launcher] ERROR: the True 1.65 content update failed, so the server was not started: " + exception.Message);
                    _refreshRunning = false;
                    SetState("Stopped");
                    _lastNotice = "The content update failed. The database is unchanged; see the log.";
                    return;
                }
            }

            _refreshRunning = false;
            Directory.CreateDirectory(_logsDirectory);
            _logWriter?.Dispose();
            _logWriter = new RollingServerLog(Path.Combine(_logsDirectory, "server-console.log"));
            _log.Add("[launcher] Starting " + _serverExecutable + " --start -config=./" + TrueMode.Config165.Replace('\\', '/'));

            var startInfo = new ProcessStartInfo(_serverExecutable)
            {
                Arguments = $"--start -config=./{TrueMode.Config165.Replace('\\', '/')}",
                WorkingDirectory = _serverDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // No byte-order mark before "exit" (the server took that mark + "exit" for a command and kept running).
                StandardInputEncoding = new System.Text.UTF8Encoding(false),
            };
            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            RollingServerLog writer = _logWriter;
            process.OutputDataReceived += (_, args) =>
            {
                try
                {
                    if (args.Data is null)
                        return;
                    writer.WriteLine(args.Data);
                    _log.Add(args.Data);
                }
                catch (Exception)
                {
                    // The output reader thread must never throw: that would take the launcher and the server down.
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                try
                {
                    if (args.Data is null)
                        return;
                    writer.WriteLine("ERROR: " + args.Data);
                    _log.Add("ERROR: " + args.Data);
                }
                catch (Exception)
                {
                }
            };
            process.Exited += (_, _) =>
            {
                _log.Add("[launcher] The server process exited.");
                lock (_sync)
                {
                    if (ReferenceEquals(_server, process))
                    {
                        _state = "Stopped";
                        _startedUtc = null;
                    }
                }

                TryWriteRuntimeState("Stopped", null);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (_sync)
            {
                _server = process;
                _startedUtc = DateTime.UtcNow;
            }

            TryWriteRuntimeState("Starting", process.Id);
            _log.Add("[launcher] Server process started (pid " + process.Id + "). Waiting for port " + GamePort + "…");
            while (!_disposed && Listening() == false && process is { HasExited: false })
                await Task.Delay(250);
            if (Listening())
            {
                TryWriteRuntimeState("Running", process.Id);
                _log.Add($"[launcher] The server is ready on port {GamePort}. ENTER REALM is available.");
            }
            else if (process.HasExited)
            {
                _log.Add("[launcher] The server exited before it became ready. See the lines above.");
            }
        }
        catch (Exception exception)
        {
            _log.Add("[launcher] ERROR: the server could not be started: " + exception.Message);
            _lastNotice = "The server could not be started: " + exception.Message;
            _refreshRunning = false;
            if (process is { HasExited: false })
            {
                try { process.Kill(true); } catch (Exception) { }
            }

            SetState("Stopped");
            TryWriteRuntimeState("Stopped", null);
        }
        finally
        {
            if (_refreshRunning)
                _refreshRunning = false;
        }
    }

    internal async Task<ActionOutcome> StopServerAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            Process? server;
            lock (_sync)
                server = _server;

            if (server is null || server.HasExited)
            {
                using Process? exact = FindExactServerProcess();
                if (exact is not null)
                    return new ActionOutcome(false,
                        "The server was started outside this launcher, so it cannot be stopped gracefully from here. " +
                        "Close that process first.");
                if (Listening())
                    return new ActionOutcome(false,
                        "Something is listening on port 10300 but it was not started by this launcher. Close it first.");
                SetState("Stopped");
                TryWriteRuntimeState("Stopped", null);
                return new ActionOutcome(true, "The server is already stopped.");
            }

            SetState("Stopping");
            TryWriteRuntimeState("Stopping", SafeId(server));
            _log.Add("[launcher] Asking the server to shut down…");
            try
            {
                server.StandardInput.WriteLine("exit");
                server.StandardInput.Flush();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
            {
                _log.Add("[launcher] The server's input was already closed: " + exception.Message);
            }

            bool exited = await WaitForExitAsync(server, 15_000).ConfigureAwait(false);
            if (!exited)
            {
                lock (_sync)
                    _state = "Running";
                _log.Add("[launcher] The server is still shutting down (world save). It has not been force-closed.");
                return new ActionOutcome(false, "The server is still shutting down; it has not been force-closed.");
            }

            _log.Add("[launcher] The server stopped.");
            SetState("Stopped");
            TryWriteRuntimeState("Stopped", null);
            return new ActionOutcome(true, "The server stopped.");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    internal ActionOutcome StartClient()
    {
        if (!Listening())
            return new ActionOutcome(false, "Start the server first: ENTER REALM needs it.");
        NewClient? client = Client();
        if (client == null)
            return new ActionOutcome(false,
                "The new client is not installed: expected new-client\\ next to runtime, or a runtime\\new-client.txt pointer.");
        try
        {
            (string account, string password) = ReadCredentials();
            client.Start(account, password, true);
            _log.Add($"[launcher] Started the game client for {account}.");
            return new ActionOutcome(true, "The game client is starting.");
        }
        catch (Exception exception)
        {
            _log.Add("[launcher] ERROR: the game client could not be started: " + exception.Message);
            return new ActionOutcome(false, "The game client could not be started: " + exception.Message);
        }
    }

    // ------------------------------------------------------------------ shutdown

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Process? server;
        lock (_sync)
            server = _server;
        try
        {
            if (server is { HasExited: false })
            {
                _log.Add("[launcher] Closing: stopping the server…");
                try
                {
                    server.StandardInput.WriteLine("exit");
                    server.StandardInput.Flush();
                }
                catch (Exception)
                {
                }

                if (!server.WaitForExit(10_000))
                {
                    _log.Add("[launcher] The server did not stop in time; closing it.");
                    try { server.Kill(true); } catch (Exception) { }
                }

                TryWriteRuntimeState("Stopped", null);
            }
        }
        catch (Exception)
        {
            // Closing the launcher must not throw.
        }
        finally
        {
            _logWriter?.Dispose();
            _logWriter = null;
            _server?.Dispose();
            _server = null;
            _lifecycle.Dispose();
        }
    }

    // ------------------------------------------------------------------ helpers

    internal static bool Listening() =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == GamePort);

    private void SetState(string state)
    {
        lock (_sync)
            _state = state;
    }

    private static int? SafeId(Process process)
    {
        try { return process.Id; }
        catch (Exception) { return null; }
    }

    private static async Task<bool> WaitForExitAsync(Process process, int timeoutMilliseconds)
    {
        if (process.HasExited)
            return true;
        using var timeout = new CancellationTokenSource(timeoutMilliseconds);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }

    private bool ModeOk(out string problem)
    {
        if (DateTime.UtcNow - _modeCheckedUtc < TimeSpan.FromSeconds(10))
        {
            problem = _modeProblem;
            return _modeOk;
        }

        try
        {
            TrueMode.Mode mode = TrueMode.Read(_root);
            _modeOk = TrueMode.DatabaseAgrees(mode, out string databaseProblem);
            _modeProblem = _modeOk ? string.Empty : databaseProblem;
        }
        catch (Exception exception)
        {
            _modeOk = false;
            _modeProblem = "The world mode could not be read: " + exception.Message;
        }

        _modeCheckedUtc = DateTime.UtcNow;
        problem = _modeProblem;
        return _modeOk;
    }

    private NewClient? Client()
    {
        if (DateTime.UtcNow - _clientCheckedUtc < TimeSpan.FromSeconds(5))
            return _client;
        _client = NewClient.Find(_root);
        _clientCheckedUtc = DateTime.UtcNow;
        return _client;
    }

    /// <summary>The local login, created on first use exactly as the WinForms launcher made it.</summary>
    private (string Account, string Password) ReadCredentials()
    {
        string path = Path.Combine(_root, "account.txt");
        if (!File.Exists(path))
        {
            const string letters = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            string password = new(Enumerable.Range(0, 16)
                .Select(_ => letters[System.Security.Cryptography.RandomNumberGenerator.GetInt32(letters.Length)]).ToArray());
            string account = "player" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000);
            File.WriteAllText(path, $"Account: {account}{Environment.NewLine}Password: {password}{Environment.NewLine}");
            _log.Add($"[launcher] Created the local login {account} in account.txt.");
        }

        var values = File.ReadLines(path)
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        return (values["Account"], values["Password"]);
    }

    private Process? FindExactServerProcess()
    {
        foreach (Process process in Process.GetProcessesByName("CoreServer"))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, _serverExecutable, StringComparison.OrdinalIgnoreCase))
                    return process;
            }
            catch (Exception)
            {
                process.Dispose();
            }
        }

        return null;
    }

    /// <summary>Best-effort metadata for the dashboard's runtime state row; a failure is never fatal.</summary>
    private void TryWriteRuntimeState(string state, int? pid)
    {
        try
        {
            using var connection = new SQLiteConnection($"Data Source={_database};Version=3;Pooling=False;Default Timeout=1");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE offline_runtime_status SET ServerState=@state, ServerPid=@pid, LastHeartbeatUtc=@now WHERE Id=1";
            command.Parameters.AddWithValue("@state", state);
            command.Parameters.AddWithValue("@pid", pid is null ? DBNull.Value : pid.Value);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // Dashboard metadata only.
        }
    }

    /// <summary>A bounded in-memory copy of the server console, served to the web UI.</summary>
    private sealed class LogBuffer
    {
        private const int Capacity = 4000;
        private readonly Queue<(long Sequence, string Text)> _lines = new();
        private readonly object _gate = new();
        private long _next = 1;

        internal void Add(string text)
        {
            lock (_gate)
            {
                _lines.Enqueue((_next++, text));
                while (_lines.Count > Capacity)
                    _lines.Dequeue();
            }
        }

        internal IReadOnlyList<string> Since(long after, out long next)
        {
            lock (_gate)
            {
                next = _next - 1;
                List<string> result = _lines.Where(line => line.Sequence > after).Select(line => line.Text).ToList();
                if (after <= 0 && result.Count > 600)
                    result = result[^600..];
                return result;
            }
        }
    }
}

internal sealed record ActionOutcome(bool Ok, string Message);

