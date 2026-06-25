using System.Collections.Generic;

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
        /// <summary>
        /// Configuration format version. Reserved for future use.
        /// </summary>
        public int Version { get; set; } = 1;

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
        /// A list of hotkey definitions. Each hotkey binds a key combination to
        /// a target profile and defines how InputFlow should behave when it is
        /// pressed. For example, pressing F13 might toggle Korean IME using
        /// the lastNonTarget return behaviour.
        /// </summary>
        public List<HotkeyConfig> Hotkeys { get; set; } = new();

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
        /// in the JSON will be ignored. If the file is missing or cannot be
        /// parsed, a new configuration with default values is returned.
        /// </summary>
        /// <param name="path">Path to the JSON configuration file.</param>
        public static InputFlowConfig Load(string path)
        {
            try
            {
                string json = System.IO.File.ReadAllText(path);
                var opts = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip
                };
                return System.Text.Json.JsonSerializer.Deserialize<InputFlowConfig>(json, opts) ?? new InputFlowConfig();
            }
            catch
            {
                // Return defaults on failure.
                return new InputFlowConfig();
            }
        }
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
