namespace WhatsAppAutomation.Domain.Entities;

/// <summary>
/// A file a user attached to a broadcast. One copy is kept per broadcast (never one per person): the WhatsApp
/// gateway uploads it to WhatsApp once and reuses that upload for every recipient. The bytes are removed as soon
/// as no unfinished broadcast needs them; the name and size stay as history.
/// </summary>
public sealed class StoredFile
{
    public const long MaxBytes = 16L * 1024 * 1024;
    public const int FileNameMaxLength = 200;
    public const int ContentTypeMaxLength = 120;

    private StoredFile()
    {
    }

    public StoredFile(Guid userId, string fileName, string contentType, byte[] data, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        FileName = fileName.Length <= FileNameMaxLength ? fileName : fileName[^FileNameMaxLength..];
        ContentType = contentType;
        SizeBytes = data.LongLength;
        Data = data;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>The file itself; null once it is no longer needed.</summary>
    public byte[]? Data { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }
}
