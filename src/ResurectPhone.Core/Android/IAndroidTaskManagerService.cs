namespace ResurectPhone.Core.Android;

public sealed record AndroidDeviceIdentity(
    string Serial,
    string State,
    string Manufacturer,
    string Model,
    string AndroidVersion,
    string ApiLevel)
{
    public bool IsReady => State.Equals("device", StringComparison.OrdinalIgnoreCase);

    public string DisplayName => string.Join(
        " ",
        new[] { Manufacturer, Model }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } name
            ? name
            : "Téléphone Android";
}

public sealed record AndroidProcessInfo(
    int ProcessId,
    int ParentProcessId,
    string Name,
    string User,
    string State,
    string CommandLine,
    double? CpuUsagePercent,
    long? ResidentMemoryBytes,
    double? StorageReadBytesPerSecond,
    double? StorageWriteBytesPerSecond,
    int? ThreadCount);

public sealed record AndroidProcessSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<AndroidProcessInfo> Processes,
    double? CpuUsagePercent,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    bool IsPartial,
    string Detail)
{
    public long UsedMemoryBytes => Math.Max(0, TotalMemoryBytes - AvailableMemoryBytes);
}

public sealed record AndroidMemoryReleaseResult(
    long BeforeAvailableMemoryBytes,
    long AfterAvailableMemoryBytes,
    AndroidProcessSnapshot Snapshot)
{
    public long ReleasedMemoryBytes => Math.Max(0, AfterAvailableMemoryBytes - BeforeAvailableMemoryBytes);
}

public sealed class AndroidAdbException(string message, Exception? innerException = null)
    : IOException(message, innerException);

public interface IAndroidTaskManagerService
{
    bool IsAdbAvailable { get; }

    Task<IReadOnlyList<AndroidDeviceIdentity>> DiscoverDevicesAsync(
        CancellationToken cancellationToken = default);

    Task<AndroidProcessSnapshot> CaptureAsync(
        string serial,
        CancellationToken cancellationToken = default);

    Task<AndroidMemoryReleaseResult> ReleaseMemoryAsync(
        string serial,
        CancellationToken cancellationToken = default);
}
