using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using InputFlow.Windows;

namespace InputFlow.Core
{
    /// <summary>
    /// Coordinates the toggling logic, profile selection, verification, and exclusion
    /// handling for InputFlow. This class encapsulates the state machine described in
    /// the design document. It supports multiple hotkeys, each of which can have its
    /// own target profile, return behaviour and fallback profile.
    /// </summary>
    public class InputFlowManager
    {
        private IReadOnlyList<InputProfile> _installedProfiles;
        private readonly ILogger _logger;
        private readonly Dictionary<int, HotkeyState> _hotkeyStates = new();
        private HashSet<string> _excludedProcessNames;

        /// <summary>
        /// Indicates whether InputFlow is currently paused. When paused, toggle
        /// requests are ignored.
        /// </summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// Creates a new <see cref="InputFlowManager"/>.
        /// </summary>
        /// <param name="installedProfiles">Installed input profiles as enumerated by <see cref="InputProfileManager"/>.</param>
        /// <param name="excludedProcesses">Process names in which toggling should be disabled.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        public InputFlowManager(IReadOnlyList<InputProfile> installedProfiles, IEnumerable<string> excludedProcesses, ILogger logger)
        {
            _logger = logger;
            _installedProfiles = Array.Empty<InputProfile>();
            _excludedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            UpdateRuntimeState(installedProfiles, excludedProcesses);
        }

