namespace RetailPOS.Application.Identity.Abstractions;

public interface IRoleGovernanceContext
{
    bool IsSuperAdmin { get; }
}
