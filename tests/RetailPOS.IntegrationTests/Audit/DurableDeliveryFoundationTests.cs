using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Outlets;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Integration;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Organisation;
using RetailPOS.Infrastructure.Integration;
using RetailPOS.Infrastructure.Outlets;
using RetailPOS.Infrastructure.Warehouses;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.Audit;

public sealed class DurableDeliveryFoundationTests
{
    [Fact]
    public Task SourceOutboxAndAuditCommitTogether_AndFailureRollsBack() => WithDatabase(async state =>
    {
        state.Audit.Initialize(AuditContext.ForBusinessSystemJob("delivery-test", 1));
        var envelope = Envelope();
        var outbox = new IntegrationOutbox(state.Db, state.Audit, TimeProvider.System,
            new DisabledIntegrationReplayAuthorization());
        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.AppendAsync(envelope, default));
        await Assert.ThrowsAsync<IOException>(() => state.Transaction.ExecuteAsync(Operation(), async token =>
        {
            state.Db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
                { BusinessId = 1, FeatureKey = "test.delivery", IsEnabled = true });
            await outbox.AppendAsync(envelope, token);
            await state.Db.SaveChangesAsync(token);
            throw new IOException("Injected precommit crash.");
#pragma warning disable CS0162
            return true;
#pragma warning restore CS0162
        }, default));
        Assert.Equal(0, await state.Db.Set<IntegrationOutboxMessage>().CountAsync());
        Assert.Equal(0, await state.Db.AuditEvents.CountAsync());
        Assert.Equal(0, await state.Db.BusinessFeatureSettings.CountAsync());
        await state.Transaction.ExecuteAsync(Operation(), async token =>
        {
            state.Db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
                { BusinessId = 1, FeatureKey = "test.delivery", IsEnabled = true });
            await outbox.AppendAsync(envelope, token);
            await outbox.AppendAsync(envelope, token);
            return true;
        }, default);
        Assert.Equal(1, await state.Db.Set<IntegrationOutboxMessage>().CountAsync());
        Assert.Equal(1, await state.Db.AuditEvents.CountAsync());
        Assert.Equal(1, await state.Db.BusinessFeatureSettings.CountAsync());
    });

    [Theory]
    [InlineData("success", "Published")]
    [InlineData("unroutable", "Pending")]
    [InlineData("nack", "Pending")]
    [InlineData("broker_down", "Pending")]
    [InlineData("poison", "DeadLetter")]
    [InlineData("exhausted", "DeadLetter")]
    public Task PublisherConfirmFailureRetryAndPoisonAreDurable(string outcome, string expected) => WithDatabase(async state =>
    {
        var row = Row();
        state.Db.Add(row); await state.Db.SaveChangesAsync(); state.Db.ChangeTracker.Clear();
        var publisher = new Mock<IConfirmedIntegrationPublisher>();
        if (outcome == "poison") publisher.Setup(p => p.PublishAsync(It.IsAny<IntegrationEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IntegrationPoisonException("poison"));
        else if (outcome != "success") publisher.Setup(p => p.PublishAsync(It.IsAny<IntegrationEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException(outcome));
        var dispatcher = new OutboxDispatcher(state.Db, publisher.Object,
            new DurableDeliveryOptions(MaxAttempts: outcome == "exhausted" ? 1 : 3),
            TimeProvider.System, NullLogger<OutboxDispatcher>.Instance);
        Assert.Equal(1, await dispatcher.DispatchBatchAsync(default));
        var saved = await state.Db.Set<IntegrationOutboxMessage>().AsNoTracking().SingleAsync();
        Assert.Equal(expected, saved.Status); Assert.Equal(1, saved.AttemptCount);
        Assert.Null(saved.LeaseOwner);
        Assert.Equal(outcome == "success", saved.PublishedAtUtc.HasValue);
        if (expected == "Pending") Assert.True(saved.NextAttemptAtUtc > DateTime.UtcNow);
    });

    [Fact]
    public Task CrashAfterConfirmBeforeMark_RedeliversOriginalIdentityOnLeaseTakeover() => WithDatabase(async state =>
    {
        var row = Row();
        row.Status = "Leased"; row.AttemptCount = 1; row.LeaseOwner = Guid.NewGuid();
        row.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        state.Db.Add(row); await state.Db.SaveChangesAsync(); state.Db.ChangeTracker.Clear();
        var publisher = new Mock<IConfirmedIntegrationPublisher>();
        publisher.Setup(p => p.PublishAsync(It.Is<IntegrationEnvelope>(e => e.MessageId == row.Id && e.PayloadHash == row.PayloadHash),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await new OutboxDispatcher(state.Db, publisher.Object, new(), TimeProvider.System,
            NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(default);
        publisher.Verify(p => p.PublishAsync(It.Is<IntegrationEnvelope>(e => e.MessageId == row.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        var saved = await state.Db.Set<IntegrationOutboxMessage>().AsNoTracking().SingleAsync();
        Assert.Equal("Published", saved.Status); Assert.Equal(2, saved.AttemptCount);
    });

    [Fact]
    public Task InboxDuplicatesRepeatSuccess_ConflictAndFailureLeaveNoPartialEffect() => WithDatabase(async state =>
    {
        var envelope = Envelope();
        var inbox = new IntegrationInbox(state.Db, state.Audit, state.Transaction, TimeProvider.System);
        var consumer = new TestConsumer(state.Db);
        consumer.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => inbox.ConsumeAsync(envelope, consumer, default));
        Assert.Empty(await state.Db.Set<IntegrationInboxMessage>().ToListAsync());
        Assert.Empty(await state.Db.BusinessFeatureSettings.ToListAsync());
        consumer.Fail = false;
        Assert.Equal("test.delivery", await inbox.ConsumeAsync(envelope, consumer, default));
        Assert.Equal("test.delivery", await inbox.ConsumeAsync(envelope, consumer, default));
        Assert.Equal(1, await state.Db.Set<IntegrationInboxMessage>().CountAsync());
        Assert.Equal(1, await state.Db.BusinessFeatureSettings.CountAsync());
        Assert.Equal(1, consumer.Successes);
        var payload = "{\"businessId\":1,\"version\":2}";
        await Assert.ThrowsAsync<IntegrationConflictException>(() => inbox.ConsumeAsync(
            envelope with { PayloadJson = payload, PayloadHash = IntegrationEnvelope.Hash(payload) }, consumer, default));
        Assert.Equal(1, consumer.Successes);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inbox.ConsumeAsync(
            Envelope(2), consumer, default));
    });

    [Fact]
    public Task OutletSourceMutationAndOutboxEventCommitTogether_AndAppendFailureRollsBack()
        => WithDatabase(async state =>
        {
            var outbox = new IntegrationOutbox(state.Db, state.Audit, TimeProvider.System,
                new DisabledIntegrationReplayAuthorization());
            var writer = new OperationalLocationSourceEventWriter(outbox);
            var store = new OutletWriteStore(state.Db, writer,
                NullLogger<OutletWriteStore>.Instance);

            var outlet = await store.CreateAsync("Main", "1 Main St", null, null, 1, OutletPosSetupInput.None, default);
            var savedEvent = await state.Db.Set<IntegrationOutboxMessage>().SingleAsync();
            Assert.Equal("Outlet", savedEvent.AggregateType);
            Assert.Equal(outlet.Id.ToString(), savedEvent.AggregateId);
            Assert.Equal("operational-location.outlet.created", savedEvent.MessageType);
            Assert.Equal(1, savedEvent.SourceVersion);
            Assert.Contains("\"businessId\":1", savedEvent.PayloadJson, StringComparison.Ordinal);

            var source = await state.Db.Outlets.AsNoTracking().SingleAsync(row => row.Id == outlet.Id);
            var replay = new OperationalLocationSourceEvent(1, "RetailPOS", "Outlet",
                source.Id.ToString(), source.SourceVersion, false, source.Name, source.Address,
                source.ContactNumber, source.ManagerId?.ToString(), source.UpdatedAt);
            var replayTimeUtc = replay.SourceUpdatedAtUtc.Kind switch
            {
                DateTimeKind.Utc => replay.SourceUpdatedAtUtc,
                DateTimeKind.Local => replay.SourceUpdatedAtUtc.ToUniversalTime(),
                _ => DateTime.SpecifyKind(replay.SourceUpdatedAtUtc, DateTimeKind.Utc)
            };
            var normalizedReplay = replay with
            {
                SourceUpdatedAtUtc = new DateTime(replayTimeUtc.Ticks
                    - replayTimeUtc.Ticks % 10, DateTimeKind.Utc)
            };
            Assert.Equal(savedEvent.PayloadJson, JsonSerializer.Serialize(normalizedReplay, LocationPayloadOptions));
            await using (var transaction = await state.Db.Database.BeginTransactionAsync())
            {
                await writer.AppendAsync("OutletCreated", replay, default);
                await writer.AppendAsync("OutletCreated", replay, default);
                await state.Db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            Assert.Equal(1, await state.Db.Set<IntegrationOutboxMessage>().CountAsync());

            var failingWriter = new Mock<IOperationalLocationSourceEventWriter>();
            failingWriter.Setup(x => x.AppendAsync(It.IsAny<string>(),
                    It.IsAny<OperationalLocationSourceEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Injected outbox failure."));
            var failingStore = new OutletWriteStore(state.Db, failingWriter.Object,
                NullLogger<OutletWriteStore>.Instance);
            await Assert.ThrowsAsync<IOException>(() =>
                failingStore.CreateAsync("Rollback", "2 Main St", null, null, 1, OutletPosSetupInput.None, default));
            Assert.Equal(1, await state.Db.Outlets.CountAsync());
            Assert.Equal(1, await state.Db.Set<IntegrationOutboxMessage>().CountAsync());
        });

    [Fact]
    public Task OutletAndWarehouseUpdatesAdvanceVersions_AndUnownedSourcesAreExcluded()
        => WithDatabase(async state =>
        {
            var outbox = new IntegrationOutbox(state.Db, state.Audit, TimeProvider.System,
                new DisabledIntegrationReplayAuthorization());
            var writer = new OperationalLocationSourceEventWriter(outbox);
            var outlets = new OutletWriteStore(state.Db, writer, NullLogger<OutletWriteStore>.Instance);
            var unowned = await outlets.CreateAsync("Unowned", "No Business", null, null, null, OutletPosSetupInput.None, default);
            Assert.Equal(0, await state.Db.Set<IntegrationOutboxMessage>().CountAsync());

            var outlet = await outlets.CreateAsync("Owned", "Outlet address", null, null, 1, OutletPosSetupInput.None, default);
            await outlets.UpdateAsync(outlet.Id, "Owned updated", "Updated address", null, null, 1, OutletPosSetupInput.None, default);
            await outlets.DeleteAsync(outlet.Id, 1, default);

            var warehouses = new WarehouseWriteStore(state.Db, writer);
            var warehouse = await warehouses.CreateAsync("Warehouse", "Warehouse address",
                null, null, 1, default);
            await warehouses.UpdateAsync(warehouse.Id, "Warehouse updated", "Updated address",
                null, null, 1, default);
            await warehouses.DeleteAsync(warehouse.Id, 1, default);

            var events = await state.Db.Set<IntegrationOutboxMessage>().AsNoTracking()
                .OrderBy(row => row.AggregateType).ThenBy(row => row.SourceVersion).ToListAsync();
            Assert.Equal(6, events.Count);
            Assert.Equal(new long[] { 1, 2, 3 },
                events.Where(row => row.AggregateType == "Outlet").Select(row => row.SourceVersion));
            Assert.Equal(new long[] { 1, 2, 3 },
                events.Where(row => row.AggregateType == "Warehouse").Select(row => row.SourceVersion));
            Assert.Equal("operational-location.outlet.deleted",
                events.Single(row => row.AggregateType == "Outlet" && row.SourceVersion == 3).MessageType);
            Assert.Equal("operational-location.warehouse.deleted",
                events.Single(row => row.AggregateType == "Warehouse" && row.SourceVersion == 3).MessageType);
            Assert.NotEqual(unowned.Id.ToString(), events.Single(row =>
                row.AggregateType == "Outlet" && row.SourceVersion == 1).AggregateId);
        });

    [Fact]
    public Task OperationalLocationInboxIsIdempotentRejectsConflictsAndRetainsTombstones()
        => WithDatabase(async state =>
        {
            state.Audit.Initialize(AuditContext.ForBusinessSystemJob("hr-location-test", 1));
            var store = new HrOrganisationStore(state.Db);
            var consumer = new OperationalLocationProjectionConsumer(store);
            var inbox = new IntegrationInbox(state.Db, state.Audit, state.Transaction, TimeProvider.System);
            var initial = LocationEvent(1, "Outlet", "21", 1, false, "Main");
            var created = LocationEnvelope(initial, "created");

            Assert.Equal("Outlet:21:Inserted", await inbox.ConsumeAsync(created, consumer, default));
            Assert.Equal("Outlet:21:Inserted", await inbox.ConsumeAsync(created, consumer, default));
            Assert.Equal(1, await state.Db.OperationalLocationProjections.CountAsync());

            var differentPayload = LocationEnvelope(initial with { Name = "Altered" }, "created") with
            {
                MessageId = created.MessageId
            };
            await Assert.ThrowsAsync<IntegrationConflictException>(() =>
                inbox.ConsumeAsync(differentPayload, consumer, default));

            var deleted = LocationEvent(1, "Outlet", "21", 2, true, "Main");
            await inbox.ConsumeAsync(LocationEnvelope(deleted, "deleted"), consumer, default);
            var stale = LocationEvent(1, "Outlet", "21", 1, false, "Main");
            Assert.Equal("Outlet:21:Stale", await inbox.ConsumeAsync(
                LocationEnvelope(stale, "updated"), consumer, default));
            var projection = await state.Db.OperationalLocationProjections.SingleAsync();
            Assert.True(projection.IsSourceDeleted);
            Assert.False(projection.IsSourceActive);

            var factory = LocationEnvelope(LocationEvent(1, "Factory", "99", 1, false, "Not supported"),
                "created") with { AggregateType = "Factory", MessageType = "operational-location.factory.created" };
            await Assert.ThrowsAsync<IntegrationPoisonException>(() => inbox.ConsumeAsync(factory, consumer, default));
            var crossBusiness = LocationEnvelope(LocationEvent(2, "Outlet", "22", 1, false, "Cross"),
                "created") with { BusinessId = 1 };
            await Assert.ThrowsAsync<InvalidDataException>(() => inbox.ConsumeAsync(crossBusiness, consumer, default));
        });

    [Fact]
    public Task ReconciliationDoesNotInferDeletionFromAnIncompleteSourceSnapshot()
        => WithDatabase(async state =>
        {
            state.Audit.Initialize(AuditContext.ForBusinessSystemJob("hr-location-test", 1));
            var oldProjection = LocationEvent(1, "Outlet", "999", 4, false, "Previously synced");
            state.Db.OperationalLocationProjections.Add(new OperationalLocationProjection
            {
                BusinessId = 1,
                SourceSystem = oldProjection.SourceSystem,
                SourceEntityType = oldProjection.SourceEntityType,
                SourceEntityId = oldProjection.SourceEntityId,
                Name = oldProjection.Name,
                Address = oldProjection.Address,
                SourceUpdatedAtUtc = oldProjection.SourceUpdatedAtUtc,
                SourceVersion = oldProjection.SourceVersion,
                SourcePayloadHash = IntegrationEnvelope.Hash(JsonSerializer.Serialize(oldProjection)),
                LastSyncedAtUtc = DateTime.UtcNow,
                IsSourceActive = true,
                SyncStatus = "Current"
            });
            state.Db.Outlets.Add(new Outlet
            {
                BusinessId = 1,
                Name = "Current source",
                Address = "1 Source St",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await state.Db.SaveChangesAsync();

            var access = new Mock<IHrOrganisationAccess>();
            access.Setup(x => x.RequireBusinessId()).Returns(1);
            access.Setup(x => x.EnsureAsync(1, HrOrganisationOperation.ReconcileOperationalLocations,
                null, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var store = new HrOrganisationStore(state.Db);
            var handler = new ReconcileOperationalLocationsCommandHandler(access.Object, store, store, state.Transaction);
            await handler.Handle(new ReconcileOperationalLocationsCommand(), default);

            var unchanged = await state.Db.OperationalLocationProjections.SingleAsync(
                row => row.SourceEntityId == "999");
            Assert.True(unchanged.IsSourceActive);
            Assert.False(unchanged.IsSourceDeleted);
            Assert.Equal(2, await state.Db.OperationalLocationProjections.CountAsync());
            Assert.True(await state.Db.AuditEvents.AnyAsync());
        });

    [Fact]
    public Task OrganisationUnitWritesAreBusinessScopedVersionedAndAudited()
        => WithDatabase(async state =>
        {
            state.Audit.Initialize(AuditContext.ForBusinessSystemJob("hr-orgunit-test", 1));
            var access = new Mock<IHrOrganisationAccess>();
            access.Setup(x => x.RequireBusinessId()).Returns(1);
            access.Setup(x => x.RequireRealUserId()).Returns(10);
            access.Setup(x => x.EnsureAsync(1, HrOrganisationOperation.ManageOrgUnits,
                null, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var store = new HrOrganisationStore(state.Db);
            var data = new HrOrgUnitWriteData("HQ", "Head Office", "Business", null, null,
                new DateOnly(2026, 1, 1), null, true);
            var created = await new CreateHrOrgUnitCommandHandler(access.Object, store, state.Transaction)
                .Handle(new CreateHrOrgUnitCommand(data), default);
            Assert.Equal(1, created.Version);

            var updated = await new UpdateHrOrgUnitCommandHandler(access.Object, store, state.Transaction)
                .Handle(new UpdateHrOrgUnitCommand(created.Id, created.Version,
                    data with { Name = "Head Office Updated" }), default);
            Assert.Equal(2, updated!.Version);
            Assert.Equal("Head Office Updated", updated.Name);
            Assert.Empty(await store.GetOrgUnitsAsync(2, null,
                new HrOperationalLocationReadScope(2, null), default));
            Assert.Equal(2, await state.Db.AuditEvents.CountAsync());
        });

    [Fact]
    public Task OutletSourceVersionIsAProviderConcurrencyToken()
        => WithDatabase(async state =>
        {
            state.Db.Outlets.Add(new Outlet
            {
                BusinessId = 1,
                Name = "Concurrent Outlet",
                Address = "Before",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await state.Db.SaveChangesAsync();
            var connection = state.Db.Database.GetConnectionString()!;
            var first = CreateState(connection);
            var second = CreateState(connection);
            await using var firstDb = first.Db;
            await using var secondDb = second.Db;
            var firstOutlet = await first.Db.Outlets.SingleAsync();
            var secondOutlet = await second.Db.Outlets.SingleAsync();

            firstOutlet.Name = "First writer";
            firstOutlet.SourceVersion++;
            secondOutlet.Name = "Second writer";
            secondOutlet.SourceVersion++;
            await first.Db.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.Db.SaveChangesAsync());
        });

    [Fact]
    public Task ConcurrentDuplicateConsumersCommitOneEffectAndOneInboxIdentity() => WithDatabase(async state =>
    {
        var connection = state.Db.Database.GetConnectionString()!;
        var first = CreateState(connection);
        var second = CreateState(connection);
        await using var firstDb = first.Db;
        await using var secondDb = second.Db;
        var envelope = Envelope();
        var firstConsumer = new TestConsumer(first.Db);
        var secondConsumer = new TestConsumer(second.Db);
        var results = await Task.WhenAll(
            new IntegrationInbox(first.Db, first.Audit, first.Transaction, TimeProvider.System)
                .ConsumeAsync(envelope, firstConsumer, default),
            new IntegrationInbox(second.Db, second.Audit, second.Transaction, TimeProvider.System)
                .ConsumeAsync(envelope, secondConsumer, default));
        Assert.All(results, result => Assert.Equal("test.delivery", result));
        Assert.Equal(1, firstConsumer.Successes + secondConsumer.Successes);
        Assert.Equal(1, await state.Db.Set<IntegrationInboxMessage>().CountAsync());
        Assert.Equal(1, await state.Db.BusinessFeatureSettings.CountAsync());
    });

    [Fact]
    public Task DeadLetterReplayPreservesOriginalIdentityAndRequiresReasonAudit() => WithDatabase(async state =>
    {
        var row = Row(); row.Status = "DeadLetter"; row.AttemptCount = 10;
        state.Db.Add(row); await state.Db.SaveChangesAsync(); state.Db.ChangeTracker.Clear();
        var authorization = new Mock<IIntegrationReplayAuthorization>();
        authorization.Setup(access => access.DemandAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var outbox = new IntegrationOutbox(state.Db, state.Audit, TimeProvider.System, authorization.Object);
        state.Audit.Initialize(AuditContext.ForBusinessSystemJob("delivery-test", 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Transaction.ExecuteAsync(Operation(), async token =>
        { await outbox.ReplayAsync(1, row.Id, "approved recovery", token); return true; }, default));
        await state.Transaction.ExecuteAsync(new RequiredAuditOperation(1, "replay", "integration",
            "approved recovery", "IntegrationOutboxMessage", row.Id.ToString()), async token =>
        { await outbox.ReplayAsync(1, row.Id, "approved recovery", token); return true; }, default);
        var saved = await state.Db.Set<IntegrationOutboxMessage>().AsNoTracking().SingleAsync();
        Assert.Equal(row.Id, saved.Id); Assert.Equal(row.PayloadHash, saved.PayloadHash);
        Assert.Equal("Pending", saved.Status); Assert.Equal(0, saved.AttemptCount);
        Assert.Equal(1, await state.Db.AuditEvents.CountAsync());
    });

    private static RequiredAuditOperation Operation() => new(1, "update", "integration", "Foundation", "test", "42");
    private static IntegrationEnvelope Envelope(long business = 1)
    {
        var payload = $"{{\"businessId\":{business}}}";
        return new(Guid.NewGuid(), business, "test", "test.changed", 1, "test", "42",
            Guid.NewGuid(), null, DateTime.UtcNow, payload, IntegrationEnvelope.Hash(payload));
    }
    private static readonly JsonSerializerOptions LocationPayloadOptions = new(JsonSerializerDefaults.Web);
    private static OperationalLocationSourceEvent LocationEvent(
        long businessId, string type, string id, long version, bool deleted, string name) =>
        new(businessId, "RetailPOS", type, id, version, deleted, name, "Address",
            null, null, DateTime.UtcNow);
    private static IntegrationEnvelope LocationEnvelope(OperationalLocationSourceEvent source, string action)
    {
        var payload = JsonSerializer.Serialize(source, LocationPayloadOptions);
        return new IntegrationEnvelope(Guid.NewGuid(), source.BusinessId, "RetailPOS",
            $"operational-location.{source.SourceEntityType.ToLowerInvariant()}.{action}", 1,
            source.SourceEntityType, source.SourceEntityId, Guid.NewGuid(), null, DateTime.UtcNow,
            payload, IntegrationEnvelope.Hash(payload), source.SourceVersion);
    }
    private static IntegrationOutboxMessage Row()
    {
        var envelope = Envelope();
        return new() { Id = envelope.MessageId, BusinessId = 1, Producer = envelope.Producer,
            MessageType = envelope.MessageType, SchemaVersion = 1, SourceVersion = 1,
            AggregateType = "test", AggregateId = "42", CorrelationId = envelope.CorrelationId,
            OccurredAtUtc = envelope.OccurredAtUtc, PayloadJson = envelope.PayloadJson,
            PayloadHash = envelope.PayloadHash, EnvelopeHash = envelope.EnvelopeHash,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1) };
    }
    private sealed class TestConsumer(RetailPOSDbContext db) : IIntegrationConsumer
    {
        public string Name => "test-consumer";
        public bool Fail { get; set; }
        public int Successes { get; private set; }
        public void ValidateTrustedEnvelope(IntegrationEnvelope envelope)
        {
            if (envelope.BusinessId != 1 || envelope.Producer != "test" || envelope.MessageType != "test.changed"
                || envelope.SchemaVersion != 1) throw new UnauthorizedAccessException("Untrusted envelope.");
        }
        public async Task<string?> ApplyAsync(IntegrationEnvelope envelope, CancellationToken cancellationToken)
        {
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
                { BusinessId = 1, FeatureKey = "test.delivery", IsEnabled = true });
            await db.SaveChangesAsync(cancellationToken);
            if (Fail) throw new IOException("Injected consumer crash.");
            Successes++; return "test.delivery";
        }
    }
    private sealed record State(RetailPOSDbContext Db, AuditContext Audit, RequiredAuditTransaction Transaction);
    private static State CreateState(string connection)
    {
        var audit = new AuditContext();
        var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connection)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(new AuditChangeTrackerInterceptor(audit, NullLogger<AuditChangeTrackerInterceptor>.Instance)).Options);
        var service = new AuditService(db, audit, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
        return new(db, audit, new RequiredAuditTransaction(db, audit, service, NullLogger<RequiredAuditTransaction>.Instance));
    }
    private static async Task WithDatabase(Func<State, Task> action)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var connection = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection))
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "RetailPOS.API", "appsettings.json"));
            connection = new ConfigurationBuilder().AddJsonFile(path).Build().GetConnectionString("DefaultConnection");
        }
        await PostgresTestDatabase.RunAsync(connection!, PostgresTestDatabasePrefix.MigrationBaseline, async database =>
        {
            var audit = new AuditContext();
            await using var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(database.ConnectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .AddInterceptors(new AuditChangeTrackerInterceptor(audit, NullLogger<AuditChangeTrackerInterceptor>.Instance)).Options);
            await db.Database.MigrateAsync();
            db.Businesses.Add(new Business { Id = 1, Name = "One" }); await db.SaveChangesAsync();
            var service = new AuditService(db, audit, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
            await action(new State(db, audit, new RequiredAuditTransaction(db, audit, service,
                NullLogger<RequiredAuditTransaction>.Instance)));
        });
    }
}
