using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Files;

/// <param name="IsBigVideo">A video above WhatsApp's 100 MB video size: it can only go as a document (a file to download).</param>
public sealed record FileDto(Guid Id, string FileName, string ContentType, long SizeBytes, bool IsBigVideo)
{
    public static FileDto From(StoredFile f) => new(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.IsBigVideo);
}

/// <summary>The bytes of a file, for the gateway only. The caller disposes the stream.</summary>
public sealed record FileContent(string FileName, string ContentType, Stream Data);

public static class FileErrors
{
    public static readonly Error NotSignedIn = new("files.not_signed_in", "Sign in first.", ErrorKind.Unauthorized);

    public static readonly Error Empty = new("files.empty", "The file is empty.", ErrorKind.Validation);

    public static readonly Error TooLarge = new(
        "files.too_large", $"The file is larger than {StoredFile.MaxBytes / 1024 / 1024} MB.", ErrorKind.Validation);

    public static readonly Error Blocked = new(
        "files.blocked", "Programs and scripts (.exe, .apk, .bat…) cannot be sent. Send a document, photo or video instead.", ErrorKind.Validation);

    public static readonly Error NotFound = new("files.not_found", "This file does not exist or is no longer kept.", ErrorKind.NotFound);
}

internal static class FileRules
{
    /// <summary>
    /// Files that run code. WhatsApp blocks several of them anyway, and sending them in bulk would look like malware.
    /// Everything else (documents, photos, videos, audio, archives) is allowed.
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

    /// <summary>A file the user may send: their own, or one attached to a template they may use.</summary>
    public static async Task<StoredFile?> UsableAsync(IAppDbContext db, Guid userId, Guid? tenantId, Guid fileId, CancellationToken cancellationToken) =>
        await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(
            f => f.Id == fileId && (f.OnDisk || f.Data != null) &&
                (f.UserId == userId || db.MessageTemplates.Any(t => t.AttachmentId == f.Id &&
                    (t.UserId == userId || (t.IsShared && tenantId != null && t.TenantId == tenantId)))),
            cancellationToken);
}

/// <summary>Keeps a file the user is about to send. The content is streamed to the file store, never held whole.</summary>
public sealed record UploadFileCommand(string FileName, string? ContentType, Stream Content) : ICommand<FileDto>;

internal sealed class UploadFileCommandHandler(IAppDbContext db, ICurrentUser currentUser, IFileStore store, TimeProvider clock)
    : ICommandHandler<UploadFileCommand, FileDto>
{
    public async Task<Result<FileDto>> Handle(UploadFileCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return FileErrors.NotSignedIn;
        }

        var name = FileRules.CleanName(command.FileName);
        if (FileRules.IsBlocked(name))
        {
            return FileErrors.Blocked;
        }

        var id = Guid.NewGuid();
        var saved = await store.SaveAsync(id, command.Content, StoredFile.MaxBytes, cancellationToken);
        if (saved is not { } info)
        {
            return FileErrors.TooLarge;
        }

        if (info.Size == 0)
        {
            store.Delete(id);
            return FileErrors.Empty;
        }

        var file = new StoredFile(id, userId, name, FileRules.CleanContentType(command.ContentType), info.Size, info.Sha256, clock.GetUtcNow());
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync(cancellationToken);
        return FileDto.From(file);
    }
}

/// <summary>
/// Keeps a copy of a file shared by link (Google Drive, "Anyone with the link"). The copy taken now is what is sent,
/// even if the file changes in Drive later.
/// </summary>
public sealed record ImportFileFromLinkCommand(string Url) : ICommand<FileDto>;

