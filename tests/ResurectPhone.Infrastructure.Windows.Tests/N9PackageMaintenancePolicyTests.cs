using ResurectPhone.Core.NokiaN9;
using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9PackageMaintenancePolicyTests
{
    [Theory]
    [InlineData("yes", "optional", true)]
    [InlineData("no", "required", true)]
    [InlineData("no", "important", true)]
    [InlineData("no", "standard", false)]
    [InlineData("no", "optional", false)]
    public void Protection_UsesEssentialAndRequiredOrImportantPriorityOnly(
        string essential,
        string priority,
        bool expected)
    {
        var metadata = new N9DebPackageMetadata(
            "twitter-qml", "1.0", "armel", essential, priority, IsUserVisible: true);

        Assert.Equal(expected, N9PackageMaintenancePolicy.IsProtectedPackage(metadata));
    }

    [Fact]
    public void MetaPackageDependency_DoesNotBlockExpertRemovalByItself()
    {
        var metadata = new N9DebPackageMetadata(
            "twitter-qml", "1.0", "armel", "no", "optional", IsUserVisible: true);

        var command = N9PackageCommands.BuildExpertRemovalCommand(metadata, expertForceConfirmed: true);

        Assert.Equal("dpkg --force-depends --remove twitter-qml", command);
    }

    [Fact]
    public void Companion_IsAlwaysProtected()
    {
        var metadata = new N9DebPackageMetadata(
            N9PackageMaintenancePolicy.CompanionPackage,
            "1.0",
            "armel");

        Assert.True(N9PackageMaintenancePolicy.IsProtectedPackage(metadata));
        Assert.Throws<InvalidOperationException>(() =>
            N9PackageCommands.BuildStandardRemovalCommand(metadata));
    }

    [Fact]
    public void PackageNotListedAsAVisibleApplication_CannotBeRemoved()
    {
        var metadata = new N9DebPackageMetadata(
            "background-service", "1.0", "armel", "no", "optional", IsUserVisible: false);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            N9PackageCommands.BuildExpertRemovalCommand(metadata, expertForceConfirmed: true));

        Assert.Contains("application visible", exception.Message);
    }

    [Fact]
    public void ParseControl_RequiresN9ArchitectureAndReadsSafetyFields()
    {
        var metadata = N9PackageMaintenancePolicy.ParseControl("""
            Package: calc
            Version: 1.2.0.2+0m7
            Architecture: armel
            Priority: optional
            Essential: no
            Description: Calculator
             for Harmattan
            """);

        Assert.Equal("calc", metadata.Package);
        Assert.Equal("1.2.0.2+0m7", metadata.Version);
        Assert.Equal("armel", metadata.Architecture);
        Assert.False(N9PackageMaintenancePolicy.IsProtectedPackage(metadata));
    }

    [Fact]
    public void ParseControl_RejectsAnIncompatibleArchitecture()
    {
        Assert.Throws<InvalidDataException>(() => N9PackageMaintenancePolicy.ParseControl("""
            Package: calc
            Version: 1.0
            Architecture: amd64
            """));
    }

    [Fact]
    public void RebuiltPackageWithoutAegisOrigin_IsNotOfferedForAutomaticRestore()
    {
        var metadata = new N9DebPackageMetadata("calc", "1.2.0.2+0m7", "armel");

        var assessment = N9PackageMaintenancePolicy.AssessRebuiltBackup(metadata);

        Assert.Equal(N9DebRestoreConfidence.DebianArchiveOnly, assessment.RestoreConfidence);
        Assert.False(assessment.CanOfferAutomaticRestore);
        Assert.Contains("Aegis", assessment.Detail);
    }

    [Fact]
    public void SelfDeclaredAegisOrigin_DoesNotEnableRestoreWithoutExternalValidation()
    {
        var metadata = new N9DebPackageMetadata(
            "calc", "1.2.0.2+0m7", "armel", AegisOrigin: "com.nokia.maemo");

        var unverified = N9PackageMaintenancePolicy.AssessRebuiltBackup(metadata);
        var notReinstalled = N9PackageMaintenancePolicy.AssessRebuiltBackup(
            metadata,
            aegisOriginPreservationVerified: true);
        var validated = N9PackageMaintenancePolicy.AssessRebuiltBackup(
            metadata,
            aegisOriginPreservationVerified: true,
            reinstallationValidatedOnN9: true);

        Assert.False(unverified.CanOfferAutomaticRestore);
        Assert.False(notReinstalled.CanOfferAutomaticRestore);
        Assert.True(validated.CanOfferAutomaticRestore);
    }

    [Fact]
    public void InstallationOfARebuiltPackage_IsBlockedUntilAegisValidation()
    {
        var metadata = new N9DebPackageMetadata("calc", "1.2.0.2+0m7", "armel");
        var assessment = N9PackageMaintenancePolicy.AssessRebuiltBackup(metadata);

        Assert.Throws<InvalidOperationException>(() => N9PackageCommands.BuildInstallCommand(
            "/var/tmp/resurectphone-123/calc.deb",
            metadata,
            assessment));
    }

    [Fact]
    public void HarmattanCommands_UseBusyBoxAndVarTmp()
    {
        var script = N9PackageCommands.ExportScript;
        var inspect = N9PackageCommands.BuildInspectControlCommand(
            "/var/tmp/resurectphone-123/package.deb");

        Assert.Contains("/var/tmp/resurectphone-", script);
        Assert.Contains("busybox tar", script);
        Assert.DoesNotContain("dpkg-deb --build", script);
        Assert.DoesNotContain('\r', script);
        Assert.Equal(
            "busybox ar -p '/var/tmp/resurectphone-123/package.deb' control.tar.gz | busybox tar -xzOf - ./control",
            inspect);
        Assert.DoesNotContain("dpkg-deb -f", inspect);
    }

    [Fact]
    public async Task AdminPassword_IsWrittenOnlyToStandardInputAndThenErased()
    {
        var characters = "test-admin-password".ToCharArray();
        var password = N9PackageCommands.EncodeAdminPassword(characters);
        await using var input = new MemoryStream();

        await N9PackageCommands.SendPasswordToStandardInputAsync(input, password);

        Assert.Equal("test-admin-password\n", System.Text.Encoding.UTF8.GetString(input.ToArray()));
        Assert.All(characters, value => Assert.Equal('\0', value));
        Assert.All(password, value => Assert.Equal(0, value));
    }
}
