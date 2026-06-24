using System;
using System.Runtime.InteropServices;

namespace InputFlow.Windows
{
    /// <summary>
    /// Provides helper methods for interacting with Windows input locales and keyboard layouts.
    /// </summary>
    public static class KeyboardLayoutHelper
    {
        /// <summary>
        /// Loads the specified keyboard layout into the system and returns a handle to it.
        /// </summary>
        /// <param name="layoutHex">The hexadecimal locale identifier string, e.g. "00020409" for US‑International.</param>
        /// <returns>An <see cref="IntPtr"/> representing the HKL.</returns>
        public static IntPtr LoadLayout(string layoutHex)
        {
            return LoadKeyboardLayout(layoutHex, 0);
        }

        /// <summary>
        /// Sets the default input language system‑wide.
        /// </summary>
        /// <param name="localeId">The locale identifier.</param>
        public static void SetDefaultInputLang(uint localeId)
        {
            // Prepare a 32‑bit buffer containing the locale ID.
            var handle = GCHandle.Alloc(localeId, GCHandleType.Pinned);
            try
            {
                SystemParametersInfo(SPI_SETDEFAULTINPUTLANG, 0, handle.AddrOfPinnedObject(), SPIF_SENDWININICHANGE);
            }
            finally
            {
                handle.Free();
            }
        }

        private const uint SPI_SETDEFAULTINPUTLANG = 0x005A;
        private const uint SPIF_SENDWININICHANGE = 0x02;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        // Additional P/Invoke declarations for activating layouts or broadcasting change messages can be added here.
    }
}
