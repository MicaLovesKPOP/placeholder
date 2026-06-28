using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using InputFlow.Windows;

namespace InputFlow.Core
{
    /// <summary>
    /// Provides methods to enumerate installed input profiles and look up profiles by identifier.
    /// </summary>
    public static class InputProfileManager
    {
        private const string UsInternationalKlid = "00020409";

        /// <summary>
        /// Enumerates the installed keyboard layouts and IMEs for the system.
        /// This uses <see cref="InputApis.GetKeyboardLayoutList"/> and creates
        /// <see cref="InputProfile"/> instances for each HKL.
        /// </summary>
        public static IReadOnlyList<InputProfile> EnumerateInstalledProfiles()
        {
            int count = InputApis.GetKeyboardLayoutList(0, Array.Empty<IntPtr>());
            var list = new IntPtr[count];
            InputApis.GetKeyboardLayoutList(count, list);
            var profiles = new List<InputProfile>(count);
            foreach (var hkl in list)
            {
                string klid = ((ulong)hkl.ToInt64() & 0xFFFFFFFF).ToString("X8");
                string? languageTag = GetLanguageTag(hkl);
                string friendlyName = GetDefaultLanguageName(hkl, languageTag);
                bool isIme = IsImeLayout(hkl);
                profiles.Add(new InputProfile(hkl, klid, friendlyName, isIme, languageTag));
            }
            return profiles;
        }

        /// <summary>
        /// Returns a stable single-line profile description for logs, diagnostics, and future picker UI.
        /// </summary>
        public static string FormatProfile(InputProfile profile)
        {
            return $"{profile.FriendlyName} KLID={profile.KLID} HKL=0x{profile.HKL.ToInt64():X8} Lang={GetProfileLanguageTag(profile)} LANGID=0x{profile.LangId:X4} IsIme={profile.IsIme}";
        }

        /// <summary>
        /// Returns the best available language tag for a profile.
        /// </summary>
        public static string GetProfileLanguageTag(InputProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(profile.LanguageTag))
            {
                return profile.LanguageTag;
            }

            return GetLanguageTagFromLangId(profile.LangId) ?? profile.LangId.ToString("X4");
        }

        /// <summary>
        /// Attempts to retrieve a human readable name for a given keyboard layout.
        /// Falls back to the locale name if a more specific description is unavailable.
        /// </summary>
        private static string GetDefaultLanguageName(IntPtr hkl, string? languageTag)
        {
            if (!string.IsNullOrWhiteSpace(languageTag))
            {
                try
                {
                    return CultureInfo.GetCultureInfo(languageTag).DisplayName;
                }
                catch (CultureNotFoundException)
                {
                    // Fall through to LANGID text.
                }
            }

            uint langId = (uint)((ulong)hkl.ToInt64() & 0xFFFF);
            return GetLanguageTagFromLangId(langId) ?? langId.ToString("X4");
        }

        private static string? GetLanguageTag(IntPtr hkl)
        {
            uint langId = (uint)((ulong)hkl.ToInt64() & 0xFFFF);
            return GetLanguageTagFromLangId(langId);
        }

