namespace MsixCore.Packaging;

/// <summary>Thrown when an MSIX container is opened through an API for a different container type.</summary>
public sealed class MsixPackageTypeException : InvalidOperationException
{
    /// <summary>Creates an exception describing a package/container type mismatch.</summary>
    public MsixPackageTypeException(string message)
        : base(message)
    {
    }
}
