namespace OfflineDaoc.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // --refresh-165: update the True 1.65 world content in place (the same step as a start in that mode) and exit;
        // for scripts and test runs. Writes data\true165-refresh.log. Never with the server running.
        if (args.Any(a => a.Equals("--refresh-165", StringComparison.OrdinalIgnoreCase)))
        {
            string root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string log = Path.Combine(root, "data", "true165-refresh.log");
            try
            {
                if (System.Diagnostics.Process.GetProcessesByName("CoreServer").Length > 0)
                    throw new InvalidOperationException("the server is running");
                bool changed = TrueMode.Refresh(root, message => File.AppendAllText(log, $"{DateTime.Now:s} {message}\r\n"));
                File.AppendAllText(log, $"{DateTime.Now:s} {(changed ? "updated" : "already current")}\r\n");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                File.AppendAllText(log, $"{DateTime.Now:s} FAILED: {ex.Message}\r\n");
                Environment.Exit(1);
            }
        }
        // --add-bots <normal|1.65> <realm 1-3> <count> <level 1|50>: the ADD BOTS buttons headless, for test rosters
        // (the same rows the buttons write). Writes data\add-bots.log. Never with the server running.
        int addAt = Array.FindIndex(args, a => a.Equals("--add-bots", StringComparison.OrdinalIgnoreCase));
        if (addAt >= 0)
        {
            string root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string log = Path.Combine(root, "data", "add-bots.log");
            try
            {
                if (System.Diagnostics.Process.GetProcessesByName("CoreServer").Length > 0)
                    throw new InvalidOperationException("the server is running");
                if (args.Length < addAt + 5)
                    throw new ArgumentException("usage: --add-bots <normal|1.65> <realm 1-3> <count> <level 1|50>");
                bool is165 = args[addAt + 1] == "1.65";
                if (!is165)
                    throw new ArgumentException("normal mode is archived (OfflineDAoC True 1.65 has True 1.65 Mode only)");
                int realm = int.Parse(args[addAt + 2]), count = int.Parse(args[addAt + 3]), level = int.Parse(args[addAt + 4]);
                TrueMode.Mode mode = TrueMode.Describe(root, is165);
                int left = count;
                while (left > 0)
                {
                    int batch = left >= 100 ? 100 : left >= 10 ? 10 : 1;
                    MainForm.GenerateBotCharacters(mode.DatabasePath, is165, realm, batch, level);
                    left -= batch;
                }
                File.AppendAllText(log, $"{DateTime.Now:s} added {count} level-{level} bots, realm {realm}, {(is165 ? "1.65" : "normal")}\r\n");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                File.AppendAllText(log, $"{DateTime.Now:s} FAILED: {ex.Message}\r\n");
                Environment.Exit(1);
            }
        }
        // --set-xp-rate <1..5000>: the Options tab's XP rate headless, for scripts and test runs (the same
        // ServerProperty row the Options tab writes). Writes data\set-xp-rate.log. Never with the server running.
        int rateAt = Array.FindIndex(args, a => a.Equals("--set-xp-rate", StringComparison.OrdinalIgnoreCase));
        if (rateAt >= 0)
        {
            string root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string log = Path.Combine(root, "data", "set-xp-rate.log");
            try
            {
                if (System.Diagnostics.Process.GetProcessesByName("CoreServer").Length > 0)
                    throw new InvalidOperationException("the server is running");
                if (args.Length < rateAt + 2)
                    throw new ArgumentException("usage: --set-xp-rate <1..5000>");
                if (!XpRateSetting.TryParse(args[rateAt + 1], out double rate))
                    throw new ArgumentException("the rate must be a number between 1 and 5000");
                TrueMode.Mode mode = TrueMode.Describe(root, true);
                XpRateSetting.Persist(mode.DatabasePath, rate);
                File.AppendAllText(log, $"{DateTime.Now:s} XP rate set to x{XpRateSetting.Format(rate)}\r\n");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                File.AppendAllText(log, $"{DateTime.Now:s} FAILED: {ex.Message}\r\n");
                Environment.Exit(1);
            }
        }
        ApplicationConfiguration.Initialize();
        // OfflineDAoC True 1.65 has no normal mode (owner 2026-10-31): no --mode= switch any more.
        Application.Run(new MainForm());
    }
}
