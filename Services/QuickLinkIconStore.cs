using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KLauncher.Services;

public static class QuickLinkIconStore
{
    private const int MaximumHtmlBytes = 512 * 1024;
    private const int MaximumIconBytes = 2 * 1024 * 1024;
    private const int MinimumIconDimension = 64;
    private const int PreferredIconDimension = 128;

    private static readonly string IconDirectory =
        Path.Combine(LauncherStorage.DataDirectory, "quick-link-icons");

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    static QuickLinkIconStore()
    {
        Client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("KLauncher", "1.0"));
    }

    public static string? GetCachedIconPath(string url)
    {
        Uri uri;
        try
        {
            uri = new Uri(QuickLinkStore.NormalizeUrl(url), UriKind.Absolute);
        }
        catch
        {
            return null;
        }

        var key = GetUrlKey(uri);
        foreach (var extension in new[] { ".ico", ".png", ".jpg", ".jpeg", ".gif", ".webp" })
        {
            var path = Path.Combine(IconDirectory, key + extension);
            if (IsUsableIconPath(path))
                return path;

            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }

        return null;
    }

    public static async Task<string?> TryFetchAndSaveAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        Uri pageUri;
        try
        {
            pageUri = new Uri(QuickLinkStore.NormalizeUrl(url), UriKind.Absolute);
        }
        catch
        {
            return null;
        }

        var cached = GetCachedIconPath(pageUri.ToString());
        if (cached is not null)
            return cached;

        var candidates = new List<IconCandidate>();
        try
        {
            var originFavicon = new Uri(
                $"{pageUri.Scheme}://{pageUri.Authority}/favicon.ico");
            candidates.Add(new IconCandidate(originFavicon, 32));
        }
        catch
        {
            // The page URI was already validated; a malformed authority is still
            // not allowed to interrupt saving the link itself.
        }

        try
        {
            using var response = await Client.GetAsync(
                pageUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode &&
                IsHtml(response.Content.Headers.ContentType))
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                if (html.Length > MaximumHtmlBytes)
                    html = html[..MaximumHtmlBytes];

                candidates.InsertRange(0, ExtractIconCandidates(html, pageUri));
            }
        }
        catch
        {
            // The direct /favicon.ico fallback is still attempted below.
        }

        try
        {
            var fallback = new Uri(
                "https://www.google.com/s2/favicons?sz=" +
                PreferredIconDimension +
                "&domain_url=" +
                Uri.EscapeDataString(pageUri.GetLeftPart(UriPartial.Authority)));
            candidates.Add(new IconCandidate(fallback, PreferredIconDimension));
        }
        catch
        {
            // A third-party fallback is optional; the site's own icons remain primary.
        }

        var uniqueCandidates = candidates
            .OrderByDescending(candidate => candidate.DeclaredSize)
            .DistinctBy(candidate => candidate.Uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidate in uniqueCandidates)
        {
            var downloaded = await TryDownloadIconAsync(candidate.Uri, cancellationToken);
            if (downloaded is null)
                continue;

            try
            {
                Directory.CreateDirectory(IconDirectory);
                var path = Path.Combine(
                    IconDirectory,
                    GetUrlKey(pageUri) + downloaded.Extension);
                await File.WriteAllBytesAsync(path, downloaded.Bytes, cancellationToken);
                return path;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static IEnumerable<IconCandidate> ExtractIconCandidates(string html, Uri pageUri)
    {
        foreach (Match tagMatch in Regex.Matches(
                     html,
                     "<link\\b[^>]*>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var tag = tagMatch.Value;
            var rel = ReadAttribute(tag, "rel");
            var relationValues = rel?.Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries) ?? [];
            if (relationValues.Length == 0 ||
                !relationValues.Any(value =>
                    value.Equals("icon", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("shortcut", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var href = ReadAttribute(tag, "href");
            if (string.IsNullOrWhiteSpace(href) ||
                !Uri.TryCreate(pageUri, href, out var iconUri) ||
                (iconUri.Scheme != Uri.UriSchemeHttp && iconUri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            var declaredSize = ReadIconSize(ReadAttribute(tag, "sizes"));
            if (declaredSize == 0 && relationValues.Any(value =>
                    value.Equals("apple-touch-icon", StringComparison.OrdinalIgnoreCase)))
            {
                declaredSize = PreferredIconDimension;
            }

            yield return new IconCandidate(iconUri, declaredSize);
        }
    }

    private static int ReadIconSize(string? sizes)
    {
        if (string.IsNullOrWhiteSpace(sizes))
            return 0;

        var largest = 0;
        foreach (Match match in Regex.Matches(sizes, @"(?<width>\d+)x(?<height>\d+)", RegexOptions.IgnoreCase))
        {
            if (!int.TryParse(match.Groups["width"].Value, out var width) ||
                !int.TryParse(match.Groups["height"].Value, out var height))
                continue;

            largest = Math.Max(largest, Math.Min(width, height));
        }

        return largest;
    }

    private static string? ReadAttribute(string tag, string attributeName)
    {
        var match = Regex.Match(
            tag,
            $"\\b{Regex.Escape(attributeName)}\\s*=\\s*[\\\"']([^\\\"']+)[\\\"']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static async Task<DownloadedIcon?> TryDownloadIconAsync(
        Uri iconUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(
                iconUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentLength > MaximumIconBytes)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0 || bytes.Length > MaximumIconBytes ||
                !LooksLikeSupportedImage(bytes, response.Content.Headers.ContentType) ||
                !TryGetImageDimensions(bytes, out var width, out var height) ||
                Math.Max(width, height) < MinimumIconDimension)
            {
                return null;
            }

            return new DownloadedIcon(
                bytes,
                GetExtension(iconUri, response.Content.Headers.ContentType));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsHtml(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true;

    private static bool LooksLikeSupportedImage(
        byte[] bytes,
        MediaTypeHeaderValue? contentType)
    {
        var mediaType = contentType?.MediaType ?? string.Empty;
        if (mediaType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase))
            return false;

        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        return bytes.Length >= 4 &&
               ((bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) ||
                (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) ||
                (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) ||
                (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00));
    }

    public static bool IsUsableIconPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > MaximumIconBytes)
                return false;

            var bytes = File.ReadAllBytes(path);
            return TryGetImageDimensions(bytes, out var width, out var height) &&
                   Math.Max(width, height) >= MinimumIconDimension;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetImageDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (bytes.Length >= 24 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            width = ReadBigEndianInt32(bytes, 16);
            height = ReadBigEndianInt32(bytes, 20);
            return width > 0 && height > 0;
        }

        if (bytes.Length >= 10 &&
            ((bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F') ||
             (bytes[0] == (byte)'g' && bytes[1] == (byte)'i' && bytes[2] == (byte)'f')))
        {
            width = bytes[6] | (bytes[7] << 8);
            height = bytes[8] | (bytes[9] << 8);
            return width > 0 && height > 0;
        }

        if (bytes.Length >= 30 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            if (bytes[12] == (byte)'V' && bytes[13] == (byte)'P' && bytes[14] == (byte)'8' && bytes[15] == (byte)'X')
            {
                width = 1 + (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16));
                height = 1 + (bytes[27] | (bytes[28] << 8) | (bytes[29] << 16));
                return width > 0 && height > 0;
            }
        }

        if (bytes.Length >= 6 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0)
        {
            var imageCount = bytes[4] | (bytes[5] << 8);
            for (var index = 0; index < imageCount; index++)
            {
                var entry = 6 + (index * 16);
                if (entry + 8 > bytes.Length)
                    break;

                var entryWidth = bytes[entry] == 0 ? 256 : bytes[entry];
                var entryHeight = bytes[entry + 1] == 0 ? 256 : bytes[entry + 1];
                width = Math.Max(width, entryWidth);
                height = Math.Max(height, entryHeight);
            }

            return width > 0 && height > 0;
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 &&
            TryGetJpegDimensions(bytes, out width, out height))
            return true;

        return false;
    }

    private static bool TryGetJpegDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        var offset = 2;

        while (offset + 9 < bytes.Length)
        {
            while (offset < bytes.Length && bytes[offset] != 0xFF)
                offset++;
            while (offset < bytes.Length && bytes[offset] == 0xFF)
                offset++;
            if (offset >= bytes.Length)
                break;

            var marker = bytes[offset++];
            if (marker is 0xD8 or 0xD9 or 0x01)
                continue;
            if (offset + 1 >= bytes.Length)
                break;

            var segmentLength = (bytes[offset] << 8) | bytes[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > bytes.Length)
                break;

            var isStartOfFrame = (marker is >= 0xC0 and <= 0xC3) ||
                                 (marker is >= 0xC5 and <= 0xC7) ||
                                 (marker is >= 0xC9 and <= 0xCB) ||
                                 (marker is >= 0xCD and <= 0xCF);
            if (isStartOfFrame && offset + 7 < bytes.Length)
            {
                height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                return width > 0 && height > 0;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) |
        (bytes[offset + 1] << 16) |
        (bytes[offset + 2] << 8) |
        bytes[offset + 3];

    private static string GetExtension(Uri iconUri, MediaTypeHeaderValue? contentType)
    {
        var mediaType = contentType?.MediaType?.ToLowerInvariant();
        var fromContentType = mediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            _ => string.Empty
        };

        if (fromContentType.Length > 0)
            return fromContentType;

        var extension = Path.GetExtension(iconUri.AbsolutePath).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".ico"
            ? extension
            : ".ico";
    }

    private static string GetUrlKey(Uri uri)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    private sealed record IconCandidate(Uri Uri, int DeclaredSize);
    private sealed record DownloadedIcon(byte[] Bytes, string Extension);
}
