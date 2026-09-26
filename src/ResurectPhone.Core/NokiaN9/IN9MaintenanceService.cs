namespace ResurectPhone.Core.NokiaN9;

public sealed class N9MaintenanceFailureException(string message, string backupPath) : N9ConnectionException(message)
{
    public string BackupPath { get; } = backupPath;
}

public sealed record N9Application(N9DebPackageMetadata Metadata, string Name, string DesktopPath)
{
    public string DisplayName => $"{Name} — {Metadata.Package} ({Metadata.Version})";
    public string? RemovalBlock => N9PackageMaintenancePolicy.GetRemovalBlockReason(Metadata);
}

public sealed record N9SettingsBackup(string FeatureId, string Path, string Label, DateTimeOffset CreatedAt)
{
    public string DisplayName => CreatedAt == DateTimeOffset.MinValue ? Label : $"{CreatedAt.ToLocalTime():dd/MM HH:mm} — {Label}";
}

public sealed record N9MaintenanceReport(string Summary, string Detail, bool CanApply = false,
    string ActionLabel = "Appliquer", string? BackupPath = null, bool CanForceRemoval = false, string? LocalFile = null, bool CanRestoreApplication = false);

public sealed record N9LocalPackage(string Path, N9DebPackageMetadata Metadata, string Sha256,
    bool IsRebuiltBackup);

public interface IN9MaintenanceService
{
    Task<IReadOnlyList<N9SettingsBackup>> ReadSettingsBackupsAsync(CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> LaunchApplicationAsync(string applicationId, CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> ExecuteActionAsync(string actionId,
        IProgress<N9OperationProgress>? progress = null, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<N9Application>> ReadApplicationsAsync(CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> DiagnoseAsync(string featureId, CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> ApplyAsync(string featureId, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default);
    Task<N9LocalPackage> InspectLocalPackageAsync(string path, CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> InstallLocalPackageAsync(N9LocalPackage package, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> ExportApplicationAsync(string packageId, string destination,
        char[]? administratorPassword = null, CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> PreviewRemovalAsync(string packageId, CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> RemoveApplicationAsync(string packageId, bool forceDependencies,
        char[]? administratorPassword = null, CancellationToken cancellationToken = default);
    Task<N9MaintenanceReport> RestoreSettingsAsync(string backupPath, char[]? administratorPassword = null,
        CancellationToken cancellationToken = default);
}
