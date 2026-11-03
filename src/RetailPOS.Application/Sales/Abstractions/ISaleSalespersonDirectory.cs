namespace RetailPOS.Application.Sales.Abstractions;

public sealed record EligibleSalespersonReadModel(long UserId, string Name, bool PrimaryOutlet);

/// <summary>Read access to the users that may be credited as the salesperson of a sale at an outlet.</summary>
public interface ISaleSalespersonDirectory
{
    Task<long?> GetOutletBusinessIdAsync(long outletId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EligibleSalespersonReadModel>> ListEligibleAsync(long businessId, long outletId, CancellationToken cancellationToken);

    /// <summary>The user who opened the terminal's currently open cash session, if any.</summary>
    Task<long?> FindOpenSessionOpenerAsync(long outletId, long terminalId, CancellationToken cancellationToken);
}
