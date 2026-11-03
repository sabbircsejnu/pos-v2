using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.AuditEvents.Commands;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Audit;

public sealed class AuditEventsHttpTests : IClassFixture<RetailPosApiFactory>
{
    private readonly RetailPosApiFactory _factory;

    public AuditEventsHttpTests(RetailPosApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_AppliesFiltersPaginationAndPreservesResponseShape()
    {
        var correlationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await SeedAsync(correlationId);
        using var client = CreateClient("audit.view");

        var response = await client.GetAsync(
            "/api/audit-events?fromDate=2026-10-01T00:00:00Z"
            + "&toDate=2026-10-02T00:00:00Z"
            + "&userId=88&actionType=update&module=inventory&source=api"
            + "&outletId=11&status=success&page=1&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(1, root.GetProperty("pageSize").GetInt32());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal(1, item.GetProperty("id").GetInt64());
        Assert.Equal(correlationId, item.GetProperty("correlationId").GetGuid());
        Assert.Equal(1, item.GetProperty("affectedEntitiesCount").GetInt32());
        Assert.Equal("Updated product", item.GetProperty("actionSummary").GetString());
    }

    [Fact]
    public async Task List_SearchByCorrelationIdReturnsMatchingEvent()
    {
        var correlationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        await SeedAsync(correlationId);
        using var client = CreateClient("audit.view");

        var response = await client.GetAsync($"/api/audit-events?search={correlationId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(
            correlationId,
            Assert.Single(json.RootElement.GetProperty("items").EnumerateArray())
                .GetProperty("correlationId")
                .GetGuid());
    }

    [Fact]
    public async Task Details_ReturnsNestedChangesAndNotFoundForMissingEvent()
    {
        await SeedAsync(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        using var client = CreateClient("audit.view");

        var response = await client.GetAsync("/api/audit-events/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var details = json.RootElement;
        Assert.Equal(1, details.GetProperty("id").GetInt64());
        Assert.Equal("inventory", Assert.Single(details.GetProperty("modulesInvolved").EnumerateArray()).GetString());
        var entity = Assert.Single(details.GetProperty("affectedEntities").EnumerateArray());
        Assert.Equal(2, entity.GetProperty("fieldsChangedCount").GetInt32());
        var changes = entity.GetProperty("fieldChanges").EnumerateArray().ToArray();
        Assert.Equal(2, changes.Length);
        Assert.Equal("Old", changes[0].GetProperty("oldValue").GetString());
        Assert.Equal("New", changes[0].GetProperty("newValue").GetString());
        Assert.True(changes[1].GetProperty("redacted").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/audit-events/404")).StatusCode);
    }

    [Fact]
    public async Task List_RequiresAuditViewPermission()
    {
        await SeedAsync(Guid.NewGuid());
        using var client = CreateClient(string.Empty);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit-events")).StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsCsvAndSelfAuditsSuccessfulExport()
    {
        var correlationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        await SeedAsync(correlationId);
        using var client = CreateClient("audit.view");

        var response = await client.PostAsJsonAsync("/api/audit-events/export", new
        {
            Search = correlationId.ToString("D"),
            Format = "csv"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("audit-events-", response.Content.Headers.ContentDisposition?.FileName);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("id,created_at,correlation_id,module,source,action_type", csv);
        Assert.Contains(correlationId.ToString("D"), csv);
        Assert.Contains("Updated product", csv);
        Assert.Contains("Product", csv);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Contains(dbContext.AuditEvents,
            auditEvent => string.Equals(
                    auditEvent.ActionType,
                    "export",
                    StringComparison.OrdinalIgnoreCase)
                && auditEvent.ActionSummary.Contains("Exported 1 audit events"));
    }

    [Fact]
    public async Task Export_RequiresAuditViewPermission()
    {
        await SeedAsync(Guid.NewGuid());
        using var client = CreateClient(string.Empty);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/audit-events/export", new { Format = "csv" }))
                .StatusCode);
    }

    [Fact]
    public async Task PosDiscountAudit_RecordsEstablishedMetadataThroughApplicationCommand()
    {
        await SeedAsync(Guid.NewGuid());
        using var client = CreateClient("sales.create");

        var response = await client.PostAsJsonAsync(
            "/api/pos-audit/discount-change",
            new
            {
                Action = "manual-discount",
                FromType = "fixed",
                ToType = "percentage",
                OutletId = 11,
                TerminalId = 22,
                Payload = new { amount = 10 }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var auditEvent = await dbContext.AuditEvents.SingleAsync(
            value => value.ActionSummary == "POS discount mode changed: manual-discount");
        Assert.Equal(AuditActionType.Update, auditEvent.ActionType);
        Assert.Equal(AuditModule.Sales, auditEvent.Module);
        Assert.Equal("POSDiscount", auditEvent.PrimaryEntityType);
        Assert.Equal("22", auditEvent.PrimaryEntityId);
        Assert.Contains("\"fromType\":\"fixed\"", auditEvent.Metadata ?? string.Empty);
        Assert.Contains("\"toType\":\"percentage\"", auditEvent.Metadata ?? string.Empty);
        Assert.Contains("\"userId\":201", auditEvent.Metadata ?? string.Empty);
    }

    [Fact]
    public async Task AuditCommand_PersistsRelatedEntityMetadata()
    {
        await SeedAsync(Guid.NewGuid());
        using var scope = _factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        await sender.Send(new RecordAuditEventCommand(new AuditEventWriteRequest(
            AuditActionType.Create,
            AuditModule.BusinessSetup,
            "Outlet created for business 1",
            "Outlet",
            "12",
            [new AuditEventEntityWriteRequest(
                "Outlet",
                "12",
                AuditOperationType.Insert,
                "{\"businessId\":1}")])));

        var dbContext = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var auditEvent = await dbContext.AuditEvents
            .Include(value => value.Entities)
            .SingleAsync(value => value.ActionSummary == "Outlet created for business 1");
        Assert.Equal("Outlet", auditEvent.PrimaryEntityType);
        Assert.Equal("12", auditEvent.PrimaryEntityId);
        var entity = Assert.Single(auditEvent.Entities);
        Assert.Equal(AuditOperationType.Insert, entity.OperationType);
        Assert.Equal("{\"businessId\":1}", entity.Metadata);
    }

    private HttpClient CreateClient(string permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-UserId", "201");
        client.DefaultRequestHeaders.Add("X-Test-Role", "AccountsAdmin");
        if (!string.IsNullOrEmpty(permissions))
            client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        return client;
    }

    private async Task SeedAsync(Guid correlationId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.AuditEvents.AddRange(
            new AuditEvent
            {
                Id = 1,
                CorrelationId = correlationId,
                RealUserId = 77,
                ActingUserId = 88,
                ActingRoleId = 22,
                OutletId = 11,
                ActionType = "update",
                ActionSummary = "Updated product",
                Module = "inventory",
                Source = "api",
                PrimaryEntityType = "Product",
                PrimaryEntityId = "product-1",
                Status = "success",
                CreatedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
                Entities =
                [
                    new AuditEventEntity
                    {
                        Id = 31,
                        AuditEventId = 1,
                        EntityType = "Product",
                        EntityId = "product-1",
                        OperationType = "update",
                        FieldsChangedCount = 2,
                        FieldChanges =
                        [
                            new AuditEventFieldChange
                            {
                                Id = 41,
                                AuditEventEntityId = 31,
                                FieldName = "Name",
                                OldValue = "\"Old\"",
                                NewValue = "\"New\""
                            },
                            new AuditEventFieldChange
                            {
                                Id = 42,
                                AuditEventEntityId = 31,
                                FieldName = "PasswordHash",
                                OldValue = "\"***REDACTED***\"",
                                NewValue = "\"***REDACTED***\""
                            }
                        ]
                    }
                ]
            },
            new AuditEvent
            {
                Id = 2,
                CorrelationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                RealUserId = 77,
                ActingUserId = 89,
                OutletId = 12,
                ActionType = "create",
                ActionSummary = "Created category",
                Module = "catalog",
                Source = "api",
                PrimaryEntityType = "Category",
                PrimaryEntityId = "category-2",
                Status = "success",
                CreatedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
            });
        await dbContext.SaveChangesAsync();
    }
}
