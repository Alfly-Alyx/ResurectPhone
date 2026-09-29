using System.Text;
using System.Text.Json;
using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9NetworkTests
{
    [Theory]
    [InlineData("192.168.1.9", true)]
    [InlineData("192.168.2.15", true)]
    [InlineData("10.0.2.6", true)]
    [InlineData("172.16.0.9", true)]
    [InlineData("172.32.0.9", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("example.org", false)]
    [InlineData("192.168.1.9; reboot", false)]
    public void SdkAddressMustBeLocalIpv4(string input, bool expected) =>
        Assert.Equal(expected, N9SshConnectionService.IsLocalAddress(input));

    [Fact]
    public void UnusualProfileNameIsDataNotAShellCommand()
    {
        const string id = "réseau'; $(reboot)\n\"";
        var encoded = N9SshConnectionService.NetworkRequest(new { action = "connect", id });
        using var decoded = JsonDocument.Parse(Convert.FromBase64String(encoded));
        Assert.Equal(id, decoded.RootElement.GetProperty("id").GetString());
        Assert.DoesNotContain("reboot", encoded);
    }

    [Fact]
    public void SnapshotDistinguishesConnectedManualUnknownAndForcedProfiles()
    {
        var state = N9SshConnectionService.ParseNetworkSnapshot("""
            {"profiles":[
              {"id":"a","name":"Café","security":"WPA_PSK","automatic":false,"forced":false,"connected":true},
              {"id":"b","name":"Maison","security":"WPA_PSK","automatic":true,"forced":true,"connected":false},
              {"id":"c","name":"Autre","security":"NONE","automatic":null,"forced":false,"connected":false}],
             "available":[{"name":"Café","signal":8},{"name":"Café","signal":7}],
             "address":"192.168.1.9","radios":15,"powerSaving":true,"searchInterval":60,"daemon":{"phase":"retrying"}}
            """, true);
        Assert.True(state.Profiles[0].Connected);
        Assert.False(state.Profiles[0].Automatic);
        Assert.True(state.Profiles[1].Forced);
        Assert.Null(state.Profiles[2].Automatic);
        Assert.True(state.WifiSdkPasswordless);
        Assert.True(state.PowerSaving);
        Assert.True(state.RadioEnabled);
        Assert.Single(state.Available);
    }

    [Fact]
    public void WifiCompanionCannotBeRemovedAsAnOrdinaryApplication()
    {
        var package = new N9DebPackageMetadata("resurectphone-n9-network", "0.1.0", "all", IsUserVisible: true);
        Assert.True(N9PackageMaintenancePolicy.IsProtectedPackage(package));
        Assert.NotNull(N9PackageMaintenancePolicy.GetRemovalBlockReason(package));
    }
}
