namespace RetailPOS.Application.Identity.Abstractions;

public interface IUserGovernanceContext
{
    long? RealUserId { get; }
}
