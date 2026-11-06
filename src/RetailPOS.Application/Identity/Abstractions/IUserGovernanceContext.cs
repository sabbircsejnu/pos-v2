namespace RetailPOS.Application.Identity.Abstractions;

public interface IUserGovernanceContext
{
    long? RealUserId { get; }

    /// <summary>Checks the request's effective permissions. Denies unless an implementation can prove otherwise.</summary>
    bool HasPermission(string permission) => false;
}
