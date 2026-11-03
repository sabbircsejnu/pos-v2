using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public sealed class JournalAttachmentStoreRelationalTests
{
    [Fact]
    public async Task AttachmentLifecycle_UsesRelationalMetadataAndCompensatesFailedUpload()
    {
        var contentRootPath = Path.Combine(
            Path.GetTempPath(),
            $"RetailPOS-journal-attachment-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRootPath);

        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseSqlite(connection)
                .Options;

            var audit = new Mock<IAuditService>();
            audit.Setup(service => service.RecordAsync(
                    It.IsAny<AuditEventInput>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            await using var dbContext = new RetailPOSDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Businesses.Add(new Business
            {
                Id = 1,
                Name = "Attachment Relational Business",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            dbContext.AccountingConfigurations.Add(new AccountingConfiguration
            {
                BusinessId = 1,
                ManualJournalAttachmentMaxBytes = 1024,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            dbContext.JournalEntries.Add(new JournalEntry
            {
                Id = 10,
                BusinessId = 1,
                JournalType = "Manual",
                Status = JournalEntryStatuses.Draft,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();

            var files = new JournalAttachmentFileStore(
                new JournalAttachmentStorageOptions(contentRootPath));
            var store = new JournalAttachmentStore(dbContext, files, audit.Object);
            var tenantScope = new TestJournalAttachmentTenantScope(1);
            var uploadHandler = new UploadJournalAttachmentCommandHandler(store, tenantScope);
            var listHandler = new GetJournalAttachmentsQueryHandler(store, tenantScope);
            var downloadHandler = new DownloadJournalAttachmentQueryHandler(store, tenantScope);
            var deleteHandler = new DeleteJournalAttachmentCommandHandler(store, tenantScope);
            var originalContent = "relational attachment"u8.ToArray();

            using var uploadStream = new MemoryStream(originalContent);
            var uploaded = await uploadHandler.Handle(
                new UploadJournalAttachmentCommand(
                    10,
                    uploadStream,
                    "evidence.pdf",
                    "application/pdf",
                    originalContent.Length,
                    901),
                CancellationToken.None);

            Assert.Equal("evidence.pdf", uploaded.FileName);
            Assert.Equal("application/pdf", uploaded.ContentType);
            Assert.Equal((long)originalContent.Length, uploaded.FileSizeBytes);
            Assert.Equal(901, uploaded.CreatedBy);

            var listed = await listHandler.Handle(
                new GetJournalAttachmentsQuery(10),
                CancellationToken.None);
            Assert.Single(listed);

            var downloaded = await downloadHandler.Handle(
                new DownloadJournalAttachmentQuery(10, uploaded.Id),
                CancellationToken.None);
            Assert.Equal(originalContent, downloaded.Content);

            var attachment = await dbContext.JournalEntryAttachments
                .SingleAsync(item => item.Id == uploaded.Id);
            files.DeleteIfExists(attachment.RelativePath);
            await Assert.ThrowsAsync<FileNotFoundException>(() => downloadHandler.Handle(
                new DownloadJournalAttachmentQuery(10, uploaded.Id),
                CancellationToken.None));
            Assert.Equal(2, audit.Invocations.Count(invocation =>
                invocation.Method.Name == nameof(IAuditService.RecordAsync)));
            using (var restoredContent = new MemoryStream(originalContent))
            {
                await files.SaveAsync(
                    1,
                    10,
                    attachment.StoredFileName,
                    restoredContent,
                    CancellationToken.None);
            }

            var tableName = dbContext.Model
                .FindEntityType(typeof(JournalEntryAttachment))!
                .GetTableName()!;
            await dbContext.Database.ExecuteSqlRawAsync(
                $"CREATE TRIGGER reject_journal_attachment BEFORE INSERT ON \"{tableName}\" " +
                "BEGIN SELECT RAISE(ABORT, 'forced attachment insert failure'); END;");

            using var rejectedStream = new MemoryStream("rollback"u8.ToArray());
            await Assert.ThrowsAsync<DbUpdateException>(() => uploadHandler.Handle(
                new UploadJournalAttachmentCommand(
                    10,
                    rejectedStream,
                    "rollback.pdf",
                    "application/pdf",
                    rejectedStream.Length,
                    901),
                CancellationToken.None));

            Assert.Equal(1, await dbContext.JournalEntryAttachments.CountAsync());
            var storedFiles = Directory.GetFiles(
                Path.Combine(contentRootPath, "wwwroot", "uploads", "journals", "1", "10"));
            Assert.Single(storedFiles);
            Assert.EndsWith(attachment.StoredFileName, storedFiles[0], StringComparison.Ordinal);
            foreach (var failedInsert in dbContext.ChangeTracker
                         .Entries<JournalEntryAttachment>()
                         .Where(entry => entry.State == EntityState.Added)
                         .ToList())
            {
                failedInsert.State = EntityState.Detached;
            }

            await dbContext.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_journal_attachment;");
            await deleteHandler.Handle(
                new DeleteJournalAttachmentCommand(10, uploaded.Id),
                CancellationToken.None);

            Assert.Empty(await dbContext.JournalEntryAttachments.ToListAsync());
            Assert.Empty(Directory.GetFiles(
                Path.Combine(contentRootPath, "wwwroot", "uploads", "journals", "1", "10")));
            Assert.Equal(3, audit.Invocations.Count(invocation =>
                invocation.Method.Name == nameof(IAuditService.RecordAsync)));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                store.GetAttachmentsAsync(2, 10, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(contentRootPath))
                Directory.Delete(contentRootPath, recursive: true);
        }
    }

    private sealed class TestJournalAttachmentTenantScope(long businessId)
        : IJournalAttachmentTenantScope
    {
        public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(businessId);
        }
    }
}
