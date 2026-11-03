namespace RetailPOS.Application.Abstractions;

/// <summary>
/// The Business the current caller is allowed to act in. Stores that own Business-scoped data use this instead of
/// trusting any client-supplied Business identifier. A Super Admin has no implicit Business: they must select one.
/// </summary>
public interface IBusinessScope
{
    /// <summary>The effective Business, or null when none is selected.</summary>
    long? EffectiveBusinessId { get; }

    /// <summary>Returns the effective Business or throws <c>BusinessContextRequiredException</c>.</summary>
    long RequireBusinessId();
}
