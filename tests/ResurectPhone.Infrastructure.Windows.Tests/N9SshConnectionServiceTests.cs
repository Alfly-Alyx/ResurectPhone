using System.Security.Cryptography;
using System.Text;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9SshConnectionServiceTests
{
    [Fact]
    public void ParseDeviceDetails_IdentifiesHarmattanPr13()
    {
        var details = N9SshConnectionService.ParseDeviceDetails("""
            HOSTNAME	Nokia-N9
            PRODUCT_NAME	N9
            PRODUCT_CODE	RM-696
            HARMATTAN	1
            RELEASE	MeeGo 1.2 Harmattan
            SYSTEM_ID	meego
            SOFTWARE_VERSION	PR1.3_40.2012.21-3_PR_001
            KERNEL	2.6.32.54-dfl61-20121301
            ARCHITECTURE	armv7l
            """);

        Assert.Equal("N9", details.ProductName);
        Assert.Equal("RM-696", details.ProductCode);
        Assert.Equal("MeeGo Harmattan", details.SystemName);
        Assert.Equal("1.2", details.SystemVersion);
        Assert.Equal("PR1.3_40.2012.21-3_PR_001", details.SystemBuild);
        Assert.Equal("2.6.32.54-dfl61-20121301", details.KernelVersion);
        Assert.Equal("armv7l", details.Architecture);
    }

    [Fact]
    public void GeneratedPrivateKey_MatchesOpenSshPublicKey()
    {
        var pair = N9SshConnectionService.GeneratePairingKey();
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pair.PrivateKey);
        var expected = rsa.ExportParameters(includePrivateParameters: false);

        var parts = pair.PublicKey.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("ssh-rsa", parts[0]);
        Assert.Equal("resurectphone-n9", parts[2]);

        var blob = Convert.FromBase64String(parts[1]);
        var offset = 0;
        Assert.Equal("ssh-rsa", Encoding.ASCII.GetString(ReadSshValue(blob, ref offset)));
        Assert.Equal(TrimLeadingZeros(expected.Exponent!), TrimLeadingZeros(ReadSshValue(blob, ref offset)));
        Assert.Equal(TrimLeadingZeros(expected.Modulus!), TrimLeadingZeros(ReadSshValue(blob, ref offset)));
        Assert.Equal(blob.Length, offset);
    }

    private static byte[] ReadSshValue(byte[] source, ref int offset)
    {
        var length = source[offset] << 24 |
            source[offset + 1] << 16 |
            source[offset + 2] << 8 |
            source[offset + 3];
        offset += 4;
        var value = source.AsSpan(offset, length).ToArray();
        offset += length;
        return value;
    }

    private static byte[] TrimLeadingZeros(byte[] value)
    {
        var offset = 0;
        while (offset < value.Length - 1 && value[offset] == 0)
            offset++;
        return value.AsSpan(offset).ToArray();
    }
}
