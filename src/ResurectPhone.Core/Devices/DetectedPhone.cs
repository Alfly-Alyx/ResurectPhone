namespace ResurectPhone.Core.Devices;

public sealed record DetectedPhone(
    string DeviceId,
    string DisplayName,
    PhonePlatform Platform,
    string? SystemName,
    string? SystemVersion,
    string? BuildNumber,
    string? ProductCode,
    PhoneCapability Capabilities)
{
    public bool Has(PhoneCapability capability) =>
        (Capabilities & capability) == capability;
}
