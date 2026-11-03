using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RetailPOS.Tests.Sales;

namespace RetailPOS.Tests.Inventory;

public sealed class MobileStockTakePostgresHttpTests
    : IClassFixture<MobileSalesDashboardPostgresFixture>
{
    private readonly MobileSalesDashboardPostgresFixture _fixture;

    public MobileStockTakePostgresHttpTests(MobileSalesDashboardPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SessionFoundationCreatesAndReadsOnlyAuthorizedBlindCountData()
    {
        using var owner = _fixture.CreateClient(
            4101,
            4110,
            "BusinessOwner",
            "StockCount.ViewAll,StockCount.Create,StockCount.Upload");

        using var createdAtThree = await owner.PostAsJsonAsync(
            "/api/mobile/v1/stock-take/sessions",
            CreateRequest(5103, "Owner session"));
        Assert.Equal(HttpStatusCode.Created, createdAtThree.StatusCode);
        using var createdThreeJson = JsonDocument.Parse(await createdAtThree.Content.ReadAsStringAsync());
        var thirdSessionId = createdThreeJson.RootElement
            .GetProperty("data").GetProperty("id").GetInt64();
        Assert.Equal("Draft", createdThreeJson.RootElement.GetProperty("data")
            .GetProperty("status").GetString());
        Assert.Equal(0, createdThreeJson.RootElement.GetProperty("data")
            .GetProperty("revision").GetInt64());
        Assert.Equal("InventoryStatementSnapshot", createdThreeJson.RootElement.GetProperty("data")
            .GetProperty("baselineConsistencyStatus").GetString());
        Assert.Equal("NotStarted", createdThreeJson.RootElement.GetProperty("data")
            .GetProperty("movementReconciliationStatus").GetString());
        Assert.Equal("outlet", createdThreeJson.RootElement.GetProperty("data")
            .GetProperty("scope").GetProperty("locationType").GetString());
        Assert.Equal(new[] { "resume" }, createdThreeJson.RootElement.GetProperty("data")
            .GetProperty("permittedActions").EnumerateArray()
            .Select(action => action.GetString()).ToArray());

        var firstOperationId = Guid.NewGuid();
        var secondOperationId = Guid.NewGuid();
        var operationBatch = new
        {
            deviceSessionId = "android-test-session",
            expectedRevision = 0,
            operations = new object[]
            {
                new
                {
                    clientOperationId = firstOperationId,
                    clientSequence = 1,
                    productVariantId = 9103,
                    countedQuantity = 0m,
                    rawBarcode = (string?)null,
                    clientObservedAt = DateTimeOffset.UtcNow
                },
                new
                {
                    clientOperationId = secondOperationId,
                    clientSequence = 2,
                    productVariantId = 9104,
                    countedQuantity = 3.5m,
                    rawBarcode = "0000009876",
                    clientObservedAt = (DateTimeOffset?)null
                }
            }
        };
        using var appliedOperations = await owner.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
            operationBatch);
        Assert.True(
            appliedOperations.StatusCode == HttpStatusCode.OK,
            await appliedOperations.Content.ReadAsStringAsync());
        using (var appliedJson = JsonDocument.Parse(await appliedOperations.Content.ReadAsStringAsync()))
        {
            var result = appliedJson.RootElement.GetProperty("data");
            Assert.Equal(2, result.GetProperty("currentRevision").GetInt64());
            Assert.Equal(new long[] { 1, 2 }, result.GetProperty("receipts").EnumerateArray()
                .Select(receipt => receipt.GetProperty("acceptedRevision").GetInt64()).ToArray());
            Assert.Equal(0m, result.GetProperty("receipts").EnumerateArray().First()
                .GetProperty("countedQuantity").GetDecimal());
        }

        using var replayedOperations = await owner.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
            operationBatch);
        Assert.Equal(HttpStatusCode.OK, replayedOperations.StatusCode);
        using (var replayedJson = JsonDocument.Parse(await replayedOperations.Content.ReadAsStringAsync()))
        {
            var result = replayedJson.RootElement.GetProperty("data");
            Assert.Equal(2, result.GetProperty("currentRevision").GetInt64());
            Assert.All(result.GetProperty("receipts").EnumerateArray(),
                receipt => Assert.True(receipt.GetProperty("isDuplicate").GetBoolean()));
        }

        var alteredReplay = new
        {
            deviceSessionId = "android-test-session",
            expectedRevision = 0,
            operations = new[]
            {
                new
                {
                    clientOperationId = firstOperationId,
                    clientSequence = 1,
                    productVariantId = 9103,
                    countedQuantity = 1m,
                    rawBarcode = "0000123456",
                    clientObservedAt = (DateTimeOffset?)null
                }
            }
        };
        using var idempotencyConflict = await owner.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
            alteredReplay);
        Assert.Equal(HttpStatusCode.Conflict, idempotencyConflict.StatusCode);
        using (var conflictJson = JsonDocument.Parse(await idempotencyConflict.Content.ReadAsStringAsync()))
        {
            Assert.False(conflictJson.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("IDEMPOTENCY_KEY_PAYLOAD_MISMATCH",
                conflictJson.RootElement.GetProperty("message").GetString());
        }

        var staleBatch = new
        {
            deviceSessionId = "android-test-session",
            expectedRevision = 0,
            operations = new[]
            {
                new
                {
                    clientOperationId = Guid.NewGuid(),
                    clientSequence = 3,
                    productVariantId = 9103,
                    countedQuantity = 4m,
                    rawBarcode = (string?)null,
                    clientObservedAt = (DateTimeOffset?)null
                }
            }
        };
        using var staleOperations = await owner.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
            staleBatch);
        Assert.Equal(HttpStatusCode.Conflict, staleOperations.StatusCode);
        using (var staleJson = JsonDocument.Parse(await staleOperations.Content.ReadAsStringAsync()))
        {
            Assert.False(staleJson.RootElement.GetProperty("success").GetBoolean());
            var result = staleJson.RootElement.GetProperty("data");
            Assert.Equal("STALE_SESSION_REVISION", result.GetProperty("errorCode").GetString());
            Assert.Equal("Rejected", Assert.Single(result.GetProperty("receipts").EnumerateArray())
                .GetProperty("outcome").GetString());
        }

        using var operationReceipt = await owner.GetAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations/{staleBatch.operations[0].clientOperationId}");
        Assert.Equal(HttpStatusCode.OK, operationReceipt.StatusCode);
        using var acceptedOperations = await owner.GetAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations?afterRevision=0");
        Assert.Equal(HttpStatusCode.OK, acceptedOperations.StatusCode);
        using (var acceptedJson = JsonDocument.Parse(await acceptedOperations.Content.ReadAsStringAsync()))
        {
            var data = acceptedJson.RootElement.GetProperty("data");
            Assert.Equal(2, data.GetProperty("currentRevision").GetInt64());
            Assert.Equal(2, data.GetProperty("items").GetArrayLength());
        }

        var invalidBarcodeBatch = new
        {
            deviceSessionId = "android-test-session",
            expectedRevision = 2,
            operations = new object[]
            {
                new
                {
                    clientOperationId = Guid.NewGuid(),
                    clientSequence = 4,
                    productVariantId = 9103,
                    countedQuantity = 5m,
                    rawBarcode = "0000123456",
                    clientObservedAt = (DateTimeOffset?)null
                },
                new
                {
                    clientOperationId = Guid.NewGuid(),
                    clientSequence = 5,
                    productVariantId = 9104,
                    countedQuantity = 6m,
                    rawBarcode = "0000009876",
                    clientObservedAt = (DateTimeOffset?)null
                }
            }
        };
        using var rejectedBarcodeBatch = await owner.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
            invalidBarcodeBatch);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejectedBarcodeBatch.StatusCode);
        using (var rejectedJson = JsonDocument.Parse(await rejectedBarcodeBatch.Content.ReadAsStringAsync()))
        {
            Assert.False(rejectedJson.RootElement.GetProperty("success").GetBoolean());
            var result = rejectedJson.RootElement.GetProperty("data");
            Assert.Equal(2, result.GetProperty("currentRevision").GetInt64());
            var receipts = result.GetProperty("receipts").EnumerateArray().ToArray();
            Assert.Equal(2, receipts.Length);
            Assert.All(receipts, receipt => Assert.Equal(
                "Rejected",
                receipt.GetProperty("outcome").GetString()));
            Assert.Contains(
                receipts,
                receipt => receipt.GetProperty("rejectionCode").GetString()
                    == "BARCODE_NOT_UNAMBIGUOUS_VARIANT");
            Assert.Contains(
                receipts,
                receipt => receipt.GetProperty("rejectionCode").GetString() == "BATCH_REJECTED");
        }

        var duplicateSequenceBatch = new
        {
            deviceSessionId = "android-test-session",
            expectedRevision = 2,
            operations = new object[]
            {
                new
                {
                    clientOperationId = Guid.NewGuid(),
                    clientSequence = 6,
                    productVariantId = 9103,
                    countedQuantity = 1m,
                    rawBarcode = (string?)null,
                    clientObservedAt = (DateTimeOffset?)null
                },
                new
                {
                    clientOperationId = Guid.NewGuid(),
                    clientSequence = 6,
                    productVariantId = 9104,
                    countedQuantity = 2m,
                    rawBarcode = (string?)null,
                    clientObservedAt = (DateTimeOffset?)null
                }
            }
        };
        using var duplicateSequences = await owner.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
            duplicateSequenceBatch);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateSequences.StatusCode);

        var concurrentBatches = new[]
        {
            new
            {
                deviceSessionId = "android-test-session",
                expectedRevision = 2,
                operations = new[]
                {
                    new
                    {
                        clientOperationId = Guid.NewGuid(),
                        clientSequence = 6,
                        productVariantId = 9103,
                        countedQuantity = 7m,
                        rawBarcode = (string?)null,
                        clientObservedAt = (DateTimeOffset?)null
                    }
                }
            },
            new
            {
                deviceSessionId = "android-test-session",
                expectedRevision = 2,
                operations = new[]
                {
                    new
                    {
                        clientOperationId = Guid.NewGuid(),
                        clientSequence = 7,
                        productVariantId = 9104,
                        countedQuantity = 8m,
                        rawBarcode = (string?)null,
                        clientObservedAt = (DateTimeOffset?)null
                    }
                }
            }
        };
        var concurrentResults = await Task.WhenAll(concurrentBatches.Select(batch =>
            owner.PostAsJsonAsync(
                $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
                batch)));
        try
        {
            Assert.Single(concurrentResults, response =>
                response.StatusCode == HttpStatusCode.OK);
            Assert.Single(concurrentResults, response =>
                response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in concurrentResults)
                response.Dispose();
        }
        Assert.True(await _fixture.CountOperationAuditEntitiesAsync() >= 7);

        using var createdAtTwo = await owner.PostAsJsonAsync(
            "/api/mobile/v1/stock-take/sessions",
            CreateRequest(5102, "Second owner session"));
        Assert.Equal(HttpStatusCode.Created, createdAtTwo.StatusCode);

        using var detail = await owner.GetAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        using var lines = await owner.GetAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/lines?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, lines.StatusCode);
        using (var linesJson = JsonDocument.Parse(await lines.Content.ReadAsStringAsync()))
        {
            var page = linesJson.RootElement.GetProperty("data");
            Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
            Assert.True(page.GetProperty("totalCount").GetInt32() >= 2);
            var pageItems = page.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(2, pageItems.Length);
            foreach (var line in pageItems)
            {
                Assert.Equal(JsonValueKind.Null, line.GetProperty("countedQuantity").ValueKind);
                Assert.False(line.TryGetProperty("currentStock", out _));
                Assert.False(line.TryGetProperty("difference", out _));
                Assert.False(line.TryGetProperty("expectedQuantity", out _));
            }
        }

        using var firstPage = await owner.GetAsync(
            "/api/mobile/v1/stock-take/sessions?status=draft&page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        using (var pageJson = JsonDocument.Parse(await firstPage.Content.ReadAsStringAsync()))
        {
            var page = pageJson.RootElement.GetProperty("data");
            Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
            Assert.Single(page.GetProperty("items").EnumerateArray());
        }

        using var outletFilter = await owner.GetAsync(
            "/api/mobile/v1/stock-take/sessions?outletId=5103&status=Draft");
        Assert.Equal(HttpStatusCode.OK, outletFilter.StatusCode);
        using (var outletJson = JsonDocument.Parse(await outletFilter.Content.ReadAsStringAsync()))
        {
            var item = Assert.Single(outletJson.RootElement.GetProperty("data")
                .GetProperty("items").EnumerateArray());
            Assert.Equal(thirdSessionId, item.GetProperty("id").GetInt64());
        }

        using var manager = _fixture.CreateClient(
            4101,
            4104,
            "SalesManager",
            "StockCount.ViewOwn,StockCount.Create,StockCount.Upload");
        using var managerCreated = await manager.PostAsJsonAsync(
            "/api/mobile/v1/stock-take/sessions",
            CreateRequest(5101, "Manager session"));
        Assert.Equal(HttpStatusCode.Created, managerCreated.StatusCode);
        using var managerOutlets = await manager.GetAsync(
            "/api/mobile/v1/stock-take/sessions?outletId=5101");
        Assert.Equal(HttpStatusCode.OK, managerOutlets.StatusCode);
        using (var managerOutletsJson = JsonDocument.Parse(
                   await managerOutlets.Content.ReadAsStringAsync()))
        {
            Assert.All(
                managerOutletsJson.RootElement.GetProperty("data")
                    .GetProperty("items").EnumerateArray(),
                item => Assert.Equal(5101, item.GetProperty("scope")
                    .GetProperty("locationId").GetInt64()));
        }
        using var unauthorizedOutlet = await manager.GetAsync(
            "/api/mobile/v1/stock-take/sessions?outletId=5103");
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedOutlet.StatusCode);
        using var unauthorizedSession = await manager.GetAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}");
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedSession.StatusCode);
        using var unauthorizedCreate = await manager.PostAsJsonAsync(
            "/api/mobile/v1/stock-take/sessions",
            CreateRequest(5103, "Out of scope"));
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedCreate.StatusCode);
        using var unauthorizedOperation = await manager.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
            operationBatch);
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedOperation.StatusCode);

        using var salesperson = _fixture.CreateClient(
            4101,
            4102,
            "SalesPerson",
            "StockCount.ViewAll,StockCount.Create");
        using var deniedSalesperson = await salesperson.GetAsync(
            "/api/mobile/v1/stock-take/sessions");
        Assert.Equal(HttpStatusCode.Forbidden, deniedSalesperson.StatusCode);
        using var deniedSalespersonCreate = await salesperson.PostAsJsonAsync(
            "/api/mobile/v1/stock-take/sessions",
            CreateRequest(5101, "Denied"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedSalespersonCreate.StatusCode);
        using var deniedSalespersonOperation = await salesperson.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
            operationBatch);
        Assert.Equal(HttpStatusCode.Forbidden, deniedSalespersonOperation.StatusCode);

        using var cashier = _fixture.CreateClient(
            4101,
            4105,
            "Cashier",
            "StockCount.ViewAll,StockCount.Create");
        using var deniedCashier = await cashier.GetAsync(
            "/api/mobile/v1/stock-take/sessions");
        Assert.Equal(HttpStatusCode.Forbidden, deniedCashier.StatusCode);

        using var missingPermission = _fixture.CreateClient(
            4101,
            4104,
            "OutletManager",
            "inventory.view");
        using var deniedMissingPermission = await missingPermission.GetAsync(
            "/api/mobile/v1/stock-take/sessions");
        Assert.Equal(HttpStatusCode.Forbidden, deniedMissingPermission.StatusCode);

        using var roleSwitched = _fixture.CreateClient(
            4101,
            4110,
            "BusinessOwner",
            "StockCount.ViewAll,StockCount.Create",
            true,
            "SalesManager");
        using var deniedRoleSwitch = await roleSwitched.GetAsync(
            "/api/mobile/v1/stock-take/sessions");
        Assert.Equal(HttpStatusCode.Forbidden, deniedRoleSwitch.StatusCode);

        using var invalidPagination = await owner.GetAsync(
            "/api/mobile/v1/stock-take/sessions?page=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPagination.StatusCode);
        using var invalidStatus = await owner.GetAsync(
            "/api/mobile/v1/stock-take/sessions?status=Unknown");
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
        using var invalidCreate = await owner.PostAsJsonAsync(
            "/api/mobile/v1/stock-take/sessions",
            CreateRequest(0, "Invalid Outlet"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);

        using var readOnlyOwner = _fixture.CreateClient(
            4101,
            4110,
            "BusinessOwner",
            "StockCount.ViewAll");
        using var deniedCreatePermission = await readOnlyOwner.PostAsJsonAsync(
            "/api/mobile/v1/stock-take/sessions",
            CreateRequest(5101, "Missing create permission"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedCreatePermission.StatusCode);
        using var deniedUploadPermission = await readOnlyOwner.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
            operationBatch);
        Assert.Equal(HttpStatusCode.Forbidden, deniedUploadPermission.StatusCode);

        using var approvalRoute = await owner.PostAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/approve",
            content: null);
        Assert.Equal(HttpStatusCode.NotFound, approvalRoute.StatusCode);

        Assert.Equal(4, await _fixture.UploadCountAndGetRevisionAsync(thirdSessionId));
        Assert.Equal(4, await _fixture.UploadCountAndGetRevisionAsync(thirdSessionId));
        Assert.True(await _fixture.MobileAdjustmentDraftIsBlockedAsync(thirdSessionId));
        using var replayAfterReview = await owner.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{thirdSessionId}/operations:batch",
            operationBatch);
        Assert.Equal(HttpStatusCode.OK, replayAfterReview.StatusCode);
        using (var replayAfterReviewJson = JsonDocument.Parse(
                   await replayAfterReview.Content.ReadAsStringAsync()))
        {
            var replayResult = replayAfterReviewJson.RootElement.GetProperty("data");
            Assert.Equal(4, replayResult.GetProperty("currentRevision").GetInt64());
            Assert.All(replayResult.GetProperty("receipts").EnumerateArray(),
                receipt => Assert.True(receipt.GetProperty("isDuplicate").GetBoolean()));
        }
        await _fixture.AddConflictingBusinessAsync();
        using var blockedByDeploymentGuard = await owner.GetAsync(
            "/api/mobile/v1/stock-take/sessions");
        Assert.Equal(HttpStatusCode.Forbidden, blockedByDeploymentGuard.StatusCode);
        using var blockedCreateByGuard = await owner.PostAsJsonAsync(
            "/api/mobile/v1/stock-take/sessions",
            CreateRequest(5101, "Guard must block"));
        Assert.Equal(HttpStatusCode.Forbidden, blockedCreateByGuard.StatusCode);
    }

    private static object CreateRequest(long outletId, string remarks) => new
    {
        outletId,
        stockCountDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
        remarks
    };
}
