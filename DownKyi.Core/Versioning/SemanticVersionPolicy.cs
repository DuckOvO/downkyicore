using System.Diagnostics.CodeAnalysis;
using Semver;

namespace DownKyi.Core.Versioning;

public static class SemanticVersionPolicy
{
    public static string NormalizeForDisplay(string? value)
    {
        return TryParse(value, out var version)
            ? version.WithoutMetadata().ToString()
            : string.Empty;
    }

    public static bool TryNormalizeIdentity(string? value, out string normalized)
    {
        if (!TryParse(value, out var version))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = version.ToString();
        return true;
    }

    public static bool IsNewer(string? candidate, string? current)
    {
        return TryParse(candidate, out var candidateVersion) &&
               TryParse(current, out var currentVersion) &&
               candidateVersion.ComparePrecedenceTo(currentVersion) > 0;
    }

    public static bool HasSamePrecedence(string? left, string? right)
    {
        return TryParse(left, out var leftVersion) &&
               TryParse(right, out var rightVersion) &&
               leftVersion.ComparePrecedenceTo(rightVersion) == 0;
    }

    private static bool TryParse(
        string? value,
        [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.Length > 1 &&
            (candidate[0] == 'v' || candidate[0] == 'V') &&
            char.IsAsciiDigit(candidate[1]))
        {
            candidate = candidate[1..];
        }

        return SemVersion.TryParse(candidate, SemVersionStyles.Strict, out version);
    }
}
