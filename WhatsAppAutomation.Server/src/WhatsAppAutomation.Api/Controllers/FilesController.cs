using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
public sealed class FilesController(ISender mediator) : ControllerBase
{
    private const long Overhead = 64 * 1024;

    /// <summary>Any document, photo, video or audio up to 16 MB (programs and scripts are refused).</summary>
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

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        var result = await mediator.Send(new UploadFileCommand(file.FileName, file.ContentType, buffer.ToArray()), cancellationToken);
        return this.ToActionResult(result, Ok);
    }
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

        return File(result.Value.Data, result.Value.ContentType);
    }
}
