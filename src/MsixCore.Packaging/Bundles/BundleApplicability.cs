using System.Globalization;
using MsixCore.Packaging.Manifest;

namespace MsixCore.Packaging.Bundles;

/// <summary>
/// Selects the application package and resource packages from a bundle that apply to a target
/// device.
/// </summary>
/// <remarks>
/// <para>
/// This implements the <b>documented Windows</b> selection behaviour, not the upstream MSIX SDK's.
/// The upstream open-source engine never reads a package's <c>Architecture</c>, never compares a
/// <c>Scale</c> value, and does not parse <c>DXFeatureLevel</c> at all — so porting it faithfully
/// would mean implementing almost nothing. Where the two differ, the divergence is deliberate and
/// documented in <c>docs/bundle-applicability.md</c>.
/// </para>
/// </remarks>
public static class BundleApplicability
{
    /// <summary>
    /// Architecture fallback order per target, best first. Windows runs a native package in
    /// preference to an emulated or WoW one, so order matters and is asserted by tests.
    /// </summary>
    private static readonly Dictionary<ProcessorArchitecture, ProcessorArchitecture[]> ArchitecturePreference = new()
    {
        // WoW64 runs 32-bit x86 on x64.
        [ProcessorArchitecture.X86] = [ProcessorArchitecture.X86, ProcessorArchitecture.Neutral],
        [ProcessorArchitecture.X64] = [ProcessorArchitecture.X64, ProcessorArchitecture.X86, ProcessorArchitecture.Neutral],
        [ProcessorArchitecture.Arm] = [ProcessorArchitecture.Arm, ProcessorArchitecture.X86, ProcessorArchitecture.Neutral],

        // ARM64 runs native ARM64, then emulated x64 (Windows 11), then emulated x86, then ARM32.
        // ARM32 is last because Windows 11 dropped ARM32 application support.
        [ProcessorArchitecture.Arm64] =
        [
            ProcessorArchitecture.Arm64,
            ProcessorArchitecture.X64,
            ProcessorArchitecture.X86OnArm64,
            ProcessorArchitecture.X86,
            ProcessorArchitecture.Arm,
            ProcessorArchitecture.Neutral,
        ],
        [ProcessorArchitecture.X86OnArm64] =
        [
            ProcessorArchitecture.X86OnArm64,
            ProcessorArchitecture.X86,
            ProcessorArchitecture.Neutral,
        ],
    };

    /// <summary>Selects the packages in <paramref name="manifest"/> that apply to <paramref name="target"/>.</summary>
    /// <param name="manifest">The parsed bundle manifest.</param>
    /// <param name="target">The device context to resolve against.</param>
    /// <param name="options">Qualifiers to ignore.</param>
    /// <returns>The applicable application package and resource packages.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> or <paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="target"/> requests a language that is not a supported BCP-47 tag.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// The bundle contains no application package at all, or none that can run on the target
    /// architecture. Tagged <see cref="MsixErrorCode.NoApplicablePackage"/>.
    /// </exception>
    public static BundleApplicabilityResult Select(
        BundleManifest manifest,
        BundleTarget target,
        BundleApplicabilityOptions options = BundleApplicabilityOptions.None)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(target);

        List<BundlePackageEntry> candidates = SelectApplicationCandidates(manifest, target, options);

