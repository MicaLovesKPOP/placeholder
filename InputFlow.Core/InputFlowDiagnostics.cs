using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace InputFlow.Core
{
    /// <summary>
    /// Builds human-readable diagnostics from current runtime/config state.
    /// </summary>
    public static class InputFlowDiagnostics
    {
        public static string BuildReport(
            InputFlowConfig config,
            IReadOnlyList<InputProfile> installedProfiles,
            string configPath,
            string logPath)
        {
            var builder = new StringBuilder();
            string lastKnownGoodPath = InputFlowConfigWriter.GetLastKnownGoodPath(configPath);
            builder.AppendLine("InputFlow diagnostics");
            builder.AppendLine($"Generated: {DateTimeOffset.Now:O}");
            builder.AppendLine($"Config path: {configPath}");
            builder.AppendLine($"Last-good config path: {lastKnownGoodPath} ({FormatExists(lastKnownGoodPath)})");
            builder.AppendLine($"Log path: {logPath}");
            builder.AppendLine($"Config version: {config.Version}");
            builder.AppendLine($"Tray icon visible: {config.ShowTrayIcon}");
            builder.AppendLine($"Log level: {config.LogLevel}");
            builder.AppendLine($"Excluded processes: {FormatList(config.ExcludedProcesses)}");
            builder.AppendLine();

            var setup = InputFlowSetupModelBuilder.Build(config, installedProfiles);

            AppendWorkflows(builder, config.Workflows);
            AppendProfiles(builder, installedProfiles);
            AppendSetupProfileOptions(builder, setup.InstalledProfiles);
            AppendMatchReports(builder, InputProfileManager.EvaluateProfileMatches(installedProfiles, config.Profiles));
            AppendWorkflowReadiness(builder, setup.Workflows);

            return builder.ToString();
        }

        private static void AppendWorkflows(StringBuilder builder, IReadOnlyList<WorkflowConfig> workflows)
        {
            builder.AppendLine($"Configured workflows: {workflows.Count}");
            foreach (var workflow in workflows)
            {
                builder.AppendLine($"- {DisplayWorkflowName(workflow)} mode={workflow.Mode} triggers={FormatTriggers(workflow.Triggers)} target={workflow.Target ?? ""} targets={FormatList(workflow.Targets)} fallback={workflow.Fallback ?? ""}");
            }
            builder.AppendLine();
        }

        private static void AppendProfiles(StringBuilder builder, IReadOnlyList<InputProfile> installedProfiles)
        {
            builder.AppendLine($"Installed input profiles: {installedProfiles.Count}");
            foreach (var profile in installedProfiles)
            {
                builder.AppendLine($"- {InputProfileManager.FormatProfile(profile)}");
            }
            builder.AppendLine();
        }

        private static void AppendSetupProfileOptions(StringBuilder builder, IReadOnlyList<SetupInstalledProfileOption> profiles)
        {
            builder.AppendLine($"Setup profile options: {profiles.Count}");
            foreach (var profile in profiles)
            {
                builder.AppendLine($"- {profile.DisplayName} configuredAs={FormatList(profile.ConfiguredProfileIds)}");
            }
            builder.AppendLine();
        }

        private static void AppendMatchReports(StringBuilder builder, IReadOnlyList<ProfileMatchReport> reports)
        {
            builder.AppendLine($"Configured profile match reports: {reports.Count}");
            foreach (var report in reports)
            {
                builder.AppendLine($"- {report.ProfileId}: {report.Health.ToString().ToLowerInvariant()} - {report.Summary}");
                if (report.MatchedProfile != null)
                {
                    builder.AppendLine($"  selected: {InputProfileManager.FormatProfile(report.MatchedProfile)}");
                }
                builder.AppendLine($"  criteria: {FormatMatchCriteria(report.Criteria)}");

                foreach (var candidate in report.Candidates)
                {
                    builder.AppendLine($"  candidate: {(candidate.IsMatch ? "match" : "no match")} {InputProfileManager.FormatProfile(candidate.Profile)} - {candidate.Reason}");
                }
            }
        }

        private static void AppendWorkflowReadiness(StringBuilder builder, IReadOnlyList<SetupWorkflowOption> workflows)
        {
            builder.AppendLine();
            builder.AppendLine($"Workflow readiness: {workflows.Count}");
            foreach (var workflow in workflows)
            {
                builder.AppendLine($"- {workflow.DisplayName} ({workflow.WorkflowId}) mode={workflow.Mode} status={(workflow.CanRegister ? "ready" : "blocked")} triggers={FormatList(workflow.TriggerKeys)} targets={FormatList(workflow.TargetProfileIds)} fallback={workflow.FallbackProfileId ?? ""}");
                foreach (string reason in workflow.BlockingReasons)
                {
                    builder.AppendLine($"  block: {reason}");
                }
            }
        }

        public static string FormatMatchCriteria(ProfileMatch? match)
        {
            if (match == null)
            {
                return "no criteria";
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(match.LanguageTag))
            {
                parts.Add($"LanguageTag={match.LanguageTag}");
            }
            if (!string.IsNullOrWhiteSpace(match.KLID))
            {
                parts.Add($"KLID={match.KLID}");
            }
            if (!string.IsNullOrWhiteSpace(match.LayoutNameContains))
            {
                parts.Add($"LayoutNameContains={match.LayoutNameContains}");
            }
            if (!string.IsNullOrWhiteSpace(match.ProfileNameContains))
            {
                parts.Add($"ProfileNameContains={match.ProfileNameContains}");
            }

            return parts.Count == 0 ? "no criteria" : string.Join(", ", parts);
        }

        private static string DisplayWorkflowName(WorkflowConfig workflow)
        {
            if (!string.IsNullOrWhiteSpace(workflow.Name))
            {
                return workflow.Name;
            }

            return string.IsNullOrWhiteSpace(workflow.Id) ? "unnamed workflow" : workflow.Id;
        }

        private static string FormatTriggers(IReadOnlyList<TriggerConfig> triggers)
        {
            return FormatList(triggers.Select(trigger => trigger.Keys));
        }

        private static string FormatList(IEnumerable<string> values)
        {
            var filtered = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            return filtered.Count == 0 ? "(none)" : string.Join(", ", filtered);
        }

        private static string FormatExists(string path)
        {
            return File.Exists(path) ? "exists" : "missing";
        }
    }
}
