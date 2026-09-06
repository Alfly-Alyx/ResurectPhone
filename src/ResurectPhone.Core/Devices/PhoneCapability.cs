namespace ResurectPhone.Core.Devices;

[Flags]
public enum PhoneCapability
{
    None = 0,
    ReadIdentity = 1 << 0,
    ReadFirmware = 1 << 1,
    ReadApplications = 1 << 2,
    InstallPackages = 1 << 3,
    ManageRepositories = 1 << 4,
    ManageCertificates = 1 << 5,
    ManageNavigation = 1 << 6,
    FlashFirmware = 1 << 7,
    InstallAlternativeSystem = 1 << 8,
    ReadProcesses = 1 << 9,
    ManageProcesses = 1 << 10
}
