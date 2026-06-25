using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using InputFlow.Core;

namespace InputFlow.App
{
    /// <summary>
    /// The entry point for the InputFlow tray application. This class
    /// bootstraps the configuration, enumerates installed profiles, registers
    /// global hotkeys, creates the tray icon and handles config reloading.
    /// The application runs without any visible main window and processes
    /// global hotkey events via a message-only window.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Determine config and log paths relative to the executable. In
            // portable mode the config sits next to the executable; an
            // installed version could place it elsewhere. For now we keep
            // everything in the app directory.
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(appDir, "inputflow.json");
            string logPath = Path.Combine(appDir, "inputflow.log");

            // Ensure a config file exists. Create a simple default if not.
            if (!File.Exists(configPath))
            {
                var defaultConfig = new InputFlowConfig
                {
                    Version = 1,
                    Startup = false,
                    ShowTrayIcon = true,
                    LogLevel = "Info",
                    ExcludedProcesses = new List<string> { "mstsc.exe", "vmconnect.exe" },
                    Profiles = new List<ProfileDefinition>
                    {
                        new ProfileDefinition
                        {
                            Id = "us-intl",
                            Match = new ProfileMatch { LanguageTag = "en-NL" }
                        },
                        new ProfileDefinition
                        {
                            Id = "korean",
                            Match = new ProfileMatch { LanguageTag = "ko-KR" },
                            EnterMode = "hangul"
                        }
                    },
                    Hotkeys = new List<HotkeyConfig>
                    {
                        new HotkeyConfig
                        {
                            Name = "Korean toggle",
                            Keys = "Ctrl+Alt+Shift+K",
                            Target = "korean",
                            ReturnBehavior = "alwaysSpecificLayout",
                            Fallback = "us-intl"
                        }
                    }
                };
                var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(defaultConfig, opts));
            }

            // Create the application context which owns the manager and tray icon.
            using var context = new TrayApplicationContext(configPath, logPath);
            Application.Run(context);
        }

        /// <summary>
        /// Custom application context that owns the lifetime of the tray icon,
        /// hotkey window, configuration and manager. Implements config
        /// reloading and pause/resume.
        /// </summary>
        private sealed class TrayApplicationContext : ApplicationContext
        {
            private const int ConfigReloadDebounceMilliseconds = 500;

            private readonly string _configPath;
            private readonly string _logPath;
            private InputFlowConfig _config;
            private readonly ILogger _logger;
            private readonly InputFlowManager _manager;
            private readonly HotkeyWindow _hotkeyWindow;
            private readonly NotifyIcon _notifyIcon;
            private readonly Control _uiDispatcher;
            private readonly System.Windows.Forms.Timer _configReloadTimer;
            private readonly Dictionary<int, (uint Modifiers, int Vk)> _registeredHotkeys = new();
            private IReadOnlyList<InputProfile> _installedProfiles;
            private int _nextHotkeyId = 1;
            private FileSystemWatcher? _configWatcher;
            private ToolStripMenuItem? _pauseMenuItem;

            public TrayApplicationContext(string configPath, string logPath)
            {
                _configPath = configPath;
                _logPath = logPath;
                _logger = new FileLogger(logPath);

                _uiDispatcher = new Control();
                _uiDispatcher.CreateControl();
                _ = _uiDispatcher.Handle;

                _configReloadTimer = new System.Windows.Forms.Timer
                {
                    Interval = ConfigReloadDebounceMilliseconds
                };
                _configReloadTimer.Tick += (_, _) =>
                {
                    _configReloadTimer.Stop();
                    ReloadConfig("file watcher");
                };

                // Load config and installed profiles, then initialise manager.
                _config = InputFlowConfig.Load(_configPath);
                _installedProfiles = InputProfileManager.EnumerateInstalledProfiles();
                _manager = new InputFlowManager(_installedProfiles, _config.ExcludedProcesses, _logger);

                _hotkeyWindow = new HotkeyWindow();
                _hotkeyWindow.HotkeyPressed += id => _manager.OnHotkeyPressed(id);

                // Set up tray icon and menu.
                _notifyIcon = new NotifyIcon
                {
                    Text = "InputFlow",
                    Icon = System.Drawing.SystemIcons.Application,
                    Visible = _config.ShowTrayIcon
                };
                _notifyIcon.ContextMenuStrip = BuildContextMenu();

                LogStartupDiagnostics();

                // Register initial hotkeys.
                RegisterHotkeys();

                // Watch for config changes.
                SetupConfigWatcher();
            }

            /// <summary>
            /// Builds the context menu for the tray icon.
            /// </summary>
            private ContextMenuStrip BuildContextMenu()
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add(new ToolStripMenuItem("Open Config", null, (_, _) => OpenPath(_configPath, "config file")));
                menu.Items.Add(new ToolStripMenuItem("Open Log", null, (_, _) => OpenPath(_logPath, "log file")));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem("Reload Config", null, (_, _) => ReloadConfig("tray menu")));
                _pauseMenuItem = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());
                menu.Items.Add(_pauseMenuItem);
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));
                UpdatePauseMenuText();
                return menu;
            }

            private void OpenPath(string path, string displayName)
            {
                try
                {
                    if (!File.Exists(path) && !Directory.Exists(path))
                    {
                        _logger.Warning($"Cannot open {displayName}: path does not exist: {path}");
                        return;
                    }

                    Process.Start(new ProcessStartInfo(path)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Cannot open {displayName} '{path}': {ex.Message}");
                }
            }

            private void TogglePause()
            {
                bool newState = !_manager.IsPaused;
                _manager.SetPaused(newState);
                UpdatePauseMenuText();
                _logger.Info(newState ? "Paused via tray." : "Resumed via tray.");
            }

            private void UpdatePauseMenuText()
            {
                if (_pauseMenuItem != null)
                {
                    _pauseMenuItem.Text = _manager.IsPaused ? "Resume" : "Pause";
                }
            }

            /// <summary>
            /// Loads and applies the configuration from disk. Re-registers
            /// hotkeys and updates the tray icon visibility. Called when the
            /// user chooses Reload Config or when the file system watcher
            /// detects a change.
            /// </summary>
            private void ReloadConfig(string reason)
            {
                try
                {
                    _logger.Info($"Reloading configuration ({reason})...");
                    var newConfig = InputFlowConfig.Load(_configPath);
                    var installed = InputProfileManager.EnumerateInstalledProfiles();

                    _config = newConfig;
                    _installedProfiles = installed;
                    _notifyIcon.Visible = _config.ShowTrayIcon;

                    UnregisterAllHotkeys();
                    _manager.ClearHotkeys();
                    _manager.UpdateRuntimeState(_installedProfiles, _config.ExcludedProcesses);

                    LogInstalledProfiles(_installedProfiles);
                    LogProfileMatches(_installedProfiles, _config.Profiles);
                    RegisterHotkeys();

                    _logger.Info("Configuration reloaded.");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to reload configuration: {ex.Message}");
                }
            }

            /// <summary>
            /// Sets up a file system watcher to monitor the configuration file.
            /// When the file is changed or renamed, the configuration is
            /// reloaded. If the watcher cannot be created, no exception is
            /// thrown; the user can still reload manually via the menu.
            /// </summary>
            private void SetupConfigWatcher()
            {
                try
                {
                    string directory = Path.GetDirectoryName(_configPath) ?? ".";
                    string fileName = Path.GetFileName(_configPath);
                    _configWatcher = new FileSystemWatcher(directory, fileName)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                    };
                    _configWatcher.Changed += (_, _) => OnConfigFileChanged();
                    _configWatcher.Created += (_, _) => OnConfigFileChanged();
                    _configWatcher.Renamed += (_, _) => OnConfigFileChanged();
                    _configWatcher.EnableRaisingEvents = true;
                    _logger.Info($"Watching config file for changes: {_configPath}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Unable to watch config file for changes: {ex.Message}");
                }
            }

            private void OnConfigFileChanged()
            {
                if (_uiDispatcher.IsDisposed || !_uiDispatcher.IsHandleCreated)
                {
                    return;
                }

                try
                {
                    _uiDispatcher.BeginInvoke((Action)ScheduleConfigReload);
                }
                catch (InvalidOperationException)
                {
                    // The app is probably exiting. Ignore late watcher events.
                }
            }

            private void ScheduleConfigReload()
            {
                _configReloadTimer.Stop();
                _configReloadTimer.Start();
            }

            /// <summary>
            /// Registers hotkeys based on the current configuration. Each hotkey
            /// in the configuration is parsed and registered with Windows. If
            /// parsing or registration fails, a warning is logged and the
            /// offending hotkey is skipped.
            /// </summary>
            private void RegisterHotkeys()
            {
                _nextHotkeyId = 1;
                var matched = InputProfileManager.MatchProfiles(_installedProfiles, _config.Profiles);
                foreach (var hk in _config.Hotkeys)
                {
                    // Only toggle mode is supported currently.
                    if (!string.Equals(hk.Mode, "toggle", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Warning($"Hotkey '{hk.Name}' uses unsupported mode '{hk.Mode}'. Skipping.");
                        continue;
                    }
                    // Find target profile.
                    if (!matched.TryGetValue(hk.Target, out var target))
                    {
                        _logger.Warning($"Hotkey '{hk.Name}' references unknown target profile '{hk.Target}'. Skipping.");
                        continue;
                    }
                    // Find fallback profile if specified.
                    InputProfile? fallback = null;
                    if (!string.IsNullOrEmpty(hk.Fallback) && !matched.TryGetValue(hk.Fallback, out fallback))
                    {
                        _logger.Warning($"Hotkey '{hk.Name}' references fallback profile '{hk.Fallback}' that did not match an installed profile.");
                    }
                    // Parse the key combination.
                    if (!TryParseHotkey(hk.Keys, out uint mods, out int vk))
                    {
                        _logger.Warning($"Hotkey '{hk.Name}' has invalid key specification '{hk.Keys}'. Skipping.");
                        continue;
                    }
                    int id = _nextHotkeyId++;
                    bool ok = RegisterHotKey(_hotkeyWindow.Handle, id, mods, vk);
                    if (!ok)
                    {
                        _logger.Warning($"Failed to register hotkey '{hk.Keys}' (Name: {hk.Name}). It may be in use.");
                        continue;
                    }
                    _registeredHotkeys[id] = (mods, vk);
                    // Inform manager of this hotkey's state.
                    var targetDefinition = _config.Profiles.FirstOrDefault(p => string.Equals(p.Id, hk.Target, StringComparison.OrdinalIgnoreCase));
                    _manager.RegisterHotkey(id, target, fallback, hk.ReturnBehavior ?? "lastNonTarget", targetDefinition?.EnterMode);
                    _logger.Info($"Registered hotkey '{hk.Keys}' for target '{hk.Target}' as ID {id}.");
                }
            }

            /// <summary>
            /// Unregisters all registered hotkeys. Called when reloading
            /// configuration or exiting.
            /// </summary>
            private void UnregisterAllHotkeys()
            {
                foreach (var kvp in _registeredHotkeys)
                {
                    UnregisterHotKey(_hotkeyWindow.Handle, kvp.Key);
                }
                _registeredHotkeys.Clear();
            }

            private void LogStartupDiagnostics()
            {
                _logger.Info("InputFlow starting.");
                _logger.Info($"Config path: {_configPath}");
                _logger.Info($"Log path: {_logPath}");
                _logger.Info($"Tray icon visible: {_config.ShowTrayIcon}");
                _logger.Info($"Configured hotkeys: {_config.Hotkeys.Count}");
                _logger.Info($"Configured profiles: {_config.Profiles.Count}");
                _logger.Info($"Excluded processes: {string.Join(", ", _config.ExcludedProcesses)}");
                LogInstalledProfiles(_installedProfiles);
                LogProfileMatches(_installedProfiles, _config.Profiles);
            }

            private void LogInstalledProfiles(IReadOnlyList<InputProfile> installed)
            {
                _logger.Info($"Installed input profiles found: {installed.Count}");
                foreach (var profile in installed)
                {
                    _logger.Info($"Installed profile: {FormatProfile(profile)}");
                }
            }

            private void LogProfileMatches(IReadOnlyList<InputProfile> installed, IEnumerable<ProfileDefinition> definitions)
            {
                var matched = InputProfileManager.MatchProfiles(installed, definitions);
                foreach (var definition in definitions)
                {
                    if (matched.TryGetValue(definition.Id, out var profile))
                    {
                        _logger.Info($"Configured profile '{definition.Id}' matched {FormatProfile(profile)} using {FormatMatchCriteria(definition.Match)}.");
                    }
                    else
                    {
                        _logger.Warning($"Configured profile '{definition.Id}' did not match any installed profile using {FormatMatchCriteria(definition.Match)}.");
                    }
                }
            }

            private static string FormatProfile(InputProfile profile)
            {
                return $"{profile.FriendlyName} KLID={profile.KLID} HKL=0x{profile.HKL.ToInt64():X8} Lang={GetCultureName(profile)} IsIme={profile.IsIme}";
            }

            private static string GetCultureName(InputProfile profile)
            {
                try
                {
                    return new CultureInfo((int)profile.LangId).Name;
                }
                catch
                {
                    return profile.LangId.ToString("X4");
                }
            }

            private static string FormatMatchCriteria(ProfileMatch? match)
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

            /// <summary>
            /// Exits the application. Ensures that hotkeys and other resources
            /// are cleaned up.
            /// </summary>
            private new void ExitThread()
            {
                // Clean up hotkeys and watchers.
                UnregisterAllHotkeys();
                _configReloadTimer.Stop();
                if (_configWatcher != null)
                {
                    _configWatcher.EnableRaisingEvents = false;
                    _configWatcher.Dispose();
                    _configWatcher = null;
                }
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _hotkeyWindow.Dispose();
                _uiDispatcher.Dispose();
                _logger.Info("Exiting InputFlow");
                Application.Exit();
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    UnregisterAllHotkeys();
                    _configReloadTimer.Dispose();
                    _configWatcher?.Dispose();
                    _notifyIcon.Dispose();
                    _hotkeyWindow.Dispose();
                    _uiDispatcher.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Hidden window used to receive WM_HOTKEY messages. The window is
        /// message-only and never shown. When a hotkey is pressed, the
        /// HotkeyPressed event is raised with the hotkey identifier.
        /// </summary>
        private class HotkeyWindow : NativeWindow, IDisposable
        {
            public event Action<int>? HotkeyPressed;

            private const int WM_HOTKEY = 0x0312;

            public HotkeyWindow()
            {
                // Create a message-only window.
                var cp = new CreateParams { Caption = "InputFlowHotkeyWindow" };
                CreateHandle(cp);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    HotkeyPressed?.Invoke(id);
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                DestroyHandle();
            }
        }

        /// <summary>
        /// Parses a hotkey string into modifier flags and a virtual key code. Supports
        /// combinations like "Ctrl+Shift+Space" or single keys like "F13".
        /// Returns true if parsing succeeded.
        /// </summary>
        private static bool TryParseHotkey(string hotkey, out uint modifiers, out int vk)
        {
            modifiers = 0;
            vk = 0;
            if (string.IsNullOrWhiteSpace(hotkey)) return false;
            string[] parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string token = part.Trim();
                switch (token.ToUpperInvariant())
                {
                    case "CTRL": modifiers |= MOD_CONTROL; break;
                    case "ALT": modifiers |= MOD_ALT; break;
                    case "SHIFT": modifiers |= MOD_SHIFT; break;
                    case "WIN": modifiers |= MOD_WIN; break;
                    default:
                        // Assume last token is the virtual key.
                        if (Enum.TryParse(typeof(Keys), token, true, out var keyObj))
                        {
                            vk = (int)keyObj;
                        }
                        else
                        {
                            return false;
                        }
                        break;
                }
            }
            return vk != 0;
        }

        // Modifier constants used by RegisterHotKey.
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
