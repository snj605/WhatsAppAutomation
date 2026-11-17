using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Files;

public sealed record FileDto(Guid Id, string FileName, string ContentType, long SizeBytes)
{
    public static FileDto From(StoredFile f) => new(f.Id, f.FileName, f.ContentType, f.SizeBytes);
}

/// <summary>The bytes of a file, for the gateway only.</summary>
public sealed record FileContent(string FileName, string ContentType, byte[] Data);

public static class FileErrors
{
    public static readonly Error NotSignedIn = new("files.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);

    public static readonly Error Empty = new("files.empty", "The file is empty.", ErrorKind.Validation);

    public static readonly Error TooLarge = new("files.too_large", "The file is larger than 16 MB.", ErrorKind.Validation);

    public static readonly Error Blocked = new(
        "files.blocked", "Programs and scripts (.exe, .apk, .bat…) cannot be sent. Send a document, photo or video instead.", ErrorKind.Validation);

    public static readonly Error NotFound = new("files.not_found", "This file does not exist or is no longer kept.", ErrorKind.NotFound);
}

internal static class FileRules
{
    /// <summary>
    /// Files that run code. WhatsApp blocks several of them anyway, and sending them in bulk would look like malware.
    /// Everything else (documents, photos, videos, audio, archives) is allowed up to 16 MB.
    /// </summary>
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".bat", ".cmd", ".com", ".scr", ".pif", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".jar", ".apk", ".app", ".sh", ".dll",
    };

    public static bool IsBlocked(string fileName) => BlockedExtensions.Contains(Path.GetExtension(fileName));

    /// <summary>Only a plain file name: no folders, no control characters.</summary>
    public static string CleanName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        name = new string(name.Where(ch => !char.IsControl(ch)).ToArray());
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    public static string CleanContentType(string? contentType)
    {
        var type = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        return type.Length is > 0 and <= StoredFile.ContentTypeMaxLength && type.Contains('/') ? type : "application/octet-stream";
    }
}

/// <summary>Keeps a file the user is about to send. It is linked to a broadcast when the broadcast is created.</summary>
public sealed record UploadFileCommand(string FileName, string? ContentType, byte[] Data) : ICommand<FileDto>;

internal sealed class UploadFileCommandHandler(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
    : ICommandHandler<UploadFileCommand, FileDto>
{
    public async Task<Result<FileDto>> Handle(UploadFileCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return FileErrors.NotSignedIn;
        }

        if (command.Data.Length == 0)
        {
            return FileErrors.Empty;
        }

        if (command.Data.LongLength > StoredFile.MaxBytes)
        {
            return FileErrors.TooLarge;
        }

        var name = FileRules.CleanName(command.FileName);
        if (FileRules.IsBlocked(name))
        {
            return FileErrors.Blocked;
        }

        var file = new StoredFile(userId, name, FileRules.CleanContentType(command.ContentType), command.Data, clock.GetUtcNow());
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync(cancellationToken);
        return FileDto.From(file);
    }
}

/// <summary>The gateway asks for a file's bytes (once per broadcast) to upload it to WhatsApp.</summary>
public sealed record GetFileForGatewayQuery(Guid Id) : IQuery<FileContent>;

internal sealed class GetFileForGatewayQueryHandler(IAppDbContext db) : IQueryHandler<GetFileForGatewayQuery, FileContent>
{
    public async Task<Result<FileContent>> Handle(GetFileForGatewayQuery query, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == query.Id && f.Data != null)
            .Select(f => new FileContent(f.FileName, f.ContentType, f.Data!))
            .SingleOrDefaultAsync(cancellationToken);
        return file is null ? FileErrors.NotFound : file;
    }
}

/// <summary>
/// Frees the bytes of files that are no longer needed: attached only to finished or cancelled broadcasts, or never
/// attached and older than a day. The name and size stay for the history.
/// </summary>
internal static class FileCleanup
{
    public static async Task<int> RunAsync(IAppDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var unusedBefore = now.AddDays(-1);
        return await db.StoredFiles
            .Where(f => f.Data != null &&
                !db.Broadcasts.Any(b => b.AttachmentId == f.Id && (b.Status == BroadcastStatus.Sending || b.Status == BroadcastStatus.Paused)) &&
                (db.Broadcasts.Any(b => b.AttachmentId == f.Id) || f.CreatedAt < unusedBefore))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Data, (byte[]?)null).SetProperty(f => f.DeletedAt, (DateTimeOffset?)now), cancellationToken);
    }
}
