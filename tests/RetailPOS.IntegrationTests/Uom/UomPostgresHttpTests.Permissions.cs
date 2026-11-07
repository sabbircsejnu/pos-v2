using System.Net;
using System.Net.Http.Json;

namespace RetailPOS.Tests.Uom;

/// <summary>I01: the six UOM keys, enforced by the server for each kind of role, never by the screen alone.</summary>
public sealed partial class UomPostgresHttpTests
{
    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Permissions_EachRoleGetsWhatItsKeysAllow_AndNothingMore()
    {
        var owner = Owner();
        var (attar, config) = await StockedAttarAsync(owner, "PRM");
        var configs = new { variantIds = new[] { attar } };
        var unit = new { code = Unique("u"), name = "Test unit", symbol = "tu", dimension = "count", quantityPrecision = 0, isWholeUnit = true };
        var open = new { variantId = attar, locationId = UomPostgresFixture.Outlet, locationType = "outlet", packCode = "P12", baseQtyPerPack = 12m, count = 1, idempotencyKey = Unique("o") };
        var quote = new { variantId = attar, sellingOptionId = Option(config, "P12"), enteredQuantity = 1m };

        // A cashier sells in units through its POS permission alone: no UOM administration key is needed.
        var cashier = _fixture.Client(UomPostgresFixture.OwnerId, "Cashier", "sales.create,pos.access");
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(cashier, HttpMethod.Post, "/api/v1/uom/variants/configs", configs));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(cashier, HttpMethod.Post, "/api/v1/uom/quote", quote));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(cashier, HttpMethod.Post, "/api/v1/uom/units", unit));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(cashier, HttpMethod.Post, "/api/v1/uom/pack-state/open", open));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(cashier, HttpMethod.Get, "/api/v1/uom/repacking"));

        // Warehouse staff read units for transfers and counts, but change nothing in the unit setup.
        var warehouse = _fixture.Client(UomPostgresFixture.OwnerId, "WarehouseStaff", "inventory.view,stock_transfers.create");
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(warehouse, HttpMethod.Post, "/api/v1/uom/variants/configs", configs));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(warehouse, HttpMethod.Post, "/api/v1/uom/units", unit));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(warehouse, HttpMethod.Post, "/api/v1/uom/repacking/1/execute", new { }));

        // An outlet manager granted the pack-state and repacking keys in Role Edit can use exactly those.
        var manager = _fixture.Client(UomPostgresFixture.OwnerId, "Manager", "uom.view,inventory.pack_state.view,inventory.repacking.view");
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(manager, HttpMethod.Get, "/api/v1/uom/repacking"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(manager, HttpMethod.Post, "/api/v1/uom/repacking", new { }));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(manager, HttpMethod.Post, "/api/v1/uom/pack-state/open-prepared", open));

        // A custom role with an unrelated permission sees none of it.
        var restricted = _fixture.Client(UomPostgresFixture.OwnerId, "Custom", "customers.view");
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(restricted, HttpMethod.Post, "/api/v1/uom/variants/configs", configs));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(restricted, HttpMethod.Get, $"/api/v1/uom/variants/{attar}/config"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(restricted, HttpMethod.Get, "/api/v1/uom/repacking"));

        // Another business's owner holds every key of its own business and none of this one.
        var foreign = _fixture.Client(UomPostgresFixture.ForeignOwnerId, "BusinessOwner", "*", UomPostgresFixture.ForeignBusiness);
        var foreignStatus = await StatusAsync(foreign, HttpMethod.Get, $"/api/v1/uom/variants/{attar}/config");
        Assert.True(foreignStatus is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest, foreignStatus.ToString());
        Assert.NotEqual(HttpStatusCode.OK, await StatusAsync(foreign, HttpMethod.Post, "/api/v1/uom/pack-state/open", open));

        // The Business Owner holds all six through the wildcard.
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(owner, HttpMethod.Get, "/api/v1/uom/repacking"));
    }
}
