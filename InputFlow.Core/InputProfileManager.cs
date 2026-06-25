using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
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
                // Derive an 8-character KLID from the HKL.
                string klid = ((ulong)hkl.ToInt64() & 0xFFFFFFFF).ToString("X8");
                // Friendly name is unknown; use the LANGID display name as a placeholder.
                string friendlyName = GetDefaultLanguageName(hkl);
                bool isIme = IsImeLayout(hkl);
                profiles.Add(new InputProfile(hkl, klid, friendlyName, isIme));
            }
            return profiles;
        }

        /// <summary>
        /// Attempts to retrieve a human readable name for a given keyboard layout.
        /// Falls back to the locale name if a more specific description is unavailable.
        /// </summary>
        private static string GetDefaultLanguageName(IntPtr hkl)
        {
            // Obtain the LANGID from the HKL.
            uint langId = (uint)((ulong)hkl.ToInt64() & 0xFFFF);
            try
            {
                var culture = new CultureInfo((int)langId);
                return culture.DisplayName;
            }
            catch
            {
                return langId.ToString("X4");
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
            foreach (var def in definitions)
            {
                var match = MatchProfile(installed, def);
                if (match != null)
                {
                    result[def.Id] = match;
                }
            }
            return result;
        }

        /// <summary>
        /// Attempts to match a single profile definition to an installed profile.
        /// All configured criteria are matched case-insensitively. A second-pass
        /// compatibility fallback maps the known English (Netherlands) /
        /// US-International workflow to KLID 00020409 when Windows exposes the
        /// layout under the en-US LANGID.
        /// </summary>
        private static InputProfile? MatchProfile(IReadOnlyList<InputProfile> installed, ProfileDefinition def)
        {
            var criteria = def.Match ?? new ProfileMatch();

            foreach (var profile in installed)
            {
                if (MatchesCriteria(profile, criteria))
                {
                    return profile;
                }
            }

            return MatchEnglishNetherlandsUsInternationalCompatibility(installed, criteria);
        }

        private static bool MatchesCriteria(InputProfile profile, ProfileMatch criteria)
        {
            if (!string.IsNullOrEmpty(criteria.KLID))
            {
                if (!string.Equals(profile.KLID, NormalizeKlid(criteria.KLID), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(criteria.LanguageTag))
            {
                try
                {
                    // Derive culture name from LANGID. CultureName might be like "en-US".
                    var culture = new CultureInfo((int)profile.LangId);
                    if (!string.Equals(culture.Name, criteria.LanguageTag, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            if (!ContainsProfileText(profile, criteria.LayoutNameContains))
            {
                return false;
            }

            if (!ContainsProfileText(profile, criteria.ProfileNameContains))
            {
                return false;
            }

            return true;
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
}
