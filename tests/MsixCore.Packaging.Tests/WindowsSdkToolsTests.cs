using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MsixCore.Packaging.Tests;

public class WindowsSdkToolsTests
{
    [Fact]
    public void ExecutableArchitectures_PrefersHostArchitectureFirst()
    {
        string[] architectures = WindowsSdkTools.ExecutableArchitectures();

        switch (RuntimeInformation.OSArchitecture)
        {
            case Architecture.Arm64:
                Assert.Equal("arm64", architectures[0]);
                break;
            case Architecture.X64:
                Assert.Equal("x64", architectures[0]);
                break;
            default:
                // Only arm64 and x64 are supported hosts; anything else runs nothing.
                Assert.Empty(architectures);
                break;
        }
    }

    [Fact]
    public void ExecutableArchitectures_NeverPrefersAnArchitectureTheHostCannotRun()
    {
        // An x64 host cannot execute the arm64 SDK binaries. Listing them made makeappx-backed tests
        // fail with a Win32Exception on x64 CI while passing on an arm64 development machine.
        string[] architectures = WindowsSdkTools.ExecutableArchitectures();

        if (RuntimeInformation.OSArchitecture != Architecture.Arm64)
        {
            Assert.DoesNotContain("arm64", architectures);
        }

        // x64 emulation on ARM64 requires Windows 11.
        if (RuntimeInformation.OSArchitecture == Architecture.Arm64
            && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.DoesNotContain("x64", architectures);
        }
    }

    [Fact]
    public void ExecutableArchitectures_NeverRunsThe32BitTools()
    {
        // The tools are 64-bit only. This says nothing about the packages they handle: an x86
        // package, or a package carrying x86 binaries, is still fully supported.
        string[] architectures = WindowsSdkTools.ExecutableArchitectures();

        Assert.DoesNotContain("x86", architectures);
        Assert.DoesNotContain("arm", architectures);
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
