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
    public partial bool? IsBlockMapValid { get; set; }

    [ObservableProperty]
    public partial string SignatureStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial PackageSignatureItem? Signature { get; set; }

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

    public ObservableCollection<string> BlockMapCoverageErrors { get; } = [];

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
        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = "Reading package…";

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            StatusMessage = $"Reading {Path.GetFileName(path)}…";
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
        IsBlockMapValid = snapshot.IsBlockMapValid;
        Signature = snapshot.Signature;
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
        Replace(BlockMapCoverageErrors, snapshot.BlockMapCoverageErrors);
    }

    private void ClearPackage()
    {
        HasPackage = false;
        SourcePath = string.Empty;
        IdentityName = string.Empty;
        Publisher = string.Empty;
        Version = string.Empty;
        Architecture = string.Empty;
        PackageFamilyName = string.Empty;
        PackageFullName = string.Empty;
        DisplayName = string.Empty;
        PublisherDisplayName = string.Empty;
        FrameworkStatus = string.Empty;
        BlockMapStatus = string.Empty;
        IsBlockMapValid = null;
        Signature = null;
        SignatureStatus = string.Empty;
        SignatureSubject = string.Empty;
        SignatureIssuer = string.Empty;
        SignatureThumbprint = string.Empty;
        SignatureValidity = string.Empty;
        SignatureCmsIntegrity = string.Empty;
        SignaturePublisherMatch = string.Empty;
        Capabilities.Clear();
        Applications.Clear();
        BlockMapFiles.Clear();
        BlockMapCoverageErrors.Clear();
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

        public required IReadOnlyList<string> BlockMapCoverageErrors { get; init; }

        public required string BlockMapStatus { get; init; }

        public required bool? IsBlockMapValid { get; init; }

        public required PackageSignatureItem? Signature { get; init; }

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
            (
                IReadOnlyList<BlockMapFileItem> files,
                IReadOnlyList<string> coverageErrors,
                string blockMapStatus,
                bool? isBlockMapValid) = ReadBlockMap(package);
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
                BlockMapCoverageErrors = coverageErrors,
                BlockMapStatus = blockMapStatus,
                IsBlockMapValid = isBlockMapValid,
                Signature = signature.Signature,
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

        private static (
            IReadOnlyList<BlockMapFileItem> Files,
            IReadOnlyList<string> CoverageErrors,
            string Status,
            bool? IsValid) ReadBlockMap(MsixPackage package)
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
                return (files, verification.CoverageErrors.ToArray(), status, verification.IsValid);
            }
            catch (Exception ex)
            {
                return ([], [], $"Unavailable — {ex.Message}", null);
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

                var item = new PackageSignatureItem(
                    signature.SubjectName,
                    signature.IssuerName,
                    signature.Thumbprint,
                    signature.NotBefore,
                    signature.NotAfter,
                    signature.IsCmsIntegrityValid,
                    signature.MatchesPublisher(publisher));

                return new SignatureSnapshot(
                    item,
                    "Signed",
                    item.SubjectName,
                    item.IssuerName,
                    item.Thumbprint,
                    $"{item.NotBefore:u} — {item.NotAfter:u}",
                    item.IsCmsIntegrityValid ? "Valid" : "Invalid",
                    item.MatchesPublisher ? "Matches" : "Does not match");
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
        PackageSignatureItem? Signature,
        string Status,
        string Subject,
        string Issuer,
        string Thumbprint,
        string Validity,
        string CmsIntegrity,
        string PublisherMatch)
    {
        public static SignatureSnapshot Unsigned { get; } =
            new(null, "Unsigned package", "—", "—", "—", "—", "Not applicable", "Not applicable");

        public static SignatureSnapshot Malformed(string error) =>
            new(null, $"Signature could not be read — {error}", "—", "—", "—", "—", "Unknown", "Unknown");
    }
}
