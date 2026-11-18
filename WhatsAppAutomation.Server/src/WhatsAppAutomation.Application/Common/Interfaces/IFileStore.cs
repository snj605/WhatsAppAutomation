namespace WhatsAppAutomation.Application.Common.Interfaces;

/// <summary>Where the bytes of stored files are kept (a folder on the server). Files are named by their id.</summary>
public interface IFileStore
{
    /// <summary>
    /// Copies the content to the store, at most <paramref name="maxBytes"/>. Returns the size and the SHA-256 (hex),
    /// or null when the content is larger (nothing is kept then).
    /// </summary>
    Task<(long Size, string Sha256)?> SaveAsync(Guid id, Stream content, long maxBytes, CancellationToken cancellationToken);

    /// <summary>The file to read, or null when it is not there.</summary>
    Stream? OpenRead(Guid id);

    void Delete(Guid id);
}

/// <summary>Downloads a file the user shares by link ("Anyone with the link" on Google Drive) into the file store.</summary>
public interface ILinkedFileFetcher
{
    /// <summary>Returns the file name, content type, size and checksum, or an error code and message for the user.</summary>
    Task<LinkedFileResult> FetchAsync(Guid id, string url, long maxBytes, CancellationToken cancellationToken);
}

public sealed record LinkedFileResult(string? FileName, string? ContentType, long Size, string? Sha256, string? ErrorCode, string? ErrorMessage)
{
    public bool Succeeded => ErrorCode is null;
}
