using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InputFlow.Core
{
    /// <summary>
    /// Creates starter configurations for new installations.
    /// </summary>
    public static class InputFlowConfigFactory
    {
        public static InputFlowConfig CreateFirstRunConfig(IReadOnlyList<InputProfile> installedProfiles)
        {
            var profiles = new List<ProfileDefinition>();
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var profile in installedProfiles ?? Array.Empty<InputProfile>())
            {
                string? languageTag = GetValidLanguageTag(profile);
                string id = CreateUniqueProfileId(profile, languageTag, usedIds);

                profiles.Add(new ProfileDefinition
                {
                    Id = id,
                    Match = new ProfileMatch
                    {
                        LanguageTag = languageTag,
                        KLID = profile.KLID
                    },
                    EnterMode = string.Equals(languageTag, "ko-KR", StringComparison.OrdinalIgnoreCase)
                        ? "hangul"
                        : null
                });
            }

            return new InputFlowConfig
            {
                Version = InputFlowConfig.CurrentVersion,
                Startup = false,
                ShowTrayIcon = true,
                LogLevel = "Info",
                ExcludedProcesses = new List<string> { "mstsc.exe", "vmconnect.exe" },
                Profiles = profiles,
                Workflows = new List<WorkflowConfig>()
            };
        }

        private static string CreateUniqueProfileId(InputProfile profile, string? languageTag, HashSet<string> usedIds)
        {
            string baseId = CreateProfileIdBase(profile, languageTag);
            string id = baseId;
            int suffix = 2;

            while (!usedIds.Add(id))
            {
                id = $"{baseId}-{suffix}";
                suffix++;
            }

            return id;
        }

        private static string CreateProfileIdBase(InputProfile profile, string? languageTag)
        {
            string source = !string.IsNullOrWhiteSpace(languageTag)
                ? languageTag
                : !string.IsNullOrWhiteSpace(profile.FriendlyName)
                    ? profile.FriendlyName
                    : profile.KLID;

            string slug = Slugify(source);
            return string.IsNullOrWhiteSpace(slug) ? $"profile-{profile.KLID.ToLowerInvariant()}" : slug;
        }

        private static string Slugify(string value)
        {
            var builder = new StringBuilder(value.Length);
            bool previousWasSeparator = false;

            foreach (char ch in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                    previousWasSeparator = false;
                }
                else if (!previousWasSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                    previousWasSeparator = true;
                }
            }

            return builder.ToString().Trim('-');
        }

        private static string? GetValidLanguageTag(InputProfile profile)
        {
            string? languageTag = profile.LanguageTag;
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                return null;
            }

            try
            {
                return CultureInfo.GetCultureInfo(languageTag.Trim()).Name;
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }
    }
}
