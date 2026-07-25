using System.Text.Json;

namespace MsixMgr;

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

    public static string ErrorCode(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => "not_found",
        UnauthorizedAccessException => "unauthorized",
        InvalidDataException or ArgumentException or InvalidOperationException => "invalid_data",
        IOException => "io_error",
        _ => "operational_error",
    };

    public static bool HasJsonFlag(IEnumerable<string> args) => args.Contains("--json", StringComparer.Ordinal);
}
