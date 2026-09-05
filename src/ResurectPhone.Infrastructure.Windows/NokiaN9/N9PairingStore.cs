using System.Security.Cryptography;
using System.Text.Json;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal sealed record N9Pairing(
    string Host,
    int Port,
    string UserName,
    string HostKeyName,
    string HostKeySha256,
    string PrivateKey);

internal sealed class N9PairingStore
{
    private static readonly byte[] Entropy = "ResurectPhone.NokiaN9.Pairing.v1"u8.ToArray();
    private readonly string _path;

    public N9PairingStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ResurectPhone", "Pairing", "nokia-n9.dat");
    }

    public bool Exists => File.Exists(_path) && TryLoad() is not null;

    public void Save(N9Pairing pairing)
    {
        Validate(pairing);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(pairing);
        try
        {
            var protectedPayload = ProtectedData.Protect(
                plaintext,
                Entropy,
                DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".new";
            File.WriteAllBytes(temporaryPath, protectedPayload);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public N9Pairing? TryLoad()
    {
        byte[]? plaintext = null;
        try
        {
            if (!File.Exists(_path))
                return null;

            plaintext = ProtectedData.Unprotect(
                File.ReadAllBytes(_path),
                Entropy,
                DataProtectionScope.CurrentUser);
            var pairing = JsonSerializer.Deserialize<N9Pairing>(plaintext);
            Validate(pairing);
            return pairing;
        }
        catch (Exception exception) when (
            exception is CryptographicException or JsonException or IOException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Forget()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private static void Validate(N9Pairing? pairing)
    {
        if (pairing is null ||
            string.IsNullOrWhiteSpace(pairing.Host) ||
            pairing.Port is < 1 or > 65535 ||
            !pairing.UserName.Equals(N9SshConnectionService.DefaultUserName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(pairing.HostKeyName) ||
            string.IsNullOrWhiteSpace(pairing.HostKeySha256) ||
            string.IsNullOrWhiteSpace(pairing.PrivateKey))
        {
            throw new ArgumentException(
                "Les informations d’appairage du Nokia N9 sont incomplètes.",
                nameof(pairing));
        }
    }
}
