namespace ResurectPhone.Core.NokiaN9;

public sealed record N9OperationProgress(string Message, double? Percent = null);

public sealed record N9KernelPreparation(string Directory, string RemoteDirectory, string SalesCode,
    string OriginalKernel, string KernelImage, string FirmwareImage, string Flasher,
    string BackupImage, string BackupSha256, string HostFingerprint, string UsbSerial);

public interface IN9KernelService
{
    Task<N9KernelPreparation> PrepareKernelAsync(IProgress<N9OperationProgress>? progress = null,
        char[]? administratorPassword = null, CancellationToken cancellationToken = default);
}
