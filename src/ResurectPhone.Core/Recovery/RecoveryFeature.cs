using ResurectPhone.Core.Devices;

namespace ResurectPhone.Core.Recovery;

public sealed record RecoveryFeature(
    string Id,
    RecoveryArea Area,
    string Title,
    string Description,
    IReadOnlySet<PhonePlatform> SupportedPlatforms,
    RecoveryRisk Risk,
    RecoveryAvailability Availability,
    PhoneCapability RequiredCapability = PhoneCapability.None,
    IReadOnlyList<string>? PackageIds = null)
{
    public bool Supports(DetectedPhone phone) =>
        SupportedPlatforms.Contains(phone.Platform) &&
        (RequiredCapability == PhoneCapability.None || phone.Has(RequiredCapability));
}
