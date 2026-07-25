using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MsixCore.Deployment;

internal interface IDurableFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    void MoveFile(string source, string destination);

    void MoveDirectory(string source, string destination);

    void DeleteFile(string path);

    void DeleteDirectory(string path, bool recursive);

    void FlushDirectory(string path);

    void CommitPoint(CommitFaultPoint point);
}

internal sealed class DurableFileSystem : IDurableFileSystem
{
    private const uint MoveFileWriteThrough = 0x00000008;
    private static readonly Lazy<MoveFileExDelegate> MoveFileExFunction =
        new(() => LoadFunction<MoveFileExDelegate>(["kernel32.dll"], "MoveFileExW"));
    private static readonly Lazy<OpenDelegate> OpenFunction =
        new(() => LoadFunction<OpenDelegate>(UnixLibraries(), "open"));
    private static readonly Lazy<FsyncDelegate> FsyncFunction =
        new(() => LoadFunction<FsyncDelegate>(UnixLibraries(), "fsync"));
    private static readonly Lazy<CloseDelegate> CloseFunction =
        new(() => LoadFunction<CloseDelegate>(UnixLibraries(), "close"));

    public static DurableFileSystem Instance { get; } = new();

    private DurableFileSystem()
    {
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void MoveFile(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            MoveWindows(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    public void MoveDirectory(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            MoveWindows(source, destination);
        }
        else
        {
            Directory.Move(source, destination);
        }
    }

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void FlushDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // MoveFileEx with MOVEFILE_WRITE_THROUGH makes each Windows rename durable before return.
            return;
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Durable directory synchronization is not supported.");
        }

        int descriptor = OpenFunction.Value(path, 0);
        if (descriptor < 0)
        {
            throw NativeIOException($"Could not open directory '{path}' for synchronization.");
        }

        try
        {
            if (FsyncFunction.Value(descriptor) != 0)
            {
                throw NativeIOException($"Could not synchronize directory '{path}'.");
            }
        }
        finally
        {
            _ = CloseFunction.Value(descriptor);
        }
    }

    public void CommitPoint(CommitFaultPoint point)
    {
    }

    private static void MoveWindows(string source, string destination)
    {
        if (!MoveFileExFunction.Value(source, destination, MoveFileWriteThrough))
        {
            throw NativeIOException($"Could not durably rename '{source}' to '{destination}'.");
        }
    }

    private static IOException NativeIOException(string message) =>
        new(message, new Win32Exception(Marshal.GetLastPInvokeError()));

    private static T LoadFunction<T>(IEnumerable<string> libraries, string name)
        where T : Delegate
    {
        foreach (string library in libraries)
        {
            if (!NativeLibrary.TryLoad(library, out nint handle))
            {
                continue;
            }

            if (NativeLibrary.TryGetExport(handle, name, out nint address))
            {
                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }

            NativeLibrary.Free(handle);
        }

        throw new PlatformNotSupportedException($"Native function '{name}' is unavailable.");
    }

    private static string[] UnixLibraries() =>
        OperatingSystem.IsMacOS()
            ? ["libSystem.B.dylib"]
            : ["libc", "libc.so.6"];

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool MoveFileExDelegate(string existingFileName, string newFileName, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    private delegate int OpenDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    private delegate int FsyncDelegate(int descriptor);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    private delegate int CloseDelegate(int descriptor);
}
