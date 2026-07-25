using BenchmarkDotNet.Running;

namespace MsixCore.Benchmarks;

/// <summary>Entry point that dispatches to BenchmarkDotNet's switcher.</summary>
internal static class Program
{
    private static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
