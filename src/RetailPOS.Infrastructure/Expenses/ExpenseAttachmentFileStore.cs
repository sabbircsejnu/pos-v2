using Microsoft.Extensions.Options;
using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.Infrastructure.Expenses;

/// <summary>Private on-disk storage; files are reachable only through the authorized attachment endpoints.</summary>
public sealed class ExpenseAttachmentFileStore(IOptions<ExpenseAttachmentOptions> options) : IExpenseAttachmentFileStore
{
    private readonly string _root = Path.GetFullPath(options.Value.StorageRoot);

    public async Task<string> SaveAsync(
        long businessId,
        long expenseId,
        string extension,
        Stream content,
        CancellationToken cancellationToken)
    {
        var storageKey = $"{businessId}/{expenseId}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var physicalPath = ResolvePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        try
        {
            await using var output = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await content.CopyToAsync(output, cancellationToken);
        }
        catch
        {
            if (File.Exists(physicalPath))
                File.Delete(physicalPath);
            throw;
        }

        return storageKey;
    }

    public bool Exists(string storageKey) => File.Exists(ResolvePath(storageKey));

    public Task<byte[]> ReadAllBytesAsync(string storageKey, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(ResolvePath(storageKey), cancellationToken);

    public void DeleteIfExists(string storageKey)
    {
        var physicalPath = ResolvePath(storageKey);
        if (File.Exists(physicalPath))
            File.Delete(physicalPath);
    }

    private string ResolvePath(string storageKey)
    {
        var physicalPath = Path.GetFullPath(Path.Combine(_root, storageKey));
        if (!physicalPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid attachment storage key.");
        return physicalPath;
    }
}
