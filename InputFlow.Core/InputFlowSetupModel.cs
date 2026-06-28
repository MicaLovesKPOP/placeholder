using System;
using System.Collections.Generic;
using System.Linq;

namespace InputFlow.Core
{
    /// <summary>
    /// UI-ready setup state derived from installed profiles and the current config.
    /// </summary>
    public sealed class InputFlowSetupModel
    {
        public InputFlowSetupModel(
            IReadOnlyList<SetupInstalledProfileOption> installedProfiles,
            IReadOnlyList<SetupConfiguredProfileOption> configuredProfiles,
            IReadOnlyList<SetupWorkflowOption> workflows)
        {
            InstalledProfiles = installedProfiles;
            ConfiguredProfiles = configuredProfiles;
            Workflows = workflows;
        }

        public IReadOnlyList<SetupInstalledProfileOption> InstalledProfiles { get; }
        public IReadOnlyList<SetupConfiguredProfileOption> ConfiguredProfiles { get; }
        public IReadOnlyList<SetupWorkflowOption> Workflows { get; }
    }

    public sealed class SetupInstalledProfileOption
    {
        public SetupInstalledProfileOption(
            InputProfile profile,
            string displayName,
            IReadOnlyList<string> configuredProfileIds)
        {
            Profile = profile;
            DisplayName = displayName;
            ConfiguredProfileIds = configuredProfileIds;
        }

        public InputProfile Profile { get; }
        public string DisplayName { get; }
        public IReadOnlyList<string> ConfiguredProfileIds { get; }
        public bool IsConfigured => ConfiguredProfileIds.Count > 0;
    }

    public sealed class SetupConfiguredProfileOption
    {
        public SetupConfiguredProfileOption(
            string profileId,
            ProfileHealthState health,
            string summary,
            ProfileMatch criteria,
            InputProfile? matchedProfile,
            string? enterMode)
        {
            ProfileId = profileId;
            Health = health;
            Summary = summary;
            Criteria = criteria;
            MatchedProfile = matchedProfile;
            EnterMode = enterMode;
        }

        public string ProfileId { get; }
        public ProfileHealthState Health { get; }
        public string Summary { get; }
        public ProfileMatch Criteria { get; }
        public InputProfile? MatchedProfile { get; }
        public string? EnterMode { get; }
        public bool CanUseForSwitching => Health is ProfileHealthState.Matched or ProfileHealthState.Changed;
    }

    public sealed class SetupWorkflowOption
    {
        public SetupWorkflowOption(
            string workflowId,
            string displayName,
            string mode,
            IReadOnlyList<string> triggerKeys,
            IReadOnlyList<string> targetProfileIds,
            string? fallbackProfileId,
            IReadOnlyList<string> blockingReasons)
        {
            WorkflowId = workflowId;
            DisplayName = displayName;
            Mode = mode;
            TriggerKeys = triggerKeys;
            TargetProfileIds = targetProfileIds;
            FallbackProfileId = fallbackProfileId;
            BlockingReasons = blockingReasons;
        }

        public string WorkflowId { get; }
        public string DisplayName { get; }
        public string Mode { get; }
        public IReadOnlyList<string> TriggerKeys { get; }
        public IReadOnlyList<string> TargetProfileIds { get; }
        public string? FallbackProfileId { get; }
        public IReadOnlyList<string> BlockingReasons { get; }
        public bool CanRegister => BlockingReasons.Count == 0;
    }

    public static class InputFlowSetupModelBuilder
    {
        public static InputFlowSetupModel Build(InputFlowConfig config, IReadOnlyList<InputProfile> installedProfiles)
        {
            config ??= new InputFlowConfig();
            installedProfiles ??= Array.Empty<InputProfile>();

            var reports = InputProfileManager.EvaluateProfileMatches(installedProfiles, config.Profiles);
            var configuredProfiles = BuildConfiguredProfiles(config, reports);
            var configuredById = configuredProfiles.ToDictionary(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase);

            return new InputFlowSetupModel(
                BuildInstalledProfiles(installedProfiles, reports),
                configuredProfiles,
                BuildWorkflows(config.Workflows, configuredById));
        }

        private static IReadOnlyList<SetupInstalledProfileOption> BuildInstalledProfiles(
            IReadOnlyList<InputProfile> installedProfiles,
            IReadOnlyList<ProfileMatchReport> reports)
        {
            var result = new List<SetupInstalledProfileOption>(installedProfiles.Count);
            foreach (var installed in installedProfiles)
            {
                var configuredIds = reports
                    .Where(report => report.MatchedProfile != null && ProfilesEqual(report.MatchedProfile, installed))
                    .Select(report => report.ProfileId)
                    .ToList();

                result.Add(new SetupInstalledProfileOption(
                    installed,
                    InputProfileManager.FormatProfile(installed),
                    configuredIds));
            }

            return result;
        }

