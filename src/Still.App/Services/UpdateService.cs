using System.Net.Http;
using System.Text.Json;

namespace Still.App.Services;

public sealed record UpdateInfo(string Version, string PageUrl, string? InstallerUrl, long InstallerSize);

/// <summary>
/// Asks GitHub for the newest release tag and its installer asset. Only a
/// version comparison is stored; nothing about the machine or the user is sent.
/// </summary>
public static class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/fefi91011/still-focus/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/fefi91011/still-focus/releases/latest";
    private const long MinimumInstallerBytes = 1_000_000;

    private static readonly HttpClient Http = CreateClient();
    private static readonly HttpClient DownloadClient = CreateDownloadClient();

    public static string ReleasesPage => ReleasesPageUrl;

    /// <summary>Null means the installed version is current; failures throw.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        var page = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;

        var latest = ParseVersion(tag);
        var current = ParseVersion(currentVersion);
        if (latest is null || current is null || latest <= current)
        {
            return null;
        }

        var (installerUrl, installerSize) = FindInstaller(root);

        return new UpdateInfo(
            Format(latest),
            string.IsNullOrWhiteSpace(page) ? ReleasesPageUrl : page!,
            installerUrl,
            installerSize);
    }

    /// <summary>
    /// Downloads the installer next to the app data and returns its path.
    /// Throws when the asset is missing, the download is incomplete or the
    /// server returns an error.
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(
        UpdateInfo update,
        string directory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.InstallerUrl))
        {
            throw new InvalidOperationException("Dieses Release enthält keinen Installer.");
        }

        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"StillSetup-{update.Version}.exe");
        var partial = target + ".part";

        using var response = await DownloadClient.GetAsync(
            update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? update.InstallerSize;
        long written = 0;

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = File.Create(partial))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    if (total > 0)
                    {
                        progress?.Report(Math.Min(1d, (double)written / total));
                    }
                }

                await file.FlushAsync(cancellationToken);
            }

            if (written < MinimumInstallerBytes || (update.InstallerSize > 0 && written != update.InstallerSize))
            {
                throw new InvalidOperationException(
                    "Der Download ist unvollständig. Bitte versuche es noch einmal.");
            }

            File.Move(partial, target, overwrite: true);
            return target;
        }
        finally
        {
            if (File.Exists(partial))
            {
                try
                {
                    File.Delete(partial);
                }
                catch
                {
                    // A leftover .part file is harmless.
                }
            }
        }
    }

    private static (string? Url, long Size) FindInstaller(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return (null, 0);
        }

        string? fallbackUrl = null;
        long fallbackSize = 0;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(url)
                || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes)
                ? bytes
                : 0;

            if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            {
                return (url, size);
            }

            fallbackUrl ??= url;
            fallbackSize = size;
        }

        return (fallbackUrl, fallbackSize);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Still-App");
        return client;
    }

    private static HttpClient CreateDownloadClient()
    {
        // The installer is ~30 MB; the 10 s API timeout would cut it off.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Still-App");
        return client;
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim().TrimStart('v', 'V');
        var cut = text.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        return Version.TryParse(text, out var version) ? version : null;
    }

    private static string Format(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}
