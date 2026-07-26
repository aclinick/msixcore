namespace MsixCore.Packaging;

/// <summary>
/// Creates and reads categorized MSIX format exceptions.
/// </summary>
/// <remarks>
/// Categories use <see cref="Exception.Data"/> because <see cref="InvalidDataException"/> is sealed
/// and cannot be subclassed. Changing the thrown type would break the published API contract and
/// callers that catch <see cref="InvalidDataException"/>.
/// </remarks>
public static class MsixError
{
    /// <summary>The public <see cref="Exception.Data"/> key containing a boxed <see cref="MsixErrorCode"/>.</summary>
    public const string ErrorCodeDataKey = "MsixCore.ErrorCode";

    /// <summary>Creates an <see cref="InvalidDataException"/> carrying the specified category.</summary>
    public static InvalidDataException Format(MsixErrorCode code, string message)
    {
        var exception = new InvalidDataException(message);
        exception.Data[ErrorCodeDataKey] = code;
        return exception;
    }

    /// <summary>Creates an <see cref="InvalidDataException"/> carrying the specified category and inner exception.</summary>
    public static InvalidDataException Format(MsixErrorCode code, string message, Exception innerException)
    {
        var exception = new InvalidDataException(message, innerException);
        exception.Data[ErrorCodeDataKey] = code;
        return exception;
    }

    /// <summary>Attempts to read an attached category without throwing for foreign exceptions or data.</summary>
    public static bool TryGetCode(Exception? exception, out MsixErrorCode code)
    {
        try
        {
            if (exception?.Data?[ErrorCodeDataKey] is MsixErrorCode attachedCode)
            {
                code = attachedCode;
                return true;
            }
        }
        catch
        {
            // Exception implementations may override Data or provide a foreign dictionary.
        }

        code = MsixErrorCode.Unknown;
        return false;
    }

    /// <summary>Returns the attached category, or <see cref="MsixErrorCode.Unknown"/> when none is present.</summary>
    public static MsixErrorCode GetCode(Exception? exception) =>
        TryGetCode(exception, out MsixErrorCode code) ? code : MsixErrorCode.Unknown;
}
