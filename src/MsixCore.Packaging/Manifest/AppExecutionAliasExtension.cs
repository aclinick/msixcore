namespace MsixCore.Packaging.Manifest;

/// <summary>
/// The <c>AppExecutionAlias</c> child of a <c>windows.appExecutionAlias</c> extension: the names by
/// which the app can be launched from a command prompt.
/// </summary>
/// <remarks>
/// Two schema forms exist — <c>uap3:AppExecutionAlias</c> containing <c>desktop:ExecutionAlias</c>,
/// and <c>uap5:AppExecutionAlias</c> containing <c>uap5:ExecutionAlias</c>. Both collapse onto this
/// type, because the codebase matches elements by local name and the two carry the same
/// information.
/// </remarks>
public sealed record AppExecutionAliasExtension : ExtensionPayload
{
    /// <summary>
    /// The aliases declared, in manifest order. Each is a file name the schema requires to end in
    /// <c>.exe</c> and to contain no path separator.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}