internal sealed class ImportFileFromLinkCommandValidator : AbstractValidator<ImportFileFromLinkCommand>
{
    public ImportFileFromLinkCommandValidator() =>
        RuleFor(c => c.Url).NotEmpty().MaximumLength(2000)
            .Must(u => Uri.TryCreate(u?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage("Paste the full https link of the file.");
}

internal sealed class ImportFileFromLinkCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, ILinkedFileFetcher fetcher, IFileStore store, TimeProvider clock)
    : ICommandHandler<ImportFileFromLinkCommand, FileDto>
{
    public async Task<Result<FileDto>> Handle(ImportFileFromLinkCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return FileErrors.NotSignedIn;
        }

        var id = Guid.NewGuid();
        var fetched = await fetcher.FetchAsync(id, command.Url.Trim(), StoredFile.MaxBytes, cancellationToken);
        if (!fetched.Succeeded)
        {
            return new Error(fetched.ErrorCode!, fetched.ErrorMessage!, ErrorKind.Validation);
        }

        var name = FileRules.CleanName(fetched.FileName ?? "file");
        if (FileRules.IsBlocked(name))
        {
            store.Delete(id);
            return FileErrors.Blocked;
        }

        var file = new StoredFile(id, userId, name, FileRules.CleanContentType(fetched.ContentType), fetched.Size, fetched.Sha256!, clock.GetUtcNow());
        db.StoredFiles.Add(file);
        await db.SaveChangesAsync(cancellationToken);
        return FileDto.From(file);
    }
}

/// <summary>The gateway asks for a file's bytes (once per broadcast) to upload it to WhatsApp.</summary>
public sealed record GetFileForGatewayQuery(Guid Id) : IQuery<FileContent>;

internal sealed class GetFileForGatewayQueryHandler(IAppDbContext db, IFileStore store) : IQueryHandler<GetFileForGatewayQuery, FileContent>
{
    public async Task<Result<FileContent>> Handle(GetFileForGatewayQuery query, CancellationToken cancellationToken)
    {
        var file = await db.StoredFiles.AsNoTracking()
            .Where(f => f.Id == query.Id && (f.OnDisk || f.Data != null))
            .Select(f => new { f.FileName, f.ContentType, f.OnDisk, f.Data })
            .SingleOrDefaultAsync(cancellationToken);
        if (file is null)
        {
            return FileErrors.NotFound;
        }

        var stream = file.OnDisk ? store.OpenRead(query.Id) : new MemoryStream(file.Data!, writable: false);
        return stream is null ? FileErrors.NotFound : new FileContent(file.FileName, file.ContentType, stream);
    }
}

/// <summary>
/// Frees the bytes of files that are no longer needed: not on a template, and attached only to finished or
/// cancelled broadcasts, or never attached and older than a day. The name and size stay for the history.
/// </summary>
internal static class FileCleanup
{
    public static async Task<int> RunAsync(IAppDbContext db, IFileStore store, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var unusedBefore = now.AddDays(-1);
        var unused = db.StoredFiles.Where(f => (f.OnDisk || f.Data != null) &&
            !db.MessageTemplates.Any(t => t.AttachmentId == f.Id) &&
            !db.Broadcasts.Any(b => b.AttachmentId == f.Id && (b.Status == BroadcastStatus.Sending || b.Status == BroadcastStatus.Paused)) &&
            (db.Broadcasts.Any(b => b.AttachmentId == f.Id) || f.CreatedAt < unusedBefore));
        var onDisk = await unused.Where(f => f.OnDisk).Select(f => f.Id).Take(500).ToListAsync(cancellationToken);
        foreach (var id in onDisk)
        {
            store.Delete(id);
        }

        return await unused.Where(f => !f.OnDisk || onDisk.Contains(f.Id))
            .ExecuteUpdateAsync(
                s => s.SetProperty(f => f.Data, (byte[]?)null).SetProperty(f => f.OnDisk, false).SetProperty(f => f.DeletedAt, (DateTimeOffset?)now),
                cancellationToken);
    }
}

/// <summary>
/// Whether the signed-in user may look at a file (their own, or a file of a template they can use), for the
/// WhatsApp-style preview. Returns the file's details; the API then gives a short-lived link to its content.
/// </summary>
public sealed record GetFileViewQuery(Guid Id) : IQuery<FileDto>;

internal sealed class GetFileViewQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetFileViewQuery, FileDto>
{
    public async Task<Result<FileDto>> Handle(GetFileViewQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return FileErrors.NotSignedIn;
        }

        var tenantId = await db.UserTenantAsync(userId, cancellationToken);
        var file = await FileRules.UsableAsync(db, userId, tenantId, query.Id, cancellationToken);
        return file is null ? FileErrors.NotFound : FileDto.From(file);
    }
}
