using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Common;
using ResurectPhone.Core.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

public sealed class N9SshConnectionService : IN9ConnectionService
{
    public const string DefaultUsbHost = "192.168.2.15";
    public const int DefaultPort = 22;
    public const string DefaultUserName = "developer";

    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
    private readonly N9PairingStore _pairingStore;

    public N9SshConnectionService() : this(new N9PairingStore())
    {
    }

    internal N9SshConnectionService(N9PairingStore pairingStore)
    {
        _pairingStore = pairingStore;
    }

    public bool HasPairing => _pairingStore.Exists;

    public async Task<N9ConnectionStatus> PairAsync(
        string temporaryPassword,
        Func<N9HostKeyIdentity, bool> approveHostKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryPassword);
        ArgumentNullException.ThrowIfNull(approveHostKey);

        N9HostKeyIdentity? acceptedKey = null;
        using var client = new SshClient(CreatePasswordConnectionInfo(temporaryPassword));
        client.HostKeyReceived += (_, eventArgs) =>
        {
            var identity = ToIdentity(eventArgs);
            eventArgs.CanTrust = approveHostKey(identity);
            if (eventArgs.CanTrust)
                acceptedKey = identity;
        };

        await ConnectAsync(client, cancellationToken);
        if (acceptedKey is null)
            throw new N9ConnectionException("L’empreinte SSH du Nokia N9 n’a pas été approuvée.");

        var status = await ProbeAsync(client, cancellationToken);
        if (!status.IsHarmattan)
        {
            throw new N9ConnectionException(
                "Le serveur SSH répond, mais MeeGo Harmattan n’a pas pu être confirmé sur cet appareil.");
        }

        var (privateKey, publicKey) = GeneratePairingKey();
        await InstallPublicKeyAsync(client, publicKey, cancellationToken);
        var pairing = new N9Pairing(
            DefaultUsbHost,
            DefaultPort,
            DefaultUserName,
            acceptedKey.Algorithm,
            acceptedKey.Sha256Fingerprint,
            privateKey);

        using (var keyClient = new SshClient(CreateKeyConnectionInfo(pairing)))
        {
            PinHostKey(keyClient, pairing);
            await ConnectAsync(keyClient, cancellationToken);
            var keyStatus = await ProbeAsync(keyClient, cancellationToken);
            if (!keyStatus.IsHarmattan)
            {
                throw new N9ConnectionException(
                    "La clé d’appairage a été installée, mais le téléphone n’a pas confirmé Harmattan.");
            }
        }

