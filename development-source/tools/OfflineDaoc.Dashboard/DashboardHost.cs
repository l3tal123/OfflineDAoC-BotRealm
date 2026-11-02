using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace OfflineDaoc.Dashboard;

/// <summary>
/// A deliberately small HTTP server on the loopback interface. It serves the files in <c>dashboard\wwwroot</c>, the
/// read-only data endpoints and a handful of launcher actions. It is a plain TCP listener so the launcher needs no
/// privileges (no URL reservations), no ASP.NET runtime and no open firewall port; nothing outside this machine can
/// reach it.
///
/// Read endpoints are open to the local page (a web page cannot read another origin's responses). The launcher
/// actions - starting the server, starting the game client, stopping the server - additionally require the per-run
/// token that <c>/api/status</c> hands to the page, so no other origin can drive them.
/// </summary>
internal sealed class DashboardHost : IDisposable
{
    private const int MaxRequestBytes = 32 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".txt"] = "text/plain; charset=utf-8",
        [".woff2"] = "font/woff2",
    };

    private readonly DashboardOptions _options;
    private readonly DashboardData _data;
    private readonly LauncherHost _launcher;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _wwwRoot;
    private readonly string _token = Guid.NewGuid().ToString("N");
    private int _listening;

    internal DashboardHost(DashboardOptions options, LauncherHost launcher)
    {
        _options = options;
        _launcher = launcher;
        _wwwRoot = Path.GetFullPath(options.WwwRoot);
        _data = new DashboardData(options.RuntimeDirectory, options.DatabasePath, options.ServerDirectory,
            options.MapsDirectory);
        _listener = new TcpListener(IPAddress.Loopback, options.Port);
    }

    internal string Url => _options.Url;

    internal void Start()
    {
        _listener.Start(64);
        Interlocked.Exchange(ref _listening, 1);
        _ = Task.Run(AcceptLoopAsync);
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (Interlocked.Exchange(ref _listening, 0) == 1)
        {
            try
            {
                _listener.Stop();
            }
            catch (SocketException)
            {
                // Already closed.
            }
        }

        _listener.Server.Dispose();
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using NetworkStream stream = client.GetStream();
                Request? request = await ReadRequestAsync(stream).ConfigureAwait(false);
                if (request == null)
                {
                    await WriteResponseAsync(stream, 400, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Bad request"), false)
                        .ConfigureAwait(false);
                    return;
                }

                if (!IsAllowedHost(request.Headers.GetValueOrDefault("host")))
                {
                    // DNS rebinding guard: a page on the web must not be able to read or drive the local launcher.
                    await WriteResponseAsync(stream, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Forbidden"), false)
                        .ConfigureAwait(false);
                    return;
                }

                bool head = string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
                bool get = string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) || head;
                bool post = string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase);
                if (!get && !post)
                {
                    await WriteResponseAsync(stream, 405, "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("Use GET for data and POST for launcher actions."), false).ConfigureAwait(false);
                    return;
                }

                if (post)
                {
                    await HandleActionAsync(stream, request).ConfigureAwait(false);
                    return;
                }

                string path = request.Path;
                if (path == "/api/status")
                {
                    // The token travels only to the page that the local launcher itself served (same origin).
                    await WriteJsonAsync(stream, new { token = _token, launcher = _launcher.Status(), server = _options.RuntimeDirectory },
                        head).ConfigureAwait(false);
                    return;
                }

                if (path == "/api/log")
                {
                    long.TryParse(request.Query.GetValueOrDefault("after"), out long after);
                    IReadOnlyList<string> lines = _launcher.LogSince(after, out long next);
                    await WriteJsonAsync(stream, new { next, lines }, head).ConfigureAwait(false);
                    return;
                }

                object? payload = path switch
                {
                    "/api/overview" => _data.Overview(),
                    "/api/population" => _data.Population(),
                    "/api/world" => _data.World(),
                    "/api/health" => new { ok = true },
                    _ => null,
                };
                if (payload != null)
                {
                    await WriteJsonAsync(stream, payload, head).ConfigureAwait(false);
                    return;
                }

                if (path.StartsWith("/zone-map/", StringComparison.OrdinalIgnoreCase))
                {
                    // The new client's painted zone map for the world map tab, when the install carries them.
                    byte[]? image = int.TryParse(path["/zone-map/".Length..], out int zoneId) && zoneId > 0
                        ? _data.ZoneImage(zoneId)
                        : null;
                    if (image == null)
                    {
                        await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8",
                            Encoding.UTF8.GetBytes("No painted map for that zone"), head).ConfigureAwait(false);
                        return;
                    }

                    await WriteResponseAsync(stream, 200, "image/png", image, head).ConfigureAwait(false);
                    return;
                }

                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("Unknown endpoint"), head).ConfigureAwait(false);
                    return;
                }

                await ServeStaticAsync(stream, path, head).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
            {
                // The browser closed the connection: nothing to report.
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"launcher: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private async Task HandleActionAsync(NetworkStream stream, Request request)
    {
        if (!request.Headers.TryGetValue("x-launcher-token", out string? token) ||
            !string.Equals(token, _token, StringComparison.Ordinal))
        {
            await WriteJsonAsync(stream, new { ok = false, message = "This action needs the launcher page's token. Reload the page." },
                false, 403).ConfigureAwait(false);
            return;
        }

        (bool Ok, string Message) result = request.Path switch
        {
            "/api/server/start" => Unwrap(_launcher.StartServer()),
            "/api/server/stop" => await _launcher.StopServerAsync().ConfigureAwait(false) is ActionOutcome stop
                ? (stop.Ok, stop.Message)
                : (false, "Unexpected result."),
            "/api/client/start" => Unwrap(_launcher.StartClient()),
            _ => (false, "Unknown action."),
        };

        Console.WriteLine($"launcher: {request.Path} -> {(result.Ok ? "ok" : "failed")}: {result.Message}");
        await WriteJsonAsync(stream, new { ok = result.Ok, message = result.Message, launcher = _launcher.Status() }, false)
            .ConfigureAwait(false);
    }

    private static (bool Ok, string Message) Unwrap(ActionOutcome outcome) => (outcome.Ok, outcome.Message);

    private async Task WriteJsonAsync(NetworkStream stream, object payload, bool head, int status = 200)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await WriteResponseAsync(stream, status, "application/json; charset=utf-8", json, head).ConfigureAwait(false);
    }

    private async Task ServeStaticAsync(NetworkStream stream, string path, bool head)
    {
        string relative = path == "/" ? "index.html" : Uri.UnescapeDataString(path.TrimStart('/'));
        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(_wwwRoot, relative));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await WriteResponseAsync(stream, 400, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Bad path"), false)
                .ConfigureAwait(false);
            return;
        }

        if (!candidate.StartsWith(_wwwRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
        {
            await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not found"), false)
                .ConfigureAwait(false);
            return;
        }

        byte[] content = await File.ReadAllBytesAsync(candidate).ConfigureAwait(false);
        string contentType = ContentTypes.GetValueOrDefault(Path.GetExtension(candidate), "application/octet-stream");
        await WriteResponseAsync(stream, 200, contentType, content, head).ConfigureAwait(false);
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string contentType, byte[] body, bool head)
    {
        string reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            500 => "Internal Server Error",
            _ => "OK",
        };
        var headers = new StringBuilder(256);
        headers.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {status} {reason}\r\n");
        headers.Append(CultureInfo.InvariantCulture, $"Content-Type: {contentType}\r\n");
        headers.Append(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n");
        // The UI is rebuilt during development and must never be served stale; the payloads are tiny.
        headers.Append("Cache-Control: no-store\r\n");
        headers.Append("X-Content-Type-Options: nosniff\r\n");
        headers.Append("Connection: close\r\n\r\n");
        byte[] headBytes = Encoding.ASCII.GetBytes(headers.ToString());
        await stream.WriteAsync(headBytes).ConfigureAwait(false);
        if (!head && body.Length > 0)
            await stream.WriteAsync(body).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static bool IsAllowedHost(string? hostHeader)
    {
        if (string.IsNullOrEmpty(hostHeader))
            return false;
        string host = hostHeader;
        if (host.StartsWith('['))
        {
            int end = host.IndexOf(']');
            if (end < 0)
                return false;
            host = host[..(end + 1)];
        }
        else
        {
            int colon = host.LastIndexOf(':');
            if (colon > 0)
                host = host[..colon];
        }

        return host is "127.0.0.1" or "localhost" or "::1" or "[::1]";
    }

    private static async Task<Request?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[4096];
        var received = new List<byte>(4096);
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read <= 0)
                return null;
            for (int index = 0; index < read; index++)
                received.Add(buffer[index]);
            if (received.Count > MaxRequestBytes)
                return null;
            headerEnd = FindHeaderEnd(received);
        }

        string head = Encoding.ASCII.GetString(received.ToArray(), 0, headerEnd);
        string[] lines = head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return null;
        string[] parts = lines[0].Split(' ');
        if (parts.Length < 2)
            return null;
        string target = parts[1];
        string path = target;
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int question = target.IndexOf('?');
        if (question >= 0)
        {
            path = target[..question];
            foreach (string pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0)
                    continue;
                query[Uri.UnescapeDataString(pair[..equals])] = Uri.UnescapeDataString(pair[(equals + 1)..]);
            }
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index < lines.Length; index++)
        {
            int colon = lines[index].IndexOf(':');
            if (colon <= 0)
                continue;
            headers[lines[index][..colon].Trim()] = lines[index][(colon + 1)..].Trim();
        }

        return new Request(parts[0], path, query, headers);
    }

    private static int FindHeaderEnd(List<byte> bytes)
    {
        for (int index = 0; index + 3 < bytes.Count; index++)
        {
            if (bytes[index] == 13 && bytes[index + 1] == 10 && bytes[index + 2] == 13 && bytes[index + 3] == 10)
                return index;
        }

        return -1;
    }

    private sealed record Request(string Method, string Path, Dictionary<string, string> Query,
        Dictionary<string, string> Headers);
}
