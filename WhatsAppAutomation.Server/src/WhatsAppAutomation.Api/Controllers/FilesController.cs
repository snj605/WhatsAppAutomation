using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WhatsAppAutomation.Api.Authorization;
using WhatsAppAutomation.Api.Extensions;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Files;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Api.Controllers;

/// <summary>Files to send with a broadcast. Upload first, then pass the id as attachmentId when creating the broadcast.</summary>
[ApiController]
[Route("api/v1/files")]
[Produces("application/json")]
public sealed class FilesController(ISender mediator, IDataProtectionProvider protection, TimeProvider clock) : ControllerBase
{
    private const long Overhead = 64 * 1024;
    private const string ViewPurpose = "files.view.v1";
    private static readonly TimeSpan ViewLinkLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// A link to the file's content, valid for 15 minutes, for the WhatsApp-style preview (photos and videos
    /// are shown by the browser or the app, which cannot send the sign-in token). Only for files the user may use.
    /// </summary>
    [HttpGet("{id:guid}/view-link")]
    [HasPermission(Permissions.Messages.Send)]
    [ProducesResponseType<FileViewLink>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ViewLink(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetFileViewQuery(id), cancellationToken);
        if (result.Error is { } error)
        {
            return this.ToProblem(error);
        }

        var expires = clock.GetUtcNow().Add(ViewLinkLifetime);
        var token = protection.CreateProtector(ViewPurpose).ToTimeLimitedDataProtector()
            .Protect(id.ToString("N"), expires);
        return Ok(new FileViewLink($"/api/v1/files/{id}/view?t={Uri.EscapeDataString(token)}", expires));
    }

    /// <summary>The file's content for a link from view-link: shown inline, in parts (videos can be played while loading).</summary>
    [HttpGet("{id:guid}/view")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> View(Guid id, [FromQuery] string? t, CancellationToken cancellationToken)
    {
        string? fileId = null;
        try
        {
            fileId = t is null ? null : protection.CreateProtector(ViewPurpose).ToTimeLimitedDataProtector().Unprotect(t, out _);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Changed, expired or from another server: not valid.
        }

        if (fileId != id.ToString("N"))
        {
            return NotFound();
        }

        var result = await mediator.Send(new GetFileForGatewayQuery(id), cancellationToken);
        if (result.Error is { } error)
        {
            return this.ToProblem(error);
        }

        Response.Headers.CacheControl = "private, max-age=600";
        Response.Headers.XContentTypeOptions = "nosniff";
        // Opened on its own, a file (for example an SVG or HTML document) can run nothing on this site.
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; media-src 'self'; style-src 'unsafe-inline'; sandbox";
        var content = result.Value;
        var inline = content.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !content.ContentType.Contains("svg", StringComparison.OrdinalIgnoreCase)
            || content.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || content.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
        return inline
            ? File(content.Data, content.ContentType, enableRangeProcessing: true)
            : File(content.Data, content.ContentType, content.FileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// Any document, photo, video or audio up to 512 MB (programs and scripts are refused). It is streamed to the
    /// file store. Videos above 100 MB can only go as documents (isBigVideo).
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Messages.Send)]
    [RequestSizeLimit(StoredFile.MaxBytes + Overhead)]
    [RequestFormLimits(MultipartBodyLengthLimit = StoredFile.MaxBytes + Overhead)]
    [ProducesResponseType<FileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return this.ToProblem(Error.Validation("Choose a file."));
        }

        if (file.Length > StoredFile.MaxBytes)
        {
            return this.ToProblem(FileErrors.TooLarge);
        }

        await using var content = file.OpenReadStream();
        var result = await mediator.Send(new UploadFileCommand(file.FileName, file.ContentType, content), cancellationToken);
        return this.ToActionResult(result, Ok);
    }

    /// <summary>Takes a copy of a file shared by link (Google Drive, "Anyone with the link").</summary>
    [HttpPost("from-link")]
    [HasPermission(Permissions.Messages.Send)]
    [EnableRateLimiting(Configurations.RateLimitPolicies.Send)]
    [ProducesResponseType<FileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FromLink(FileFromLinkRequest request, CancellationToken cancellationToken) =>
        this.ToActionResult(await mediator.Send(new ImportFileFromLinkCommand(request.Url), cancellationToken), Ok);

}

/// <summary>
/// Called only by the WhatsApp gateway (shared secret), to fetch a broadcast's file once and upload it to WhatsApp.
/// In production the reverse proxy must not forward /internal at all.
/// </summary>
[ApiController]
[Route("internal/files")]
[AllowAnonymous]
[GatewaySecret]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalFilesController(ISender mediator) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetFileForGatewayQuery(id), cancellationToken);
        if (result.Error is { } error)
        {
            return this.ToProblem(error);
        }

        return File(result.Value.Data, result.Value.ContentType, enableRangeProcessing: false);
    }
}

public sealed record FileFromLinkRequest(string Url);

/// <param name="Url">Relative to the API: /api/v1/files/{id}/view?t=…</param>
public sealed record FileViewLink(string Url, DateTimeOffset ExpiresAt);