        private static string? GetLanguageTagFromLangId(uint langId)
        {
            try
            {
                return new CultureInfo((int)langId).Name;
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Rudimentary detection of whether the given HKL corresponds to an IME.
        /// This method checks the high word of the HKL for zero; if non-zero,
        /// it indicates an IME handle. More accurate detection requires TSF APIs.
        /// </summary>
        private static bool IsImeLayout(IntPtr hkl)
        {
            ulong value = (ulong)hkl.ToInt64();
            // In HKL, high word is non-zero for IMEs.
            return (value >> 16) != 0;
        }

        /// <summary>
        /// Attempts to find an input profile matching the given KLID string or friendly name.
        /// This legacy helper is retained for backwards compatibility with simple
        /// configurations that reference profiles by KLID or display name directly.
        /// </summary>
        public static InputProfile? FindProfile(IReadOnlyList<InputProfile> profiles, string identifier)
        {
            foreach (var profile in profiles)
            {
                if (string.Equals(profile.KLID, identifier, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(profile.FriendlyName, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }
            return null;
        }

        /// <summary>
        /// Matches installed profiles against a list of profile definitions. Each
        /// definition contains matching criteria such as language tag, KLID or
        /// substrings of the friendly name. Returns a dictionary mapping profile
        /// Ids to matched <see cref="InputProfile"/> instances. Definitions that
        /// cannot be matched are omitted. This helper does not throw when
        /// matching fails; the caller should handle missing definitions.
        /// </summary>
        /// <param name="installed">Installed profiles enumerated by <see cref="EnumerateInstalledProfiles"/>.</param>
        /// <param name="definitions">Profile definitions from configuration.</param>
        public static Dictionary<string, InputProfile> MatchProfiles(IReadOnlyList<InputProfile> installed, IEnumerable<ProfileDefinition> definitions)
        {
            var result = new Dictionary<string, InputProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var report in EvaluateProfileMatches(installed, definitions))
            {
                if (report.MatchedProfile != null && report.Health != ProfileHealthState.Ambiguous)
                {
                    result[report.ProfileId] = report.MatchedProfile;
                }
            }
            return result;
        }

        /// <summary>
        /// Evaluates configured profile definitions against installed profiles and returns diagnostics.
        /// </summary>
        public static IReadOnlyList<ProfileMatchReport> EvaluateProfileMatches(IReadOnlyList<InputProfile> installed, IEnumerable<ProfileDefinition> definitions)
        {
            var reports = new List<ProfileMatchReport>();
            foreach (var definition in definitions)
            {
                reports.Add(EvaluateProfileMatch(installed, definition));
            }

            return reports;
        }

        private static ProfileMatchReport EvaluateProfileMatch(IReadOnlyList<InputProfile> installed, ProfileDefinition definition)
        {
            var criteria = definition.Match ?? new ProfileMatch();
            var candidates = new List<ProfileCandidateMatch>();
            var matchedCandidates = new List<ProfileCandidateMatch>();

            foreach (var profile in installed)
            {
                var candidate = EvaluateCandidate(profile, criteria);
                candidates.Add(candidate);
                if (candidate.IsMatch)
                {
                    matchedCandidates.Add(candidate);
                }
            }

            if (matchedCandidates.Count == 1)
            {
                return new ProfileMatchReport(
                    definition.Id,
                    criteria,
                    matchedCandidates[0].Profile,
                    "Matched all configured criteria.",
                    usedCompatibilityFallback: false,
                    candidates,
                    ProfileHealthState.Matched);
            }

            if (matchedCandidates.Count > 1)
            {
                return new ProfileMatchReport(
                    definition.Id,
                    criteria,
                    matchedCandidates[0].Profile,
                    $"Ambiguous match: {matchedCandidates.Count} installed profiles matched all configured criteria.",
                    usedCompatibilityFallback: false,
                    candidates,
                    ProfileHealthState.Ambiguous);
            }

            InputProfile? compatibilityMatch = MatchEnglishNetherlandsUsInternationalCompatibility(installed, criteria);
            if (compatibilityMatch != null)
            {
                return new ProfileMatchReport(
                    definition.Id,
                    criteria,
                    compatibilityMatch,
                    "Matched English (Netherlands) / US-International compatibility fallback by KLID 00020409.",
                    usedCompatibilityFallback: true,
                    candidates,
                    ProfileHealthState.Changed);
            }

            return new ProfileMatchReport(
                definition.Id,
                criteria,
                matchedProfile: null,
                summary: "No installed profile matched all configured criteria.",
                usedCompatibilityFallback: false,
                candidates,
                ProfileHealthState.Missing);
        }

        private static ProfileCandidateMatch EvaluateCandidate(InputProfile profile, ProfileMatch criteria)
        {
            var failures = new List<string>();

            if (!string.IsNullOrEmpty(criteria.KLID) && !string.Equals(profile.KLID, NormalizeKlid(criteria.KLID), StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"KLID expected {NormalizeKlid(criteria.KLID)} got {profile.KLID}");
            }

            if (!string.IsNullOrEmpty(criteria.LanguageTag))
            {
                string actualLanguageTag = GetProfileLanguageTag(profile);
                if (!string.Equals(actualLanguageTag, criteria.LanguageTag, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"LanguageTag expected {criteria.LanguageTag} got {actualLanguageTag}");
                }
            }

            if (!ContainsProfileText(profile, criteria.LayoutNameContains))
            {
                failures.Add($"LayoutNameContains expected '{criteria.LayoutNameContains}' not found in '{profile.FriendlyName}' or '{profile.KLID}'");
            }

            if (!ContainsProfileText(profile, criteria.ProfileNameContains))
            {
                failures.Add($"ProfileNameContains expected '{criteria.ProfileNameContains}' not found in '{profile.FriendlyName}' or '{profile.KLID}'");
            }

            return new ProfileCandidateMatch(profile, failures.Count == 0, failures.Count == 0 ? "Matched all configured criteria." : string.Join("; ", failures));
        }

        private static bool ContainsProfileText(InputProfile profile, string? expected)
        {
            if (string.IsNullOrEmpty(expected))
            {
                return true;
            }

            return profile.FriendlyName.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0 ||
                profile.KLID.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static InputProfile? MatchEnglishNetherlandsUsInternationalCompatibility(IReadOnlyList<InputProfile> installed, ProfileMatch criteria)
        {
            if (!string.Equals(criteria.LanguageTag, "en-NL", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(criteria.KLID) ||
                !string.IsNullOrEmpty(criteria.LayoutNameContains) ||
                !string.IsNullOrEmpty(criteria.ProfileNameContains))
            {
                return null;
            }

            foreach (var profile in installed)
            {
                if (string.Equals(profile.KLID, UsInternationalKlid, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return null;
        }

        private static string NormalizeKlid(string klid)
        {
            string value = klid.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value[2..];
            }

            return value.Length < 8 ? value.PadLeft(8, '0').ToUpperInvariant() : value.ToUpperInvariant();
        }
    }

    /// <summary>
    /// Diagnostic result for one configured profile definition.
    /// </summary>
    public sealed class ProfileMatchReport
    {
        public ProfileMatchReport(
            string profileId,
            ProfileMatch criteria,
            InputProfile? matchedProfile,
            string summary,
            bool usedCompatibilityFallback,
            IReadOnlyList<ProfileCandidateMatch> candidates,
            ProfileHealthState health)
        {
            ProfileId = profileId;
            Criteria = criteria;
            MatchedProfile = matchedProfile;
            Summary = summary;
            UsedCompatibilityFallback = usedCompatibilityFallback;
            Candidates = candidates;
            Health = health;
        }

        public string ProfileId { get; }
        public ProfileMatch Criteria { get; }
        public InputProfile? MatchedProfile { get; }
        public string Summary { get; }
        public bool UsedCompatibilityFallback { get; }
        public IReadOnlyList<ProfileCandidateMatch> Candidates { get; }
        public bool IsMatch => MatchedProfile != null;
        public ProfileHealthState Health { get; }
    }

    public enum ProfileHealthState
    {
        Matched,
        Missing,
        Ambiguous,
        Changed
    }

    /// <summary>
    /// Diagnostic result for one installed profile considered against one configured profile.
    /// </summary>
    public sealed class ProfileCandidateMatch
    {
        public ProfileCandidateMatch(InputProfile profile, bool isMatch, string reason)
        {
            Profile = profile;
            IsMatch = isMatch;
            Reason = reason;
        }

        public InputProfile Profile { get; }
        public bool IsMatch { get; }
        public string Reason { get; }
    }
}
