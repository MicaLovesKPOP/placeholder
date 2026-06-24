; InputFlow – Smart input switching for Windows
; 
; This script lets you toggle seamlessly between your normal keyboard
; layout (for example US‑International) and the Korean IME.  Pressing
; **Right Alt** will switch you into the Korean IME if you are using any
; other layout.  If you are already in the Korean IME, the same key
; toggles between Hangul and English modes inside the IME.  Pressing
; **Right Ctrl + Right Alt** will return you to your previous layout (or
; enter Korean if you are not currently using it).

; Recommended AutoHotkey directives.  These improve performance and avoid
; conflicts with other scripts.
#NoEnv                 ; Avoids checking empty environment variables.
#Warn                  ; Enable warnings to help catch common mistakes.
#SingleInstance Force  ; Prevent multiple instances of this script from running.
SendMode Input         ; Use the more reliable SendInput mode for sending keystrokes.

; Global variable used to remember the previous input locale (HKL).
global prevLayout := 0

;----------------------------------------------------------------------
; Helper functions
;----------------------------------------------------------------------

; GetCurrentLayout() – returns the current thread's input locale handle (HKL).
; The HKL is a 32‑bit handle whose low word is the LANGID.  For example,
; Korean has LANGID 0x0412 and US English has LANGID 0x0409.  You can
; extract the LANGID by masking the return value with 0xFFFF.
GetCurrentLayout() {
    ; Get the active window handle.
    WinGet, hwnd, ID, A
    ; Obtain the thread ID for the active window.
    threadId := DllCall("GetWindowThreadProcessId", "UInt", hwnd, "UInt*", 0, "UInt")
    ; Retrieve the keyboard layout (HKL) for the thread.
    layout := DllCall("GetKeyboardLayout", "UInt", threadId, "UInt")
    return layout
}

; SwitchToLayout(localeId) – activates the given input locale as the
; system default and broadcasts the change to all top‑level windows.
SwitchToLayout(localeId) {
    ; Load the keyboard layout specified by localeId.  The identifier must
    ; be formatted as 8 hexadecimal digits (e.g. 00020409 for US‑Intl).
    newHKL := DllCall("LoadKeyboardLayout", "Str", Format("{:08X}", localeId), "UInt", 0, "UPtr")
    ; Prepare to set the system default input language.  Windows expects
    ; a binary DWORD containing the LANGID.
    VarSetCapacity(binaryLocaleId, 4, 0)
    NumPut(localeId, binaryLocaleId, 0, "UInt")
    ; Call SystemParametersInfo to set the default input language.  Use
    ; SPIF_SENDWININICHANGE to broadcast the change.
    static SPI_SETDEFAULTINPUTLANG := 0x005A, SPIF_SENDWININICHANGE := 0x02
    DllCall("SystemParametersInfo", "UInt", SPI_SETDEFAULTINPUTLANG
        , "UInt", 0, "UPtr", &binaryLocaleId, "UInt", SPIF_SENDWININICHANGE)
    ; Send a WM_INPUTLANGCHANGEREQUEST message (0x50) to all top‑level
    ; windows so they adopt the new layout immediately.
    WinGet, winList, List
    Loop, % winList {
        PostMessage, 0x50, 0, % newHKL, , % "ahk_id " winList%A_Index%
    }
    return
}

; RemoveInputFlowTooltip – hides the tooltip after a brief delay.
RemoveInputFlowTooltip:
    SetTimer, RemoveInputFlowTooltip, Off
    ToolTip
    return

;----------------------------------------------------------------------
; Hotkeys
;----------------------------------------------------------------------

; Right Alt: Enter Korean or toggle Hangul/English within Korean.
RAlt::
{
    current := GetCurrentLayout()
    currentLang := current & 0xFFFF
    ; LANGID 0x0412 corresponds to Korean (Korea).
    if (currentLang != 0x0412) {
        ; Save the current HKL before switching to Korean.
        prevLayout := current
        SwitchToLayout(0x0412)
        ToolTip, InputFlow: Korean IME activated, 0, 0, 1
        SetTimer, RemoveInputFlowTooltip, 2000
    } else {
        ; Already using Korean IME.  Send the Hangul key to toggle
        ; between Hangul and English modes.  AutoHotkey uses {Hangul}
        ; to represent the VK_HANGUL key.
        Send, {Hangul}
        ToolTip, InputFlow: Hangul/English toggled, 0, 0, 1
        SetTimer, RemoveInputFlowTooltip, 2000
    }
    return
}

; Right Ctrl + Right Alt: Restore previous layout or enter Korean.
RCtrl & RAlt::
{
    current := GetCurrentLayout()
    currentLang := current & 0xFFFF
    if (currentLang == 0x0412) {
        ; Currently in Korean IME.  Restore previous layout if available.
        if (prevLayout) {
            SwitchToLayout(prevLayout & 0xFFFFFFFF)
            ToolTip, InputFlow: Restored previous layout, 0, 0, 1
            prevLayout := 0
        } else {
            ; No previous layout recorded – default to US‑International (00020409).
            SwitchToLayout(0x00020409)
            ToolTip, InputFlow: Switched to US‑International, 0, 0, 1
        }
    } else {
        ; Not using Korean.  Save current layout and switch to Korean.
        prevLayout := current
        SwitchToLayout(0x0412)
        ToolTip, InputFlow: Korean IME activated, 0, 0, 1
    }
    SetTimer, RemoveInputFlowTooltip, 2000
    return
}
