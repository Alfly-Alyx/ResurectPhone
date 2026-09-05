using System.Runtime.InteropServices;
using System.Text;

namespace ResurectPhone.Infrastructure.Windows.Discovery;

internal static class NativeMethods
{
    internal const uint DigcfPresent = 0x00000002;
    internal const uint DigcfAllClasses = 0x00000004;
    internal const uint SpdrpDeviceDesc = 0x00000000;
    internal const uint SpdrpHardwareId = 0x00000001;
    internal const uint SpdrpMfg = 0x0000000B;
    internal const uint SpdrpFriendlyName = 0x0000000C;
    internal const int ErrorInsufficientBuffer = 122;
    internal const int ErrorNoMoreItems = 259;
    internal static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDevInfoData
    {
        internal uint Size;
        internal Guid ClassGuid;
        internal uint DeviceInstance;
        internal IntPtr Reserved;
    }

    internal static SpDevInfoData CreateDeviceInfoData() => new()
    {
        Size = (uint)Marshal.SizeOf<SpDevInfoData>()
    };

    internal static string ReadInstanceId(IntPtr deviceSet, ref SpDevInfoData info)
    {
        SetupDiGetDeviceInstanceId(deviceSet, ref info, null, 0, out var required);
        if (required <= 1)
            return string.Empty;

        var buffer = new StringBuilder(required);
        return SetupDiGetDeviceInstanceId(deviceSet, ref info, buffer, buffer.Capacity, out _)
            ? buffer.ToString()
            : string.Empty;
    }

    internal static string ReadProperty(IntPtr deviceSet, ref SpDevInfoData info, uint property)
    {
        SetupDiGetDeviceRegistryProperty(
            deviceSet,
            ref info,
            property,
            out _,
            null,
            0,
            out var required);
        if (required == 0 || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            return string.Empty;

        var buffer = new byte[required];
        if (!SetupDiGetDeviceRegistryProperty(
                deviceSet,
                ref info,
                property,
                out _,
                buffer,
                (uint)buffer.Length,
                out _))
            return string.Empty;

        return Encoding.Unicode.GetString(buffer).TrimEnd('\0').Replace('\0', ' ').Trim();
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        StringBuilder? deviceInstanceId,
        int deviceInstanceIdSize,
        out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        uint property,
        out uint propertyRegistryDataType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
