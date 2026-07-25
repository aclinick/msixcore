using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MsixCore.Packaging;
using MsixCore.Packaging.Integrity;
using MsixCore.Packaging.Manifest;
using MsixCore.Studio.Services;

namespace MsixCore.Studio.ViewModels;

public sealed partial class MainWindowViewModel(IStoragePicker storagePicker) : ObservableObject
{
    [ObservableProperty]
    public partial bool HasPackage { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Open an MSIX/APPX file or loose package folder to begin.";

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourcePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string IdentityName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Publisher { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Version { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Architecture { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PackageFamilyName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PackageFullName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PublisherDisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrameworkStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BlockMapStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SignatureStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SignatureSubject { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SignatureIssuer { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SignatureThumbprint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SignatureValidity { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SignatureCmsIntegrity { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SignaturePublisherMatch { get; set; } = string.Empty;

    public ObservableCollection<string> Capabilities { get; } = [];

    public ObservableCollection<ApplicationItem> Applications { get; } = [];

    public ObservableCollection<BlockMapFileItem> BlockMapFiles { get; } = [];

    [RelayCommand]
    private async Task OpenPackageAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            string? path = await storagePicker.PickPackageAsync();
            if (path is not null)
            {
                await LoadPackageAsync(path, isDirectory: false);
            }
        }
        catch (Exception ex)
        {
            ShowError("The file picker failed.", ex);
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            string? path = await storagePicker.PickFolderAsync();
            if (path is not null)
            {
                await LoadPackageAsync(path, isDirectory: true);
            }
        }
        catch (Exception ex)
        {
            ShowError("The folder picker failed.", ex);
        }
    }

    public async Task LoadPackageAsync(string path, bool isDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = $"Reading {Path.GetFileName(path)}…";

        try
        {
            PackageSnapshot snapshot = await Task.Run(() => PackageSnapshot.Load(path, isDirectory));
            Apply(snapshot);
            StatusMessage = $"Loaded {snapshot.IdentityName}";
            HasPackage = true;
        }
        catch (Exception ex)
        {
            ClearPackage();
            ShowError("The package could not be opened.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(PackageSnapshot snapshot)
    {
        SourcePath = snapshot.SourcePath;
        IdentityName = snapshot.IdentityName;
        Publisher = snapshot.Publisher;
        Version = snapshot.Version;
        Architecture = snapshot.Architecture;
        PackageFamilyName = snapshot.PackageFamilyName;
        PackageFullName = snapshot.PackageFullName;
        DisplayName = snapshot.DisplayName;
        PublisherDisplayName = snapshot.PublisherDisplayName;
        FrameworkStatus = snapshot.FrameworkStatus;
        BlockMapStatus = snapshot.BlockMapStatus;
        SignatureStatus = snapshot.SignatureStatus;
        SignatureSubject = snapshot.SignatureSubject;
        SignatureIssuer = snapshot.SignatureIssuer;
        SignatureThumbprint = snapshot.SignatureThumbprint;
        SignatureValidity = snapshot.SignatureValidity;
        SignatureCmsIntegrity = snapshot.SignatureCmsIntegrity;
        SignaturePublisherMatch = snapshot.SignaturePublisherMatch;

        Replace(Capabilities, snapshot.Capabilities);
        Replace(Applications, snapshot.Applications);
        Replace(BlockMapFiles, snapshot.BlockMapFiles);
    }

    private void ClearPackage()
    {
        HasPackage = false;
        SourcePath = string.Empty;
        Capabilities.Clear();
        Applications.Clear();
        BlockMapFiles.Clear();
    }

    private void ShowError(string context, Exception exception)
    {
        ErrorMessage = $"{context} {exception.Message}";
        StatusMessage = "Unable to load package.";
        HasError = true;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
        {
            target.Add(value);
        }
    }

    private sealed record PackageSnapshot
    {
        public required string SourcePath { get; init; }

        public required string IdentityName { get; init; }

        public required string Publisher { get; init; }

        public required string Version { get; init; }

        public required string Architecture { get; init; }

        public required string PackageFamilyName { get; init; }

        public required string PackageFullName { get; init; }

        public required string DisplayName { get; init; }

        public required string PublisherDisplayName { get; init; }

        public required string FrameworkStatus { get; init; }

        public required IReadOnlyList<string> Capabilities { get; init; }

        public required IReadOnlyList<ApplicationItem> Applications { get; init; }

        public required IReadOnlyList<BlockMapFileItem> BlockMapFiles { get; init; }

        public required string BlockMapStatus { get; init; }

        public required string SignatureStatus { get; init; }

        public required string SignatureSubject { get; init; }

        public required string SignatureIssuer { get; init; }

        public required string SignatureThumbprint { get; init; }

        public required string SignatureValidity { get; init; }

        public required string SignatureCmsIntegrity { get; init; }

        public required string SignaturePublisherMatch { get; init; }

        public static PackageSnapshot Load(string path, bool isDirectory)
        {
            using MsixPackage package = isDirectory
                ? MsixPackage.OpenDirectory(path)
                : MsixPackage.Open(path);

            AppxManifest manifest = package.Manifest;
            PackageIdentity identity = package.Identity;
            (IReadOnlyList<BlockMapFileItem> files, string blockMapStatus) = ReadBlockMap(package);
            SignatureSnapshot signature = ReadSignature(package, identity.Publisher);

            return new PackageSnapshot
            {
                SourcePath = Path.GetFullPath(path),
                IdentityName = identity.Name,
                Publisher = identity.Publisher,
                Version = identity.Version.ToString(),
                Architecture = PackageIdentity.ArchitectureMoniker(identity.Architecture),
                PackageFamilyName = identity.PackageFamilyName,
                PackageFullName = identity.PackageFullName,
                DisplayName = package.DisplayName,
                PublisherDisplayName = package.PublisherDisplayName,
                FrameworkStatus = manifest.IsFramework ? "Framework package" : "Application package",
                Capabilities = package.Capabilities.ToArray(),
                Applications = manifest.Applications.Select(ToApplicationItem).ToArray(),
                BlockMapFiles = files,
                BlockMapStatus = blockMapStatus,
                SignatureStatus = signature.Status,
                SignatureSubject = signature.Subject,
                SignatureIssuer = signature.Issuer,
                SignatureThumbprint = signature.Thumbprint,
                SignatureValidity = signature.Validity,
                SignatureCmsIntegrity = signature.CmsIntegrity,
                SignaturePublisherMatch = signature.PublisherMatch,
            };
        }

        private static ApplicationItem ToApplicationItem(ManifestApplication application) =>
            new(
                application.Id,
                ValueOrDash(application.VisualElements.DisplayName),
                ValueOrDash(application.Executable),
                ValueOrDash(application.EntryPoint));

        private static (IReadOnlyList<BlockMapFileItem> Files, string Status) ReadBlockMap(MsixPackage package)
        {
            try
            {
                BlockMap blockMap = package.BlockMap;
                BlockMapVerificationResult verification = package.VerifyBlockMap();
                Dictionary<string, BlockMapFileResult> results = verification.Files.ToDictionary(
                    result => result.Name,
                    StringComparer.OrdinalIgnoreCase);

                BlockMapFileItem[] files = blockMap.Files.Select(file =>
                {
                    string verdict = results.TryGetValue(file.Name, out BlockMapFileResult? result)
                        ? result.IsValid ? "Valid" : result.Error ?? "Invalid"
                        : "Not verified";
                    return new BlockMapFileItem(file.Name, file.Size, file.Blocks.Count, verdict);
                }).ToArray();

                string status = verification.IsValid
                    ? $"Valid — {files.Length} file(s) verified with {blockMap.HashMethod}."
                    : BuildVerificationFailure(verification);
                return (files, status);
            }
            catch (Exception ex)
            {
                return ([], $"Unavailable — {ex.Message}");
            }
        }

        private static string BuildVerificationFailure(BlockMapVerificationResult verification)
        {
            int invalidFiles = verification.Files.Count(file => !file.IsValid);
            int coverageErrors = verification.CoverageErrors.Count;
            return $"Invalid — {invalidFiles} file failure(s), {coverageErrors} coverage error(s).";
        }

        private static SignatureSnapshot ReadSignature(MsixPackage package, string publisher)
        {
            if (!package.IsSigned)
            {
                return SignatureSnapshot.Unsigned;
            }

            try
            {
                PackageSignature? signature = package.ReadSignature();
                if (signature is null)
                {
                    return SignatureSnapshot.Unsigned;
                }

                return new SignatureSnapshot(
                    "Signed",
                    signature.SubjectName,
                    signature.IssuerName,
                    signature.Thumbprint,
                    $"{signature.NotBefore:u} — {signature.NotAfter:u}",
                    signature.IsCmsIntegrityValid ? "Valid" : "Invalid",
                    signature.MatchesPublisher(publisher) ? "Matches" : "Does not match");
            }
            catch (Exception ex)
            {
                return SignatureSnapshot.Malformed(ex.Message);
            }
        }

        private static string ValueOrDash(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    private sealed record SignatureSnapshot(
        string Status,
        string Subject,
        string Issuer,
        string Thumbprint,
        string Validity,
        string CmsIntegrity,
        string PublisherMatch)
    {
        public static SignatureSnapshot Unsigned { get; } =
            new("Unsigned package", "—", "—", "—", "—", "Not applicable", "Not applicable");

        public static SignatureSnapshot Malformed(string error) =>
            new($"Signature could not be read — {error}", "—", "—", "—", "—", "Unknown", "Unknown");
    }
}
