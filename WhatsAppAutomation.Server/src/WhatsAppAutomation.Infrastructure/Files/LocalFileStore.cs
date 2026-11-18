using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;

namespace WhatsAppAutomation.Infrastructure.Files;

public sealed class FileStoreOptions
{
    public const string SectionName = "Files";

    /// <summary>Folder for stored files. A relative path is under the app's folder. Back it up with the database.</summary>
    public string Path { get; set; } = "App_Data/files";
}

/// <summary>Keeps files in a folder, named by id, written to a temporary name first so a cut-off copy is never used.</summary>
internal sealed class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(IOptions<FileStoreOptions> options)
    {
        _root = System.IO.Path.GetFullPath(options.Value.Path, AppContext.BaseDirectory);
        Directory.CreateDirectory(_root);
    }

    public async Task<(long Size, string Sha256)?> SaveAsync(Guid id, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        var final = PathOf(id);
        var temp = final + ".part";
        long size = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    size += read;
                    if (size > maxBytes)
                    {
                        output.Close();
                        File.Delete(temp);
                        return null;
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            File.Move(temp, final, overwrite: true);
            return (size, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw;
        }
    }

    public Stream? OpenRead(Guid id)
    {
        var path = PathOf(id);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
    }

    public void Delete(Guid id)
    {
        var path = PathOf(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    // The id is a Guid, so the name can never leave the folder.
    private string PathOf(Guid id) => System.IO.Path.Combine(_root, id.ToString("N"));
}
