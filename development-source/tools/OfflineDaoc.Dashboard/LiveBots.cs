using System.Text.Json;

namespace OfflineDaoc.Dashboard;

/// <summary>
/// The live bot snapshot, fetched the way the launcher fetches it: write a request id to
/// <c>server/bot-world.request</c>, then read <c>server/state-165/bot-world.json</c> until it carries that id.
/// Nothing new listens on the network for this - it is the server's existing state-165 file broker.
/// </summary>
internal sealed class LiveBots
{
    private const int SnapshotMaxAgeMilliseconds = 20_000;
    private const int SnapshotWaitMilliseconds = 3_000;
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ReuseWindow = TimeSpan.FromMilliseconds(1_500);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _serverDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;
    private DateTime _lastReadUtc = DateTime.MinValue;
    private Snapshot? _cached;

    internal LiveBots(string serverDirectory)
    {
        _serverDirectory = serverDirectory;
    }

    internal string ResponsePath => Path.Combine(_serverDirectory, "state-165", "bot-world.json");

    private string RequestPath => Path.Combine(_serverDirectory, "bot-world.request");

    /// <summary>
    /// The freshest snapshot available, asking the server for a new one when the last is older than
    /// <see cref="ReuseWindow"/>. Returns null when the server does not answer in time.
    /// </summary>
    internal Snapshot? TryGet(bool serverRunning)
    {
        if (!serverRunning)
            return null;

        DateTime now = DateTime.UtcNow;
        Snapshot? cached = Volatile.Read(ref _cached);
        if (cached != null && now - _lastReadUtc < ReuseWindow && IsFresh(cached, now))
            return cached;

        _gate.Wait();
        try
        {
            now = DateTime.UtcNow;
            cached = _cached;
            if (cached != null && now - _lastReadUtc < ReuseWindow && IsFresh(cached, now))
                return cached;
            if (now - _lastRequestUtc < RequestSpacing && cached != null)
                return IsFresh(cached, now) ? cached : null;

            _lastRequestUtc = now;
            Snapshot? snapshot = Request(now);
            if (snapshot != null)
            {
                _cached = snapshot;
                _lastReadUtc = DateTime.UtcNow;
                return snapshot;
            }

            // The server may still be publishing an older snapshot: prefer it to an empty map for a few seconds.
            return cached != null && IsFresh(cached, now) ? cached : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Snapshot? Request(DateTime now)
    {
        string requestId = Guid.NewGuid().ToString("N");
        string temporary = RequestPath + $".{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temporary, requestId);
            File.Move(temporary, RequestPath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ReadSnapshot();
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // One fixed process-scoped temporary name is reused by the next request.
            }
        }

        DateTime deadline = now.AddMilliseconds(SnapshotWaitMilliseconds);
        do
        {
            Snapshot? snapshot = ReadSnapshot();
            if (snapshot?.RequestId == requestId && IsFresh(snapshot, DateTime.UtcNow))
                return snapshot;
            Thread.Sleep(25);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    private Snapshot? ReadSnapshot()
    {
        try
        {
            if (!File.Exists(ResponsePath))
                return null;
            using var stream = new FileStream(ResponsePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<Snapshot>(stream, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool IsFresh(Snapshot snapshot, DateTime nowUtc)
    {
        if (!snapshot.Running || snapshot.Bots == null)
            return false;
        TimeSpan age = nowUtc - snapshot.UpdatedUtc.ToUniversalTime();
        return age >= TimeSpan.FromSeconds(-5) && age <= TimeSpan.FromMilliseconds(SnapshotMaxAgeMilliseconds);
    }

    /// <summary>Wire format of state-165/bot-world.json (the server's AutonomousBotDashboard.Snapshot).</summary>
    internal sealed record Snapshot(DateTime UpdatedUtc, bool Running, string RequestId, List<BotStatus>? Bots);

    internal sealed record BotStatus(long BotId, int Level, string? ZoneName, string? Activity,
        string? CurrentGoal, string? TargetName, string? TravelDestination, string? ObjectiveProgress,
        bool IsAlive, string? ItineraryJson, string? ObjectiveKind, string? ObjectiveAssignmentId,
        string? ObjectiveAssignedUtc, string? ObjectivePhase, string? ObjectiveExpiresUtc,
        string? Name, string? ClassName, int Realm, int RegionId, int ZoneId, int X, int Y, int Z);
}
