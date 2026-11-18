using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WhatsAppAutomation.Application.Common.Interfaces;

namespace WhatsAppAutomation.Infrastructure.Files;

/// <summary>
/// Downloads a file shared on Google Drive as "Anyone with the link". Only Drive links are accepted, so the server
/// is never made to fetch arbitrary addresses. Large files get Google's "can't scan for viruses" page first; its
/// download button is followed.
/// </summary>
internal sealed partial class DriveFileFetcher(HttpClient http, IFileStore store, ILogger<DriveFileFetcher> logger) : ILinkedFileFetcher
{
    public const string HttpClientName = "drive";

    public async Task<LinkedFileResult> FetchAsync(Guid id, string url, long maxBytes, CancellationToken cancellationToken)
    {
        if (FileIdOf(url) is not { } fileId)
        {
            return Fail("files.link_not_drive", "Paste a Google Drive link to a file (shared as \"Anyone with the link\").");
        }

        try
        {
            var download = new Uri($"https://drive.usercontent.google.com/download?id={Uri.EscapeDataString(fileId)}&export=download");
            for (var step = 0; step < 2; step++)
            {
                using var response = await http.GetAsync(download, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                {
                    return Fail("files.link_private", "This file is not shared as \"Anyone with the link\". Change the sharing in Drive and try again.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Fail("files.link_failed", "Google Drive did not give the file. Try again in a moment.");
                }

                var type = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                if (type == "text/html")
                {
                    // Google's warning page for big files (or a sign-in page for private ones).
                    var html = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (ConfirmLink(html, fileId) is not { } confirm)
                    {
                        return Fail("files.link_private", "This file is not shared as \"Anyone with the link\". Change the sharing in Drive and try again.");
                    }

                    download = confirm;
                    continue;
                }

                if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
                {
                    return Fail("files.too_large", $"The file is larger than {maxBytes / 1024 / 1024} MB.");
                }

                await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
                if (await store.SaveAsync(id, content, maxBytes, cancellationToken) is not { } saved)
                {
                    return Fail("files.too_large", $"The file is larger than {maxBytes / 1024 / 1024} MB.");
                }

                var name = response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                    ?? "file";
                return new LinkedFileResult(name, type, saved.Size, saved.Sha256, null, null);
            }

            return Fail("files.link_failed", "Google Drive did not give the file. Try again in a moment.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Drive download failed");
            return Fail("files.link_unreachable", "Google Drive cannot be reached from the server. Try again, or upload the file instead.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("files.link_timeout", "Google Drive took too long. Try again, or upload the file instead.");
        }
    }

    /// <summary>The file id of drive.google.com/file/d/{id}/…, …/open?id={id} or …/uc?id={id} links.</summary>
    internal static string? FileIdOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host is not ("drive.google.com" or "docs.google.com" or "drive.usercontent.google.com"))
        {
            return null;
        }

        var path = PathId().Match(uri.AbsolutePath);
        if (path.Success)
        {
            return path.Groups[1].Value;
        }

        var query = QueryId().Match(uri.Query);
        return query.Success ? query.Groups[1].Value : null;
    }

    private static Uri? ConfirmLink(string html, string fileId)
    {
        var uuid = HiddenInput("uuid").Match(html);
        var confirm = HiddenInput("confirm").Match(html);
        if (!uuid.Success && !confirm.Success)
        {
            return null;
        }

        var link = $"https://drive.usercontent.google.com/download?id={Uri.EscapeDataString(fileId)}&export=download" +
            $"&confirm={Uri.EscapeDataString(confirm.Success ? confirm.Groups[1].Value : "t")}" +
            (uuid.Success ? $"&uuid={Uri.EscapeDataString(uuid.Groups[1].Value)}" : string.Empty);
        return new Uri(link);
    }

    private static LinkedFileResult Fail(string code, string message) => new(null, null, 0, null, code, message);

    [GeneratedRegex(@"/(?:file/)?d/([A-Za-z0-9_-]{10,})")]
    private static partial Regex PathId();

    [GeneratedRegex(@"[?&]id=([A-Za-z0-9_-]{10,})")]
    private static partial Regex QueryId();

    private static Regex HiddenInput(string name) =>
        new($"name=\"{Regex.Escape(name)}\"\\s+value=\"([^\"]+)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
}
