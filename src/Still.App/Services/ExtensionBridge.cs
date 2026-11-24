using System.Net;
using System.Text;
using System.Text.Json;
using Still.Core.Runtime;

namespace Still.App.Services;

/// <summary>
/// Small loopback API for the browser extension. It deliberately binds only to
/// 127.0.0.1 and accepts browser requests only from extension origins.
/// </summary>
public sealed class ExtensionBridge : IDisposable
{
    public const int Port = 47821;

    private const int MaxBodyBytes = 16 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly StillRuntime _runtime;
    private readonly Action _openApp;
    private readonly Action _contact;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _clientGate = new();
    private ExtensionClient? _client;
    private bool _started;
    private bool _disposed;

    public ExtensionBridge(StillRuntime runtime, Action openApp, Action contact)
    {
        _runtime = runtime;
        _openApp = openApp;
        _contact = contact;
    }

    public ExtensionClient? LastClient
    {
        get
        {
            lock (_clientGate)
            {
                return _client;
            }
        }
    }

    public bool IsConnected => LastClient is { } client
        && DateTimeOffset.UtcNow - client.SeenAt <= TimeSpan.FromMinutes(5);

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _started = true;
        _ = Task.Run(LoopAsync);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();

        try
        {
            _listener.Stop();
        }
        catch
        {
        }

        try
        {
            _listener.Close();
        }
        catch
        {
        }

    }

    private async Task LoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                if (_cancellation.IsCancellationRequested)
                {
                    break;
                }

                await Task.Delay(100).ConfigureAwait(false);
                continue;
            }
            catch (InvalidOperationException)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            AddCorsHeaders(request, response);

            var origin = request.Headers["Origin"];
            if (!string.IsNullOrWhiteSpace(origin) && !IsExtensionOrigin(origin))
            {
                await WriteJsonAsync(response, 403, new { ok = false, message = "Origin nicht erlaubt." });
                return;
            }

            if (string.Equals(request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = 204;
                return;
            }

            var path = (request.Url?.AbsolutePath ?? "/").TrimEnd('/');
            if (path.Length == 0)
            {
                path = "/";
            }

            switch (path)
            {
                case "/v1/ping":
                    await WriteJsonAsync(response, 200, new
                    {
                        ok = true,
                        app = "Still",
                        version = AppInfo.Version,
                        port = Port
                    });
                    return;

                case "/v1/hello":
                {
                    var hello = await ReadJsonAsync<HelloRequest>(request);
                    var userAgent = hello?.UserAgent
                        ?? request.QueryString["ua"]
                        ?? request.UserAgent
                        ?? string.Empty;
                    var version = hello?.Version
                        ?? request.QueryString["version"]
                        ?? "?";
                    var name = string.IsNullOrWhiteSpace(hello?.Browser)
                        ? ParseBrowser(userAgent)
                        : hello!.Browser!.Trim();

                    lock (_clientGate)
                    {
                        _client = new ExtensionClient(
                            name,
                            version,
                            userAgent,
                            DateTimeOffset.UtcNow);
                    }

                    await WriteJsonAsync(response, 200, new
                    {
                        ok = true,
                        app = "Still",
                        version = AppInfo.Version
                    });
                    return;
                }

                case "/v1/blocked":
                {
                    var snapshot = _runtime.Snapshot;
                    await WriteJsonAsync(response, 200, new
                    {
                        ok = true,
                        domains = snapshot.BlockedDomains
                            .OrderBy(domain => domain, StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                        lockdown = snapshot.LockdownActive,
                        updatedAt = snapshot.TakenAt
                    });
                    return;
                }

                case "/v1/status":
                {
                    var host = ReadHost(request);
                    if (host is null)
                    {
                        await WriteJsonAsync(response, 400, new { ok = false, message = "Host fehlt." });
                        return;
                    }

                    await WriteJsonAsync(response, 200, new
                    {
                        ok = true,
                        status = _runtime.GetWebsiteStatus(host)
                    });
                    return;
                }

                case "/v1/grace":
                {
                    var parsed = await ReadJsonAsync<GraceRequest>(request);
                    if (parsed is null || parsed.RuleId == Guid.Empty)
                    {
                        await WriteJsonAsync(response, 400, new { ok = false, message = "Regel fehlt." });
                        return;
                    }

                    if (ReadHost(request, parsed.Host) is not { } host)
                    {
                        await WriteJsonAsync(response, 400, new { ok = false, message = "Host fehlt." });
                        return;
                    }

                    var result = _runtime.GrantWebsiteGrace(host, parsed.RuleId);
                    await WriteJsonAsync(response, 200, new
                    {
                        ok = result.Ok,
                        message = result.Message,
                        status = _runtime.GetWebsiteStatus(host)
                    });
                    return;
                }

                case "/v1/open":
                    _openApp();
                    await WriteJsonAsync(response, 200, new { ok = true });
                    return;

                case "/v1/contact":
                    _contact();
                    await WriteJsonAsync(response, 200, new
                    {
                        ok = true,
                        message = "Still öffnet deine Kontaktpersonen."
                    });
                    return;

                default:
                    await WriteJsonAsync(response, 404, new { ok = false, message = "Unbekannter Endpunkt." });
                    return;
            }
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(response, 500, new
                {
                    ok = false,
                    message = "Interner Fehler: " + ex.Message
                });
            }
            catch
            {
            }
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch
            {
            }
        }
    }

    private static string? ReadHost(HttpListenerRequest request, string? fallback = null)
    {
        var host = string.IsNullOrWhiteSpace(fallback)
            ? request.QueryString["host"]
            : fallback;
        host = host?.Trim();
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253)
        {
            return null;
        }

        return host;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest request)
    {
        if (request.ContentLength64 > MaxBodyBytes)
        {
            return default;
        }

        using var reader = new StreamReader(
            request.InputStream,
            request.ContentEncoding ?? Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        if (body.Length == 0 || body.Length > MaxBodyBytes)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object payload)
    {
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

    private static void AddCorsHeaders(HttpListenerRequest request, HttpListenerResponse response)
    {
        var origin = request.Headers["Origin"];
        if (!string.IsNullOrWhiteSpace(origin) && IsExtensionOrigin(origin))
        {
            response.Headers["Access-Control-Allow-Origin"] = origin;
            response.Headers["Vary"] = "Origin";
        }

        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        response.Headers["Access-Control-Allow-Private-Network"] = "true";
        response.Headers["Access-Control-Max-Age"] = "600";
    }

    private static bool IsExtensionOrigin(string origin) =>
        origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
        || origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase)
        || origin.StartsWith("safari-web-extension://", StringComparison.OrdinalIgnoreCase);

    private static string ParseBrowser(string userAgent)
    {
        if (userAgent.Contains("Edg/", StringComparison.Ordinal))
        {
            return "Microsoft Edge";
        }

        if (userAgent.Contains("OPR/", StringComparison.Ordinal))
        {
            return "Opera";
        }

        if (userAgent.Contains("Vivaldi/", StringComparison.Ordinal))
        {
            return "Vivaldi";
        }

        if (userAgent.Contains("Firefox/", StringComparison.Ordinal))
        {
            return "Mozilla Firefox";
        }

        if (userAgent.Contains("Chrome/", StringComparison.Ordinal))
        {
            return "Google Chrome";
        }

        return "Browser";
    }

    private sealed record GraceRequest(Guid RuleId, string? Host);
    private sealed record HelloRequest(string? Version, string? Browser, string? UserAgent);
}

public sealed record ExtensionClient(
    string Name,
    string Version,
    string UserAgent,
    DateTimeOffset SeenAt);
