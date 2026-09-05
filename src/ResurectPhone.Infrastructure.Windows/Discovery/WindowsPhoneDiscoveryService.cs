using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ResurectPhone.Core.Devices;
using ResurectPhone.Core.Discovery;

namespace ResurectPhone.Infrastructure.Windows.Discovery;

/// <summary>
/// Reads the list of devices already published by Windows. It does not open a
/// port, install a driver, send a command or change the connected phone.
/// </summary>
public sealed class WindowsPhoneDiscoveryService : IPhoneDiscoveryService
{
    public Task<IReadOnlyList<DetectedPhone>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DetectedPhone>>(Discover());
    }

    private static IReadOnlyList<DetectedPhone> Discover()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var deviceSet = NativeMethods.SetupDiGetClassDevs(
            IntPtr.Zero,
            null,
            IntPtr.Zero,
            NativeMethods.DigcfPresent | NativeMethods.DigcfAllClasses);
        if (deviceSet == NativeMethods.InvalidHandleValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var candidates = new List<DeviceCandidate>();
            for (uint index = 0; ; index++)
            {
                var info = NativeMethods.CreateDeviceInfoData();
                if (!NativeMethods.SetupDiEnumDeviceInfo(deviceSet, index, ref info))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ErrorNoMoreItems)
                        break;
                    throw new Win32Exception(error);
                }

                var instanceId = NativeMethods.ReadInstanceId(deviceSet, ref info);
                var friendlyName = NativeMethods.ReadProperty(deviceSet, ref info, NativeMethods.SpdrpFriendlyName);
                var description = NativeMethods.ReadProperty(deviceSet, ref info, NativeMethods.SpdrpDeviceDesc);
                var manufacturer = NativeMethods.ReadProperty(deviceSet, ref info, NativeMethods.SpdrpMfg);
                var hardwareIds = NativeMethods.ReadProperty(deviceSet, ref info, NativeMethods.SpdrpHardwareId);
                var evidence = string.Join(' ', instanceId, friendlyName, description, manufacturer, hardwareIds);
                var platform = Classify(evidence);
                if (platform == PhonePlatform.Unknown)
                    continue;

                var displayName = platform == PhonePlatform.MeeGoHarmattan
                    ? "Nokia N9"
                    : FirstNonEmpty(friendlyName, description, "Lumia");
                candidates.Add(new DeviceCandidate(instanceId, displayName, platform));
            }

            return candidates
                .GroupBy(candidate => new { candidate.Platform, candidate.DisplayName })
                .Select(group => group
                    .OrderBy(candidate => candidate.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
                    .First())
                .Select(ToPhone)
                .OrderBy(phone => phone.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceSet);
        }
    }

    private static PhonePlatform Classify(string evidence)
    {
        if (evidence.Contains("Nokia N9", StringComparison.OrdinalIgnoreCase) ||
            evidence.Contains("VID_0421&PID_0518", StringComparison.OrdinalIgnoreCase) ||
            evidence.Contains("VID_0421&PID_0519", StringComparison.OrdinalIgnoreCase) ||
            evidence.Contains("VID_0421&PID_051A", StringComparison.OrdinalIgnoreCase))
            return PhonePlatform.MeeGoHarmattan;

        if (evidence.Contains("Lumia", StringComparison.OrdinalIgnoreCase) ||
            evidence.Contains("Windows Phone", StringComparison.OrdinalIgnoreCase))
            return PhonePlatform.WindowsPhone;

        return PhonePlatform.Unknown;
    }

    private static DetectedPhone ToPhone(DeviceCandidate candidate) => new(
        StableId(candidate.InstanceId),
        candidate.DisplayName,
        candidate.Platform,
        candidate.Platform == PhonePlatform.WindowsPhone ? "Windows Phone" : null,
        null,
        null,
        null,
        PhoneCapability.ReadIdentity);

    private static string StableId(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "usb-" + Convert.ToHexString(digest.AsSpan(0, 10)).ToLowerInvariant();
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value)).Trim();

    private sealed record DeviceCandidate(
        string InstanceId,
        string DisplayName,
        PhonePlatform Platform);
}
