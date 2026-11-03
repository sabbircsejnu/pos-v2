using RetailPOS.Infrastructure.ConfidentialFiles;

namespace RetailPOS.IntegrationTests.Audit;

public sealed class PrivateFileSystemBlobStoreTests
{
    [Fact]
    public async Task PrivateCiphertextIsOpaqueBusinessBoundAndOwnedCleanupIsTargeted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"retailpos-private-test-{Guid.NewGuid():N}");
        var store = new PrivateFileSystemBlobStore(new(root, AppContext.BaseDirectory));
        string? key = null;
        try
        {
            key = await store.StageAsync(1, new byte[] { 1, 2, 3 }, CancellationToken.None);
            Assert.DoesNotContain("upload", key);
            await using (var stream = await store.OpenAsync(1, key, CancellationToken.None))
            {
                Assert.Equal(3, stream.Length);
            }
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.OpenAsync(2, key, CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.OpenAsync(1, "..\\secret", CancellationToken.None));
            await store.DeleteOwnedStageAsync(1, key, CancellationToken.None);
            Assert.Empty(Directory.GetFiles(root));
        }
        finally
        {
            if (key is not null) await store.DeleteOwnedStageAsync(1, key, CancellationToken.None);
            Directory.Delete(root, recursive: false);
        }
    }

    [Fact]
    public void PublicOrImplicitRootIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PrivateFileSystemBlobStore(new(null, AppContext.BaseDirectory)));
        Assert.Throws<InvalidOperationException>(() => new PrivateFileSystemBlobStore(
            new(Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads", "hr"), AppContext.BaseDirectory)));
    }
}
