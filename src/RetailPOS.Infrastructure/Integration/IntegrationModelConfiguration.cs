using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.Integration;

namespace RetailPOS.Infrastructure.Integration;

internal static class IntegrationModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<IntegrationOutboxMessage>(entity =>
        {
            entity.ToTable("integration_outbox_messages");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Producer).HasMaxLength(100);
            entity.Property(row => row.MessageType).HasMaxLength(200);
            entity.Property(row => row.AggregateType).HasMaxLength(100);
            entity.Property(row => row.AggregateId).HasMaxLength(150);
            entity.Property(row => row.PayloadHash).HasMaxLength(64);
            entity.Property(row => row.EnvelopeHash).HasMaxLength(64);
            entity.Property(row => row.Status).HasMaxLength(30);
            entity.Property(row => row.LastErrorCode).HasMaxLength(100);
            entity.HasIndex(row => new { row.Status, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc });
            entity.HasIndex(row => new { row.BusinessId, row.AggregateType, row.AggregateId });
        });
        model.Entity<IntegrationInboxMessage>(entity =>
        {
            entity.ToTable("integration_inbox_messages");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.ConsumerName).HasMaxLength(100);
            entity.Property(row => row.MessageType).HasMaxLength(200);
            entity.Property(row => row.PayloadHash).HasMaxLength(64);
            entity.Property(row => row.EnvelopeHash).HasMaxLength(64);
            entity.Property(row => row.EffectReference).HasMaxLength(150);
            entity.HasIndex(row => new { row.BusinessId, row.ConsumerName, row.MessageId }).IsUnique();
        });
    }
}
