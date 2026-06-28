using System;
using System.Collections.Generic;
using System.Linq;

namespace InputFlow.Core
{
    public sealed class InputFlowTriggerParseResult
    {
        private InputFlowTriggerParseResult(bool success, uint modifiers, int virtualKey, string normalizedKeys, string? error)
        {
            Success = success;
            Modifiers = modifiers;
            VirtualKey = virtualKey;
            NormalizedKeys = normalizedKeys;
            Error = error;
        }

        public bool Success { get; }
        public uint Modifiers { get; }
        public int VirtualKey { get; }
        public string NormalizedKeys { get; }
        public string? Error { get; }
        public bool IsSingleKeyTrigger => Success && Modifiers == 0 && IsSupportedSingleKeyVirtualKey(VirtualKey);

        public static InputFlowTriggerParseResult Parsed(uint modifiers, int virtualKey, string normalizedKeys)
        {
            return new InputFlowTriggerParseResult(true, modifiers, virtualKey, normalizedKeys, null);
        }

        public static InputFlowTriggerParseResult Failed(string error)
        {
            return new InputFlowTriggerParseResult(false, 0, 0, string.Empty, error);
        }

        private static bool IsSupportedSingleKeyVirtualKey(int virtualKey)
        {
            return virtualKey is
                InputFlowVirtualKeys.RightAlt or
                InputFlowVirtualKeys.LeftAlt or
                InputFlowVirtualKeys.RightControl or
                InputFlowVirtualKeys.LeftControl or
                InputFlowVirtualKeys.RightShift or
                InputFlowVirtualKeys.LeftShift;
        }
    }

    public static class InputFlowTriggerParser
    {
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;

        private static readonly Dictionary<string, (uint Modifier, string Name)> ModifierTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CTRL"] = (ModControl, "Ctrl"),
            ["CONTROL"] = (ModControl, "Ctrl"),
            ["ALT"] = (ModAlt, "Alt"),
            ["SHIFT"] = (ModShift, "Shift"),
            ["WIN"] = (ModWin, "Win"),
            ["WINDOWS"] = (ModWin, "Win")
        };

