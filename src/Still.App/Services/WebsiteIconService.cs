using System.Collections.Concurrent;
using System.Net.Http;
using Avalonia.Media.Imaging;

namespace Still.App.Services;

/// <summary>
/// Loads a site's icon straight from the site itself, so entering a domain never
/// sends it to a third-party favicon service. Several candidates are fetched in
/// parallel because many sites only serve one of them; the caller picks the
/// first payload it can decode. Results are cached per session.
/// </summary>
public static class WebsiteIconService
{
    private const int MaxBytes = 512 * 1024;

    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, Task<IReadOnlyList<byte[]>>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task<IReadOnlyList<byte[]>> GetAsync(string domain) =>
        string.IsNullOrWhiteSpace(domain)
            ? Task.FromResult<IReadOnlyList<byte[]>>(Array.Empty<byte[]>())
            : Cache.GetOrAdd(domain.Trim().ToLowerInvariant(), LoadAsync);

    /// <summary>Returns the first decodable favicon candidate, or null when the site has none.</summary>
    public static async Task<Bitmap?> GetBitmapAsync(string domain)
    {
        var payloads = await GetAsync(domain);
        foreach (var bytes in payloads)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
            catch (Exception)
            {
                // Try the next candidate; not every ICO variant is decodable here.
            }
        }

        return null;
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Still/0.1");
        return client;
    }

    private static async Task<IReadOnlyList<byte[]>> LoadAsync(string domain)
    {
        // Small images first: ICO files are common but not every decoder reads them.
        var urls = new[]
        {
            $"https://{domain}/apple-touch-icon.png",
            $"https://{domain}/favicon.png",
            $"https://{domain}/favicon.ico"
        };

        var payloads = await Task.WhenAll(urls.Select(FetchAsync));
        return payloads.Where(payload => payload is not null).Cast<byte[]>().ToList();
    }

    private static async Task<byte[]?> FetchAsync(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length is > 0 and <= MaxBytes ? bytes : null;
        }
        catch (Exception)
        {
            // A missing icon must never surface as an error in the editor.
            return null;
        }
    }
}
