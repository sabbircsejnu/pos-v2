using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Infrastructure;

/// <summary>
/// Production-accurate Super Admin fixtures: an active user row with the Super Admin role and no home Business,
/// and a token without a <c>businessId</c> claim that selects a Business only through the validated X-Business-Id header.
/// </summary>
internal static class TestSuperAdmin
{
    public const string RoleName = "Super Admin";

    public static async Task SeedAsync(RetailPOSDbContext db, long userId)
    {
        var role = db.Roles.Local.FirstOrDefault(candidate => candidate.Name == RoleName)
            ?? db.Roles.FirstOrDefault(candidate => candidate.Name == RoleName);
        if (role is null)
        {
            role = new Role { Id = 990001, Name = RoleName, Permissions = """["*"]""" };
            db.Roles.Add(role);
        }

        db.Users.Add(new User
        {
            Id = userId,
            BusinessId = null,
            Name = $"Super Admin {userId}",
            Email = $"super-admin-{userId}@example.test",
            PasswordHash = "x",
            Role = role,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Configures a client as a Super Admin; pass <paramref name="selectedBusinessId"/> null to send no Business context.</summary>
    public static HttpClient Configure(
        HttpClient client,
        long userId,
        long? selectedBusinessId,
        string permissions = "*")
    {
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "none");
        client.DefaultRequestHeaders.Add("X-Test-Role", RoleName);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
        if (selectedBusinessId.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Business-Id", selectedBusinessId.Value.ToString());
        }

        return client;
    }
}
