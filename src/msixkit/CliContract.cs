using System.Text.Json;
using MsixCore.Packaging;

namespace MsixKit;

internal static class CliContract
{
    internal const int SchemaVersion = 1;

    internal static class ExitCodes
    {
        public const int Success = 0;
        public const int NegativeVerdict = 1;
        public const int Usage = 2;
        public const int OperationalError = 3;
    }

    public static void WriteError(
        TextWriter output,
        TextWriter error,
        bool json,
        string prefix,
        string message,
        string? usage,
        string code = "usage")
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new ErrorReport { Code = code, Message = usage is null ? message : $"{message} {usage}" },
                ReportJsonContext.Default.ErrorReport));
            return;
        }

        error.WriteLine($"{prefix}: {message}");
        if (usage is not null)
        {
            error.WriteLine(usage);
        }
    }

    public static string ErrorCode(Exception ex)
    {
        if (MsixError.TryGetCode(ex, out MsixErrorCode code))
        {
            return ToSnakeCase(code);
        }

        return ex switch
        {
            FileNotFoundException or DirectoryNotFoundException => "not_found",
            UnauthorizedAccessException => "unauthorized",
            NotSupportedException => "not_supported",
            InvalidDataException or ArgumentException or InvalidOperationException => "invalid_data",
            IOException => "io_error",
            _ => "operational_error",
        };
    }

    internal static string ToSnakeCase(MsixErrorCode code)
    {
        string value = code.ToString();
        var result = new System.Text.StringBuilder(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (index > 0 && char.IsUpper(character))
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }

    public static bool IsOperationalException(Exception ex) =>
        ex is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or NotSupportedException;

    public static bool HasJsonFlag(IEnumerable<string> args) => args.Contains("--json", StringComparer.Ordinal);

    public static bool TryReadOptionValue(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        string valueDescription,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        if (index + 1 >= args.Count || args[index + 1].StartsWith('-'))
        {
            error = $"option '{option}' requires {valueDescription}.";
            return false;
        }

        value = args[++index];
        return true;
    }
}
