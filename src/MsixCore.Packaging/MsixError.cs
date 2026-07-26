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
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="code"/> is not a defined member, or is <see cref="MsixErrorCode.Unknown"/>,
    /// which is a read-side fallback only.
    /// </exception>
    public static InvalidDataException Format(MsixErrorCode code, string message)
    {
        Validate(code);
        var exception = new InvalidDataException(message);
        exception.Data[ErrorCodeDataKey] = code;
        return exception;
    }

    /// <summary>Creates an <see cref="InvalidDataException"/> carrying the specified category and inner exception.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="code"/> is not a defined member, or is <see cref="MsixErrorCode.Unknown"/>.
    /// </exception>
    public static InvalidDataException Format(MsixErrorCode code, string message, Exception innerException)
    {
        Validate(code);
        var exception = new InvalidDataException(message, innerException);
        exception.Data[ErrorCodeDataKey] = code;
        return exception;
    }

    /// <summary>
    /// Attaches a category to an exception whose type is not <see cref="InvalidDataException"/> and
    /// returns it, so it can be used directly in a <c>throw</c> expression.
    /// </summary>
    /// <remarks>
    /// Intended for use at the construction site only — <c>throw MsixError.Tag(new Xxx(...), code)</c>
    /// — so that a category is never retro-fitted onto an exception that has already propagated.
    /// Prefer <see cref="Format(MsixErrorCode, string)"/> where an <see cref="InvalidDataException"/>
    /// is appropriate.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="code"/> is not a defined member, or is <see cref="MsixErrorCode.Unknown"/>.
    /// </exception>
    public static TException Tag<TException>(TException exception, MsixErrorCode code)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(exception);
        Validate(code);
        exception.Data[ErrorCodeDataKey] = code;
        return exception;
    }

    private static void Validate(MsixErrorCode code)
    {
        // An undefined value would serialize as its number (e.g. "9999"), escaping the documented
        // registry and the [a-z_] shape callers are told to expect.
        if (!Enum.IsDefined(code) || code == MsixErrorCode.Unknown)
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "The error code must be a defined MsixErrorCode other than Unknown.");
        }
    }

    /// <summary>Attempts to read an attached category without throwing for foreign exceptions or data.</summary>
    public static bool TryGetCode(Exception? exception, out MsixErrorCode code)
    {
        try
        {
            if (exception?.Data?[ErrorCodeDataKey] is MsixErrorCode attachedCode
                && Enum.IsDefined(attachedCode)
                && attachedCode != MsixErrorCode.Unknown)
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
