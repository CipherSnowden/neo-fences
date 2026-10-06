using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using NeoFences.Core.Library;

namespace NeoFences.Shell;

/// <summary>
/// Online art (M34, ADR-055), only when the user said yes: game covers from the Steam store's public search and asset
/// service (no account, no key), website icons from the sites themselves. Only names and addresses are sent; replies are
/// size-capped and must be images. Every failure is null (the caller keeps the glow tile or the letter badge).
/// </summary>
public static class OnlineArt
{
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private const long MaxPageBytes = 1024 * 1024;
    private const string AssetHost = "https://shared.akamai.steamstatic.com/store_item_assets/";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = MaxImageBytes };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NeoFences", typeof(OnlineArt).Assembly.GetName().Version?.ToString(3) ?? "0"));
        return client;
    }

    /// <summary>A Steam store result: the app id and its store name.</summary>
    public sealed record StoreGame(int AppId, string Name);

    /// <summary>The Steam store's search for a game's name (at most 10 results; the term is <see cref="GameArt.SearchTerm"/>).</summary>
    public static async Task<IReadOnlyList<StoreGame>?> SearchAsync(string name, CancellationToken cancel = default)
    {
        try
        {
            var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(GameArt.SearchTerm(name))}&l=english&cc=US";
            using var reply = JsonDocument.Parse(await Http.GetStringAsync(url, cancel));
            if (!reply.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return [];
            return [.. items.EnumerateArray()
                .Where(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && item.TryGetProperty("name", out _))
                .Select(item => new StoreGame(item.GetProperty("id").GetInt32(), item.GetProperty("name").GetString() ?? ""))];
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return null; // offline or Steam unreachable: nothing recorded, tried again at the next scan
        }
    }

    /// <summary>
    /// The 2:3 library covers of these apps: the asset service names each app's current file (new games keep it under a
    /// hashed folder; the old fixed address gives 404 for them).
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, Uri>> CoverUrlsAsync(IReadOnlyList<int> appIds, CancellationToken cancel = default)
    {
        var covers = new Dictionary<int, Uri>();
        if (appIds.Count == 0) return covers;
        try
        {
            var request = JsonSerializer.Serialize(new
            {
                ids = appIds.Select(appId => new { appid = appId }),
                context = new { language = "english", country_code = "US" },
                data_request = new { include_assets = true },
            });
            var url = $"https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json={Uri.EscapeDataString(request)}";
            using var reply = JsonDocument.Parse(await Http.GetStringAsync(url, cancel));
            if (!reply.RootElement.TryGetProperty("response", out var response) || !response.TryGetProperty("store_items", out var items)) return covers;
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("appid", out var appId) || !item.TryGetProperty("assets", out var assets)) continue;
                var format = assets.TryGetProperty("asset_url_format", out var formatValue) ? formatValue.GetString() : null;
                var file = (assets.TryGetProperty("library_capsule_2x", out var big) ? big.GetString() : null) is { Length: > 0 } large ? large
                    : assets.TryGetProperty("library_capsule", out var small) ? small.GetString() : null;
                if (format is null || string.IsNullOrEmpty(file) || !format.Contains("${FILENAME}", StringComparison.Ordinal)) continue;
                if (Uri.TryCreate(AssetHost + format.Replace("${FILENAME}", file, StringComparison.Ordinal), UriKind.Absolute, out var cover)) covers[appId.GetInt32()] = cover;
            }
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
        {
            // none: the caller records nothing and tries again later
        }
        return covers;
    }

    /// <summary>An image's bytes (≤ 5 MB, an image content type); null on any failure.</summary>
    public static async Task<byte[]?> GetImageAsync(Uri url, CancellationToken cancel = default)
    {
        try
        {
            using var reply = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!reply.IsSuccessStatusCode || reply.Content.Headers.ContentLength > MaxImageBytes) return null;
            var type = reply.Content.Headers.ContentType?.MediaType ?? "";
            if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !type.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)) return null;
            var bytes = await reply.Content.ReadAsByteArrayAsync(cancel);
            return bytes.Length == 0 || bytes.Length > MaxImageBytes ? null : bytes;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Downloads an image (≤ 5 MB, an image content type) to <paramref name="path"/>; false on any failure.</summary>
    public static async Task<bool> DownloadImageAsync(Uri url, string path, CancellationToken cancel = default)
    {
        try
        {
            if (await GetImageAsync(url, cancel) is not { } bytes) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, cancel);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// A website's own icon into <paramref name="folder"/>: the page's declared icon (the largest), else <c>/favicon.ico</c>.
    /// The file name, or null when the site gave none.
    /// </summary>
    public static async Task<string?> FetchSiteIconAsync(Uri page, string folder, CancellationToken cancel = default)
    {
        Uri? declared = null;
        try
        {
            using var reply = await Http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (reply.IsSuccessStatusCode && (reply.Content.Headers.ContentType?.MediaType ?? "").Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = await reply.Content.ReadAsStreamAsync(cancel);
                var buffer = new byte[MaxPageBytes];
                var read = 0;
                int chunk;
                while (read < buffer.Length && (chunk = await stream.ReadAsync(buffer.AsMemory(read), cancel)) > 0) read += chunk;
                declared = SiteIcons.DeclaredIcon(System.Text.Encoding.UTF8.GetString(buffer, 0, read), reply.RequestMessage?.RequestUri ?? page);
            }
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            // the page could not be read: the favicon may still be there
        }
        foreach (var icon in declared is null ? [SiteIcons.FallbackIcon(page)] : new[] { declared, SiteIcons.FallbackIcon(page) })
        {
            if (icon.Scheme is not ("http" or "https")) continue;
            var extension = Path.GetExtension(icon.AbsolutePath).ToLowerInvariant() is { Length: > 1 and <= 5 } known && known != ".svg" ? known : ".png";
            var file = $"{page.Host.ToLowerInvariant()}{extension}";
            if (await DownloadImageAsync(icon, Path.Combine(folder, file), cancel)) return file;
        }
        return null;
    }
}
