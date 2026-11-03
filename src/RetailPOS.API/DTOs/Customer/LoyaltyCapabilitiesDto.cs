namespace RetailPOS.API.DTOs.Customer;

public class LoyaltyCapabilitiesDto
{
    /// <summary>True only when FeatureFlags:LegacyLoyaltyPointsMutationEnabled is on; the legacy add/redeem endpoints answer 403 otherwise.</summary>
    public bool LegacyPointsMutationEnabled { get; set; }
}
