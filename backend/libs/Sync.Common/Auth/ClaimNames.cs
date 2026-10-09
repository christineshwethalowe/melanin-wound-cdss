namespace Sync.Common.Auth;

/// <summary>
/// JWT claims issued by the identity service and read by every resource server (architecture §7.3, §12).
/// </summary>
public static class ClaimNames
{
    public const string Subject = "sub";
    public const string DeviceId = "device_id";
    public const string FacilityId = "facility_id";
    public const string Role = "role";
    public const string ClientId = "client_id";
}
