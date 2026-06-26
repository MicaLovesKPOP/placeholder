using System;

namespace InputFlow.Core
{
    /// <summary>
    /// Represents a Windows input profile (keyboard layout or IME) identified by its HKL.
    /// </summary>
    public class InputProfile
    {
        /// <summary>
        /// Creates a new <see cref="InputProfile"/> with the specified handle and friendly name.
        /// </summary>
        /// <param name="hkl">The input locale identifier (HKL).</param>
        /// <param name="klid">The 8-character KLID string (e.g. "00000409").</param>
        /// <param name="friendlyName">A descriptive name for this profile.</param>
        /// <param name="isIme">True if this profile represents an IME; otherwise false.</param>
        /// <param name="languageTag">Best-effort BCP-47 language tag derived from the HKL LANGID.</param>
        public InputProfile(IntPtr hkl, string klid, string friendlyName, bool isIme, string? languageTag = null)
        {
            HKL = hkl;
            KLID = klid;
            FriendlyName = friendlyName;
            IsIme = isIme;
            LanguageTag = languageTag;
        }

        /// <summary>
        /// Gets the input locale identifier for this profile.
        /// </summary>
        public IntPtr HKL { get; }

        /// <summary>
        /// Gets the 8-character KLID string representing this profile.
        /// </summary>
        public string KLID { get; }

        /// <summary>
        /// Gets the friendly name of this profile. This may be empty if no description was found.
        /// </summary>
        public string FriendlyName { get; }

        /// <summary>
        /// Gets a value indicating whether this profile is an IME.
        /// </summary>
        public bool IsIme { get; }

        /// <summary>
        /// Gets the best-effort BCP-47 language tag derived from the LANGID, if available.
        /// </summary>
        public string? LanguageTag { get; }

        /// <summary>
        /// Gets the LANGID (low 16 bits) of the HKL.
        /// </summary>
        public uint LangId => (uint)((ulong)HKL.ToInt64() & 0xFFFF);

        public override string ToString()
        {
            return $"{FriendlyName} ({KLID})";
        }
    }
}
