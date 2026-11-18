namespace WhatsAppAutomation.Domain.Entities;

/// <summary>
/// A file a user attached to a broadcast or a template. One copy is kept (never one per person): the WhatsApp
/// gateway uploads it to WhatsApp once and reuses that upload for every recipient. The bytes are removed as soon
/// as no unfinished broadcast or template needs them; the name and size stay as history. New files are kept on
/// disk (see IFileStore); <see cref="Data"/> only holds files saved by older versions.
/// </summary>
public sealed class StoredFile
{
    /// <summary>Largest file kept: big enough for long videos, which then go as documents.</summary>
    public const long MaxBytes = 512L * 1024 * 1024;

    /// <summary>WhatsApp's limit for a video shown as a video; a larger one can only go as a document.</summary>
    public const long MaxVideoBytes = 100L * 1024 * 1024;
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

    /// <summary>A file kept on disk under its id.</summary>
    public StoredFile(Guid id, Guid userId, string fileName, string contentType, long sizeBytes, string sha256, DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        FileName = fileName.Length <= FileNameMaxLength ? fileName : fileName[^FileNameMaxLength..];
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        OnDisk = true;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The bytes are in the file store, not in <see cref="Data"/>. False once freed.</summary>
    public bool OnDisk { get; private set; }

    /// <summary>Checksum of the bytes (hex), to check the file was not changed.</summary>
    public string? Sha256 { get; private set; }

    public bool IsKept => OnDisk || Data != null;

    public bool IsBigVideo => ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) && SizeBytes > MaxVideoBytes;

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>The file itself; null once it is no longer needed.</summary>
    public byte[]? Data { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }
}
