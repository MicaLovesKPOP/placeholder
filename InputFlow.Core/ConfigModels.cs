using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace InputFlow.Core
{
    /// <summary>
    /// Represents the top-level configuration for InputFlow loaded from a JSON file.
    /// This mirrors the suggested schema in the design document and can be extended
    /// as new features are implemented. Unknown properties in the JSON are
    /// ignored so that forward compatibility is maintained.
    /// </summary>
    public class InputFlowConfig
    {
        public const int CurrentVersion = 2;

        /// <summary>
        /// Configuration format version.
        /// </summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>
        /// Whether InputFlow should start automatically with Windows. The host
        /// application must implement the startup logic; this flag only records
        /// the user preference.
        /// </summary>
        public bool Startup { get; set; } = false;

        /// <summary>
        /// Whether the tray icon should be shown. Most users will want this
        /// enabled; advanced users may hide the tray icon if they rely on
        /// hotkeys exclusively.
        /// </summary>
        public bool ShowTrayIcon { get; set; } = true;

        /// <summary>
        /// Desired minimum log level. Must be one of: Off, Error, Warning,
        /// Info, Debug, Trace. The logger implementation should honour this.
        /// </summary>
        public string LogLevel { get; set; } = "Info";

        /// <summary>
        /// Legacy v1 hotkey definitions. These are migrated to Workflows during
        /// loading and remain only for backwards compatibility with existing configs.
        /// </summary>
        public List<HotkeyConfig> Hotkeys { get; set; } = new();

        /// <summary>
        /// Version 2 workflow definitions. Each workflow owns its triggers and
        /// behavior, making two-profile toggles, direct switches, and cycles explicit.
        /// </summary>
        public List<WorkflowConfig> Workflows { get; set; } = new();

        /// <summary>
        /// A list of profile definitions used to match installed input methods.
        /// Each profile can be referenced by its Id in hotkey configurations.
        /// Matching rules allow users to specify either a language tag, KLID,
        /// or substrings of the layout or profile name. Additional fields may
        /// be added in the future.
        /// </summary>
        public List<ProfileDefinition> Profiles { get; set; } = new();

        /// <summary>
        /// Process names (without path) in which InputFlow should be disabled.
        /// Examples: "mstsc.exe", "vmconnect.exe". Matches are
        /// case-insensitive.
        /// </summary>
        public List<string> ExcludedProcesses { get; set; } = new();

        /// <summary>
        /// Loads a configuration object from a JSON file. Unknown properties
        /// in the JSON will be ignored. If the file is missing, cannot be
        /// parsed, or fails validation, a new configuration with default values
        /// is returned. Use <see cref="LoadDetailed"/> when callers need to
        /// preserve an existing runtime config after a bad reload.
        /// </summary>
        /// <param name="path">Path to the JSON configuration file.</param>
        public static InputFlowConfig Load(string path)
        {
            var result = LoadDetailed(path);
            return result.Success ? result.Config : new InputFlowConfig();
        }

        /// <summary>
        /// Loads and validates a configuration file without hiding parse or
        /// validation failures from the caller.
        /// </summary>
        /// <param name="path">Path to the JSON configuration file.</param>
        public static InputFlowConfigLoadResult LoadDetailed(string path)
        {
            if (!File.Exists(path))
            {
                return InputFlowConfigLoadResult.Valid(new InputFlowConfig());
            }

            var primary = TryLoadAndValidate(path);
            if (primary.Success)
            {
                return primary;
            }

            string lastKnownGoodPath = InputFlowConfigWriter.GetLastKnownGoodPath(path);
            if (File.Exists(lastKnownGoodPath))
            {
                var fallback = TryLoadAndValidate(lastKnownGoodPath);
                if (fallback.Success)
                {
                    return InputFlowConfigLoadResult.Valid(
                        fallback.Config,
                        primary.Errors.Concat(new[] { $"Loaded last-known-good config from {lastKnownGoodPath}." }).ToList());
                }

                return InputFlowConfigLoadResult.Invalid(
                    new InputFlowConfig(),
                    primary.Errors.Concat(fallback.Errors.Select(error => $"Last-known-good config failed: {error}")).ToList());
            }

            return primary;
        }

        private static InputFlowConfigLoadResult TryLoadAndValidate(string path)
        {
            InputFlowConfig? config;
            try
            {
                string json = File.ReadAllText(path);
                config = JsonSerializer.Deserialize<InputFlowConfig>(json, CreateJsonOptions());
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return InputFlowConfigLoadResult.Invalid(new InputFlowConfig(), $"Could not read or parse config '{path}': {ex.Message}");
            }

            if (config == null)
            {
                return InputFlowConfigLoadResult.Invalid(new InputFlowConfig(), $"Config file '{path}' did not contain a valid JSON object.");
            }

            NormalizeAndMigrate(config);

            var errors = InputFlowConfigValidator.Validate(config);
            return errors.Count == 0
                ? InputFlowConfigLoadResult.Valid(config)
                : InputFlowConfigLoadResult.Invalid(config, errors.Select(error => $"{path}: {error}").ToList());
        }

        private static void NormalizeAndMigrate(InputFlowConfig config)
        {
            config.Hotkeys ??= new List<HotkeyConfig>();
            config.Workflows ??= new List<WorkflowConfig>();
            config.Profiles ??= new List<ProfileDefinition>();
            config.ExcludedProcesses ??= new List<string>();

            foreach (var workflow in config.Workflows.Where(w => w != null))
            {
                workflow.Triggers ??= new List<TriggerConfig>();
                workflow.Targets ??= new List<string>();
            }

            if (config.Workflows.Count == 0 && config.Hotkeys.Count > 0)
            {
                for (int i = 0; i < config.Hotkeys.Count; i++)
                {
                    var hotkey = config.Hotkeys[i];
                    if (hotkey != null)
                    {
                        config.Workflows.Add(WorkflowConfig.FromLegacyHotkey(hotkey, i));
                    }
                }
            }

            if (config.Version == 1)
            {
                config.Version = CurrentVersion;
            }
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
        }
    }

    /// <summary>
    /// Result of loading and validating an InputFlow configuration file.
    /// </summary>
    public sealed class InputFlowConfigLoadResult
    {
        private InputFlowConfigLoadResult(InputFlowConfig config, bool success, IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
        {
            Config = config;
            Success = success;
            Errors = errors;
            Warnings = warnings;
        }

        public InputFlowConfig Config { get; }
        public bool Success { get; }
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }

        public static InputFlowConfigLoadResult Valid(InputFlowConfig config, IReadOnlyList<string>? warnings = null)
        {
            return new InputFlowConfigLoadResult(config, true, Array.Empty<string>(), warnings ?? Array.Empty<string>());
        }

        public static InputFlowConfigLoadResult Invalid(InputFlowConfig config, string error)
        {
            return new InputFlowConfigLoadResult(config, false, new[] { error }, Array.Empty<string>());
        }

        public static InputFlowConfigLoadResult Invalid(InputFlowConfig config, IReadOnlyList<string> errors)
        {
            return new InputFlowConfigLoadResult(config, false, errors.Count == 0 ? new[] { "Config validation failed." } : errors, Array.Empty<string>());
        }
    }

    /// <summary>
    /// Represents a v2 workflow. Workflows are the runtime-facing replacement
    /// for legacy v1 hotkeys.
    /// </summary>
    public class WorkflowConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Mode { get; set; } = "toggle";
        public List<TriggerConfig> Triggers { get; set; } = new();
        public string? Target { get; set; }
        public List<string> Targets { get; set; } = new();
        public string ReturnBehavior { get; set; } = "lastNonTarget";
        public string? Fallback { get; set; }

        public static WorkflowConfig FromLegacyHotkey(HotkeyConfig hotkey, int index)
        {
            string name = string.IsNullOrWhiteSpace(hotkey.Name) ? $"Hotkey {index + 1}" : hotkey.Name.Trim();
            return new WorkflowConfig
            {
                Id = CreateId(name, index),
                Name = name,
                Mode = string.IsNullOrWhiteSpace(hotkey.Mode) ? "toggle" : hotkey.Mode.Trim(),
                Triggers = string.IsNullOrWhiteSpace(hotkey.Keys)
                    ? new List<TriggerConfig>()
                    : new List<TriggerConfig> { new TriggerConfig { Keys = hotkey.Keys.Trim() } },
                Target = string.IsNullOrWhiteSpace(hotkey.Target) ? null : hotkey.Target.Trim(),
                ReturnBehavior = string.IsNullOrWhiteSpace(hotkey.ReturnBehavior) ? "lastNonTarget" : hotkey.ReturnBehavior.Trim(),
                Fallback = string.IsNullOrWhiteSpace(hotkey.Fallback) ? null : hotkey.Fallback.Trim()
            };
        }

        private static string CreateId(string name, int index)
        {
            var chars = name
                .Trim()
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray();
            string id = new string(chars).Trim('-');
            while (id.Contains("--", StringComparison.Ordinal))
            {
                id = id.Replace("--", "-", StringComparison.Ordinal);
            }

            return string.IsNullOrWhiteSpace(id) ? $"workflow-{index + 1}" : id;
        }
    }

    /// <summary>
    /// A trigger that activates a workflow.
    /// </summary>
    public class TriggerConfig
    {
        public string Keys { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a hotkey binding. A hotkey binds a set of modifier keys and
    /// a virtual key to a specific target profile. It also defines how to
    /// return from the target (return behaviour) and which fallback profile
    /// should be used if returning fails or if the return behaviour dictates
    /// always returning to a specific layout.
    /// </summary>
    public class HotkeyConfig
    {
        /// <summary>
        /// A friendly name for this hotkey. Used only for display and logs.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The key combination to listen for. Keys should be specified using
        /// '+' as a delimiter. Valid tokens include CTRL, ALT, SHIFT, WIN and
        /// any key defined in the <see cref="System.Windows.Forms.Keys"/> enum,
        /// such as F13, Space, A, etc. Example: "Ctrl+Shift+Space".
        /// </summary>
        public string Keys { get; set; } = string.Empty;

        /// <summary>
        /// The toggle mode: "toggle", "hold", or "cycle". Only "toggle" is
        /// implemented in this version. Other modes will be ignored.
        /// </summary>
        public string Mode { get; set; } = "toggle";

        /// <summary>
        /// The identifier of the target profile to switch to when the hotkey is
        /// pressed. This must match the Id of a <see cref="ProfileDefinition"/>
        /// in the configuration.
        /// </summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>
        /// The return behaviour after the target profile is active and the
        /// hotkey is pressed again. Supported values: "lastNonTarget",
        /// "alwaysSpecificLayout", "manualOnly". Additional values can be
        /// added later. See design document for semantics. Unrecognised
        /// values default to lastNonTarget.
        /// </summary>
        public string ReturnBehavior { get; set; } = "lastNonTarget";

        /// <summary>
        /// The identifier of the fallback profile to use when the previous
        /// non-target profile is unavailable or when ReturnBehavior is set to
        /// alwaysSpecificLayout. If null or empty, no fallback is defined and
        /// InputFlow will attempt to use the current system default instead.
        /// </summary>
        public string? Fallback { get; set; }
    }

    /// <summary>
    /// Represents a definition of an input profile that can be used in
    /// hotkey configurations. The matching rules are used to identify
    /// installed input profiles on the system. See the design document for
    /// examples.
    /// </summary>
    public class ProfileDefinition
    {
        /// <summary>
        /// A short, unique identifier for this profile. Used to refer to
        /// profiles in hotkey configurations. Examples: "us-intl", "korean",
        /// "german-qwertz".
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Rules for matching installed profiles. All specified fields must
        /// match for an installed profile to be considered a match. Fields
        /// are case-insensitive and partial matches (contains) are used for
        /// names.
        /// </summary>
        public ProfileMatch Match { get; set; } = new ProfileMatch();

        /// <summary>
        /// Optional mode to enter when switching to this profile. For example,
        /// "hangul" for Korean IME.
        /// </summary>
        public string? EnterMode { get; set; }
    }

    /// <summary>
    /// Matching criteria for a profile. At least one criterion should be set.
    /// If multiple fields are set, all must match. LanguageTag uses the
    /// BCP47 format (e.g. "en-US", "ko-KR"). KLID uses the Windows keyboard
    /// layout identifier format (e.g. "00020409" for US-International).
    /// LayoutNameContains and ProfileNameContains match against names as
    /// reported by Windows where available. These matches are case-insensitive.
    /// </summary>
    public class ProfileMatch
    {
        public string? LanguageTag { get; set; }
        public string? KLID { get; set; }
        public string? LayoutNameContains { get; set; }
        public string? ProfileNameContains { get; set; }
    }
}
