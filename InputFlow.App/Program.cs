using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using InputFlow.Core;

namespace InputFlow.App
{
    /// <summary>
    /// The entry point for the InputFlow tray application.  This class
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

            // Determine config and log paths relative to the executable.  In
            // portable mode the config sits next to the executable; an
            // installed version could place it elsewhere.  For now we keep
            // everything in the app directory.
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(appDir, "inputflow.json");
            string logPath = Path.Combine(appDir, "inputflow.log");

            // Ensure a config file exists.  Create a simple default if not.
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
                            Match = new ProfileMatch { LanguageTag = "en-US" }
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
                            Keys = "F13",
                            Target = "korean",
                            ReturnBehavior = "lastNonTarget",
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
        /// hotkey window, configuration and manager.  Implements config
        /// reloading and pause/resume.
        /// </summary>
        private sealed class TrayApplicationContext : ApplicationContext
        {
            private readonly string _configPath;
            private readonly string _logPath;
            private InputFlowConfig _config;
            private readonly ILogger _logger;
            private readonly InputFlowManager _manager;
            private readonly HotkeyWindow _hotkeyWindow;
            private readonly NotifyIcon _notifyIcon;
            private readonly Dictionary<int, (uint Modifiers, int Vk)> _registeredHotkeys = new();
            private int _nextHotkeyId = 1;
            private FileSystemWatcher? _configWatcher;

            public TrayApplicationContext(string configPath, string logPath)
            {
                _configPath = configPath;
                _logPath = logPath;
                _logger = new FileLogger(logPath);

                // Load config and installed profiles, then initialise manager.
                _config = InputFlowConfig.Load(_configPath);
                var installed = InputProfileManager.EnumerateInstalledProfiles();
                _manager = new InputFlowManager(installed, _config.ExcludedProcesses, _logger);

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
                menu.Items.Add(new ToolStripMenuItem("Pause/Resume", null, (_, _) => TogglePause()));
                menu.Items.Add(new ToolStripMenuItem("Reload Config", null, (_, _) => ReloadConfig()));
                menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));
                return menu;
            }

            private void TogglePause()
            {
                bool newState = !_manager.IsPaused;
                _manager.SetPaused(newState);
                _logger.Info(newState ? "Paused via tray." : "Resumed via tray.");
            }

            /// <summary>
            /// Loads and applies the configuration from disk.  Re-registers
            /// hotkeys and updates the tray icon visibility.  Called when the
            /// user chooses Reload Config or when the file system watcher
            /// detects a change.
            /// </summary>
            private void ReloadConfig()
            {
                try
                {
                    _logger.Info("Reloading configuration...");
                    var newConfig = InputFlowConfig.Load(_configPath);
                    _config = newConfig;
                    // Update tray visibility.
                    _notifyIcon.Visible = _config.ShowTrayIcon;
                    // Unregister existing hotkeys and register new ones.
                    UnregisterAllHotkeys();
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
            /// reloaded.  If the watcher cannot be created, no exception is
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
                    _configWatcher.Renamed += (_, _) => OnConfigFileChanged();
                    _configWatcher.EnableRaisingEvents = true;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Unable to watch config file for changes: {ex.Message}");
                }
            }

            private void OnConfigFileChanged()
            {
                // FileSystemWatcher may raise events multiple times.  Debounce by
                // invoking reload on the UI thread after a small delay.
                // Use BeginInvoke to ensure we are on the correct thread.
                ReloadConfig();
            }

            /// <summary>
            /// Registers hotkeys based on the current configuration.  Each hotkey
            /// in the configuration is parsed and registered with Windows.  If
            /// parsing or registration fails, a warning is logged and the
            /// offending hotkey is skipped.  Hotkeys are assigned unique IDs
            /// starting from 1.
            /// </summary>
            private void RegisterHotkeys()
            {
                var installed = InputProfileManager.EnumerateInstalledProfiles();
                var matched = InputProfileManager.MatchProfiles(installed, _config.Profiles);
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
                    if (!string.IsNullOrEmpty(hk.Fallback))
                    {
                        matched.TryGetValue(hk.Fallback, out fallback);
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
                    _logger.Info($"Registered hotkey '{hk.Keys}' for target '{hk.Target}'.");
                }
            }

            /// <summary>
            /// Unregisters all registered hotkeys.  Called when reloading
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

            /// <summary>
            /// Exits the application.  Ensures that hotkeys and other resources
            /// are cleaned up.
            /// </summary>
            private new void ExitThread()
            {
                // Clean up hotkeys and watchers
                UnregisterAllHotkeys();
                if (_configWatcher != null)
                {
                    _configWatcher.EnableRaisingEvents = false;
                    _configWatcher.Dispose();
                    _configWatcher = null;
                }
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _hotkeyWindow.Dispose();
                _logger.Info("Exiting InputFlow");
                Application.Exit();
            }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    UnregisterAllHotkeys();
                    _configWatcher?.Dispose();
                    _notifyIcon.Dispose();
                    _hotkeyWindow.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Hidden window used to receive WM_HOTKEY messages.  The window is
        /// message-only and never shown.  When a hotkey is pressed, the
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
        /// Parses a hotkey string into modifier flags and a virtual key code.  Supports
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

        // Modifier constants used by RegisterHotKey
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