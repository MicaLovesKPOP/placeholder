using System;
using InputFlow.Windows;

namespace InputFlow.Core
{
    /// <summary>
    /// Manages toggling between a target IME and the previously active input profile.
    /// This is a minimal skeleton designed to illustrate the core logic.  It
    /// should be expanded with state management, verification, and error handling
    /// as described in the design document.
    /// </summary>
    public class InputSwitcher
    {
        private uint? _previousLayoutId;
        private readonly uint _targetLayoutId;
        private readonly uint _fallbackLayoutId;

        /// <summary>
        /// Initializes a new <see cref="InputSwitcher"/>.
        /// </summary>
        /// <param name="targetLayoutId">The LANGID of the target IME (e.g. 0x0412 for Korean).</param>
        /// <param name="fallbackLayoutId">A fallback LANGID to use if no previous layout is known (e.g. 0x0409 for US English).</param>
        public InputSwitcher(uint targetLayoutId, uint fallbackLayoutId)
        {
            _targetLayoutId = targetLayoutId;
            _fallbackLayoutId = fallbackLayoutId;
        }

        /// <summary>
        /// Toggles between the target IME and the last non‑target input.  If currently
        /// not using the target, the method remembers the current LANGID and switches
        /// to the target.  If already using the target, it returns to the previously
        /// saved LANGID or the fallback if none is available.
        /// </summary>
        public void Toggle()
        {
            uint currentLangId = GetCurrentLangId();
            if (currentLangId == _targetLayoutId)
            {
                // Return to previous or fallback.
                uint destination = _previousLayoutId ?? _fallbackLayoutId;
                KeyboardLayoutHelper.SetDefaultInputLang(destination);
                _previousLayoutId = null;
            }
            else
            {
                // Switch to target.
                _previousLayoutId = currentLangId;
                KeyboardLayoutHelper.SetDefaultInputLang(_targetLayoutId);
            }
        }

        /// <summary>
        /// Retrieves the LANGID of the current thread’s input locale.  This uses
        /// Windows APIs and should be moved to a platform module.
        /// </summary>
        private static uint GetCurrentLangId()
        {
            // On a real implementation, call GetKeyboardLayout from user32.dll.
            // This placeholder returns 0x0409 (US English) as a stub.
            return 0x0409;
        }
    }
}
