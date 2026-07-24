using MsixCore.Deployment;

namespace MsixCore.Deployment.Tests;

public class ScaffoldTests
{
    [Fact]
    public void DeploymentOptions_AreFlags()
    {
        var combined = DeploymentOptions.ForceApplicationShutdown | DeploymentOptions.ExtractOnly;
        Assert.True(combined.HasFlag(DeploymentOptions.ExtractOnly));
        Assert.True(combined.HasFlag(DeploymentOptions.ForceApplicationShutdown));
        Assert.Equal(DeploymentOptions.None, DeploymentOptions.None);
    }

    [Fact]
    public void InstallationStep_HasOrderedStages()
    {
        Assert.True((int)InstallationStep.Started < (int)InstallationStep.Extraction);
        Assert.True((int)InstallationStep.Extraction < (int)InstallationStep.Completed);
    }

    [Fact]
    public void PackageManager_AddRemove_NotYetImplemented_UntilPhase5()
    {
        var manager = new PackageManager();

        // Contract guards: flip to real behavior in Phase 5 (deployment engine).
        Assert.Throws<NotImplementedException>(() => manager.AddPackage("nonexistent.msix"));
        Assert.Throws<NotImplementedException>(() => manager.RemovePackage("Contoso.MyApp_1.0.0.0_x64__abc"));
    }
}
