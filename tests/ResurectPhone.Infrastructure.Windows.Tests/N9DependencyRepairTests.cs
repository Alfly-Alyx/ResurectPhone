using ResurectPhone.Infrastructure.Windows.NokiaN9;

namespace ResurectPhone.Infrastructure.Windows.Tests;

public sealed class N9DependencyRepairTests
{
    private const string Wrappers = "mp-harmattan-005-pr\t40.2012.21-3\tinstall ok installed\n" +
        "facebook\t1.3.0+0m8\tinstall ok installed\n" +
        "twitter\t1.3.50+0m8\tinstall ok installed\n";
    private static readonly string[] Packages = ["facebookqml=1.3.2+0m8", "twitter-qml=1.3.50+0m8"];
    private const string ValidPlan = "Inst facebookqml (1.3.2+0m8 mirror [armel])\nInst twitter-qml (1.3.50+0m8 mirror [armel])\n" +
        "Conf facebookqml (1.3.2+0m8 mirror [armel])\nConf twitter-qml (1.3.50+0m8 mirror [armel])\n";

    [Fact]
    public void MissingApplicationsArePinnedToTheCompatibleNokiaVersions() =>
        Assert.Equal(Packages, N9SshConnectionService.FindMissingNokiaApplications(Wrappers +
            "facebookqml\t1.4.0-1+0m7\tdeinstall ok config-files\ntwitter-qml\t\tunknown ok not-installed\n"));

    [Theory]
    [InlineData("install ok installed")]
    [InlineData("install ok half-configured")]
    [InlineData("install reinstreq half-installed")]
    [InlineData("hold ok installed")]
    public void ExistingOrPartlyInstalledApplicationsAreNotReplaced(string status) =>
        Assert.Empty(N9SshConnectionService.FindMissingNokiaApplications(Wrappers +
            $"facebookqml\t1.4.0\t{status}\ntwitter-qml\t1.3.95\t{status}\n"));

    [Theory]
    [InlineData("40.2012.21-3", "other-build")]
    [InlineData("mp-harmattan-005-pr", "mp-harmattan-other")]
    public void UnknownSystemBuildIsNotRepaired(string before, string after) =>
        Assert.Empty(N9SshConnectionService.FindMissingNokiaApplications(Wrappers.Replace(before, after)));

    [Fact]
    public void WrapperWithAnotherVersionIsNotRepaired() =>
        Assert.Equal(new[] { Packages[1] }, N9SshConnectionService.FindMissingNokiaApplications(Wrappers.Replace("1.3.0+0m8", "2.0")));

    [Fact]
    public void ExactSuccessfulPlanIsAccepted() => Assert.True(N9SshConnectionService.IsExactRepairPlan(0, ValidPlan, Packages));

    [Theory]
    [InlineData("Remv mp-harmattan-005-pr [40.2012.21-3]\n")]
    [InlineData("Inst unrelated (1.0 mirror [armel])\n")]
    [InlineData("Conf unrelated (1.0 mirror [armel])\n")]
    [InlineData("Inst facebookqml (1.3.2+0m8 mirror [armel])\n")]
    public void ExtraPackageChangesAndDuplicateActionsAreRejected(string extra) =>
        Assert.False(N9SshConnectionService.IsExactRepairPlan(0, ValidPlan + extra, Packages));

    [Fact]
    public void WrongVersionIsRejected() => Assert.False(N9SshConnectionService.IsExactRepairPlan(0,
        ValidPlan.Replace("1.3.50+0m8", "1.3.95"), Packages));

    [Fact]
    public void UpgradeInsteadOfFreshInstallationIsRejected() => Assert.False(N9SshConnectionService.IsExactRepairPlan(0,
        ValidPlan.Replace("Inst facebookqml (", "Inst facebookqml [1.3.0] ("), Packages));

    [Fact]
    public void FailedAndIncompletePlansAreRejected()
    {
        Assert.False(N9SshConnectionService.IsExactRepairPlan(100, ValidPlan, Packages));
        Assert.False(N9SshConnectionService.IsExactRepairPlan(0, "", Packages));
        Assert.False(N9SshConnectionService.IsExactRepairPlan(0, "", []));
    }
}
