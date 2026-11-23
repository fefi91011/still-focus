using System.Net.Http;
using System.Text.Json;

namespace Still.App.Services;

public sealed record UpdateInfo(string Version, string PageUrl);

/// <summary>
/// Asks GitHub for the newest release tag. Only a version comparison is stored;
/// nothing about the machine or the user is sent.
/// </summary>
public static class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/fefi91011/still-focus/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/fefi91011/still-focus/releases/latest";

    private static readonly HttpClient Http = CreateClient();

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

        return new UpdateInfo(
            Format(latest),
            string.IsNullOrWhiteSpace(page) ? ReleasesPageUrl : page!);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
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
