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

        private static readonly HashSet<string> SupportedWorkflowModes = new(StringComparer.OrdinalIgnoreCase)
        {
            "toggle",
            "switchTo",
            "cycle",
            "previous"
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

            if (config.Version != InputFlowConfig.CurrentVersion)
            {
                errors.Add($"Unsupported config version '{config.Version}'. This build supports version {InputFlowConfig.CurrentVersion}.");
            }

            if (!SupportedLogLevels.Contains(config.LogLevel ?? string.Empty))
            {
                errors.Add($"LogLevel must be one of: {string.Join(", ", SupportedLogLevels)}.");
            }

            ValidateProfiles(config.Profiles ?? new List<ProfileDefinition>(), errors);
            ValidateWorkflows(config.Workflows ?? new List<WorkflowConfig>(), config.Profiles ?? new List<ProfileDefinition>(), errors);
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

        private static void ValidateWorkflows(IReadOnlyList<WorkflowConfig> workflows, IReadOnlyList<ProfileDefinition> profiles, List<string> errors)
        {
            var profileIds = profiles
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id))
                .Select(p => p.Id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenTriggers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < workflows.Count; i++)
            {
                var workflow = workflows[i];
                string label = $"Workflows[{i}]";

                if (workflow == null)
                {
                    errors.Add($"{label} must not be null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(workflow.Id))
                {
                    errors.Add($"{label}.Id is required.");
                }
                else if (!seenIds.Add(workflow.Id.Trim()))
                {
                    errors.Add($"Workflow id '{workflow.Id}' is duplicated.");
                }

                string mode = string.IsNullOrWhiteSpace(workflow.Mode) ? "toggle" : workflow.Mode.Trim();
                if (!SupportedWorkflowModes.Contains(mode))
                {
                    errors.Add($"{label}.Mode '{workflow.Mode}' is not supported by this build. Supported values: {string.Join(", ", SupportedWorkflowModes)}.");
                    continue;
                }

                ValidateWorkflowTriggers(workflow, label, seenTriggers, errors);
                ValidateWorkflowTargets(workflow, label, mode, profileIds, errors);
                ValidateWorkflowReturnBehavior(workflow, label, mode, errors);

                if (!string.IsNullOrWhiteSpace(workflow.Fallback) && !profileIds.Contains(workflow.Fallback.Trim()))
                {
                    errors.Add($"{label}.Fallback references unknown profile '{workflow.Fallback}'.");
                }
            }
        }

        private static void ValidateWorkflowTriggers(WorkflowConfig workflow, string label, Dictionary<string, string> seenTriggers, List<string> errors)
        {
            if (workflow.Triggers == null || workflow.Triggers.Count == 0)
            {
                errors.Add($"{label}.Triggers must contain at least one trigger.");
                return;
            }

            for (int i = 0; i < workflow.Triggers.Count; i++)
            {
                var trigger = workflow.Triggers[i];
                if (trigger == null)
                {
                    errors.Add($"{label}.Triggers[{i}] must not be null.");
                }
                else if (string.IsNullOrWhiteSpace(trigger.Keys))
                {
                    errors.Add($"{label}.Triggers[{i}].Keys is required.");
                }
                else
                {
                    var parseResult = InputFlowTriggerParser.Parse(trigger.Keys);
                    if (!parseResult.Success)
                    {
                        errors.Add($"{label}.Triggers[{i}].Keys '{trigger.Keys}' is invalid: {parseResult.Error}");
                    }
                    else
                    {
                        string triggerOwner = $"{label}.Triggers[{i}]";
                        if (seenTriggers.TryGetValue(parseResult.NormalizedKeys, out string? existingOwner))
                        {
                            errors.Add($"{triggerOwner}.Keys '{trigger.Keys}' duplicates trigger '{parseResult.NormalizedKeys}' already used by {existingOwner}.");
                        }
                        else
                        {
                            seenTriggers.Add(parseResult.NormalizedKeys, triggerOwner);
                        }
                    }
                }
            }
        }

        private static void ValidateWorkflowTargets(WorkflowConfig workflow, string label, string mode, HashSet<string> profileIds, List<string> errors)
        {
            if (mode.Equals("previous", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (mode.Equals("cycle", StringComparison.OrdinalIgnoreCase))
            {
                if (workflow.Targets == null || workflow.Targets.Count < 2)
                {
                    errors.Add($"{label}.Targets must contain at least two profiles for cycle mode.");
                    return;
                }

                ValidateTargetList(workflow.Targets, $"{label}.Targets", profileIds, errors);
                return;
            }

            if (string.IsNullOrWhiteSpace(workflow.Target))
            {
                errors.Add($"{label}.Target is required for {mode} mode.");
            }
            else if (!profileIds.Contains(workflow.Target.Trim()))
            {
                errors.Add($"{label}.Target references unknown profile '{workflow.Target}'.");
            }
        }

        private static void ValidateTargetList(IReadOnlyList<string> targets, string label, HashSet<string> profileIds, List<string> errors)
        {
            var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Count; i++)
            {
                string target = targets[i];
                if (string.IsNullOrWhiteSpace(target))
                {
                    errors.Add($"{label}[{i}] is required.");
                }
                else if (!profileIds.Contains(target.Trim()))
                {
                    errors.Add($"{label}[{i}] references unknown profile '{target}'.");
                }
                else if (!seenTargets.Add(target.Trim()))
                {
                    errors.Add($"{label}[{i}] duplicates profile '{target}'.");
                }
            }
        }

        private static void ValidateWorkflowReturnBehavior(WorkflowConfig workflow, string label, string mode, List<string> errors)
        {
            if (mode.Equals("cycle", StringComparison.OrdinalIgnoreCase) ||
                mode.Equals("switchTo", StringComparison.OrdinalIgnoreCase) ||
                mode.Equals("previous", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string returnBehavior = string.IsNullOrWhiteSpace(workflow.ReturnBehavior) ? "lastNonTarget" : workflow.ReturnBehavior.Trim();
            if (!SupportedReturnBehaviors.Contains(returnBehavior))
            {
                errors.Add($"{label}.ReturnBehavior '{workflow.ReturnBehavior}' is not supported. Supported values: {string.Join(", ", SupportedReturnBehaviors)}.");
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