        private static readonly Dictionary<string, (int VirtualKey, string Name)> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SPACE"] = (InputFlowVirtualKeys.Space, "Space"),
            ["ENTER"] = (InputFlowVirtualKeys.Enter, "Enter"),
            ["RETURN"] = (InputFlowVirtualKeys.Enter, "Enter"),
            ["TAB"] = (InputFlowVirtualKeys.Tab, "Tab"),
            ["ESC"] = (InputFlowVirtualKeys.Escape, "Escape"),
            ["ESCAPE"] = (InputFlowVirtualKeys.Escape, "Escape"),
            ["BACK"] = (InputFlowVirtualKeys.Backspace, "Backspace"),
            ["BACKSPACE"] = (InputFlowVirtualKeys.Backspace, "Backspace"),
            ["DELETE"] = (InputFlowVirtualKeys.Delete, "Delete"),
            ["DEL"] = (InputFlowVirtualKeys.Delete, "Delete"),
            ["INSERT"] = (InputFlowVirtualKeys.Insert, "Insert"),
            ["INS"] = (InputFlowVirtualKeys.Insert, "Insert"),
            ["HOME"] = (InputFlowVirtualKeys.Home, "Home"),
            ["END"] = (InputFlowVirtualKeys.End, "End"),
            ["PAGEUP"] = (InputFlowVirtualKeys.PageUp, "PageUp"),
            ["PGUP"] = (InputFlowVirtualKeys.PageUp, "PageUp"),
            ["PAGEDOWN"] = (InputFlowVirtualKeys.PageDown, "PageDown"),
            ["PGDN"] = (InputFlowVirtualKeys.PageDown, "PageDown"),
            ["UP"] = (InputFlowVirtualKeys.Up, "Up"),
            ["DOWN"] = (InputFlowVirtualKeys.Down, "Down"),
            ["LEFT"] = (InputFlowVirtualKeys.Left, "Left"),
            ["RIGHT"] = (InputFlowVirtualKeys.Right, "Right"),
            ["RIGHTALT"] = (InputFlowVirtualKeys.RightAlt, "RightAlt"),
            ["RALT"] = (InputFlowVirtualKeys.RightAlt, "RightAlt"),
            ["ALTGR"] = (InputFlowVirtualKeys.RightAlt, "RightAlt"),
            ["LEFTALT"] = (InputFlowVirtualKeys.LeftAlt, "LeftAlt"),
            ["LALT"] = (InputFlowVirtualKeys.LeftAlt, "LeftAlt"),
            ["RIGHTCTRL"] = (InputFlowVirtualKeys.RightControl, "RightCtrl"),
            ["RCTRL"] = (InputFlowVirtualKeys.RightControl, "RightCtrl"),
            ["LEFTCTRL"] = (InputFlowVirtualKeys.LeftControl, "LeftCtrl"),
            ["LCTRL"] = (InputFlowVirtualKeys.LeftControl, "LeftCtrl"),
            ["RIGHTSHIFT"] = (InputFlowVirtualKeys.RightShift, "RightShift"),
            ["RSHIFT"] = (InputFlowVirtualKeys.RightShift, "RightShift"),
            ["LEFTSHIFT"] = (InputFlowVirtualKeys.LeftShift, "LeftShift"),
            ["LSHIFT"] = (InputFlowVirtualKeys.LeftShift, "LeftShift")
        };

        public static InputFlowTriggerParseResult Parse(string keys)
        {
            if (string.IsNullOrWhiteSpace(keys))
            {
                return InputFlowTriggerParseResult.Failed("Trigger is required.");
            }

            uint modifiers = 0;
            int virtualKey = 0;
            string? virtualKeyName = null;
            var modifierNames = new List<string>();
            string[] parts = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return InputFlowTriggerParseResult.Failed("Trigger is required.");
            }

            foreach (string rawPart in parts)
            {
                string token = NormalizeToken(rawPart);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (ModifierTokens.TryGetValue(token, out var modifier))
                {
                    if ((modifiers & modifier.Modifier) == 0)
                    {
                        modifiers |= modifier.Modifier;
                        modifierNames.Add(modifier.Name);
                    }
                    continue;
                }

                if (!TryParseVirtualKey(token, out int parsedVirtualKey, out string parsedName))
                {
                    return InputFlowTriggerParseResult.Failed($"Unsupported trigger key '{rawPart.Trim()}'.");
                }

                if (virtualKey != 0)
                {
                    return InputFlowTriggerParseResult.Failed("Trigger must contain exactly one non-modifier key.");
                }

                virtualKey = parsedVirtualKey;
                virtualKeyName = parsedName;
            }

            if (virtualKey == 0 || string.IsNullOrWhiteSpace(virtualKeyName))
            {
                return InputFlowTriggerParseResult.Failed("Trigger must contain one non-modifier key.");
            }

            string normalized = string.Join("+", modifierNames.Concat(new[] { virtualKeyName }));
            return InputFlowTriggerParseResult.Parsed(modifiers, virtualKey, normalized);
        }

        private static bool TryParseVirtualKey(string token, out int virtualKey, out string name)
        {
            if (NamedKeys.TryGetValue(token, out var named))
            {
                virtualKey = named.VirtualKey;
                name = named.Name;
                return true;
            }

            if (token.Length == 1 && token[0] is >= 'A' and <= 'Z')
            {
                virtualKey = token[0];
                name = token;
                return true;
            }

            if (token.Length == 1 && token[0] is >= '0' and <= '9')
            {
                virtualKey = token[0];
                name = token;
                return true;
            }

            if (token.Length is >= 2 and <= 3 && token[0] == 'F' && int.TryParse(token[1..], out int functionKey) && functionKey is >= 1 and <= 24)
            {
                virtualKey = InputFlowVirtualKeys.F1 + functionKey - 1;
                name = $"F{functionKey}";
                return true;
            }

            virtualKey = 0;
            name = string.Empty;
            return false;
        }

        private static string NormalizeToken(string token)
        {
            return token
                .Trim()
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToUpperInvariant();
        }

    }

    public static class InputFlowVirtualKeys
    {
        public const int Backspace = 0x08;
        public const int Tab = 0x09;
        public const int Enter = 0x0D;
        public const int Escape = 0x1B;
        public const int Space = 0x20;
        public const int PageUp = 0x21;
        public const int PageDown = 0x22;
        public const int End = 0x23;
        public const int Home = 0x24;
        public const int Left = 0x25;
        public const int Up = 0x26;
        public const int Right = 0x27;
        public const int Down = 0x28;
        public const int Insert = 0x2D;
        public const int Delete = 0x2E;
        public const int LeftShift = 0xA0;
        public const int RightShift = 0xA1;
        public const int LeftControl = 0xA2;
        public const int RightControl = 0xA3;
        public const int LeftAlt = 0xA4;
        public const int RightAlt = 0xA5;
        public const int F1 = 0x70;
    }
}

