using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using InputFlow.Core;
using InputFlow.Windows;

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
        private const string SingleInstanceMutexName = @"Local\InputFlow.SingleInstance";

        [STAThread]
        private static void Main()
        {
            using var singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: SingleInstanceMutexName,
                createdNew: out bool createdNew);
            if (!createdNew)
            {
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string configDirectory = GetAppDataDirectory(Environment.SpecialFolder.ApplicationData, appDir);
            string logDirectory = GetAppDataDirectory(Environment.SpecialFolder.LocalApplicationData, appDir);
            Directory.CreateDirectory(configDirectory);
            Directory.CreateDirectory(logDirectory);

            string configPath = Path.Combine(configDirectory, "inputflow.json");
            string logPath = Path.Combine(logDirectory, "inputflow.log");
            string legacyConfigPath = Path.Combine(appDir, "inputflow.json");
            string? migratedLegacyConfigPath = TryMigrateLegacyConfig(configPath, legacyConfigPath);

            if (!File.Exists(configPath))
            {
                var defaultConfig = InputFlowConfigFactory.CreateFirstRunConfig(InputProfileManager.EnumerateInstalledProfiles());
                var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(defaultConfig, opts));
            }

            using var context = new TrayApplicationContext(configPath, logPath, migratedLegacyConfigPath);
            Application.Run(context);
        }

        private static string GetAppDataDirectory(Environment.SpecialFolder folder, string fallbackDirectory)
        {
            string root = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = fallbackDirectory;
            }

            return Path.Combine(root, "InputFlow");
        }

        private static string? TryMigrateLegacyConfig(string configPath, string legacyConfigPath)
        {
            if (File.Exists(configPath) || !File.Exists(legacyConfigPath))
            {
                return null;
            }

            File.Copy(legacyConfigPath, configPath, overwrite: false);
            return legacyConfigPath;
        }

        private sealed class TrayApplicationContext : ApplicationContext
        {
            private const int ConfigReloadDebounceMilliseconds = 500;

            private readonly string _configPath;
            private readonly string _logPath;
            private readonly string? _migratedLegacyConfigPath;
            private InputFlowConfig _config;
            private readonly ILogger _logger;
            private readonly InputFlowManager _manager;
            private readonly HotkeyWindow _hotkeyWindow;
            private readonly NotifyIcon _notifyIcon;
            private readonly Control _uiDispatcher;
            private readonly System.Windows.Forms.Timer _configReloadTimer;
            private readonly SingleKeyTriggerHook _singleKeyHook;
            private readonly Dictionary<int, (uint Modifiers, int Vk)> _registeredHotkeys = new();
            private IReadOnlyList<InputProfile> _installedProfiles;
            private int _nextHotkeyId = 1;
            private FileSystemWatcher? _configWatcher;
            private ToolStripMenuItem? _pauseMenuItem;
            private SetupStatusForm? _setupStatusForm;

            public TrayApplicationContext(string configPath, string logPath, string? migratedLegacyConfigPath)
            {
                _configPath = configPath;
                _logPath = logPath;
                _migratedLegacyConfigPath = migratedLegacyConfigPath;
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

                var initialLoad = InputFlowConfig.LoadDetailed(_configPath);
                _config = initialLoad.Success ? initialLoad.Config : new InputFlowConfig();
                _installedProfiles = InputProfileManager.EnumerateInstalledProfiles();
                _manager = new InputFlowManager(_installedProfiles, _config.ExcludedProcesses, _logger);
                _singleKeyHook = new SingleKeyTriggerHook(_logger, OnSingleKeyTriggerPressed);

                _hotkeyWindow = new HotkeyWindow();
                _hotkeyWindow.HotkeyPressed += id => _manager.OnHotkeyPressed(id);

                _notifyIcon = new NotifyIcon
                {
                    Text = "InputFlow",
                    Icon = System.Drawing.SystemIcons.Application,
                    Visible = _config.ShowTrayIcon
                };
                _notifyIcon.ContextMenuStrip = BuildContextMenu();

                LogStartupDiagnostics();
                if (!initialLoad.Success)
                {
                    LogConfigLoadErrors("startup", initialLoad);
                    _logger.Warning("Startup config was invalid; running with built-in defaults until a valid config is saved or reloaded.");
                }

                RegisterHotkeys();
                SetupConfigWatcher();
            }

            private ContextMenuStrip BuildContextMenu()
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add(new ToolStripMenuItem("Open Config", null, (_, _) => OpenPath(_configPath, "config file")));
                menu.Items.Add(new ToolStripMenuItem("Open Log", null, (_, _) => OpenPath(_logPath, "log file")));
                menu.Items.Add(new ToolStripMenuItem("Setup Status", null, (_, _) => OpenSetupStatus()));
                menu.Items.Add(new ToolStripMenuItem("Copy Diagnostics", null, (_, _) => CopyDiagnostics()));
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

            private void OpenSetupStatus()
            {
                try
                {
                    if (_setupStatusForm == null || _setupStatusForm.IsDisposed)
                    {
                        _setupStatusForm = new SetupStatusForm(
                            CopyDiagnostics,
                            () => OpenPath(_configPath, "config file"),
                            OpenAddWorkflow);
                        _setupStatusForm.FormClosed += (_, _) => _setupStatusForm = null;
                    }

                    _setupStatusForm.RefreshModel(InputFlowSetupModelBuilder.Build(_config, _installedProfiles));
                    _setupStatusForm.Show();
                    _setupStatusForm.Activate();
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Cannot open setup status window: {ex.Message}");
                }
            }

            private void OpenAddWorkflow()
            {
                var setup = InputFlowSetupModelBuilder.Build(_config, _installedProfiles);
                if (!setup.ConfiguredProfiles.Any(profile => profile.CanUseForSwitching))
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        "No matched configured profiles are available for switching.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                using var dialog = new WorkflowDialog(setup.ConfiguredProfiles);
                if (dialog.ShowDialog(_setupStatusForm) != DialogResult.OK)
                {
                    return;
                }

                SaveWorkflow(dialog.Draft);
            }

            private void SaveWorkflow(WorkflowDraft draft)
            {
                var updated = CloneConfig(_config);
                var workflow = new WorkflowConfig
                {
                    Id = CreateUniqueWorkflowId(draft.Name, updated.Workflows),
                    Name = draft.Name,
                    Mode = draft.Mode,
                    Triggers = new List<TriggerConfig> { new TriggerConfig { Keys = draft.Trigger } },
                    ReturnBehavior = draft.ReturnBehavior,
                    Fallback = draft.FallbackProfileId
                };
                if (draft.Mode.Equals("cycle", StringComparison.OrdinalIgnoreCase))
                {
                    workflow.Targets = draft.TargetProfileIds;
                }
                else
                {
                    workflow.Target = draft.TargetProfileId;
                }

                updated.Workflows.Add(workflow);

                var saveResult = InputFlowConfigWriter.SaveValidated(updated, _configPath);
                if (!saveResult.Success)
                {
                    string message = string.Join(Environment.NewLine, saveResult.Errors);
                    _logger.Warning($"Setup workflow save failed: {message}");
                    MessageBox.Show(
                        _setupStatusForm,
                        message,
                        "InputFlow could not save the workflow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _logger.Info($"Saved setup workflow '{draft.Name}' ({draft.Mode}).");
                ReloadConfig("setup status window");
                RefreshSetupStatusWindow();
            }

            private void CopyDiagnostics()
            {
                try
                {
                    Clipboard.SetText(InputFlowDiagnostics.BuildReport(_config, _installedProfiles, _configPath, _logPath));
                    _logger.Info("Copied diagnostics to clipboard.");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Cannot copy diagnostics to clipboard: {ex.Message}");
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

            private void ReloadConfig(string reason)
            {
                try
                {
                    _logger.Info($"Reloading configuration ({reason})...");
                    var loadResult = InputFlowConfig.LoadDetailed(_configPath);
                    if (!loadResult.Success)
                    {
                        LogConfigLoadErrors(reason, loadResult);
                        _logger.Warning("Configuration reload rejected; keeping the previous working configuration active.");
                        return;
                    }

                    var newConfig = loadResult.Config;
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
                    RefreshSetupStatusWindow();

                    _logger.Info("Configuration reloaded.");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to reload configuration: {ex.Message}");
                }
            }

            private void RefreshSetupStatusWindow()
            {
                if (_setupStatusForm == null || _setupStatusForm.IsDisposed)
                {
                    return;
                }

                _setupStatusForm.RefreshModel(InputFlowSetupModelBuilder.Build(_config, _installedProfiles));
            }

            private void LogConfigLoadErrors(string reason, InputFlowConfigLoadResult loadResult)
            {
                foreach (string error in loadResult.Errors)
                {
                    _logger.Error($"Config load error ({reason}): {error}");
                }
            }

            private static InputFlowConfig CloneConfig(InputFlowConfig config)
            {
                string json = JsonSerializer.Serialize(config);
                return JsonSerializer.Deserialize<InputFlowConfig>(json) ?? new InputFlowConfig();
            }

            private static string CreateUniqueWorkflowId(string name, IReadOnlyList<WorkflowConfig> existing)
            {
                string baseId = Slugify(name);
                if (string.IsNullOrWhiteSpace(baseId))
                {
                    baseId = "workflow";
                }

                var used = existing
                    .Where(workflow => workflow != null && !string.IsNullOrWhiteSpace(workflow.Id))
                    .Select(workflow => workflow.Id.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                string id = baseId;
                int suffix = 2;
                while (!used.Add(id))
                {
                    id = $"{baseId}-{suffix}";
                    suffix++;
                }

                return id;
            }

            private static string Slugify(string value)
            {
                var chars = value
                    .Trim()
                    .ToLowerInvariant()
                    .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                    .ToArray();
                string slug = new string(chars).Trim('-');
                while (slug.Contains("--", StringComparison.Ordinal))
                {
                    slug = slug.Replace("--", "-", StringComparison.Ordinal);
                }

                return slug;
            }

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

            private void RegisterHotkeys()
            {
                _nextHotkeyId = 1;
                var matched = InputProfileManager.MatchProfiles(_installedProfiles, _config.Profiles);
                foreach (var workflow in _config.Workflows)
                {
                    if (!TryResolveWorkflowTargets(workflow, matched, out var targetIds, out var targets))
                    {
                        continue;
                    }

                    InputProfile? fallback = null;
                    if (!string.IsNullOrWhiteSpace(workflow.Fallback) && !matched.TryGetValue(workflow.Fallback, out fallback))
                    {
                        _logger.Warning($"Workflow '{GetWorkflowDisplayName(workflow)}' references fallback profile '{workflow.Fallback}' that did not match an installed profile.");
                    }

                    var enterModesByKlid = BuildEnterModeMap(targetIds, targets, workflow.Fallback, fallback);

                    foreach (var trigger in workflow.Triggers)
                    {
                        if (trigger == null)
                        {
                            _logger.Warning($"Workflow '{GetWorkflowDisplayName(workflow)}' contains a null trigger. Skipping.");
                            continue;
                        }

                        var parsedTrigger = InputFlowTriggerParser.Parse(trigger.Keys);
                        if (!parsedTrigger.Success)
                        {
                            _logger.Warning($"Workflow '{GetWorkflowDisplayName(workflow)}' has invalid trigger '{trigger.Keys}'. Skipping.");
                            continue;
                        }

                        uint mods = parsedTrigger.Modifiers;
                        int vk = parsedTrigger.VirtualKey;
                        int id = _nextHotkeyId++;
                        if (parsedTrigger.IsSingleKeyTrigger)
                        {
                            if (!_singleKeyHook.Register(vk, id, trigger.Keys))
                            {
                                _logger.Warning($"Failed to register single-key trigger '{trigger.Keys}' (Workflow: {GetWorkflowDisplayName(workflow)}). It may already be registered.");
                                continue;
                            }

                            RegisterManagerWorkflow(id, workflow, targets, fallback, enterModesByKlid);
                            _logger.Info($"Registered single-key trigger '{trigger.Keys}' for workflow '{GetWorkflowDisplayName(workflow)}' as ID {id}.");
                            continue;
                        }

                        bool ok = RegisterHotKey(_hotkeyWindow.Handle, id, mods, vk);
                        if (!ok)
                        {
                            _logger.Warning($"Failed to register trigger '{trigger.Keys}' (Workflow: {GetWorkflowDisplayName(workflow)}). It may be in use.");
                            continue;
                        }

                        _registeredHotkeys[id] = (mods, vk);
                        RegisterManagerWorkflow(id, workflow, targets, fallback, enterModesByKlid);
                        _logger.Info($"Registered trigger '{trigger.Keys}' for workflow '{GetWorkflowDisplayName(workflow)}' as ID {id}.");
                    }
                }
            }

            private bool TryResolveWorkflowTargets(
                WorkflowConfig workflow,
                Dictionary<string, InputProfile> matched,
                out List<string> targetIds,
                out List<InputProfile> targets)
            {
                targetIds = GetWorkflowTargetIds(workflow).ToList();
                targets = new List<InputProfile>();

                if (targetIds.Count == 0)
                {
                    _logger.Warning($"Workflow '{GetWorkflowDisplayName(workflow)}' does not define any target profiles. Skipping.");
                    return false;
                }

                foreach (string targetId in targetIds)
                {
                    if (!matched.TryGetValue(targetId, out var target))
                    {
                        _logger.Warning($"Workflow '{GetWorkflowDisplayName(workflow)}' references target profile '{targetId}' that did not match an installed profile. Skipping.");
                        return false;
                    }

                    targets.Add(target);
                }

                return true;
            }

            private static IReadOnlyList<string> GetWorkflowTargetIds(WorkflowConfig workflow)
            {
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

            private Dictionary<string, string?> BuildEnterModeMap(
                IReadOnlyList<string> targetIds,
                IReadOnlyList<InputProfile> targets,
                string? fallbackId,
                InputProfile? fallback)
            {
                var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < targetIds.Count && i < targets.Count; i++)
                {
                    result[targets[i].KLID] = GetEnterModeForProfileId(targetIds[i]);
                }

                if (fallback != null && !string.IsNullOrWhiteSpace(fallbackId) && !result.ContainsKey(fallback.KLID))
                {
                    result[fallback.KLID] = GetEnterModeForProfileId(fallbackId);
                }

                return result;
            }

            private string? GetEnterModeForProfileId(string profileId)
            {
                var definition = _config.Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
                return definition?.EnterMode;
            }

            private void RegisterManagerWorkflow(
                int id,
                WorkflowConfig workflow,
                IReadOnlyList<InputProfile> targets,
                InputProfile? fallback,
                IReadOnlyDictionary<string, string?> enterModesByKlid)
            {
                _manager.RegisterWorkflow(
                    id,
                    workflow.Mode,
                    targets,
                    fallback,
                    workflow.ReturnBehavior ?? "lastNonTarget",
                    enterModesByKlid);
            }

            private static string GetWorkflowDisplayName(WorkflowConfig workflow)
            {
                if (!string.IsNullOrWhiteSpace(workflow.Name))
                {
                    return workflow.Name;
                }

                return string.IsNullOrWhiteSpace(workflow.Id) ? "unnamed workflow" : workflow.Id;
            }

            private void OnSingleKeyTriggerPressed(int id)
            {
                if (_uiDispatcher.IsDisposed || !_uiDispatcher.IsHandleCreated)
                {
                    return;
                }

                try
                {
                    _uiDispatcher.BeginInvoke((Action)(() => _manager.OnHotkeyPressed(id)));
                }
                catch (InvalidOperationException)
                {
                    // The app is probably exiting. Ignore late hook callbacks.
                }
            }

            private void UnregisterAllHotkeys()
            {
                foreach (var kvp in _registeredHotkeys)
                {
                    UnregisterHotKey(_hotkeyWindow.Handle, kvp.Key);
                }
                _registeredHotkeys.Clear();
                _singleKeyHook.Clear();
            }

            private void LogStartupDiagnostics()
            {
                _logger.Info("InputFlow starting.");
                _logger.Info($"Config path: {_configPath}");
                _logger.Info($"Log path: {_logPath}");
                if (!string.IsNullOrWhiteSpace(_migratedLegacyConfigPath))
                {
                    _logger.Info($"Migrated legacy config from: {_migratedLegacyConfigPath}");
                }
                _logger.Info($"Tray icon visible: {_config.ShowTrayIcon}");
                _logger.Info($"Configured workflows: {_config.Workflows.Count}");
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
                    _logger.Info($"Installed profile: {InputProfileManager.FormatProfile(profile)}");
                }
            }

            private void LogProfileMatches(IReadOnlyList<InputProfile> installed, IEnumerable<ProfileDefinition> definitions)
            {
                var reports = InputProfileManager.EvaluateProfileMatches(installed, definitions);
                foreach (var report in reports)
                {
                    if (report.MatchedProfile != null)
                    {
                        _logger.Info($"Configured profile '{report.ProfileId}' health={report.Health} matched {InputProfileManager.FormatProfile(report.MatchedProfile)} using {InputFlowDiagnostics.FormatMatchCriteria(report.Criteria)}. {report.Summary}");
                    }
                    else
                    {
                        _logger.Warning($"Configured profile '{report.ProfileId}' health={report.Health} did not match any installed profile using {InputFlowDiagnostics.FormatMatchCriteria(report.Criteria)}. {report.Summary}");
                    }

                    foreach (var candidate in report.Candidates)
                    {
                        _logger.Info($"Profile match candidate for '{report.ProfileId}': {(candidate.IsMatch ? "match" : "no match")} {InputProfileManager.FormatProfile(candidate.Profile)} - {candidate.Reason}");
                    }
                }
            }

            private new void ExitThread()
            {
                UnregisterAllHotkeys();
                _configReloadTimer.Stop();
                if (_configWatcher != null)
                {
                    _configWatcher.EnableRaisingEvents = false;
                    _configWatcher.Dispose();
                    _configWatcher = null;
                }
                _singleKeyHook.Dispose();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _setupStatusForm?.Dispose();
                _hotkeyWindow.Dispose();
                _uiDispatcher.Dispose();
                _logger.Info("Exiting InputFlow");
                Application.Exit();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    UnregisterAllHotkeys();
                    _configReloadTimer.Dispose();
                    _configWatcher?.Dispose();
                    _singleKeyHook.Dispose();
                    _notifyIcon.Dispose();
                    _setupStatusForm?.Dispose();
                    _hotkeyWindow.Dispose();
                    _uiDispatcher.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private class HotkeyWindow : NativeWindow, IDisposable
        {
            public event Action<int>? HotkeyPressed;

            private const int WM_HOTKEY = 0x0312;

            public HotkeyWindow()
            {
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

        private sealed class SingleKeyTriggerHook : IDisposable
        {
            private const int VkHangul = 0x15;

            private readonly ILogger _logger;
            private readonly Action<int> _triggerPressed;
            private readonly InputApis.LowLevelKeyboardProc _hookProc;
            private readonly Dictionary<int, int> _hotkeysByVk = new();
            private readonly HashSet<int> _pressedKeys = new();
            private IntPtr _hookHandle;
            private bool _disposed;

            public SingleKeyTriggerHook(ILogger logger, Action<int> triggerPressed)
            {
                _logger = logger;
                _triggerPressed = triggerPressed;
                _hookProc = HookCallback;
            }

            public bool Register(int vk, int id, string displayName)
            {
                if (_disposed)
                {
                    return false;
                }

                if (!_hotkeysByVk.TryAdd(vk, id))
                {
                    return false;
                }

                bool addedHangulAlias = false;
                if (vk == (int)Keys.RMenu)
                {
                    addedHangulAlias = _hotkeysByVk.TryAdd(VkHangul, id);
                }

                if (!EnsureInstalled())
                {
                    _hotkeysByVk.Remove(vk);
                    if (addedHangulAlias)
                    {
                        _hotkeysByVk.Remove(VkHangul);
                    }
                    return false;
                }

                _logger.Warning($"Single-key trigger '{displayName}' is active and will suppress that key while InputFlow is running.");
                if (addedHangulAlias)
                {
                    _logger.Info($"Single-key trigger '{displayName}' also listens for VK_HANGUL while Korean IME is active.");
                }
                return true;
            }

            public void Clear()
            {
                _hotkeysByVk.Clear();
                _pressedKeys.Clear();
            }

            private bool EnsureInstalled()
            {
                if (_hookHandle != IntPtr.Zero)
                {
                    return true;
                }

                using var process = Process.GetCurrentProcess();
                using ProcessModule? module = process.MainModule;
                IntPtr moduleHandle = module != null ? InputApis.GetModuleHandle(module.ModuleName) : IntPtr.Zero;
                _hookHandle = InputApis.SetWindowsHookEx(InputApis.WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);

                if (_hookHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    _logger.Warning($"Failed to install single-key trigger hook. Win32 error={error}.");
                    return false;
                }

                _logger.Info("Installed single-key trigger hook.");
                return true;
            }

            private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode < 0)
                {
                    return InputApis.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                var data = Marshal.PtrToStructure<InputApis.KBDLLHOOKSTRUCT>(lParam);
                int vk = data.vkCode;
                if (!_hotkeysByVk.TryGetValue(vk, out int id))
                {
                    return InputApis.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                int message = wParam.ToInt32();
                int pressedKey = GetPressedKeyBucket(vk);
                if (message == InputApis.WM_KEYDOWN || message == InputApis.WM_SYSKEYDOWN)
                {
                    _pressedKeys.Add(pressedKey);
                    return (IntPtr)1;
                }

                if (message == InputApis.WM_KEYUP || message == InputApis.WM_SYSKEYUP)
                {
                    if (_pressedKeys.Remove(pressedKey))
                    {
                        _triggerPressed(id);
                    }
                    return (IntPtr)1;
                }

                return InputApis.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            private int GetPressedKeyBucket(int vk)
            {
                if (vk == VkHangul &&
                    _hotkeysByVk.TryGetValue(VkHangul, out int hangulId) &&
                    _hotkeysByVk.TryGetValue((int)Keys.RMenu, out int rightAltId) &&
                    hangulId == rightAltId)
                {
                    return (int)Keys.RMenu;
                }

                return vk;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Clear();
                if (_hookHandle != IntPtr.Zero)
                {
                    InputApis.UnhookWindowsHookEx(_hookHandle);
                    _hookHandle = IntPtr.Zero;
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
