using System.Globalization;
using ResurectPhone.Core.Android;

namespace ResurectPhone.App.Presentation;

public sealed record AndroidProcessViewModel(AndroidProcessInfo Process)
{
    public string Name => Process.Name;
    public int ProcessId => Process.ProcessId;
    public int ParentProcessId => Process.ParentProcessId;
    public string User => Process.User;
    public string State => Process.State;
    public string CommandLine => Process.CommandLine;
    public double? CpuUsagePercent => Process.CpuUsagePercent;
    public long? ResidentMemoryBytes => Process.ResidentMemoryBytes;
    public double? StorageReadBytesPerSecond => Process.StorageReadBytesPerSecond;
    public double? StorageWriteBytesPerSecond => Process.StorageWriteBytesPerSecond;
    public int? ThreadCount => Process.ThreadCount;

    public string CpuText => CpuUsagePercent is { } value
        ? value.ToString("0.0' %'", CultureInfo.CurrentCulture)
        : "—";

    public string MemoryText => ResidentMemoryBytes is { } bytes
        ? FormatBytes(bytes)
        : "—";

    public string ThreadCountText => ThreadCount?.ToString(CultureInfo.CurrentCulture) ?? "—";
    public string StorageReadText => StorageReadBytesPerSecond is { } value ? FormatBytes((long)value) + "/s" : "—";
    public string StorageWriteText => StorageWriteBytesPerSecond is { } value ? FormatBytes((long)value) + "/s" : "—";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