        _pairingStore.Save(pairing);
        return status with { IsPaired = true };
    }

    public async Task<N9ConnectionStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var pairing = _pairingStore.TryLoad();
        if (pairing is null)
        {
            return new(
                false,
                false,
                false,
                "Ouvrez SDK Connectivity sur le N9, choisissez USB, puis utilisez « Appairer le N9 ».");
        }

        try
        {
            using var client = new SshClient(CreateKeyConnectionInfo(pairing));
            PinHostKey(client, pairing);
            await ConnectAsync(client, cancellationToken);
            return await ProbeAsync(client, cancellationToken) with { IsPaired = true };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (N9ConnectionException exception) when (
            exception.Message.Contains("empreinte", StringComparison.OrdinalIgnoreCase))
        {
            throw;
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            return new(
                true,
                false,
                false,
                "Le N9 est appairé, mais sa liaison développeur ne répond pas. Laissez SDK Connectivity ouvert et choisissez le mode USB SDK.");
        }
    }

    public void ForgetPairing() => _pairingStore.Forget();

    public async Task<N9DeviceDetails> ReadDeviceDetailsAsync(
        CancellationToken cancellationToken = default)
    {
        var pairing = _pairingStore.TryLoad() ?? throw new N9ConnectionException(
            "Le Nokia N9 doit d’abord être appairé avec ResurectPhone.");

        using var client = new SshClient(CreateKeyConnectionInfo(pairing));
        PinHostKey(client, pairing);
        await ConnectAsync(client, cancellationToken);
        using var command = client.CreateCommand(
            "LC_ALL=C; " +
            "software=$(cat /etc/osso_software_version 2>/dev/null | head -n 1); " +
            "if test -z \"$software\" && command -v sysinfoclient >/dev/null 2>&1; then software=$(sysinfoclient -p /device/sw-release-ver 2>/dev/null); fi; " +
            "printf 'HOSTNAME\\t%s\\n' \"$(hostname 2>/dev/null | head -n 1)\"; " +
            "printf 'PRODUCT_NAME\\t%s\\n' \"$(sysinfoclient -p /component/product-name 2>/dev/null)\"; " +
            "printf 'PRODUCT_CODE\\t%s\\n' \"$(sysinfoclient -p /component/product 2>/dev/null)\"; " +
            "printf 'HARMATTAN\\t'; if test -e /etc/harmattan-release || test -e /etc/osso_software_version || printf '%s' \"$software\" | grep -qi harmattan; then printf '1'; else printf '0'; fi; printf '\\n'; " +
            "printf 'RELEASE\\t%s\\n' \"$(cat /etc/harmattan-release /etc/meego-release /etc/issue 2>/dev/null | sed -n '/[^[:space:]]/{p;q;}')\"; " +
            "printf 'SYSTEM_ID\\t%s\\n' \"$(sed -n 's/^ID=//p' /etc/os-release 2>/dev/null | head -n 1 | tr -d '\"')\"; " +
            "printf 'PRETTY_NAME\\t%s\\n' \"$(sed -n 's/^PRETTY_NAME=//p' /etc/os-release 2>/dev/null | head -n 1 | tr -d '\"')\"; " +
            "printf 'VERSION_ID\\t%s\\n' \"$(sed -n 's/^VERSION_ID=//p' /etc/os-release 2>/dev/null | head -n 1 | tr -d '\"')\"; " +
            "printf 'SOFTWARE_VERSION\\t%s\\n' \"$software\"; " +
            "printf 'KERNEL\\t%s\\n' \"$(uname -r 2>/dev/null)\"; " +
            "printf 'ARCHITECTURE\\t%s\\n' \"$(uname -m 2>/dev/null)\"");
        command.CommandTimeout = OperationTimeout;
        await command.ExecuteAsync(cancellationToken);
        if (command.ExitStatus is not 0)
            throw new N9ConnectionException("Le Nokia N9 n’a pas communiqué son identité système.");

        var details = ParseDeviceDetails(command.Result);
        if (!details.SystemName.Contains("Harmattan", StringComparison.OrdinalIgnoreCase))
            throw new N9ConnectionException("Le téléphone connecté n’a pas confirmé MeeGo Harmattan.");
        return details;
    }

    internal static N9DeviceDetails ParseDeviceDetails(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in output.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var separator = rawLine.IndexOf('\t');
            if (separator <= 0)
                continue;
            values[rawLine[..separator]] = rawLine[(separator + 1)..].Trim();
        }

        string Value(string key) => values.GetValueOrDefault(key, string.Empty);

        var release = Value("RELEASE");
        var prettyName = Value("PRETTY_NAME");
        var softwareVersion = Value("SOFTWARE_VERSION");
        var harmattan = Value("HARMATTAN") == "1" ||
            release.Contains("Harmattan", StringComparison.OrdinalIgnoreCase) ||
            softwareVersion.Contains("Harmattan", StringComparison.OrdinalIgnoreCase);
        var version = Value("VERSION_ID");
        if (string.IsNullOrWhiteSpace(version) && release.Length > 0)
        {
            var match = Regex.Match(
                release,
                @"\b\d+\.\d+(?:\.\d+)?\b",
                RegexOptions.CultureInvariant);
            if (match.Success)
                version = match.Value;
        }
        if (string.IsNullOrWhiteSpace(version) && harmattan)
            version = "1.2";

        var fallbackName = string.IsNullOrWhiteSpace(prettyName)
            ? string.IsNullOrWhiteSpace(release) ? Value("SYSTEM_ID") : release
            : prettyName;

        return new N9DeviceDetails
        {
            Hostname = Value("HOSTNAME"),
            ProductName = string.IsNullOrWhiteSpace(Value("PRODUCT_NAME")) ? "Nokia N9" : Value("PRODUCT_NAME"),
            ProductCode = Value("PRODUCT_CODE"),
            SystemName = harmattan ? "MeeGo Harmattan" : fallbackName,
            SystemVersion = version,
            SystemBuild = softwareVersion,
            KernelVersion = Value("KERNEL"),
            Architecture = Value("ARCHITECTURE")
        };
    }

    internal static (string PrivateKey, string PublicKey) GeneratePairingKey()
    {
        using var rsa = RSA.Create(2048);
        var publicParameters = rsa.ExportParameters(includePrivateParameters: false);
        var privateKeyBytes = rsa.ExportRSAPrivateKey();
        try
        {
            var privateKey = PemEncoding.WriteString("RSA PRIVATE KEY", privateKeyBytes);
            using var publicKey = new MemoryStream();
            WriteSshValue(publicKey, "ssh-rsa"u8);
            WriteSshValue(publicKey, ToSshMpint(publicParameters.Exponent!));
            WriteSshValue(publicKey, ToSshMpint(publicParameters.Modulus!));
            return (
                privateKey,
                $"ssh-rsa {Convert.ToBase64String(publicKey.ToArray())} resurectphone-n9");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
        }
    }

    private static async Task<N9ConnectionStatus> ProbeAsync(
        SshClient client,
        CancellationToken cancellationToken)
    {
        using var command = client.CreateCommand(
            "uname -s; " +
            "if test -e /etc/harmattan-release || test -e /etc/osso_software_version; then printf 'HARMATTAN_MARKER=1\\n'; fi; " +
            "(cat /etc/harmattan-release /etc/osso_software_version /etc/os-release /etc/issue 2>/dev/null) | head -n 16");
        command.CommandTimeout = OperationTimeout;
        await command.ExecuteAsync(cancellationToken);
        if (command.ExitStatus is not 0)
            throw new N9ConnectionException("Le Nokia N9 a refusé la vérification de sa liaison développeur.");

        var output = command.Result;
        var harmattan = output.Contains("Linux", StringComparison.OrdinalIgnoreCase) &&
            (output.Contains("Harmattan", StringComparison.OrdinalIgnoreCase) ||
             output.Contains("MeeGo", StringComparison.OrdinalIgnoreCase) ||
             output.Contains("HARMATTAN_MARKER=1", StringComparison.Ordinal));
        return new(
            false,
            true,
            harmattan,
            harmattan
                ? "Le Nokia N9 répond par sa liaison développeur USB."
                : "Le serveur SSH répond, mais Harmattan n’a pas été confirmé.",
            client.ConnectionInfo.ServerVersion ?? string.Empty,
            client.ConnectionInfo.CurrentKeyExchangeAlgorithm ?? string.Empty);
    }

    private static ConnectionInfo CreatePasswordConnectionInfo(string password)
    {
        var passwordMethod = new PasswordAuthenticationMethod(DefaultUserName, password);
        var keyboardMethod = new KeyboardInteractiveAuthenticationMethod(DefaultUserName);
        keyboardMethod.AuthenticationPrompt += (_, eventArgs) =>
        {
            foreach (var prompt in eventArgs.Prompts)
                prompt.Response = password;
        };
        return CreateConnectionInfo(passwordMethod, keyboardMethod);
    }

    private static ConnectionInfo CreateKeyConnectionInfo(N9Pairing pairing)
    {
        var keyBytes = Encoding.ASCII.GetBytes(pairing.PrivateKey);
        try
        {
            using var stream = new MemoryStream(keyBytes, writable: false);
            var keyMethod = new PrivateKeyAuthenticationMethod(
                pairing.UserName,
                new PrivateKeyFile(stream));
            return new ConnectionInfo(
                pairing.Host,
                pairing.Port,
                pairing.UserName,
                keyMethod)
            {
                Timeout = ConnectionTimeout,
                RetryAttempts = 1,
                Encoding = Encoding.UTF8
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static ConnectionInfo CreateConnectionInfo(params AuthenticationMethod[] methods) =>
        new(DefaultUsbHost, DefaultPort, DefaultUserName, methods)
        {
            Timeout = ConnectionTimeout,
            RetryAttempts = 1,
            Encoding = Encoding.UTF8
        };

    private static async Task InstallPublicKeyAsync(
        SshClient client,
        string publicKey,
        CancellationToken cancellationToken)
    {
        if (!publicKey.StartsWith("ssh-rsa ", StringComparison.Ordinal) || publicKey.Contains('\''))
            throw new InvalidDataException("La clé publique générée pour le Nokia N9 est invalide.");

        using var command = client.CreateCommand(
            "umask 077; key='" + publicKey + "'; file=\"$HOME/.ssh/authorized_keys2\"; " +
            "mkdir -p \"$HOME/.ssh\" && chmod 700 \"$HOME/.ssh\" && " +
            "touch \"$file\" && chmod 600 \"$file\" && " +
            "{ grep -qxF \"$key\" \"$file\" || printf '%s\\n' \"$key\" >> \"$file\"; }");
        command.CommandTimeout = OperationTimeout;
        await command.ExecuteAsync(cancellationToken);
        if (command.ExitStatus is not 0)
            throw new N9ConnectionException("Le Nokia N9 a refusé l’installation de la clé d’appairage.");
    }

    private static void PinHostKey(BaseClient client, N9Pairing pairing)
    {
        client.HostKeyReceived += (_, eventArgs) =>
        {
            eventArgs.CanTrust = eventArgs.HostKeyName.Equals(
                    pairing.HostKeyName,
                    StringComparison.Ordinal) &&
                eventArgs.FingerPrintSHA256.Equals(
                    pairing.HostKeySha256,
                    StringComparison.Ordinal);
        };
    }

    private static N9HostKeyIdentity ToIdentity(HostKeyEventArgs eventArgs) =>
        new(eventArgs.HostKeyName, eventArgs.FingerPrintSHA256, eventArgs.KeyLength);

    private static async Task ConnectAsync(BaseClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.ConnectAsync(cancellationToken);
        }
        catch (SshAuthenticationException exception)
        {
            throw new N9ConnectionException(
                "Le mot de passe SDK du Nokia N9 a été refusé. Recopiez celui affiché actuellement par SDK Connectivity.",
                exception);
        }
        catch (SshConnectionException exception) when (
            exception.Message.Contains("host key", StringComparison.OrdinalIgnoreCase))
        {
            throw new N9ConnectionException(
                "L’empreinte SSH du Nokia N9 ne correspond plus à celle qui a été enregistrée.",
                exception);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            throw new N9ConnectionException(
                "La liaison développeur du Nokia N9 ne répond pas. Ouvrez SDK Connectivity et choisissez le mode USB SDK.",
                exception);
        }
    }

    private static bool IsConnectionFailure(Exception exception) =>
        exception is SshException or SocketException or IOException or TimeoutException;

    private static byte[] ToSshMpint(byte[] value)
    {
        var first = 0;
        while (first < value.Length - 1 && value[first] == 0)
            first++;
        var prependZero = (value[first] & 0x80) != 0;
        var result = new byte[value.Length - first + (prependZero ? 1 : 0)];
        value.AsSpan(first).CopyTo(result.AsSpan(prependZero ? 1 : 0));
        return result;
    }

    private static void WriteSshValue(Stream destination, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        var size = value.Length;
        length[0] = (byte)(size >> 24);
        length[1] = (byte)(size >> 16);
        length[2] = (byte)(size >> 8);
        length[3] = (byte)size;
        destination.Write(length);
        destination.Write(value);
    }
}