        private static IReadOnlyList<SetupConfiguredProfileOption> BuildConfiguredProfiles(
            InputFlowConfig config,
            IReadOnlyList<ProfileMatchReport> reports)
        {
            var definitionsById = config.Profiles
                .Where(profile => profile != null && !string.IsNullOrWhiteSpace(profile.Id))
                .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            return reports
                .Select(report =>
                {
                    definitionsById.TryGetValue(report.ProfileId, out var definition);
                    return new SetupConfiguredProfileOption(
                        report.ProfileId,
                        report.Health,
                        report.Summary,
                        report.Criteria,
                        report.MatchedProfile,
                        definition?.EnterMode);
                })
                .ToList();
        }

        private static IReadOnlyList<SetupWorkflowOption> BuildWorkflows(
            IReadOnlyList<WorkflowConfig> workflows,
            IReadOnlyDictionary<string, SetupConfiguredProfileOption> configuredById)
        {
            return workflows
                .Select(workflow => BuildWorkflow(workflow, configuredById))
                .ToList();
        }

        private static SetupWorkflowOption BuildWorkflow(
            WorkflowConfig workflow,
            IReadOnlyDictionary<string, SetupConfiguredProfileOption> configuredById)
        {
            var targets = GetWorkflowTargetIds(workflow).ToList();
            var triggers = workflow.Triggers
                .Where(trigger => trigger != null && !string.IsNullOrWhiteSpace(trigger.Keys))
                .Select(trigger => trigger.Keys.Trim())
                .ToList();
            var blockingReasons = new List<string>();

            if (triggers.Count == 0)
            {
                blockingReasons.Add("No trigger is configured.");
            }

            if (targets.Count == 0 && !IsPreviousWorkflow(workflow))
            {
                blockingReasons.Add("No target profile is configured.");
            }

            foreach (string target in targets)
            {
                AddProfileBlockingReason("Target", target, configuredById, blockingReasons);
            }

            string? fallback = string.IsNullOrWhiteSpace(workflow.Fallback) ? null : workflow.Fallback.Trim();
            if (fallback != null)
            {
                AddProfileBlockingReason("Fallback", fallback, configuredById, blockingReasons);
            }

            return new SetupWorkflowOption(
                string.IsNullOrWhiteSpace(workflow.Id) ? string.Empty : workflow.Id.Trim(),
                GetWorkflowDisplayName(workflow),
                string.IsNullOrWhiteSpace(workflow.Mode) ? "toggle" : workflow.Mode.Trim(),
                triggers,
                targets,
                fallback,
                blockingReasons);
        }

        private static void AddProfileBlockingReason(
            string role,
            string profileId,
            IReadOnlyDictionary<string, SetupConfiguredProfileOption> configuredById,
            List<string> blockingReasons)
        {
            if (!configuredById.TryGetValue(profileId, out var profile))
            {
                blockingReasons.Add($"{role} profile '{profileId}' is not configured.");
                return;
            }

            if (!profile.CanUseForSwitching)
            {
                blockingReasons.Add($"{role} profile '{profileId}' is {profile.Health.ToString().ToLowerInvariant()}.");
            }
        }

        private static IReadOnlyList<string> GetWorkflowTargetIds(WorkflowConfig workflow)
        {
            if (IsPreviousWorkflow(workflow))
            {
                return Array.Empty<string>();
            }

            if (string.Equals(workflow.Mode, "cycle", StringComparison.OrdinalIgnoreCase))
            {
                return workflow.Targets
                    .Where(target => !string.IsNullOrWhiteSpace(target))
                    .Select(target => target.Trim())
                    .ToList();
            }

            return string.IsNullOrWhiteSpace(workflow.Target)
                ? Array.Empty<string>()
                : new[] { workflow.Target.Trim() };
        }

        private static bool IsPreviousWorkflow(WorkflowConfig workflow)
        {
            return string.Equals(workflow.Mode, "previous", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetWorkflowDisplayName(WorkflowConfig workflow)
        {
            if (!string.IsNullOrWhiteSpace(workflow.Name))
            {
                return workflow.Name.Trim();
            }

            return string.IsNullOrWhiteSpace(workflow.Id) ? "unnamed workflow" : workflow.Id.Trim();
        }

        private static bool ProfilesEqual(InputProfile a, InputProfile b)
        {
            return string.Equals(a.KLID, b.KLID, StringComparison.OrdinalIgnoreCase);
        }
    }
}
