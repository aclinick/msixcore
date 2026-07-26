using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MsixCore.Packaging.Tests;

public class WindowsSdkToolsTests
{
    [Fact]
    public void ExecutableArchitectures_IsExactlyTheNativeArchitecture()
    {
        string[] architectures = WindowsSdkTools.ExecutableArchitectures();

        switch (RuntimeInformation.OSArchitecture)
        {
            case Architecture.Arm64:
                // Never the emulated x64 build, even though Windows 11 could run it.
                Assert.Equal(["arm64"], architectures);
                break;
            case Architecture.X64:
                Assert.Equal(["x64"], architectures);
                break;
            default:
                // Only arm64 and x64 are supported hosts; anything else runs nothing.
                Assert.Empty(architectures);
                break;
        }
    }

    [Fact]
    public void ExecutableArchitectures_NeverRunsAnEmulatedOrNon64BitTool()
    {
        // An x64 host cannot execute the arm64 SDK binaries at all, and an arm64 host must not fall
        // back to emulated x64: the toolset is always native. The first of those made makeappx-backed
        // tests fail with a Win32Exception on x64 CI while passing on an arm64 development machine.
        string[] architectures = WindowsSdkTools.ExecutableArchitectures();

        Assert.True(architectures.Length <= 1);
        Assert.DoesNotContain("x86", architectures);
        Assert.DoesNotContain("arm", architectures);

        if (RuntimeInformation.OSArchitecture != Architecture.Arm64)
        {
            Assert.DoesNotContain("arm64", architectures);
        }

        if (RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            Assert.DoesNotContain("x64", architectures);
        }
    }

    [Fact]
    public async Task FindMakeAppx_WhenFound_ReturnsAnExecutableThisHostCanStart()
    {
        if (WindowsSdkTools.FindMakeAppx() is not { } makeAppx)
        {
            return;
        }

        // Starting it is the only way to prove the architecture choice is right; an unusable binary
        // throws Win32Exception here rather than reporting a usage error. Both streams are drained
        // before waiting, because a redirected stream that is never read deadlocks once the tool
        // writes more than the pipe buffer holds.
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = makeAppx,
            Arguments = "/?",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });

        Assert.NotNull(process);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Task.WhenAll(
            process.StandardOutput.ReadToEndAsync(timeout.Token),
            process.StandardError.ReadToEndAsync(timeout.Token));
        await process.WaitForExitAsync(timeout.Token);
    }
}
