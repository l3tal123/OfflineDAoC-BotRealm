using System.Diagnostics;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The optional new (Godot) client (owner 2026-10-30: one launcher, two ENTER buttons; the new client's is disabled when
/// it isn't installed). It runs against this launcher's server, as the 0.36c launcher in the new client's own folder does.
/// Where it is looked for, first found wins:
///   runtime\new-client.txt   one line: the new client's folder (absolute, or relative to runtime)
///   ..\new-client            beside runtime (the release layout: the optional download unpacked into the install)
///   ..\..\OFFLINE DAOC MMOENGINE GODOT   the development layout (both folders inside DAOC HIATUS)
/// A folder counts when it has godot\project.godot and the Godot .NET runner in tools\godot_net.
/// </summary>
internal sealed record NewClient(string Folder, string Runner)
{
    public static NewClient? Find(string runtimeRoot)
    {
        var candidates = new List<string>();
        string pointer = Path.Combine(runtimeRoot, "new-client.txt");
        if (File.Exists(pointer))
        {
            string line = File.ReadLines(pointer).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) ?? string.Empty;
            if (line.Length > 0)
                candidates.Add(Path.GetFullPath(Path.IsPathRooted(line) ? line : Path.Combine(runtimeRoot, line)));
        }
        candidates.Add(Path.GetFullPath(Path.Combine(runtimeRoot, "..", "new-client")));
        candidates.Add(Path.GetFullPath(Path.Combine(runtimeRoot, "..", "..", "OFFLINE DAOC MMOENGINE GODOT")));
        foreach (string folder in candidates)
        {
            string tools = Path.Combine(folder, "tools", "godot_net");
            if (!File.Exists(Path.Combine(folder, "godot", "project.godot")) || !Directory.Exists(tools))
                continue;
            string? runner = Directory.EnumerateFiles(tools, "Godot_*_mono_win64.exe").OrderByDescending(f => f).FirstOrDefault();
            if (runner != null)
                return new NewClient(folder, runner);
        }
        return null;
    }

    /// <summary>Starts the new client logged in to the local server with the launcher's account (as connect.exe does for
    /// the original client), in the server's world mode.</summary>
    public Process? Start(string account, string password, bool is165)
    {
        var start = new ProcessStartInfo(Runner)
        {
            WorkingDirectory = Folder,
            UseShellExecute = false,
            ArgumentList = { "--path", Path.Combine(Folder, "godot"), "--", "--server=127.0.0.1", $"--account={account}",
                $"--password={password}", $"--world-mode={(is165 ? "1.65" : "normal")}" },
        };
        // everything inside its folder (any PC): the game's saves and the .NET it runs on
        start.Environment["APPDATA"] = Path.Combine(Folder, "save");
        string dotnet = Path.Combine(Folder, "tools", "dotnet");
        if (Directory.Exists(dotnet))
        {
            start.Environment["DOTNET_ROOT"] = dotnet;
            start.Environment["DOTNET_ROOT_X64"] = dotnet;
            start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            start.Environment["PATH"] = dotnet + ";" + Environment.GetEnvironmentVariable("PATH");
        }
        return Process.Start(start);
    }
}