        /// <summary>
        /// Updates runtime state that can change after a config reload.
        /// </summary>
        public void UpdateRuntimeState(IReadOnlyList<InputProfile> installedProfiles, IEnumerable<string> excludedProcesses)
        {
            _installedProfiles = installedProfiles ?? Array.Empty<InputProfile>();
            _excludedProcessNames = new HashSet<string>(excludedProcesses ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Clears all registered hotkey state before a config reload registers the new set.
        /// </summary>
        public void ClearHotkeys()
        {
            _hotkeyStates.Clear();
        }

        /// <summary>
        /// Registers a legacy toggle hotkey with the manager.
        /// </summary>
        public void RegisterHotkey(int id, InputProfile target, InputProfile? fallback, string returnBehavior, string? enterMode = null)
        {
            RegisterWorkflow(
                id,
                "toggle",
                new[] { target },
                fallback,
                returnBehavior,
                CreateEnterModeMap(new[] { target }, enterMode));
        }

        /// <summary>
        /// Registers a workflow with one or more target profiles.
        /// </summary>
        public void RegisterWorkflow(
            int id,
            string mode,
            IReadOnlyList<InputProfile> targets,
            InputProfile? fallback,
            string returnBehavior,
            IReadOnlyDictionary<string, string?> enterModesByKlid)
        {
            if (targets == null || targets.Count == 0)
            {
                _logger.Error($"Cannot register workflow hotkey ID {id}: no target profiles were provided.");
                return;
            }

            _hotkeyStates[id] = new HotkeyState
            {
                Mode = ParseWorkflowMode(mode),
                Targets = targets.ToList(),
                Fallback = fallback,
                ReturnBehavior = ParseReturnBehavior(returnBehavior),
                EnterModesByKlid = CopyEnterModeMap(enterModesByKlid)
            };
        }

        /// <summary>
        /// Called when a registered hotkey is pressed. Determines whether to switch
        /// into or out of the target profile based on the current active profile.
        /// </summary>
        /// <param name="hotkeyId">Identifier of the pressed hotkey.</param>
        public void OnHotkeyPressed(int hotkeyId)
        {
            if (IsPaused)
            {
                _logger.Info("Toggle ignored because InputFlow is paused.");
                return;
            }

            string foregroundProcess = GetForegroundProcessName();
            if (!string.IsNullOrEmpty(foregroundProcess) && _excludedProcessNames.Contains(foregroundProcess))
            {
                _logger.Info($"Toggle ignored in excluded process: {foregroundProcess}");
                return;
            }

            if (!_hotkeyStates.TryGetValue(hotkeyId, out var state))
            {
                _logger.Error($"No hotkey state found for ID {hotkeyId}");
                return;
            }

            InputProfile current = GetCurrentProfile();

            switch (state.Mode)
            {
                case WorkflowMode.SwitchTo:
                    SwitchToFirstTarget(state, "Switched to target");
                    break;
                case WorkflowMode.Cycle:
                    CycleToNextTarget(state, current);
                    break;
                case WorkflowMode.Toggle:
                default:
                    ToggleTarget(state, current);
                    break;
            }
        }

        /// <summary>
        /// Sets the paused state. When paused, hotkey presses are ignored except for
        /// a resume/disabled override. Logging is produced to indicate the
        /// transition.
        /// </summary>
        /// <param name="paused">True to pause; false to resume.</param>
        public void SetPaused(bool paused)
        {
            if (IsPaused != paused)
            {
                IsPaused = paused;
                _logger.Info(paused ? "InputFlow paused." : "InputFlow resumed.");
            }
        }

        private void ToggleTarget(HotkeyState state, InputProfile current)
        {
            InputProfile target = state.Targets[0];

            if (ProfilesEqual(current, target))
            {
                InputProfile? dest = null;
                switch (state.ReturnBehavior)
                {
                    case ReturnBehavior.LastNonTarget:
                        dest = state.PreviousNonTarget ?? state.Fallback;
                        break;
                    case ReturnBehavior.AlwaysSpecificLayout:
                        dest = state.Fallback;
                        if (dest == null && state.PreviousNonTarget != null)
                        {
                            dest = state.PreviousNonTarget;
                            _logger.Warning("AlwaysSpecificLayout fallback was unavailable; returning to the remembered previous non-target profile.");
                        }
                        break;
                    case ReturnBehavior.ManualOnly:
                        _logger.Info("ManualOnly return behaviour: not switching back.");
                        return;
                }

                if (dest == null)
                {
                    _logger.Warning("No fallback or previous profile available; not switching.");
                    return;
                }

                bool success = SwitchTo(dest);
                if (success)
                {
                    ApplyEnterModeIfNeeded(GetEnterModeForTarget(state, dest));
                    _logger.Info($"Switched back to {dest.FriendlyName}");
                    state.PreviousNonTarget = null;
                }
                else
                {
                    _logger.Error($"Failed to switch back to {dest.FriendlyName}");
                }
            }
            else
            {
                state.PreviousNonTarget = current;
                bool success = SwitchTo(target);
                if (success)
                {
                    ApplyEnterModeIfNeeded(GetEnterModeForTarget(state, target));
                    _logger.Info($"Switched to target {target.FriendlyName}");
                }
                else
                {
                    _logger.Error($"Failed to switch to target {target.FriendlyName}");
                }
            }
        }

        private void SwitchToFirstTarget(HotkeyState state, string successPrefix)
        {
            InputProfile target = state.Targets[0];
            bool success = SwitchTo(target);
            if (success)
            {
                ApplyEnterModeIfNeeded(GetEnterModeForTarget(state, target));
                _logger.Info($"{successPrefix} {target.FriendlyName}");
            }
            else
            {
                _logger.Error($"Failed to switch to target {target.FriendlyName}");
            }
        }

        private void CycleToNextTarget(HotkeyState state, InputProfile current)
        {
            if (state.Targets.Count == 0)
            {
                _logger.Warning("Cycle workflow has no target profiles; not switching.");
                return;
            }

            int currentIndex = state.Targets.FindIndex(profile => ProfilesEqual(profile, current));
            int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % state.Targets.Count : 0;
            InputProfile target = state.Targets[nextIndex];

            if (currentIndex < 0)
            {
                state.PreviousNonTarget = current;
            }

            bool success = SwitchTo(target);
            if (success)
            {
                ApplyEnterModeIfNeeded(GetEnterModeForTarget(state, target));
                _logger.Info($"Cycled to target {target.FriendlyName}");
            }
            else
            {
                _logger.Error($"Failed to cycle to target {target.FriendlyName}");
            }
        }

        /// <summary>
        /// Determines whether two profiles represent the same input method by comparing
        /// their KLIDs. If either profile is null, returns false.
        /// </summary>
        private static bool ProfilesEqual(InputProfile? a, InputProfile? b)
        {
            if (a == null || b == null) return false;
            return string.Equals(a.KLID, b.KLID, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Attempts to switch to the specified profile using the Win32 API and
        /// verifies the switch by reading back the current profile. Returns
        /// true if the switch succeeded; false otherwise.
        /// </summary>
        private bool SwitchTo(InputProfile profile)
        {
            try
            {
                IntPtr foregroundWindow = InputApis.GetForegroundWindow();

                IntPtr hkl = profile.HKL;

                _logger.Info($"Switch request: {profile.FriendlyName} KLID={profile.KLID} HKL=0x{profile.HKL.ToInt64():X8}");

                if (hkl == IntPtr.Zero)
                {
                    hkl = InputApis.LoadKeyboardLayout(profile.KLID, InputApis.KLF_ACTIVATE);
                    _logger.Warning($"Profile HKL was zero; loaded layout by KLID {profile.KLID}, result HKL=0x{hkl.ToInt64():X8}.");
                }

                if (hkl == IntPtr.Zero)
                {
                    _logger.Error($"Cannot switch to {profile.FriendlyName}: no valid HKL.");
                    return false;
                }

                if (foregroundWindow != IntPtr.Zero)
                {
                    InputApis.PostMessage(foregroundWindow, InputApis.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
                }

                InputApis.ActivateKeyboardLayout(hkl, InputApis.KLF_SETFORPROCESS);
                System.Threading.Thread.Sleep(200);

                InputProfile after = GetCurrentProfile();
                if (ProfilesEqual(after, profile))
                {
                    _logger.Info($"Switch verified: {after.FriendlyName} KLID={after.KLID} HKL=0x{after.HKL.ToInt64():X8}");
                    return true;
                }

                if (foregroundWindow != IntPtr.Zero)
                {
                    InputApis.PostMessage(foregroundWindow, InputApis.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
                }

                InputApis.ActivateKeyboardLayout(hkl, InputApis.KLF_SETFORPROCESS);
                System.Threading.Thread.Sleep(300);

                after = GetCurrentProfile();
                bool success = ProfilesEqual(after, profile);

                if (success)
                {
                    _logger.Info($"Switch verified after retry: {after.FriendlyName} KLID={after.KLID} HKL=0x{after.HKL.ToInt64():X8}");
                }
                else
                {
                    _logger.Warning($"Switch verification failed. Requested {profile.FriendlyName} KLID={profile.KLID} HKL=0x{profile.HKL.ToInt64():X8}; current is {after.FriendlyName} KLID={after.KLID} HKL=0x{after.HKL.ToInt64():X8}.");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error($"Exception during switch: {ex}");
                return false;
            }
        }

        private InputProfile GetCurrentProfile()
        {
            uint threadId = InputApis.GetWindowThreadProcessId(InputApis.GetForegroundWindow(), out _);
            IntPtr currentHkl = InputApis.GetKeyboardLayout(threadId);
            string klid = ((ulong)currentHkl.ToInt64() & 0xFFFFFFFF).ToString("X8");
            foreach (var profile in _installedProfiles)
            {
                if (string.Equals(profile.KLID, klid, StringComparison.OrdinalIgnoreCase))
                    return profile;
            }
            return new InputProfile(currentHkl, klid, klid, false);
        }

        /// <summary>
        /// Retrieves the name of the foreground process. Returns empty string if
        /// retrieval fails. Only the executable name (without path) is
        /// returned.
        /// </summary>
        private static string GetForegroundProcessName()
        {
            IntPtr hwnd = InputApis.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return string.Empty;
            uint pid;
            InputApis.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return string.Empty;
            IntPtr hProcess = InputApis.OpenProcess(InputApis.PROCESS_QUERY_INFORMATION | InputApis.PROCESS_VM_READ, false, pid);
            if (hProcess == IntPtr.Zero)
            {
                return string.Empty;
            }
            try
            {
                var builder = new System.Text.StringBuilder(260);
                if (InputApis.GetModuleFileNameEx(hProcess, IntPtr.Zero, builder, builder.Capacity) != 0)
                {
                    string fullPath = builder.ToString();
                    return Path.GetFileName(fullPath);
                }
                return string.Empty;
            }
            finally
            {
                InputApis.CloseHandle(hProcess);
            }
        }

        private enum WorkflowMode
        {
            Toggle,
            SwitchTo,
            Cycle
        }

        private enum ReturnBehavior
        {
            LastNonTarget,
            AlwaysSpecificLayout,
            ManualOnly
        }

        private class HotkeyState
        {
            public WorkflowMode Mode;
            public List<InputProfile> Targets = new();
            public InputProfile? Fallback;
            public InputProfile? PreviousNonTarget;
            public ReturnBehavior ReturnBehavior;
            public Dictionary<string, string?> EnterModesByKlid = new(StringComparer.OrdinalIgnoreCase);
        }

        private void ApplyEnterModeIfNeeded(string? enterMode)
        {
            if (!string.Equals(enterMode, "hangul", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                System.Threading.Thread.Sleep(120);

                IntPtr foregroundWindow = InputApis.GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                {
                    _logger.Warning("Cannot set Hangul mode: no foreground window.");
                    return;
                }

                uint threadId = InputApis.GetWindowThreadProcessId(foregroundWindow, out _);
                IntPtr focusedWindow = foregroundWindow;

                var gui = new InputApis.GUITHREADINFO();
                gui.cbSize = System.Runtime.InteropServices.Marshal.SizeOf<InputApis.GUITHREADINFO>();

                if (InputApis.GetGUIThreadInfo(threadId, ref gui) && gui.hwndFocus != IntPtr.Zero)
                {
                    focusedWindow = gui.hwndFocus;
                }

                if (TrySetHangulViaImeContext(focusedWindow))
                {
                    return;
                }

                if (focusedWindow != foregroundWindow && TrySetHangulViaImeContext(foregroundWindow))
                {
                    return;
                }

                if (TrySetHangulViaDefaultImeWindow(focusedWindow))
                {
                    return;
                }

                if (focusedWindow != foregroundWindow && TrySetHangulViaDefaultImeWindow(foregroundWindow))
                {
                    return;
                }

                _logger.Warning("Cannot set Hangul mode safely: no usable IME context or default IME window.");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Cannot set Hangul mode safely: {ex.Message}");
            }
        }

        private bool TrySetHangulViaImeContext(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return false;
            }

            IntPtr imc = InputApis.ImmGetContext(window);
            if (imc == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (!InputApis.ImmGetConversionStatus(imc, out int conversion, out int sentence))
                {
                    return false;
                }

                int desired = conversion | InputApis.IME_CMODE_NATIVE;

                if (desired == conversion)
                {
                    _logger.Info($"Hangul/native conversion mode already active through IME context. Conversion={conversion}.");
                    return true;
                }

                if (InputApis.ImmSetConversionStatus(imc, desired, sentence))
                {
                    _logger.Info($"Applied Hangul/native conversion mode through IME context: {conversion} -> {desired}.");
                    return true;
                }

                return false;
            }
            finally
            {
                InputApis.ImmReleaseContext(window, imc);
            }
        }

        private bool TrySetHangulViaDefaultImeWindow(IntPtr ownerWindow)
        {
            if (ownerWindow == IntPtr.Zero)
            {
                return false;
            }

            IntPtr imeWindow = InputApis.ImmGetDefaultIMEWnd(ownerWindow);
            if (imeWindow == IntPtr.Zero)
            {
                return false;
            }

            IntPtr openBefore = InputApis.SendMessage(imeWindow, InputApis.WM_IME_CONTROL, (IntPtr)InputApis.IMC_GETOPENSTATUS, IntPtr.Zero);
            IntPtr conversionBefore = InputApis.SendMessage(imeWindow, InputApis.WM_IME_CONTROL, (IntPtr)InputApis.IMC_GETCONVERSIONMODE, IntPtr.Zero);

            int currentConversion = unchecked((int)conversionBefore.ToInt64());
            int desiredConversion = currentConversion | InputApis.IME_CMODE_NATIVE;

            InputApis.SendMessage(imeWindow, InputApis.WM_IME_CONTROL, (IntPtr)InputApis.IMC_SETOPENSTATUS, (IntPtr)1);
            InputApis.SendMessage(imeWindow, InputApis.WM_IME_CONTROL, (IntPtr)InputApis.IMC_SETCONVERSIONMODE, (IntPtr)desiredConversion);

            System.Threading.Thread.Sleep(60);

            IntPtr openAfter = InputApis.SendMessage(imeWindow, InputApis.WM_IME_CONTROL, (IntPtr)InputApis.IMC_GETOPENSTATUS, IntPtr.Zero);
            IntPtr conversionAfter = InputApis.SendMessage(imeWindow, InputApis.WM_IME_CONTROL, (IntPtr)InputApis.IMC_GETCONVERSIONMODE, IntPtr.Zero);

            _logger.Info($"Default IME window Hangul/native set attempt. Open {openBefore.ToInt64()} -> {openAfter.ToInt64()}, conversion {currentConversion} -> {conversionAfter.ToInt64()} requested={desiredConversion}.");

            return (conversionAfter.ToInt64() & InputApis.IME_CMODE_NATIVE) != 0;
        }

        private static string? GetEnterModeForTarget(HotkeyState state, InputProfile target)
        {
            return state.EnterModesByKlid.TryGetValue(target.KLID, out string? enterMode) ? enterMode : null;
        }

        private static Dictionary<string, string?> CreateEnterModeMap(IEnumerable<InputProfile> targets, string? enterMode)
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets)
            {
                result[target.KLID] = enterMode;
            }

            return result;
        }

        private static Dictionary<string, string?> CopyEnterModeMap(IReadOnlyDictionary<string, string?>? enterModes)
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (enterModes == null)
            {
                return result;
            }

            foreach (var pair in enterModes)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }

        private static WorkflowMode ParseWorkflowMode(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return WorkflowMode.Toggle;
            return mode.ToLowerInvariant() switch
            {
                "switchto" => WorkflowMode.SwitchTo,
                "cycle" => WorkflowMode.Cycle,
                _ => WorkflowMode.Toggle,
            };
        }

        private static ReturnBehavior ParseReturnBehavior(string behavior)
        {
            if (string.IsNullOrEmpty(behavior)) return ReturnBehavior.LastNonTarget;
            return behavior.ToLowerInvariant() switch
            {
                "lastnontarget" => ReturnBehavior.LastNonTarget,
                "alwaysspecificlayout" => ReturnBehavior.AlwaysSpecificLayout,
                "manualonly" => ReturnBehavior.ManualOnly,
                _ => ReturnBehavior.LastNonTarget,
            };
        }
    }
}
