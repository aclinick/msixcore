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
}
