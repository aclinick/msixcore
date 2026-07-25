using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace MsixCore.CorpusRoundtrip;

internal static class AppxOsReader
{
    private static readonly Guid AppxFactoryClsid = new("5842A140-FF9F-4166-8F5C-62F5B7B0C781");

    public static void Read(string path)
    {
        string fullPath = Path.GetFullPath(path);
        SHCreateStreamOnFileEx(fullPath, 0, 0, false, null, out IStream stream);
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(AppxFactoryClsid, throwOnError: true)!)!;
        var factory = (IAppxFactory)instance;
        factory.CreatePackageReader(stream, out IAppxPackageReader reader);

        reader.GetManifest(out IAppxManifestReader manifest);
        manifest.GetPackageId(out IAppxManifestPackageId packageId);
        packageId.GetPackageFullName(out string fullName);
        packageId.GetName(out string name);
        packageId.GetPublisher(out string publisher);
        packageId.GetVersion(out ulong version);

        reader.GetPayloadFiles(out IAppxFilesEnumerator payloadFiles);
        int payloadCount = CountFiles(payloadFiles);

        reader.GetBlockMap(out IAppxBlockMapReader blockMap);
        blockMap.GetStream(out IStream blockMapStream);
        _ = ReadOneByte(blockMapStream);

        Console.WriteLine("OS AppxPackaging reader: OK");
        Console.WriteLine($"  FullName={fullName}");
        Console.WriteLine($"  Name={name}");
        Console.WriteLine($"  Publisher={publisher}");
        Console.WriteLine($"  Version=0x{version:X16}");
        Console.WriteLine($"  PayloadFiles={payloadCount}");
        Console.WriteLine("  BlockMap=read");
    }

    private static int CountFiles(IAppxFilesEnumerator files)
    {
        int count = 0;
        files.GetHasCurrent(out bool hasCurrent);
        while (hasCurrent)
        {
            files.GetCurrent(out IAppxFile file);
            file.GetName(out _);
            count++;
            files.MoveNext(out hasCurrent);
        }

        return count;
    }

    private static int ReadOneByte(IStream stream)
    {
        byte[] buffer = new byte[1];
        nint bytesRead = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            stream.Read(buffer, 1, bytesRead);
            return Marshal.ReadInt32(bytesRead);
        }
        finally
        {
            Marshal.FreeHGlobal(bytesRead);
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern void SHCreateStreamOnFileEx(
        string fileName,
        uint grfMode,
        uint attributes,
        [MarshalAs(UnmanagedType.Bool)] bool create,
        IStream? template,
        out IStream stream);
}

[ComImport]
[Guid("BEB94909-E451-438B-B5A7-D79E767B75D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxFactory
{
    void CreatePackageWriter(IStream outputStream, nint settings, out object packageWriter);

    void CreatePackageReader(IStream inputStream, out IAppxPackageReader packageReader);

    void CreateManifestReader(IStream inputStream, out IAppxManifestReader manifestReader);

    void CreateBlockMapReader(IStream inputStream, out IAppxBlockMapReader blockMapReader);

    void CreateValidatedBlockMapReader(IStream blockMapStream, string signatureFileName, out IAppxBlockMapReader blockMapReader);
}

[ComImport]
[Guid("B5C49650-99BC-481C-9A34-3D53A4106708")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxPackageReader
{
    void GetBlockMap(out IAppxBlockMapReader blockMapReader);

    void GetFootprintFile(int type, out IAppxFile file);

    void GetPayloadFile([MarshalAs(UnmanagedType.LPWStr)] string fileName, out IAppxFile file);

    void GetPayloadFiles(out IAppxFilesEnumerator filesEnumerator);

    void GetManifest(out IAppxManifestReader manifestReader);
}

[ComImport]
[Guid("F007EEAF-9831-411C-9847-917CDC62D1FE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxFilesEnumerator
{
    void GetCurrent(out IAppxFile file);

    void GetHasCurrent([MarshalAs(UnmanagedType.Bool)] out bool hasCurrent);

    void MoveNext([MarshalAs(UnmanagedType.Bool)] out bool hasNext);
}

[ComImport]
[Guid("91DF827B-94FD-468F-827B-57F41B2F6F2E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxFile
{
    void GetCompressionOption(out int compressionOption);

    void GetContentType([MarshalAs(UnmanagedType.LPWStr)] out string contentType);

    void GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);

    void GetSize(out ulong size);

    void GetStream(out IStream stream);
}

[ComImport]
[Guid("4E1BD148-55A0-4480-A3D1-15544710637C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxManifestReader
{
    void GetPackageId(out IAppxManifestPackageId packageId);
}

[ComImport]
[Guid("283CE2D7-7153-4A91-9649-7A0F7240945F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxManifestPackageId
{
    void GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);

    void GetArchitecture(out int architecture);

    void GetPublisher([MarshalAs(UnmanagedType.LPWStr)] out string publisher);

    void GetVersion(out ulong packageVersion);

    void GetResourceId([MarshalAs(UnmanagedType.LPWStr)] out string resourceId);

    void ComparePublisher([MarshalAs(UnmanagedType.LPWStr)] string other, [MarshalAs(UnmanagedType.Bool)] out bool isSame);

    void GetPackageFullName([MarshalAs(UnmanagedType.LPWStr)] out string packageFullName);

    void GetPackageFamilyName([MarshalAs(UnmanagedType.LPWStr)] out string packageFamilyName);
}

[ComImport]
[Guid("5EFEC991-BCA3-42D1-9EC2-E92D609EC22A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAppxBlockMapReader
{
    void GetFile([MarshalAs(UnmanagedType.LPWStr)] string filename, out object file);

    void GetFiles(out object enumerator);

    void GetHashMethod([MarshalAs(UnmanagedType.LPWStr)] out string hashMethod);

    void GetStream(out IStream stream);
}
