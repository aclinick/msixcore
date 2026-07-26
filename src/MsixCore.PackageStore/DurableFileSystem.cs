using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MsixCore.PackageStore;

internal interface IDurableFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    IEnumerable<string> EnumerateFileSystemEntries(string path);

    FileAttributes GetAttributes(string path);

    void FlushFile(string path);

    void MoveFile(string source, string destination);

    void ReplaceFile(string source, string destination);

    void MoveDirectory(string source, string destination);

    void DeleteFile(string path);

    void DeleteDirectory(string path, bool recursive);

    void FlushDirectory(string path);

    void CommitPoint(CommitFaultPoint point);
}

internal sealed class DurableFileSystem : IDurableFileSystem
{
    private const uint MoveFileWriteThrough = 0x00000008;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private static readonly nint InvalidHandleValue = new(-1);
    private static readonly Lazy<MoveFileExDelegate> MoveFileExFunction =
        new(() => LoadFunction<MoveFileExDelegate>(["kernel32.dll"], "MoveFileExW"));
    private static readonly Lazy<CreateFileDelegate> CreateFileFunction =
        new(() => LoadFunction<CreateFileDelegate>(["kernel32.dll"], "CreateFileW"));
    private static readonly Lazy<FlushFileBuffersDelegate> FlushFileBuffersFunction =
        new(() => LoadFunction<FlushFileBuffersDelegate>(["kernel32.dll"], "FlushFileBuffers"));
    private static readonly Lazy<CloseHandleDelegate> CloseHandleFunction =
        new(() => LoadFunction<CloseHandleDelegate>(["kernel32.dll"], "CloseHandle"));
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

    public IEnumerable<string> EnumerateFileSystemEntries(string path) =>
        Directory.EnumerateFileSystemEntries(path);

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public void FlushFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        stream.Flush(flushToDisk: true);
    }

    public void MoveFile(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            MoveWindows(source, destination, replaceExisting: false);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    public void ReplaceFile(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            MoveWindows(source, destination, replaceExisting: true);
        }
        else
        {
            File.Move(source, destination, overwrite: true);
        }
    }

    public void MoveDirectory(string source, string destination)
    {
        Directory.Move(source, destination);
    }

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void FlushDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            FlushWindowsDirectory(path);
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

    private static void MoveWindows(string source, string destination, bool replaceExisting)
    {
        uint flags = MoveFileWriteThrough | (replaceExisting ? 0x00000001u : 0u);
        if (!MoveFileExFunction.Value(source, destination, flags))
        {
            throw NativeIOException($"Could not durably rename '{source}' to '{destination}'.");
        }
    }

    private static void FlushWindowsDirectory(string path)
    {
        nint handle = CreateFileFunction.Value(
            path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite | FileShareDelete,
            0,
            OpenExisting,
            FileFlagBackupSemantics,
            0);
        if (handle == InvalidHandleValue)
        {
            throw NativeIOException($"Could not open directory '{path}' for synchronization.");
        }

        try
        {
            if (!FlushFileBuffersFunction.Value(handle))
            {
                throw NativeIOException($"Could not synchronize directory '{path}'.");
            }
        }
        finally
        {
            _ = CloseHandleFunction.Value(handle);
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
    private delegate nint CreateFileDelegate(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool FlushFileBuffersDelegate(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool CloseHandleDelegate(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    private delegate int OpenDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    private delegate int FsyncDelegate(int descriptor);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    private delegate int CloseDelegate(int descriptor);
}
