namespace MsixCore.Deployment.Tests;

public class MsixResponseTests
{
    [Fact]
    public async Task Complete_SetsCompletedStateAndCompletesTask()
    {
        using var response = new MsixResponse(CancellationToken.None);

        response.Complete();

        await response.Completion;
        Assert.Equal(InstallationStep.Completed, response.Status);
        Assert.Equal(100f, response.Percentage);
        Assert.Null(response.Failure);
    }

    [Fact]
    public void Report_UpdatesStateAndRaisesEvent()
    {
        using var response = new MsixResponse(CancellationToken.None);
        int raised = 0;
        response.ProgressChanged += (_, _) => raised++;

        response.Report(InstallationStep.Extraction, 42f, "Extracting");

        Assert.Equal(InstallationStep.Extraction, response.Status);
        Assert.Equal(42f, response.Percentage);
        Assert.Equal("Extracting", response.StatusText);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Report_ClampsPercentage()
    {
        using var response = new MsixResponse(CancellationToken.None);

        response.Report(InstallationStep.Extraction, 150f, "over");
        Assert.Equal(100f, response.Percentage);

        response.Report(InstallationStep.Extraction, -10f, "under");
        Assert.Equal(0f, response.Percentage);
    }

    [Fact]
    public async Task Fail_WithException_FaultsTask()
    {
        using var response = new MsixResponse(CancellationToken.None);
        var error = new InvalidOperationException("boom");

        response.Fail(error);

        Assert.Equal(InstallationStep.Error, response.Status);
        Assert.Same(error, response.Failure);
        InvalidOperationException thrown =
            await Assert.ThrowsAsync<InvalidOperationException>(() => response.Completion);
        Assert.Equal("boom", thrown.Message);
    }

    [Fact]
    public async Task Fail_WithCancellation_CancelsTask()
    {
        using var response = new MsixResponse(CancellationToken.None);

        response.Fail(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response.Completion);
    }

    [Fact]
    public void Cancel_SignalsToken()
    {
        using var response = new MsixResponse(CancellationToken.None);

        response.Cancel();

        Assert.True(response.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Report_AfterComplete_IsIgnored()
    {
        using var response = new MsixResponse(CancellationToken.None);
        response.Complete();
        await response.Completion;

        // A late progress update must not move a completed response back to an in-progress state.
        response.Report(InstallationStep.Extraction, 10f, "late");

        Assert.Equal(InstallationStep.Completed, response.Status);
        Assert.Equal(100f, response.Percentage);
    }

    [Fact]
    public async Task Fail_AfterComplete_IsIgnored()
    {
        using var response = new MsixResponse(CancellationToken.None);
        response.Complete();

        response.Fail(new InvalidOperationException("too late"));

        await response.Completion; // still a successful completion, not faulted
        Assert.Equal(InstallationStep.Completed, response.Status);
        Assert.Null(response.Failure);
    }

    [Fact]
    public async Task ThrowingSubscriber_DoesNotStrandCompletion()
    {
        using var response = new MsixResponse(CancellationToken.None);
        response.ProgressChanged += (_, _) => throw new InvalidOperationException("bad subscriber");

        response.Complete();

        // Completion was settled before subscriber notification, so it must not hang or fault.
        await response.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(InstallationStep.Completed, response.Status);
    }

    [Fact]
    public void ExternalCancellation_SignalsToken()
    {
        using var cts = new CancellationTokenSource();
        using var response = new MsixResponse(cts.Token);

        cts.Cancel();

        Assert.True(response.Token.IsCancellationRequested);
    }
}
