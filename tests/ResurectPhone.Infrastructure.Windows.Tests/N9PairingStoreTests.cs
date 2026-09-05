using System.Security.Cryptography;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9PairingStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ResurectPhone-N9-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoad_ProtectsThePrivateKeyForTheCurrentWindowsUser()
    {
        var path = Path.Combine(_directory, "pairing.dat");
        var store = new N9PairingStore(path);
        var expected = new N9Pairing(
            "192.168.2.15",
            22,
            "developer",
            "ssh-rsa",
            "test-fingerprint",
            "test-private-key");

        try
        {
            store.Save(expected);
        }
        catch (CryptographicException)
        {
            return;
        }

        Assert.True(store.Exists);
        Assert.Equal(expected, store.TryLoad());
        Assert.DoesNotContain("test-private-key", File.ReadAllText(path));

        store.Forget();
        Assert.False(store.Exists);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CorruptedPayload_IsNotAccepted()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "pairing.dat");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        var store = new N9PairingStore(path);

        Assert.Null(store.TryLoad());
        Assert.False(store.Exists);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
