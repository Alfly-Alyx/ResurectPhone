namespace ResurectPhone.Core.NokiaN9;

public sealed record N9HostKeyIdentity(
    string Algorithm,
    string Sha256Fingerprint,
    int KeyLength)
{
    public string DisplayFingerprint => $"SHA256:{Sha256Fingerprint}";
}

public sealed record N9ConnectionStatus(
    bool IsPaired,
    bool IsReachable,
    bool IsHarmattan,
    string Detail,
    string ServerVersion = "",
    string KeyExchangeAlgorithm = "");

public sealed record N9DeviceDetails
{
    public string Hostname { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string SalesCode { get; init; } = string.Empty;
    public string SystemName { get; init; } = string.Empty;
    public string SystemVersion { get; init; } = string.Empty;
    public string SystemBuild { get; init; } = string.Empty;
    public string KernelVersion { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
}

public class N9ConnectionException(string message, Exception? innerException = null)
    : IOException(message, innerException);

public sealed class N9AuthenticationRequiredException(string message, Exception? innerException = null)
    : N9ConnectionException(message, innerException);

public sealed class N9AdministratorRequiredException()
    : N9ConnectionException("Le mot de passe administrateur d’origine a été refusé. Saisissez celui configuré sur le N9.");

public sealed record N9UsbAccessResult(bool AlreadyConfigured, string BackupPath);

public interface IN9ConnectionService
{
    bool HasPairing { get; }
    string? SavedAddress { get; }
    Task<N9ConnectionStatus> ConnectAtAddressAsync(string address, string? temporaryPassword = null,
        Func<N9HostKeyIdentity, bool>? approveHostKey = null, CancellationToken cancellationToken = default);

    Task<N9ConnectionStatus> PairAsync(
        string temporaryPassword,
        Func<N9HostKeyIdentity, bool> approveHostKey,
        CancellationToken cancellationToken = default);

    Task<N9ConnectionStatus> PairWithoutPasswordAsync(
        CancellationToken cancellationToken = default);

    Task<N9ConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<N9DeviceDetails> ReadDeviceDetailsAsync(CancellationToken cancellationToken = default);

    Task<N9UsbAccessResult> PrepareUsbAccessAsync(
        byte[] setupScript,
        char[]? administratorPassword = null,
        CancellationToken cancellationToken = default);

    void ForgetPairing();
}
