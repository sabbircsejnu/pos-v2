using RetailPOS.Application.AccountingJournals.Abstractions;

namespace RetailPOS.Infrastructure.AccountingJournals;

public sealed class JournalAttachmentFileStore(JournalAttachmentStorageOptions options)
    : IJournalAttachmentFileStore
{
    public async Task<string> SaveAsync(
        long businessId,
        long journalEntryId,
        string storedFileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        var relativeDirectory = Path.Combine(
            "uploads",
            "journals",
            businessId.ToString(),
            journalEntryId.ToString());
        var physicalDirectory = Path.Combine(
            options.ContentRootPath,
            "wwwroot",
            relativeDirectory);
        Directory.CreateDirectory(physicalDirectory);

        var relativePath = "/" + Path.Combine(relativeDirectory, storedFileName).Replace("\\", "/");
        var physicalPath = ResolvePath(relativePath);
        await using (var output = File.Create(physicalPath))
        {
            await content.CopyToAsync(output, cancellationToken);
        }

        return relativePath;
    }

    public bool Exists(string relativePath) => File.Exists(ResolvePath(relativePath));

    public Task<byte[]> ReadAllBytesAsync(
        string relativePath,
        CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(ResolvePath(relativePath), cancellationToken);

    public void DeleteIfExists(string relativePath)
    {
        var physicalPath = ResolvePath(relativePath);
        if (File.Exists(physicalPath))
            File.Delete(physicalPath);
    }

    private string ResolvePath(string relativePath) =>
        Path.Combine(
            options.ContentRootPath,
            "wwwroot",
            relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
}
