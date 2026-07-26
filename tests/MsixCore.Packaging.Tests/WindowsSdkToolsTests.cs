using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MsixCore.Packaging.Tests;

public class WindowsSdkToolsTests
{
    [Fact]
    public void ExecutableArchitectures_PrefersHostArchitectureFirst()
    {
        string expected = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "x64",
        };

        Assert.Equal(expected, WindowsSdkTools.ExecutableArchitectures()[0]);
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

        if (RuntimeInformation.OSArchitecture == Architecture.X86)
        {
            Assert.DoesNotContain("x64", architectures);
        }
    }

    [Fact]
    public void FindMakeAppx_WhenFound_ReturnsAnExecutableThisHostCanStart()
    {
        if (WindowsSdkTools.FindMakeAppx() is not { } makeAppx)
        {
            return;
        }

        // Starting it is the only way to prove the architecture choice is right; an unusable binary
        // throws Win32Exception here rather than reporting a usage error.
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = makeAppx,
            Arguments = "/?",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });

        Assert.NotNull(process);
        process.WaitForExit();
    }
}