        return new BundleApplicabilityResult
        {
            ApplicationPackage = candidates[0],
            CandidateApplicationPackages = candidates,
            ResourcePackages = SelectResourcePackages(manifest, target, options),
        };
    }

    private static List<BundlePackageEntry> SelectApplicationCandidates(
        BundleManifest manifest,
        BundleTarget target,
        BundleApplicabilityOptions options)
    {
        List<BundlePackageEntry> applications = manifest.Packages
            .Where(p => p.Type == BundlePackageType.Application)
            .ToList();

        if (applications.Count == 0)
        {
            throw MsixError.Format(
                MsixErrorCode.NoApplicablePackage,
                "The bundle contains no application package.");
        }

        bool skipArchitecture = options.HasFlag(BundleApplicabilityOptions.SkipArchitecture);

        if (!ArchitecturePreference.TryGetValue(target.Architecture, out ProcessorArchitecture[]? preference))
        {
            if (skipArchitecture)
            {
                return applications;
            }

            throw MsixError.Format(
                MsixErrorCode.NoApplicablePackage,
                $"Bundle applicability cannot be resolved for target architecture '{target.Architecture}'.");
        }

        // Rank by position in the preference list; anything absent from it cannot run. Under
        // SkipArchitecture, unrunnable packages are kept rather than dropped, but the ranking still
        // applies so the chosen package remains the best one for the target.
        List<BundlePackageEntry> candidates = applications
            .Select(p => (Package: p, Rank: Array.IndexOf(preference, p.Architecture)))
            .Where(x => skipArchitecture || x.Rank >= 0)
            .OrderBy(x => x.Rank >= 0 ? x.Rank : int.MaxValue)
            .Select(x => x.Package)
            .ToList();

        if (candidates.Count == 0)
        {
            string available = string.Join(", ", applications.Select(p => p.Architecture).Distinct());
            throw MsixError.Format(
                MsixErrorCode.NoApplicablePackage,
                $"The bundle contains no application package applicable to architecture "
                + $"'{target.Architecture}'. Available architectures: {available}.");
        }

        return candidates;
    }

    private static List<BundlePackageEntry> SelectResourcePackages(
        BundleManifest manifest,
        BundleTarget target,
        BundleApplicabilityOptions options)
    {
        // A requested tag we cannot parse is an error, not a no-op. Silently dropping it would, once
        // every requested tag had been dropped, leave requested.Count == 0 and disable language
        // filtering entirely — quietly selecting every language in the bundle.
        var requested = new List<Bcp47Tag>(target.Languages.Count);
        foreach (string language in target.Languages)
        {
            if (Bcp47Tag.Parse(language) is not { } tag)
            {
                throw new ArgumentException(
                    $"'{language}' is not a supported BCP-47 language tag.",
                    nameof(target));
            }

            requested.Add(tag);
        }

        // Selection is tracked by manifest index, not by value. BundlePackageEntry is a record, so
        // a value-based lookup would conflate two structurally identical Package entries.
        var selected = new List<int>();
        var languageFallbacks = new List<int>();
        bool anyLanguageMatched = false;

        // Pass 1: language and DirectX. Scale is resolved afterwards, over only the packages that
        // survive here — resolving it across the whole bundle first can pick a scale that no
        // remaining package offers and then discard everything. For example a bundle of
        // (en, scale-100) and (fr, scale-200) resolved for en at scale 150 would globally choose
        // 200, then drop the English package on scale and the French one on language, returning
        // nothing.
        for (int index = 0; index < manifest.Packages.Count; index++)
        {
            BundlePackageEntry resource = manifest.Packages[index];
            if (resource.Type != BundlePackageType.Resource)
            {
                continue;
            }

            // A resource package with no qualifiers at all carries unconditional payload.
            if (resource.Resources.Count == 0)
            {
                selected.Add(index);
                continue;
            }

            if (!DXFeatureLevelApplies(resource, target, options))
            {
                continue;
            }

            LanguageMatch match = BestLanguageMatch(resource, requested, options);
            if (match == LanguageMatch.None)
            {
                continue;
            }

            if (match == LanguageMatch.Variant)
            {
                languageFallbacks.Add(index);
                continue;
            }

            // Only a real language match suppresses the fallbacks. An 'und' package carries
            // language-neutral payload, so counting it here would leave a fr-FR user with no French
            // at all whenever the bundle also happens to ship an 'und' package.
            if (match is LanguageMatch.Exact or LanguageMatch.Neutral)
            {
                anyLanguageMatched = true;
            }

            selected.Add(index);
        }

        // Sibling/child-region packages are a last resort: include them only when nothing matched
        // the request directly, so a fr-FR user does not also receive fr-CA.
        if (!anyLanguageMatched)
        {
            selected.AddRange(languageFallbacks);
        }

        selected.Sort();

        // Pass 2: resolve the requested scale against what the surviving packages actually offer.
        int?[] availableScales = selected
            .SelectMany(i => manifest.Packages[i].Resources)
            .Select(r => ParseScale(r.Scale))
            .Where(s => s is not null)
            .Distinct()
            .ToArray();

        int? chosenScale = ChooseScale(target.Scale, availableScales);

        return selected
            .Where(i => ScaleApplies(manifest.Packages[i], chosenScale, options))
            .Select(i => manifest.Packages[i])
            .ToList();
    }

    private static bool HasLanguageQualifier(BundlePackageEntry resource) =>
        resource.Resources.Any(r => !string.IsNullOrWhiteSpace(r.Language));

    private static LanguageMatch BestLanguageMatch(
        BundlePackageEntry resource,
        List<Bcp47Tag> requested,
        BundleApplicabilityOptions options)
    {
        if (!HasLanguageQualifier(resource))
        {
            // Not a language resource; language cannot disqualify it.
            return LanguageMatch.Undetermined;
        }

        if (options.HasFlag(BundleApplicabilityOptions.SkipLanguage) || requested.Count == 0)
        {
            return LanguageMatch.Undetermined;
        }

        // A declared language that cannot be parsed is skipped below rather than treated as absent.
        // Treating it as absent would make the package unqualified and therefore applicable to
        // everyone, which is the opposite of the safe reading: payload declared for a language we
        // cannot understand is payload we cannot claim applies.
        LanguageMatch best = LanguageMatch.None;
        foreach (BundleResource offeredResource in resource.Resources)
        {
            if (Bcp47Tag.Parse(offeredResource.Language) is not { } offered)
            {
                continue;
            }

            foreach (Bcp47Tag request in requested)
            {
                LanguageMatch match = Bcp47Tag.Compare(request, offered);
                if (match > best)
                {
                    best = match;
                }
            }
        }

        return best;
    }

    private static bool ScaleApplies(
        BundlePackageEntry resource,
        int? chosenScale,
        BundleApplicabilityOptions options)
    {
        if (options.HasFlag(BundleApplicabilityOptions.SkipScale) || chosenScale is null)
        {
            return true;
        }

        int?[] scales = resource.Resources.Select(r => ParseScale(r.Scale)).Where(s => s is not null).ToArray();
        return scales.Length == 0 || scales.Contains(chosenScale);
    }

    /// <summary>
    /// Resolves the requested scale onto one the bundle actually carries: the exact scale when
    /// present, otherwise the next largest (Windows scales down more cleanly than up), otherwise
    /// the largest available.
    /// </summary>
    private static int? ChooseScale(int? requested, int?[] available)
    {
        if (requested is null || available.Length == 0)
        {
            return requested;
        }

        if (available.Contains(requested))
        {
            return requested;
        }

        int[] sorted = available.Select(s => s!.Value).OrderBy(s => s).ToArray();
        foreach (int scale in sorted)
        {
            if (scale > requested)
            {
                return scale;
            }
        }

        return sorted[^1];
    }

    private static bool DXFeatureLevelApplies(
        BundlePackageEntry resource,
        BundleTarget target,
        BundleApplicabilityOptions options)
    {
        if (options.HasFlag(BundleApplicabilityOptions.SkipDXFeatureLevel) || target.DXFeatureLevel is null)
        {
            return true;
        }

        string[] levels = resource.Resources
            .Select(r => r.DXFeatureLevel)
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => d!)
            .ToArray();

        return levels.Length == 0
            || levels.Contains(target.DXFeatureLevel, StringComparer.OrdinalIgnoreCase);
    }

    private static int? ParseScale(string? scale) =>
        int.TryParse(scale, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
}
