using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;
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
            bool createdFirstRunConfig = false;

            if (!File.Exists(configPath))
            {
                var defaultConfig = InputFlowConfigFactory.CreateFirstRunConfig(InputProfileManager.EnumerateInstalledProfiles());
                var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(defaultConfig, opts));
                createdFirstRunConfig = true;
            }

            using var context = new TrayApplicationContext(configPath, logPath, migratedLegacyConfigPath, createdFirstRunConfig);
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
            private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            private const string StartupRegistryValueName = "InputFlow";

            private readonly string _configPath;
            private readonly string _logPath;
            private readonly string? _migratedLegacyConfigPath;
            private readonly bool _createdFirstRunConfig;
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
            private ToolStripMenuItem? _startWithWindowsMenuItem;
            private SetupStatusForm? _setupStatusForm;

            public TrayApplicationContext(string configPath, string logPath, string? migratedLegacyConfigPath, bool createdFirstRunConfig)
            {
                _configPath = configPath;
                _logPath = logPath;
                _migratedLegacyConfigPath = migratedLegacyConfigPath;
                _createdFirstRunConfig = createdFirstRunConfig;
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
                LogConfigLoadWarnings("startup", initialLoad);
                if (!initialLoad.Success)
                {
                    LogConfigLoadErrors("startup", initialLoad);
                    _logger.Warning("Startup config was invalid; running with built-in defaults until a valid config is saved or reloaded.");
                }

                RegisterHotkeys();
                SetupConfigWatcher();
                OpenSetupStatusOnFirstRun();
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
                menu.Items.Add(new ToolStripMenuItem("Restore Last Good Config", null, (_, _) => RestoreLastGoodConfig()));
                menu.Items.Add(new ToolStripMenuItem("Reset Setup", null, (_, _) => ResetSetupConfig()));
                _startWithWindowsMenuItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartWithWindows());
                menu.Items.Add(_startWithWindowsMenuItem);
                _pauseMenuItem = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());
                menu.Items.Add(_pauseMenuItem);
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));
                UpdateStartWithWindowsMenuText();
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

            private void ResetSetupConfig()
            {
                string backupPath = CreateConfigBackupPath();
                var result = MessageBox.Show(
                    _setupStatusForm,
                    $"Reset InputFlow to a fresh starter setup?{Environment.NewLine}{Environment.NewLine}Your current config will be backed up to:{Environment.NewLine}{backupPath}",
                    "InputFlow reset setup",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    if (File.Exists(_configPath))
                    {
                        File.Copy(_configPath, backupPath, overwrite: false);
                    }

                    var starterConfig = InputFlowConfigFactory.CreateFirstRunConfig(InputProfileManager.EnumerateInstalledProfiles());
                    var saveResult = InputFlowConfigWriter.SaveValidated(starterConfig, _configPath);
                    if (!saveResult.Success)
                    {
                        string message = string.Join(Environment.NewLine, saveResult.Errors);
                        _logger.Warning($"Setup reset failed: {message}");
                        MessageBox.Show(
                            _setupStatusForm,
                            message,
                            "InputFlow could not reset setup",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    _logger.Info(File.Exists(backupPath)
                        ? $"Reset setup config. Backup saved to {backupPath}."
                        : "Reset setup config. No existing config was present to back up.");
                    ReloadConfig("setup reset");
                    OpenSetupStatus();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _logger.Warning($"Setup reset failed: {ex.Message}");
                    MessageBox.Show(
                        _setupStatusForm,
                        ex.Message,
                        "InputFlow could not reset setup",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            private void RestoreLastGoodConfig()
            {
                string lastKnownGoodPath = InputFlowConfigWriter.GetLastKnownGoodPath(_configPath);
                if (!File.Exists(lastKnownGoodPath))
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        "No last-known-good config has been saved yet.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                string backupPath = CreateConfigBackupPath();
                var result = MessageBox.Show(
                    _setupStatusForm,
                    $"Restore the last-known-good InputFlow config?{Environment.NewLine}{Environment.NewLine}Your current config will be backed up to:{Environment.NewLine}{backupPath}",
                    "InputFlow restore config",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    if (File.Exists(_configPath))
                    {
                        File.Copy(_configPath, backupPath, overwrite: false);
                    }

                    File.Copy(lastKnownGoodPath, _configPath, overwrite: true);
                    _logger.Info(File.Exists(backupPath)
                        ? $"Restored last-known-good config from {lastKnownGoodPath}. Backup saved to {backupPath}."
                        : $"Restored last-known-good config from {lastKnownGoodPath}. No existing config was present to back up.");
                    ReloadConfig("last-known-good restore");
                    OpenSetupStatus();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _logger.Warning($"Last-known-good restore failed: {ex.Message}");
                    MessageBox.Show(
                        _setupStatusForm,
                        ex.Message,
                        "InputFlow could not restore the config",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            private string CreateConfigBackupPath()
            {
                string directory = Path.GetDirectoryName(_configPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                string fileName = Path.GetFileName(_configPath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string path = Path.Combine(directory, $"{fileName}.{timestamp}.bak");
                int suffix = 2;
                while (File.Exists(path))
                {
                    path = Path.Combine(directory, $"{fileName}.{timestamp}-{suffix}.bak");
                    suffix++;
                }

                return path;
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
                            OpenAddProfile,
                            OpenAddProfile,
                            EditProfile,
                            RemoveProfile,
                            OpenAddWorkflow,
                            EditWorkflow,
                            RemoveWorkflow,
                            OpenAddExcludedProcess,
                            RemoveExcludedProcess);
                        _setupStatusForm.FormClosed += (_, _) => _setupStatusForm = null;
                    }

                    _setupStatusForm.RefreshModel(InputFlowSetupModelBuilder.Build(_config, _installedProfiles), GetConfigRecoveryStatusText());
                    _setupStatusForm.Show();
                    _setupStatusForm.Activate();
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Cannot open setup status window: {ex.Message}");
                }
            }

            private void OpenSetupStatusOnFirstRun()
            {
                if (!_createdFirstRunConfig || _config.Workflows.Count > 0)
                {
                    return;
                }

                try
                {
                    _uiDispatcher.BeginInvoke((Action)(() =>
                    {
                        _logger.Info("Opening Setup Status for first-run configuration.");
                        OpenSetupStatus();
                    }));
                }
                catch (InvalidOperationException)
                {
                    _logger.Warning("Could not open Setup Status for first-run configuration because the UI dispatcher is not available.");
                }
            }

            private void OpenAddProfile()
            {
                OpenAddProfile(null);
            }

            private void OpenAddProfile(InputProfile? initialProfile)
            {
                var setup = InputFlowSetupModelBuilder.Build(_config, _installedProfiles);
                if (setup.InstalledProfiles.Count == 0)
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        "No Windows input profiles were found.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var initialDraft = initialProfile == null
                    ? null
                    : new ProfileDraft { InstalledProfile = initialProfile };
                using var dialog = new ProfileDialog(setup.InstalledProfiles, initialDraft);
                if (dialog.ShowDialog(_setupStatusForm) != DialogResult.OK)
                {
                    return;
                }

                AddProfile(dialog.Draft);
            }

            private void EditProfile(string profileId)
            {
                var definition = _config.Profiles.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
                if (definition == null)
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        $"Profile '{profileId}' was not found.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var setup = InputFlowSetupModelBuilder.Build(_config, _installedProfiles);
                var configured = setup.ConfiguredProfiles.FirstOrDefault(profile => string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
                using var dialog = new ProfileDialog(
                    setup.InstalledProfiles,
                    new ProfileDraft
                    {
                        ProfileId = definition.Id,
                        InstalledProfile = configured?.MatchedProfile,
                        EnterMode = definition.EnterMode
                    },
                    allowIdEdit: false);
                if (dialog.ShowDialog(_setupStatusForm) != DialogResult.OK)
                {
                    return;
                }

                SaveProfile(dialog.Draft, profileId);
            }

            private void AddProfile(ProfileDraft draft)
            {
                var updated = CloneConfig(_config);
                updated.Profiles.Add(CreateProfileDefinition(draft, draft.ProfileId));

                SaveProfileConfig(updated, $"Saved setup profile '{draft.ProfileId}'.", "save");
            }

            private void SaveProfile(ProfileDraft draft, string profileId)
            {
                var updated = CloneConfig(_config);
                int index = updated.Profiles.FindIndex(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        $"Profile '{profileId}' was not found.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                updated.Profiles[index] = CreateProfileDefinition(draft, profileId);

                SaveProfileConfig(updated, $"Edited setup profile '{profileId}'.", "edit");
            }

            private static ProfileDefinition CreateProfileDefinition(ProfileDraft draft, string profileId)
            {
                return new ProfileDefinition
                {
                    Id = profileId,
                    Match = new ProfileMatch
                    {
                        LanguageTag = GetValidLanguageTag(draft.InstalledProfile),
                        KLID = draft.InstalledProfile?.KLID
                    },
                    EnterMode = draft.EnterMode
                };
            }

            private void SaveProfileConfig(InputFlowConfig updated, string successMessage, string operation)
            {
                var saveResult = InputFlowConfigWriter.SaveValidated(updated, _configPath);
                if (!saveResult.Success)
                {
                    string message = string.Join(Environment.NewLine, saveResult.Errors);
                    _logger.Warning($"Setup profile {operation} failed: {message}");
                    MessageBox.Show(
                        _setupStatusForm,
                        message,
                        $"InputFlow could not {operation} the profile",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _logger.Info(successMessage);
                ReloadConfig("setup status window");
                RefreshSetupStatusWindow();
            }

            private void RemoveProfile(string profileId)
            {
                var references = FindProfileReferences(profileId, _config.Workflows);
                if (references.Count > 0)
                {
                    string message = $"Profile '{profileId}' is still used by:{Environment.NewLine}{string.Join(Environment.NewLine, references.Select(reference => "- " + reference))}{Environment.NewLine}{Environment.NewLine}Remove or edit those workflows first.";
                    MessageBox.Show(
                        _setupStatusForm,
                        message,
                        "InputFlow could not remove the profile",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var updated = CloneConfig(_config);
                int removed = updated.Profiles.RemoveAll(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        $"Profile '{profileId}' was not found.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                SaveProfileConfig(updated, $"Removed setup profile '{profileId}'.", "remove");
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

            private void EditWorkflow(string workflowId)
            {
                var workflow = _config.Workflows.FirstOrDefault(candidate => string.Equals(candidate.Id, workflowId, StringComparison.OrdinalIgnoreCase));
                if (workflow == null)
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        $"Workflow '{workflowId}' was not found.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var setup = InputFlowSetupModelBuilder.Build(_config, _installedProfiles);
                using var dialog = new WorkflowDialog(setup.ConfiguredProfiles, CreateWorkflowDraft(workflow));
                if (dialog.ShowDialog(_setupStatusForm) != DialogResult.OK)
                {
                    return;
                }

                SaveWorkflow(dialog.Draft, workflowId);
            }

            private void SaveWorkflow(WorkflowDraft draft)
            {
                var updated = CloneConfig(_config);
                updated.Workflows.Add(CreateWorkflowConfig(draft, CreateUniqueWorkflowId(draft.Name, updated.Workflows)));

                SaveWorkflowConfig(updated, $"Saved setup workflow '{draft.Name}' ({draft.Mode}).", "save");
            }

            private void SaveWorkflow(WorkflowDraft draft, string workflowId)
            {
                var updated = CloneConfig(_config);
                int index = updated.Workflows.FindIndex(workflow => string.Equals(workflow.Id, workflowId, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        $"Workflow '{workflowId}' was not found.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                updated.Workflows[index] = CreateWorkflowConfig(draft, workflowId);

                SaveWorkflowConfig(updated, $"Edited setup workflow '{workflowId}' ({draft.Mode}).", "edit");
            }

            private static WorkflowConfig CreateWorkflowConfig(WorkflowDraft draft, string workflowId)
            {
                var workflow = new WorkflowConfig
                {
                    Id = workflowId,
                    Name = draft.Name,
                    Mode = draft.Mode,
                    Triggers = draft.Triggers
                        .Where(trigger => !string.IsNullOrWhiteSpace(trigger))
                        .Select(trigger => new TriggerConfig { Keys = trigger.Trim() })
                        .ToList(),
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

                return workflow;
            }

            private void SaveWorkflowConfig(InputFlowConfig updated, string successMessage, string operation)
            {
                var saveResult = InputFlowConfigWriter.SaveValidated(updated, _configPath);
                if (!saveResult.Success)
                {
                    string message = string.Join(Environment.NewLine, saveResult.Errors);
                    _logger.Warning($"Setup workflow {operation} failed: {message}");
                    MessageBox.Show(
                        _setupStatusForm,
                        message,
                        $"InputFlow could not {operation} the workflow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _logger.Info(successMessage);
                ReloadConfig("setup status window");
                RefreshSetupStatusWindow();
            }

            private static WorkflowDraft CreateWorkflowDraft(WorkflowConfig workflow)
            {
                string mode = string.IsNullOrWhiteSpace(workflow.Mode) ? "toggle" : workflow.Mode.Trim();
                return new WorkflowDraft
                {
                    Name = string.IsNullOrWhiteSpace(workflow.Name) ? workflow.Id : workflow.Name,
                    Mode = mode,
                    Triggers = workflow.Triggers
                        .Where(trigger => !string.IsNullOrWhiteSpace(trigger.Keys))
                        .Select(trigger => trigger.Keys.Trim())
                        .ToList(),
                    TargetProfileId = workflow.Target,
                    TargetProfileIds = workflow.Targets
                        .Where(target => !string.IsNullOrWhiteSpace(target))
                        .Select(target => target.Trim())
                        .ToList(),
                    FallbackProfileId = workflow.Fallback,
                    ReturnBehavior = string.IsNullOrWhiteSpace(workflow.ReturnBehavior) ? "lastNonTarget" : workflow.ReturnBehavior
                };
            }

            private void RemoveWorkflow(string workflowId)
            {
                var updated = CloneConfig(_config);
                int removed = updated.Workflows.RemoveAll(workflow => string.Equals(workflow.Id, workflowId, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        $"Workflow '{workflowId}' was not found.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var saveResult = InputFlowConfigWriter.SaveValidated(updated, _configPath);
                if (!saveResult.Success)
                {
                    string message = string.Join(Environment.NewLine, saveResult.Errors);
                    _logger.Warning($"Setup workflow remove failed: {message}");
                    MessageBox.Show(
                        _setupStatusForm,
                        message,
                        "InputFlow could not remove the workflow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _logger.Info($"Removed setup workflow '{workflowId}'.");
                ReloadConfig("setup status window");
                RefreshSetupStatusWindow();
            }

            private void OpenAddExcludedProcess()
            {
                using var dialog = new ExcludedProcessDialog();
                if (dialog.ShowDialog(_setupStatusForm) != DialogResult.OK)
                {
                    return;
                }

                AddExcludedProcess(dialog.ProcessName);
            }

            private void AddExcludedProcess(string processName)
            {
                var updated = CloneConfig(_config);
                string normalized = NormalizeProcessName(processName);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        "Process name is required.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (updated.ExcludedProcesses.Any(process => string.Equals(process.Trim(), normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        $"'{normalized}' is already excluded.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                updated.ExcludedProcesses.Add(normalized);
                SaveExcludedProcessesConfig(updated, $"Added excluded process '{normalized}'.", "add");
            }

            private void RemoveExcludedProcess(string processName)
            {
                var updated = CloneConfig(_config);
                string normalized = NormalizeProcessName(processName);
                int removed = updated.ExcludedProcesses.RemoveAll(process => string.Equals(process.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    MessageBox.Show(
                        _setupStatusForm,
                        $"Excluded process '{normalized}' was not found.",
                        "InputFlow",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                SaveExcludedProcessesConfig(updated, $"Removed excluded process '{normalized}'.", "remove");
            }

            private void SaveExcludedProcessesConfig(InputFlowConfig updated, string successMessage, string operation)
            {
                updated.ExcludedProcesses = updated.ExcludedProcesses
                    .Where(process => !string.IsNullOrWhiteSpace(process))
                    .Select(process => NormalizeProcessName(process))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(process => process, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var saveResult = InputFlowConfigWriter.SaveValidated(updated, _configPath);
                if (!saveResult.Success)
                {
                    string message = string.Join(Environment.NewLine, saveResult.Errors);
                    _logger.Warning($"Setup excluded process {operation} failed: {message}");
                    MessageBox.Show(
                        _setupStatusForm,
                        message,
                        $"InputFlow could not {operation} the excluded process",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _logger.Info(successMessage);
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

            private void ToggleStartWithWindows()
            {
                try
                {
                    bool enable = !IsStartWithWindowsEnabled();
                    using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(StartupRegistryPath, writable: true);
                    if (key == null)
                    {
                        _logger.Warning("Cannot update Start with Windows: registry key could not be opened.");
                        return;
                    }

                    if (enable)
                    {
                        key.SetValue(StartupRegistryValueName, GetStartupCommand(), RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(StartupRegistryValueName, throwOnMissingValue: false);
                    }

                    UpdateStartWithWindowsMenuText();
                    _logger.Info(enable ? "Enabled Start with Windows via tray." : "Disabled Start with Windows via tray.");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Cannot update Start with Windows: {ex.Message}");
                    MessageBox.Show(
                        _setupStatusForm,
                        ex.Message,
                        "InputFlow could not update Start with Windows",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            private void UpdateStartWithWindowsMenuText()
            {
                if (_startWithWindowsMenuItem == null)
                {
                    return;
                }

                try
                {
                    _startWithWindowsMenuItem.Checked = IsStartWithWindowsEnabled();
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Cannot read Start with Windows state: {ex.Message}");
                    _startWithWindowsMenuItem.Checked = false;
                }
            }

            private static bool IsStartWithWindowsEnabled()
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, writable: false);
                return key?.GetValue(StartupRegistryValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }

            private static string GetStartupCommand()
            {
                return $"\"{Application.ExecutablePath}\"";
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
                    LogConfigLoadWarnings(reason, loadResult);
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

                _setupStatusForm.RefreshModel(InputFlowSetupModelBuilder.Build(_config, _installedProfiles), GetConfigRecoveryStatusText());
            }

            private string GetConfigRecoveryStatusText()
            {
                string lastKnownGoodPath = InputFlowConfigWriter.GetLastKnownGoodPath(_configPath);
                return File.Exists(lastKnownGoodPath)
                    ? $"Recovery: last-good config available at {lastKnownGoodPath}"
                    : $"Recovery: no last-good config saved yet ({lastKnownGoodPath})";
            }

            private void LogConfigLoadErrors(string reason, InputFlowConfigLoadResult loadResult)
            {
                foreach (string error in loadResult.Errors)
                {
                    _logger.Error($"Config load error ({reason}): {error}");
                }
            }

            private void LogConfigLoadWarnings(string reason, InputFlowConfigLoadResult loadResult)
            {
                foreach (string warning in loadResult.Warnings)
                {
                    _logger.Warning($"Config load warning ({reason}): {warning}");
                }
            }

            private static InputFlowConfig CloneConfig(InputFlowConfig config)
            {
                string json = JsonSerializer.Serialize(config);
                return JsonSerializer.Deserialize<InputFlowConfig>(json) ?? new InputFlowConfig();
            }

            private static string NormalizeProcessName(string processName)
            {
                string process = (processName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(process))
                {
                    return string.Empty;
                }

                process = Path.GetFileName(process);
                if (string.IsNullOrWhiteSpace(process))
                {
                    return string.Empty;
                }

                if (!process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    process += ".exe";
                }

                return process;
            }

            private static IReadOnlyList<string> FindProfileReferences(string profileId, IEnumerable<WorkflowConfig> workflows)
            {
                var references = new List<string>();
                foreach (var workflow in workflows)
                {
                    string workflowName = GetWorkflowDisplayName(workflow);
                    if (GetWorkflowTargetIds(workflow).Any(target => string.Equals(target, profileId, StringComparison.OrdinalIgnoreCase)))
                    {
                        references.Add($"{workflowName} target");
                    }

                    if (!string.IsNullOrWhiteSpace(workflow.Fallback) &&
                        string.Equals(workflow.Fallback.Trim(), profileId, StringComparison.OrdinalIgnoreCase))
                    {
                        references.Add($"{workflowName} fallback");
                    }
                }

                return references;
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

            private static string? GetValidLanguageTag(InputProfile? profile)
            {
                string? languageTag = profile?.LanguageTag;
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

                    var enterModesByKlid = IsPreviousWorkflow(workflow)
                        ? BuildAllMatchedEnterModeMap(matched)
                        : BuildEnterModeMap(targetIds, targets, workflow.Fallback, fallback);

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

                if (IsPreviousWorkflow(workflow))
                {
                    return true;
                }

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

            private Dictionary<string, string?> BuildAllMatchedEnterModeMap(IReadOnlyDictionary<string, InputProfile> matched)
            {
                var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in matched)
                {
                    if (!result.ContainsKey(pair.Value.KLID))
                    {
                        result[pair.Value.KLID] = GetEnterModeForProfileId(pair.Key);
                    }
                }

                return result;
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
