using RetailPOS.Application.ConfidentialFiles;

namespace RetailPOS.Infrastructure.ConfidentialFiles;

public sealed record PrivateFileSystemOptions(string? RootPath, string ContentRootPath);

// Not registered by default: provider/root/OS ACL/key/scanner decisions must be approved before composition.
public sealed class PrivateFileSystemBlobStore : IPrivateBlobStore
{
    private readonly string root;

    public PrivateFileSystemBlobStore(PrivateFileSystemOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RootPath) || !Path.IsPathFullyQualified(options.RootPath))
            throw new InvalidOperationException("An explicit absolute private root is required.");
        root = Path.GetFullPath(options.RootPath);
        var publicRoot = Path.GetFullPath(options.ContentRootPath).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if ((root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .StartsWith(publicRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Private files cannot live beneath the application content/public root.");
        Directory.CreateDirectory(root);
        EnsureNoLinks(root);
    }

    public async Task<string> StageAsync(long businessId, ReadOnlyMemory<byte> ciphertext, CancellationToken cancellationToken)
    {
        if (businessId <= 0 || ciphertext.IsEmpty) throw new ArgumentException("Scoped nonempty ciphertext is required.");
        var key = $"{businessId}-{Guid.NewGuid():N}";
        var path = Resolve(businessId, key);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await file.WriteAsync(ciphertext, cancellationToken);
        await file.FlushAsync(cancellationToken);
        return key;
    }

    public Task<Stream> OpenAsync(long businessId, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(businessId, key);
        EnsureNoLinks(path);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous));
    }

    public Task DeleteOwnedStageAsync(long businessId, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(businessId, key);
        if (File.Exists(path)) { EnsureNoLinks(path); File.Delete(path); }
        return Task.CompletedTask;
    }

    private string Resolve(long businessId, string key)
    {
        var prefix = $"{businessId}-";
        if (businessId <= 0 || !key.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(key[prefix.Length..], "N", out _))
            throw new UnauthorizedAccessException("Private key is not owned by the validated Business.");
        EnsureNoLinks(root);
        return Path.Combine(root, key + ".private");
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = new FileInfo(path); current is not null; current = current.Directory is { } directory
            ? new FileInfo(directory.FullName) : null)
            if (File.Exists(current.FullName) || Directory.Exists(current.FullName))
                if ((File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Private storage cannot traverse symbolic links or junctions.");
    }
}
