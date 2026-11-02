namespace OfflineDaoc.Dashboard;

/// <summary>
/// Where the dashboard finds the install it should describe. The web dashboard is a separate tool: it is given a
/// playable folder (the one with runtime/data and runtime/server in it) and never changes anything there.
/// </summary>
internal sealed class DashboardOptions
{
    internal const int DefaultPort = 10310;

    private DashboardOptions()
    {
    }

    /// <summary>Loopback port the dashboard listens on. Bound to 127.0.0.1 only.</summary>
    internal int Port { get; private init; } = DefaultPort;

    /// <summary>The playable folder (the one that holds <c>data/</c> and <c>server/</c>).</summary>
    internal string RuntimeDirectory { get; private init; } = string.Empty;

    internal string DatabasePath => Path.Combine(RuntimeDirectory, "data", "opendaoc-165.sqlite3.db");

    internal string ServerDirectory => Path.Combine(RuntimeDirectory, "server");

    internal string WwwRoot { get; private init; } = string.Empty;

    internal bool OpenBrowser { get; private init; } = true;

    /// <summary>
    /// The new client's painted zone maps (godot/content/maps), when the playable folder has them. They are the same
    /// images the launcher draws under its world map; without them the map shows the plain zone outlines.
    /// </summary>
    internal string? MapsDirectory { get; private init; }

    internal string Url => $"http://127.0.0.1:{Port}/";

    internal static string Usage =>
        """
        Offline DAoC BotRealm dashboard (preview)

          OfflineDaoc.Dashboard.exe [options]

          --port <number>     Loopback port to listen on (default 10310). The UI is served at http://127.0.0.1:<port>/
          --root <folder>     A playable folder's runtime directory (the one holding data/ and server/).
                              Detected automatically when the exe sits in <playable>\runtime\dashboard.
          --maps <folder>     The new client's godot/content/maps folder, for the painted zone maps.
                              Detected automatically next to the playable folder.
          --no-open           Do not open the default browser on start.
          --help              Show this text.

        The dashboard is read-only: it opens the world database read-only and asks the running server for its
        live bot snapshot through the same state-165 file broker the launcher uses.
        """;

    internal static DashboardOptions Parse(string[] arguments)
    {
        string? runtime = null;
        string? maps = null;
        int port = DefaultPort;
        bool open = true;

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            switch (argument.ToLowerInvariant())
            {
                case "--port":
                case "-p":
                    if (++index >= arguments.Length || !int.TryParse(arguments[index], out int parsed) || parsed < 1 || parsed > 65535)
                        throw new DashboardUsageException("--port needs a number between 1 and 65535.");
                    port = parsed;
                    break;
                case "--root":
                case "-r":
                    if (++index >= arguments.Length || arguments[index].Length == 0)
                        throw new DashboardUsageException("--root needs a folder.");
                    runtime = Path.GetFullPath(arguments[index]);
                    break;
                case "--maps":
                    if (++index >= arguments.Length || arguments[index].Length == 0)
                        throw new DashboardUsageException("--maps needs a folder.");
                    maps = Path.GetFullPath(arguments[index]);
                    break;
                case "--no-open":
                    open = false;
                    break;
                case "--open":
                    open = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    throw new DashboardUsageException(string.Empty);
                default:
                    throw new DashboardUsageException($"Unknown option '{argument}'.");
            }
        }

        string wwwRoot = ResolveWwwRoot();
        if (!Directory.Exists(wwwRoot))
            throw new DashboardUsageException($"The web files are missing: {wwwRoot} was not found next to the exe.");

        string resolvedRuntime = runtime ?? DetectRuntimeDirectory()
            ?? throw new DashboardUsageException(
                "Could not find a playable folder. Run the exe from <playable>\\runtime\\dashboard, or pass --root <playable>\\runtime.");

        if (!File.Exists(Path.Combine(resolvedRuntime, "data", "opendaoc-165.sqlite3.db")))
            throw new DashboardUsageException(
                $"No world database in {Path.Combine(resolvedRuntime, "data")}. Pass --root <playable>\\runtime.");

        string? mapsDirectory = maps ?? DetectMapsDirectory(resolvedRuntime);
        if (mapsDirectory != null && !Directory.Exists(mapsDirectory))
            mapsDirectory = null;

        return new DashboardOptions
        {
            Port = port,
            RuntimeDirectory = resolvedRuntime,
            WwwRoot = wwwRoot,
            OpenBrowser = open,
            MapsDirectory = mapsDirectory,
        };
    }

    /// <summary>
    /// The UI files. In a playable folder they live in <c>runtime\dashboard\wwwroot</c> next to the launcher exe, so
    /// everything the launcher ships in that folder can be updated or removed together; a development build keeps
    /// <c>wwwroot</c> next to the exe.
    /// </summary>
    private static string ResolveWwwRoot()
    {
        foreach (string candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "dashboard", "wwwroot"),
                     Path.Combine(AppContext.BaseDirectory, "wwwroot"),
                 })
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return Path.Combine(AppContext.BaseDirectory, "dashboard", "wwwroot");
    }

    /// <summary>The new client's painted zone maps, if the playable folder carries them.</summary>
    private static string? DetectMapsDirectory(string runtimeDirectory)
    {
        string playable = Path.GetFullPath(Path.Combine(runtimeDirectory, ".."));
        foreach (string candidate in new[]
                 {
                     Path.Combine(playable, "new-client", "godot", "content", "maps"),
                     Path.Combine(runtimeDirectory, "new-client", "godot", "content", "maps"),
                 })
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Finds the runtime directory without being told: the deployed layout is <c>runtime\dashboard\exe</c>, the
    /// development layout is a bin folder inside the repository that has a <c>runtime</c> folder somewhere above it.
    /// </summary>
    private static string? DetectRuntimeDirectory()
    {
        var candidates = new List<string>();
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int depth = 0; directory != null && depth < 10; depth++, directory = directory.Parent)
        {
            candidates.Add(directory.FullName);
            candidates.Add(Path.Combine(directory.FullName, "runtime"));
        }

        foreach (string candidate in candidates)
        {
            string normalized = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(normalized, "data", "opendaoc-165.sqlite3.db")) &&
                Directory.Exists(Path.Combine(normalized, "server")))
                return normalized;
        }

        return null;
    }
}

/// <summary>A startup problem that should be printed with the usage text rather than as a crash.</summary>
internal sealed class DashboardUsageException : Exception
{
    internal DashboardUsageException(string message)
        : base(message)
    {
    }
}
