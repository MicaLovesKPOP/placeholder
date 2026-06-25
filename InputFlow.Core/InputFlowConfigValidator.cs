using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace InputFlow.Core
{
    /// <summary>
    /// Validates the currently supported configuration schema before runtime
    /// state is changed. Keep this validator independent from Win32 so it can
    /// be reused by tests and the future settings UI.
    /// </summary>
    public static class InputFlowConfigValidator
    {
        private static readonly HashSet<string> SupportedLogLevels = new(StringComparer.OrdinalIgnoreCase)
        {
            "Off",
            "Error",
            "Warning",
            "Info",
            "Debug",
            "Trace"
        };

        private static readonly HashSet<string> SupportedModes = new(StringComparer.OrdinalIgnoreCase)
        {
            "toggle"
        };

        private static readonly HashSet<string> SupportedReturnBehaviors = new(StringComparer.OrdinalIgnoreCase)
        {
            "lastNonTarget",
            "alwaysSpecificLayout",
            "manualOnly"
        };

        private static readonly HashSet<string> SupportedEnterModes = new(StringComparer.OrdinalIgnoreCase)
        {
            "hangul"
        };

        public static IReadOnlyList<string> Validate(InputFlowConfig config)
        {
            var errors = new List<string>();

            if (config.Version != 1)
            {
                errors.Add($"Unsupported config version '{config.Version}'. This build supports version 1.");
            }

            if (!SupportedLogLevels.Contains(config.LogLevel ?? string.Empty))
            {
                errors.Add($"LogLevel must be one of: {string.Join(", ", SupportedLogLevels)}.");
            }

            ValidateProfiles(config.Profiles ?? new List<ProfileDefinition>(), errors);
            ValidateHotkeys(config.Hotkeys ?? new List<HotkeyConfig>(), config.Profiles ?? new List<ProfileDefinition>(), errors);
            ValidateExcludedProcesses(config.ExcludedProcesses ?? new List<string>(), errors);

            return errors;
        }

        private static void ValidateProfiles(IReadOnlyList<ProfileDefinition> profiles, List<string> errors)
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                string label = $"Profiles[{i}]";

                if (profile == null)
                {
                    errors.Add($"{label} must not be null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(profile.Id))
                {
                    errors.Add($"{label}.Id is required.");
                }
                else if (!seenIds.Add(profile.Id.Trim()))
                {
                    errors.Add($"Profile id '{profile.Id}' is duplicated.");
                }

                if (profile.Match == null || !HasAnyProfileMatchCriterion(profile.Match))
                {
                    errors.Add($"{label}.Match must define at least one criterion.");
                }
                else
                {
                    ValidateProfileMatch(profile.Match, label, errors);
                }

                if (!string.IsNullOrWhiteSpace(profile.EnterMode) && !SupportedEnterModes.Contains(profile.EnterMode))
                {
                    errors.Add($"{label}.EnterMode '{profile.EnterMode}' is not supported. Supported values: {string.Join(", ", SupportedEnterModes)}.");
                }
            }
        }

        private static void ValidateProfileMatch(ProfileMatch match, string label, List<string> errors)
        {
            if (!string.IsNullOrWhiteSpace(match.LanguageTag))
            {
                try
                {
                    _ = CultureInfo.GetCultureInfo(match.LanguageTag.Trim());
                }
                catch (CultureNotFoundException)
                {
                    errors.Add($"{label}.Match.LanguageTag '{match.LanguageTag}' is not a valid culture name.");
                }
            }

            if (!string.IsNullOrWhiteSpace(match.KLID) && !IsValidKlid(match.KLID))
            {
                errors.Add($"{label}.Match.KLID '{match.KLID}' must be 1 to 8 hexadecimal characters, optionally prefixed with 0x.");
            }
        }

        private static void ValidateHotkeys(IReadOnlyList<HotkeyConfig> hotkeys, IReadOnlyList<ProfileDefinition> profiles, List<string> errors)
        {
            var profileIds = profiles
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id))
                .Select(p => p.Id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < hotkeys.Count; i++)
            {
                var hotkey = hotkeys[i];
                string label = $"Hotkeys[{i}]";

                if (hotkey == null)
                {
                    errors.Add($"{label} must not be null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(hotkey.Keys))
                {
                    errors.Add($"{label}.Keys is required.");
                }

                string mode = string.IsNullOrWhiteSpace(hotkey.Mode) ? "toggle" : hotkey.Mode.Trim();
                if (!SupportedModes.Contains(mode))
                {
                    errors.Add($"{label}.Mode '{hotkey.Mode}' is not supported by this build. Supported values: {string.Join(", ", SupportedModes)}.");
                }

                if (string.IsNullOrWhiteSpace(hotkey.Target))
                {
                    errors.Add($"{label}.Target is required.");
                }
                else if (!profileIds.Contains(hotkey.Target.Trim()))
                {
                    errors.Add($"{label}.Target references unknown profile '{hotkey.Target}'.");
                }

                string returnBehavior = string.IsNullOrWhiteSpace(hotkey.ReturnBehavior) ? "lastNonTarget" : hotkey.ReturnBehavior.Trim();
                if (!SupportedReturnBehaviors.Contains(returnBehavior))
                {
                    errors.Add($"{label}.ReturnBehavior '{hotkey.ReturnBehavior}' is not supported. Supported values: {string.Join(", ", SupportedReturnBehaviors)}.");
                }

                if (!string.IsNullOrWhiteSpace(hotkey.Fallback) && !profileIds.Contains(hotkey.Fallback.Trim()))
                {
                    errors.Add($"{label}.Fallback references unknown profile '{hotkey.Fallback}'.");
                }
            }
        }

        private static void ValidateExcludedProcesses(IReadOnlyList<string> excludedProcesses, List<string> errors)
        {
            for (int i = 0; i < excludedProcesses.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(excludedProcesses[i]))
                {
                    errors.Add($"ExcludedProcesses[{i}] must not be empty.");
                }
            }
        }

        private static bool HasAnyProfileMatchCriterion(ProfileMatch match)
        {
            return !string.IsNullOrWhiteSpace(match.LanguageTag) ||
                !string.IsNullOrWhiteSpace(match.KLID) ||
                !string.IsNullOrWhiteSpace(match.LayoutNameContains) ||
                !string.IsNullOrWhiteSpace(match.ProfileNameContains);
        }

        private static bool IsValidKlid(string value)
        {
            string normalized = value.Trim();
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[2..];
            }

            return normalized.Length is >= 1 and <= 8 && normalized.All(Uri.IsHexDigit);
        }
    }
}
