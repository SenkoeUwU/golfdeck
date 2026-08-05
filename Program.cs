using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GolfDeck
{
    // ---------------- XInput ----------------

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_BATTERY_INFORMATION
    {
        public byte BatteryType;   // 0 none, 1 wired, 2 alkaline, 3 nimh, 0xFF unknown
        public byte BatteryLevel;  // 0 empty, 1 low, 2 medium, 3 full
    }

    static class XInput
    {
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern int GetState14(int idx, out XINPUT_STATE state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern int GetState910(int idx, out XINPUT_STATE state);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
        static extern int GetBattery14(int idx, byte devType, out XINPUT_BATTERY_INFORMATION info);

        static int mode = 0; // 0 = try 1_4, 2 = fall back to 9_1_0
        static bool batteryOk = true; // xinput9_1_0 has no battery export

        public static int GetState(int idx, out XINPUT_STATE state)
        {
            if (mode != 2)
            {
                try { return GetState14(idx, out state); }
                catch (DllNotFoundException) { mode = 2; }
            }
            return GetState910(idx, out state);
        }

        public static bool GetBattery(int idx, out XINPUT_BATTERY_INFORMATION info)
        {
            info = new XINPUT_BATTERY_INFORMATION();
            if (!batteryOk || mode == 2) return false;
            try { return GetBattery14(idx, 0, out info) == 0; }
            catch (DllNotFoundException) { batteryOk = false; }
            catch (EntryPointNotFoundException) { batteryOk = false; }
            return false;
        }
    }

    // ---------------- SendInput ----------------

    // ---------------- legacy joystick API (winmm): generic DirectInput/HID pads ----------------
    // Devices that are not XInput-compatible (arcade encoders, Arduino/Leonardo HID
    // gamepads, "Generic USB Joystick") are invisible to XInput. winmm ships inside
    // Windows and reads any HID joystick, so it keeps GolfDeck dependency-free.

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct JOYCAPS
    {
        public ushort wMid;
        public ushort wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax;
        public uint wNumButtons;
        public uint wPeriodMin, wPeriodMax;
        public uint wRmin, wRmax, wUmin, wUmax, wVmin, wVmax;
        public uint wCaps;
        public uint wMaxAxes, wNumAxes, wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szOEMVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOYINFOEX
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1, dwReserved2;
    }

    static class WinMM
    {
        [DllImport("winmm.dll")]
        public static extern uint joyGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "joyGetDevCapsW")]
        public static extern uint joyGetDevCaps(UIntPtr id, ref JOYCAPS caps, uint size);

        [DllImport("winmm.dll")]
        public static extern uint joyGetPosEx(uint id, ref JOYINFOEX info);

        public const uint JOYERR_NOERROR = 0;
        public const uint JOY_RETURNALL = 0x000000ff;
        public const uint POV_CENTERED = 0xFFFF;
        public const uint JOYCAPS_HASZ = 0x0001;
        public const uint JOYCAPS_HASR = 0x0002;
        public const uint JOYCAPS_HASU = 0x0004;
        public const uint JOYCAPS_HASV = 0x0008;
        public const uint JOYCAPS_HASPOV = 0x0010;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // ---------------- key sending ----------------

    enum SendMode { VirtualKeys = 0, ScanCodes = 1 }

    struct KeyEv
    {
        public ushort Vk;
        public bool Down;
        public KeyEv(ushort vk, bool down) { Vk = vk; Down = down; }
    }

    static class KeySender
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint code, uint mapType);

        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_SCANCODE = 0x0008;

        public static SendMode Mode = SendMode.VirtualKeys;
        public static long KeysSent = 0;
        public static string LastSent = "";

        // keys that need the extended-key flag (arrows, nav cluster, win keys)
        static readonly HashSet<ushort> extended = new HashSet<ushort>
        {
            0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C
        };

        static INPUT Make(ushort vk, bool down)
        {
            var inp = new INPUT();
            inp.type = 1; // INPUT_KEYBOARD
            uint flags = down ? 0u : KEYEVENTF_KEYUP;
            if (extended.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
            ushort scan = (ushort)MapVirtualKey(vk, 0); // MAPVK_VK_TO_VSC
            if (Mode == SendMode.ScanCodes)
            {
                inp.U.ki.wVk = 0;
                inp.U.ki.wScan = scan;
                flags |= KEYEVENTF_SCANCODE;
            }
            else
            {
                inp.U.ki.wVk = vk;
                inp.U.ki.wScan = scan;
            }
            inp.U.ki.dwFlags = flags;
            return inp;
        }

        static void Emit(List<KeyEv> seq)
        {
            if (seq.Count == 0) return;
            var arr = new INPUT[seq.Count];
            for (int i = 0; i < seq.Count; i++) arr[i] = Make(seq[i].Vk, seq[i].Down);
            SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
            KeysSent += arr.Length;
        }

        public static void Press(List<ushort> mods, ushort vk)
        {
            var seq = new List<KeyEv>();
            foreach (var m in mods) seq.Add(new KeyEv(m, true));
            seq.Add(new KeyEv(vk, true));
            Emit(seq);
        }

        public static void Release(List<ushort> mods, ushort vk)
        {
            var seq = new List<KeyEv>();
            seq.Add(new KeyEv(vk, false));
            for (int i = mods.Count - 1; i >= 0; i--) seq.Add(new KeyEv(mods[i], false));
            Emit(seq);
        }

        public static void Tap(List<ushort> mods, ushort vk)
        {
            var seq = new List<KeyEv>();
            foreach (var m in mods) seq.Add(new KeyEv(m, true));
            seq.Add(new KeyEv(vk, true));
            seq.Add(new KeyEv(vk, false));
            for (int i = mods.Count - 1; i >= 0; i--) seq.Add(new KeyEv(mods[i], false));
            Emit(seq);
        }
    }

    // ---------------- key name parsing ----------------

    static class KeyNames
    {
        static readonly Dictionary<string, ushort> named = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            { "CTRL", 0x11 }, { "CONTROL", 0x11 }, { "SHIFT", 0x10 }, { "ALT", 0x12 }, { "WIN", 0x5B },
            { "UP", 0x26 }, { "DOWN", 0x28 }, { "LEFT", 0x25 }, { "RIGHT", 0x27 },
            { "SPACE", 0x20 }, { "ENTER", 0x0D }, { "RETURN", 0x0D }, { "TAB", 0x09 },
            { "ESC", 0x1B }, { "ESCAPE", 0x1B }, { "BACKSPACE", 0x08 },
            { "DELETE", 0x2E }, { "DEL", 0x2E }, { "INSERT", 0x2D },
            { "HOME", 0x24 }, { "END", 0x23 }, { "PGUP", 0x21 }, { "PGDN", 0x22 },
            { "'", 0xDE }, { ";", 0xBA }, { "/", 0xBF }, { "`", 0xC0 },
            { "[", 0xDB }, { "\\", 0xDC }, { "]", 0xDD },
            { "=", 0xBB }, { ",", 0xBC }, { "-", 0xBD }, { ".", 0xBE }
        };

        public static bool TryParse(string token, out ushort vk)
        {
            token = token.Trim();
            if (named.TryGetValue(token, out vk)) return true;
            if (token.Length == 1)
            {
                char c = char.ToUpperInvariant(token[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) { vk = (ushort)c; return true; }
            }
            if (token.Length >= 2 && (token[0] == 'F' || token[0] == 'f'))
            {
                int n;
                if (int.TryParse(token.Substring(1), out n) && n >= 1 && n <= 24)
                {
                    vk = (ushort)(0x6F + n);
                    return true;
                }
            }
            vk = 0;
            return false;
        }

        public static bool IsModifier(ushort vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B;
        }
    }

    // ---------------- mapping model ----------------

    class MapEntry
    {
        public string Input;
        public string Label = "";
        public string KeysText = "";
        public string Mode = "hold";
        public int RepeatMs = 170;
        public List<ushort> Mods = new List<ushort>();
        public ushort Vk;

        // taphold: second function fired when held past HoldMs
        public int HoldMs = 500;
        public string HoldKeysText = "";
        public List<ushort> HoldMods = new List<ushort>();
        public ushort HoldVk;

        public bool WasDown;
        public int NextRepeat;
        public bool Holding;
        public int PressStart;
        public bool HoldFired;
        // explicit flag rather than a PressStart==0 sentinel: TickCount passes
        // through zero every ~49 days, which would swallow a press
        public bool AwaitingSecond;

        public string CaptionText
        {
            get { return HoldKeysText.Length > 0 ? KeysText + " / " + HoldKeysText : KeysText; }
        }
    }

    static class Config
    {
        public static string Dir
        {
            get
            {
                string d = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GolfDeck");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string MappingPath
        {
            get { return Path.Combine(Dir, "mapping.txt"); }
        }

        // pre-1.7 installs kept mapping.txt next to the exe; move it over once
        public static void MigrateOldMapping()
        {
            try
            {
                string old = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "mapping.txt");
                if (File.Exists(old) && !File.Exists(MappingPath))
                {
                    try { File.Move(old, MappingPath); }
                    catch { File.Copy(old, MappingPath); } // read-only exe dir: copy, leave original
                }
            }
            catch { /* keep the old file in place on any failure */ }
        }

        public static string DefaultFor(int layout)
        {
            return DefaultFor(layout, false);
        }

        public static string DefaultFor(int layout, bool generic)
        {
            if (generic) return layout == 1 ? DefaultMappingV2Hid : DefaultMappingV1Hid;
            return layout == 1 ? DefaultMappingV2 : DefaultMappingV1;
        }

        // Generic (DirectInput/HID) wiring for the V2 board, taken from the maker's
        // JoyToKey profile, which numbers buttons exactly the way winmm reports them.
        public const string DefaultMappingV2Hid = @"# GolfDeck mapping - V2 board, generic USB joystick
#
# For boxes that are NOT Xbox-compatible (they show up as ""Generic USB
# Joystick""). Button numbers match the maker's JoyToKey profile.
# Use Options > Input monitor to see which Btn number each button reports,
# then edit the numbers below if your unit differs.
#
# format:   input = keys | label | mode | window_ms | second keys
#
# settings (percent):
stick_threshold = 37
trigger_threshold = 25

Btn4    = T       | SCORECARD   | doubletap | 500 | I
Btn3    = Space   | FAST FWD    | tap
Btn2    = Y       | HEATMAP     | doubletap | 500 | 1
Btn1    = B       | HIDE OBJECT | doubletap | 500 | 2
Btn6    = J       | SHOTCAM     | doubletap | 500 | K
Btn5    = Ctrl+M  | MULLIGAN    | tap
Btn12   = U       | PUTT        | doubletap | 500 | C
Btn11   = O       | FLYOVER     | doubletap | 500 | V

# aim: the profile wires aim-right to a button (the WAKE button), the other
# three directions to the stick axes. Axis1p is harmless if unused.
Btn8    = Right | | hold
Axis1n  = Left  | | hold
Axis1p  = Right | | hold
Axis2n  = Up    | | hold
Axis2p  = Down  | | hold

# uncomment if your unit reports a hat switch instead of stick axes
# POV_Up    = Up    | | hold
# POV_Down  = Down  | | hold
# POV_Left  = Left  | | hold
# POV_Right = Right | | hold
";

        // Wired V1 wiring taken from the maker's JoyToKey profile: every control
        // is a plain button, including the aim arrows (Btn9-12). No axes.
        public const string DefaultMappingV1Hid = @"# GolfDeck mapping - V1 board, generic USB joystick (wired unit)
#
# Button numbers match the maker's JoyToKey profile for the wired box.
# If your unit differs, open Options > Input monitor, press each button,
# and correct the numbers below.
#
# format:   input = keys | label | mode
#
# settings (percent):
stick_threshold = 37
trigger_threshold = 25

Btn3    = U       | PUTT       | tap
Btn4    = Y       | HEATMAP    | tap
Btn5    = O       | FLYOVER    | tap
Btn6    = J       | SHOTCAM    | tap
Btn2    = I       | CLUB UP    | tap
Btn1    = K       | CLUB DOWN  | tap
Btn7    = A       | AIM POINT  | tap
Btn8    = Ctrl+M  | MULLIGAN   | tap

# aim arrows are wired as buttons on this unit
Btn9    = Left  | | hold
Btn10   = Up    | | hold
Btn11   = Down  | | hold
Btn12   = Right | | hold

# uncomment if your unit reports stick axes or a hat switch instead
# Axis1n  = Left  | | hold
# Axis1p  = Right | | hold
# Axis2n  = Up    | | hold
# Axis2p  = Down  | | hold
# POV_Up    = Up    | | hold
# POV_Down  = Down  | | hold
# POV_Left  = Left  | | hold
# POV_Right = Right | | hold
";

        public const string DefaultMappingV2 = @"# GolfDeck mapping - V2 board
# Wiring and dual functions taken from the maker's JoyToKey profile.
#
# doubletap buttons: single press sends the first key, pressing twice
# quickly (within the window, ms, 4th field) sends the second key
# (the green print on the box).
#
# format:   input = keys | label | mode | window_ms | second keys
#
# settings (percent):
stick_threshold = 37
trigger_threshold = 25

Y       = T       | SCORECARD   | doubletap | 500 | I
X       = Space   | FAST FWD    | tap
B       = Y       | HEATMAP     | doubletap | 500 | 1
A       = B       | HIDE OBJECT | doubletap | 500 | 2
RB      = J       | SHOTCAM     | doubletap | 500 | K
LB      = Ctrl+M  | MULLIGAN    | tap
RT      = U       | PUTT        | doubletap | 500 | C
LT      = O       | FLYOVER     | doubletap | 500 | V

# WAKE button (Menu input) doubles as aim right
Menu     = Right | | hold
LS_Up    = Up    | | hold
LS_Down  = Down  | | hold
LS_Left  = Left  | | hold
LS_Right = Right | | hold
";

        public const string DefaultMappingV1 = @"# GolfDeck mapping - V1 board (defaults = GSPro standard shortcuts)
#
# GSPro keys: U putt toggle, Y heat map, O flyover, J shot cam,
#             I club up, K club down, A reset aim, Ctrl+M mulligan,
#             arrow keys aim
#
# format:   input = keys | label | mode | repeat_ms
#   keys:   single key or combo with +   (K, Ctrl+M, Shift+F5)
#   label:  which printed board button this is (GUI matches by label)
#   mode:   hold    = key held down while button held (default)
#           tap     = one keypress per button press
#           repeat  = keypress repeats every repeat_ms while held
#           taphold   = quick press sends keys, holding past repeat_ms sends
#                       the 5th field's keys (input = k | label | taphold | 500 | k2)
#           doubletap = single press sends keys, two presses within repeat_ms
#                       send the 5th field's keys
#
# inputs:  A B X Y LB RB LT RT Menu View LS RS
#          DPad_Up DPad_Down DPad_Left DPad_Right
#          LS_Up LS_Down LS_Left LS_Right (left stick)
#          RS_Up RS_Down RS_Left RS_Right (right stick)
#
# settings (percent):
stick_threshold = 37
trigger_threshold = 25

X       = U       | PUTT       | tap
Y       = Y       | HEATMAP    | tap
LB      = O       | FLYOVER    | tap
RB      = J       | SHOTCAM    | tap
B       = I       | CLUB UP    | tap
A       = K       | CLUB DOWN  | tap
Menu    = A       | AIM POINT  | tap
LT      = Ctrl+M  | MULLIGAN   | tap

LS_Up    = Up    | | hold
LS_Down  = Down  | | hold
LS_Left  = Left  | | hold
LS_Right = Right | | hold
";

        // generic HID names: Btn1-32 (Button01 accepted), Axis1n-Axis6p, POV_*
        public static bool IsGenericInput(string s)
        {
            if (s.StartsWith("POV_"))
                return s == "POV_UP" || s == "POV_DOWN" || s == "POV_LEFT" || s == "POV_RIGHT";
            int n;
            if (s.StartsWith("BTN") && int.TryParse(s.Substring(3), out n)) return n >= 1 && n <= 32;
            if (s.StartsWith("BUTTON") && int.TryParse(s.Substring(6), out n)) return n >= 1 && n <= 32;
            if (s.StartsWith("AXIS") && s.Length >= 6)
            {
                char last = s[s.Length - 1];
                if ((last == 'N' || last == 'P') && int.TryParse(s.Substring(4, s.Length - 5), out n))
                    return n >= 1 && n <= 6;
            }
            return false;
        }

        // Button01 and Btn1 are the same input; store one canonical form
        public static string NormalizeInput(string s)
        {
            int n;
            if (s.StartsWith("BUTTON") && int.TryParse(s.Substring(6), out n) && n >= 1 && n <= 32)
                return "BTN" + n;
            if (s.StartsWith("BTN") && int.TryParse(s.Substring(3), out n) && n >= 1 && n <= 32)
                return "BTN" + n;
            return s;
        }

        public static List<MapEntry> Load(Engine engine, out List<string> errors)
        {
            errors = new List<string>();
            var entries = new List<MapEntry>();
            string[] lines;
            try { lines = File.ReadAllLines(MappingPath); }
            catch (Exception ex)
            {
                errors.Add("cannot read mapping.txt: " + ex.Message);
                return entries;
            }

            var validInputs = new HashSet<string>
            {
                "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "MENU", "START", "VIEW", "BACK", "LS", "RS",
                "DPAD_UP", "DPAD_DOWN", "DPAD_LEFT", "DPAD_RIGHT",
                "LS_UP", "LS_DOWN", "LS_LEFT", "LS_RIGHT",
                "RS_UP", "RS_DOWN", "RS_LEFT", "RS_RIGHT"
            };

            for (int ln = 0; ln < lines.Length; ln++)
            {
                string raw = lines[ln];
                int hash = raw.IndexOf('#');
                if (hash >= 0) raw = raw.Substring(0, hash);
                raw = raw.Trim();
                if (raw.Length == 0) continue;

                int eq = raw.IndexOf('=');
                if (eq < 0) { errors.Add("line " + (ln + 1) + ": no '='"); continue; }

                string left = raw.Substring(0, eq).Trim();
                string right = raw.Substring(eq + 1).Trim();
                string leftUp = left.ToUpperInvariant();

                // settings
                if (leftUp == "STICK_THRESHOLD" || leftUp == "TRIGGER_THRESHOLD")
                {
                    int pct;
                    if (int.TryParse(right, out pct) && pct >= 1 && pct <= 99)
                    {
                        if (leftUp == "STICK_THRESHOLD") engine.StickThreshold = 32767 * pct / 100;
                        else engine.TriggerThreshold = 255 * pct / 100;
                    }
                    else errors.Add("line " + (ln + 1) + ": bad percent");
                    continue;
                }

                if (!validInputs.Contains(leftUp) && !IsGenericInput(leftUp))
                {
                    errors.Add("line " + (ln + 1) + ": unknown input '" + left + "'");
                    continue;
                }
                leftUp = NormalizeInput(leftUp);
                if (leftUp == "START") leftUp = "MENU";
                if (leftUp == "BACK") leftUp = "VIEW";

                string[] parts = right.Split('|');
                var e = new MapEntry();
                e.Input = leftUp;
                e.KeysText = parts[0].Trim();
                if (parts.Length > 1) e.Label = parts[1].Trim();
                if (parts.Length > 2 && parts[2].Trim().Length > 0) e.Mode = parts[2].Trim().ToLowerInvariant();
                if (parts.Length > 3)
                {
                    int ms;
                    if (int.TryParse(parts[3].Trim(), out ms) && ms >= 20) { e.RepeatMs = ms; e.HoldMs = ms; }
                }
                if (e.Mode != "hold" && e.Mode != "tap" && e.Mode != "repeat" && e.Mode != "taphold" && e.Mode != "doubletap")
                {
                    errors.Add("line " + (ln + 1) + ": unknown mode '" + e.Mode + "'");
                    e.Mode = "hold";
                }

                // parse key combo
                bool ok = true;
                string[] toks = e.KeysText.Split('+');
                for (int i = 0; i < toks.Length; i++)
                {
                    ushort vk;
                    if (!KeyNames.TryParse(toks[i], out vk))
                    {
                        errors.Add("line " + (ln + 1) + ": unknown key '" + toks[i].Trim() + "'");
                        ok = false;
                        break;
                    }
                    if (i < toks.Length - 1)
                    {
                        if (!KeyNames.IsModifier(vk))
                        {
                            errors.Add("line " + (ln + 1) + ": '" + toks[i].Trim() + "' is not a modifier");
                            ok = false;
                            break;
                        }
                        e.Mods.Add(vk);
                    }
                    else e.Vk = vk;
                }

                // taphold / doubletap: 5th field holds the secondary keys
                if (ok && (e.Mode == "taphold" || e.Mode == "doubletap"))
                {
                    if (parts.Length > 4 && parts[4].Trim().Length > 0)
                    {
                        e.HoldKeysText = parts[4].Trim();
                        string[] htoks = e.HoldKeysText.Split('+');
                        for (int i = 0; i < htoks.Length; i++)
                        {
                            ushort hvk;
                            if (!KeyNames.TryParse(htoks[i], out hvk))
                            {
                                errors.Add("line " + (ln + 1) + ": unknown hold key '" + htoks[i].Trim() + "'");
                                ok = false;
                                break;
                            }
                            if (i < htoks.Length - 1)
                            {
                                if (!KeyNames.IsModifier(hvk))
                                {
                                    errors.Add("line " + (ln + 1) + ": '" + htoks[i].Trim() + "' is not a modifier");
                                    ok = false;
                                    break;
                                }
                                e.HoldMods.Add(hvk);
                            }
                            else e.HoldVk = hvk;
                        }
                    }
                    else
                    {
                        errors.Add("line " + (ln + 1) + ": " + e.Mode + " needs secondary keys in the 5th field");
                        e.Mode = "tap";
                    }
                }
                if (ok) entries.Add(e);
            }
            return entries;
        }
    }

    // ---------------- input sources ----------------

    class DeviceInfo
    {
        public string Id;     // "xinput:0" .. "xinput:3", "hid:0" .. "hid:15"
        public string Label;
    }

    abstract class InputSource
    {
        public abstract string Id { get; }
        public abstract string Label { get; }
        // compact form for the board's status line, which has limited width
        public virtual string ShortLabel { get { return Label; } }
        public abstract bool Poll();                        // false once the device goes away
        public abstract bool IsDown(string input, Engine cfg);
        public abstract bool Knows(string input);           // name belongs to this device family
        public abstract string Describe();                  // live state for the input monitor
        public virtual bool TryBattery(out byte type, out byte level)
        {
            type = 0; level = 0; return false;
        }
    }

    class XInputSource : InputSource
    {
        int index;
        XINPUT_STATE state;

        public XInputSource(int idx) { index = idx; }

        public override string Id { get { return "xinput:" + index; } }
        public override string Label { get { return "Xbox controller (player " + (index + 1) + ")"; } }
        public override string ShortLabel { get { return "Controller connected (P" + (index + 1) + ")"; } }

        public override bool Poll()
        {
            return XInput.GetState(index, out state) == 0;
        }

        public override bool TryBattery(out byte type, out byte level)
        {
            type = 0; level = 0;
            XINPUT_BATTERY_INFORMATION bi;
            if (!XInput.GetBattery(index, out bi)) return false;
            type = bi.BatteryType;
            level = bi.BatteryLevel;
            return true;
        }

        static readonly HashSet<string> names = new HashSet<string>
        {
            "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "MENU", "VIEW", "LS", "RS",
            "DPAD_UP", "DPAD_DOWN", "DPAD_LEFT", "DPAD_RIGHT",
            "LS_UP", "LS_DOWN", "LS_LEFT", "LS_RIGHT",
            "RS_UP", "RS_DOWN", "RS_LEFT", "RS_RIGHT"
        };

        public override bool Knows(string input) { return names.Contains(input); }

        public override bool IsDown(string input, Engine cfg)
        {
            ushort b = state.Gamepad.wButtons;
            switch (input)
            {
                case "A": return (b & 0x1000) != 0;
                case "B": return (b & 0x2000) != 0;
                case "X": return (b & 0x4000) != 0;
                case "Y": return (b & 0x8000) != 0;
                case "LB": return (b & 0x0100) != 0;
                case "RB": return (b & 0x0200) != 0;
                case "MENU": return (b & 0x0010) != 0;
                case "VIEW": return (b & 0x0020) != 0;
                case "LS": return (b & 0x0040) != 0;
                case "RS": return (b & 0x0080) != 0;
                case "DPAD_UP": return (b & 0x0001) != 0;
                case "DPAD_DOWN": return (b & 0x0002) != 0;
                case "DPAD_LEFT": return (b & 0x0004) != 0;
                case "DPAD_RIGHT": return (b & 0x0008) != 0;
                case "LT": return state.Gamepad.bLeftTrigger > cfg.TriggerThreshold;
                case "RT": return state.Gamepad.bRightTrigger > cfg.TriggerThreshold;
                case "LS_UP": return state.Gamepad.sThumbLY > cfg.StickThreshold;
                case "LS_DOWN": return state.Gamepad.sThumbLY < -cfg.StickThreshold;
                case "LS_LEFT": return state.Gamepad.sThumbLX < -cfg.StickThreshold;
                case "LS_RIGHT": return state.Gamepad.sThumbLX > cfg.StickThreshold;
                case "RS_UP": return state.Gamepad.sThumbRY > cfg.StickThreshold;
                case "RS_DOWN": return state.Gamepad.sThumbRY < -cfg.StickThreshold;
                case "RS_LEFT": return state.Gamepad.sThumbRX < -cfg.StickThreshold;
                case "RS_RIGHT": return state.Gamepad.sThumbRX > cfg.StickThreshold;
            }
            return false;
        }

        public override string Describe()
        {
            var sb = new StringBuilder();
            ushort b = state.Gamepad.wButtons;
            string[] bn = { "DPAD_UP", "DPAD_DOWN", "DPAD_LEFT", "DPAD_RIGHT", "MENU", "VIEW", "LS", "RS",
                            "LB", "RB", "", "", "A", "B", "X", "Y" };
            for (int i = 0; i < 16; i++)
                if ((b & (1 << i)) != 0 && bn[i].Length > 0) sb.Append(bn[i] + "  ");
            if (state.Gamepad.bLeftTrigger > 30) sb.Append("LT  ");
            if (state.Gamepad.bRightTrigger > 30) sb.Append("RT  ");
            if (sb.Length == 0) sb.Append("(no buttons)");
            sb.Append("\r\nLeft stick: " + state.Gamepad.sThumbLX + ", " + state.Gamepad.sThumbLY);
            sb.Append("\r\nRight stick: " + state.Gamepad.sThumbRX + ", " + state.Gamepad.sThumbRY);
            return sb.ToString();
        }
    }

    class HidSource : InputSource
    {
        uint id;
        JOYCAPS caps;
        JOYINFOEX info;
        string label;
        bool[] axisPresent = new bool[6];

        public HidSource(uint devId, JOYCAPS c)
        {
            id = devId;
            caps = c;
            label = (c.szPname ?? "").Trim();
            if (label.Length == 0) label = "Generic joystick";
            shortLabel = label;
            label += "  (device " + (devId + 1) + ")";
            // X/Y always exist; the rest are advertised individually by capability
            // flag. Counting axes instead would assume Z/R/U/V appear in order,
            // which is wrong for a device that has, say, R but no Z.
            axisPresent[0] = axisPresent[1] = true;
            axisPresent[2] = (c.wCaps & WinMM.JOYCAPS_HASZ) != 0;
            axisPresent[3] = (c.wCaps & WinMM.JOYCAPS_HASR) != 0;
            axisPresent[4] = (c.wCaps & WinMM.JOYCAPS_HASU) != 0;
            axisPresent[5] = (c.wCaps & WinMM.JOYCAPS_HASV) != 0;
        }

        string shortLabel;

        public override string Id { get { return "hid:" + id; } }
        public override string Label { get { return label; } }
        public override string ShortLabel { get { return shortLabel; } }

        public override bool Poll()
        {
            info = new JOYINFOEX();
            info.dwSize = (uint)Marshal.SizeOf(typeof(JOYINFOEX));
            info.dwFlags = WinMM.JOY_RETURNALL;
            return WinMM.joyGetPosEx(id, ref info) == WinMM.JOYERR_NOERROR;
        }

        // normalize a raw axis reading to -1.0 .. 1.0 using the device's own range
        float Axis(int n)
        {
            uint raw, min, max;
            switch (n)
            {
                case 0: raw = info.dwXpos; min = caps.wXmin; max = caps.wXmax; break;
                case 1: raw = info.dwYpos; min = caps.wYmin; max = caps.wYmax; break;
                case 2: raw = info.dwZpos; min = caps.wZmin; max = caps.wZmax; break;
                case 3: raw = info.dwRpos; min = caps.wRmin; max = caps.wRmax; break;
                case 4: raw = info.dwUpos; min = caps.wUmin; max = caps.wUmax; break;
                default: raw = info.dwVpos; min = caps.wVmin; max = caps.wVmax; break;
            }
            if (max <= min) return 0f;
            float t = (raw - (float)min) / (max - (float)min); // 0..1
            return t * 2f - 1f;
        }

        int PovDeg
        {
            get
            {
                if (info.dwPOV == WinMM.POV_CENTERED || info.dwPOV > 36000) return -1;
                return (int)(info.dwPOV / 100);
            }
        }

        public override bool Knows(string input)
        {
            return ParseButton(input) > 0 || ParseAxis(input) >= 0 || input.StartsWith("POV_");
        }

        static int ParseButton(string input)
        {
            // Btn1 / Button1 / Button01 all mean the same physical button
            string digits = null;
            if (input.StartsWith("BTN")) digits = input.Substring(3);
            else if (input.StartsWith("BUTTON")) digits = input.Substring(6);
            int n;
            if (digits != null && int.TryParse(digits, out n) && n >= 1 && n <= 32) return n;
            return 0;
        }

        // AXIS1N / AXIS1P .. AXIS6N / AXIS6P -> axis index, sign via the suffix
        static int ParseAxis(string input)
        {
            if (!input.StartsWith("AXIS") || input.Length < 6) return -1;
            char last = input[input.Length - 1];
            if (last != 'N' && last != 'P') return -1;
            int n;
            if (!int.TryParse(input.Substring(4, input.Length - 5), out n)) return -1;
            if (n < 1 || n > 6) return -1;
            return n - 1;
        }

        public override bool IsDown(string input, Engine cfg)
        {
            int btn = ParseButton(input);
            if (btn > 0) return (info.dwButtons & (1u << (btn - 1))) != 0;

            int ax = ParseAxis(input);
            if (ax >= 0)
            {
                if (!axisPresent[ax]) return false;
                float v = Axis(ax);
                float t = cfg.StickThreshold / 32767f;
                return input[input.Length - 1] == 'N' ? v < -t : v > t;
            }

            int pov = PovDeg;
            if (pov < 0) return false;
            // Bounds are inclusive so an 8-way hat's diagonals register as both
            // neighbouring directions, the way a d-pad does. Exclusive bounds left
            // 45/135/225/315 matching nothing, making diagonal aim dead.
            switch (input)
            {
                case "POV_UP": return pov >= 315 || pov <= 45;
                case "POV_RIGHT": return pov >= 45 && pov <= 135;
                case "POV_DOWN": return pov >= 135 && pov <= 225;
                case "POV_LEFT": return pov >= 225 && pov <= 315;
            }
            return false;
        }

        public override string Describe()
        {
            var sb = new StringBuilder();
            var pressed = new List<string>();
            for (int i = 0; i < 32; i++)
                if ((info.dwButtons & (1u << i)) != 0) pressed.Add("Btn" + (i + 1));
            sb.Append(pressed.Count > 0 ? string.Join("  ", pressed.ToArray()) : "(no buttons)");
            sb.Append("\r\n");
            for (int a = 0; a < 6; a++)
            {
                if (!axisPresent[a]) continue;
                sb.Append("Axis" + (a + 1) + ": " + ((int)(Axis(a) * 100)).ToString() + "   ");
            }
            int pov = PovDeg;
            sb.Append("\r\nPOV: " + (pov < 0 ? "centered" : pov + "°"));
            return sb.ToString();
        }
    }

    static class InputSources
    {
        public static List<DeviceInfo> Enumerate()
        {
            var list = new List<DeviceInfo>();
            XINPUT_STATE st;
            for (int i = 0; i < 4; i++)
                if (XInput.GetState(i, out st) == 0)
                    list.Add(new DeviceInfo { Id = "xinput:" + i, Label = "Xbox controller (player " + (i + 1) + ")" });

            uint n = 0;
            try { n = WinMM.joyGetNumDevs(); }
            catch (DllNotFoundException) { n = 0; }
            for (uint i = 0; i < n && i < 16; i++)
            {
                JOYCAPS caps = new JOYCAPS();
                if (WinMM.joyGetDevCaps((UIntPtr)i, ref caps, (uint)Marshal.SizeOf(typeof(JOYCAPS))) != WinMM.JOYERR_NOERROR)
                    continue;
                var probe = new JOYINFOEX();
                probe.dwSize = (uint)Marshal.SizeOf(typeof(JOYINFOEX));
                probe.dwFlags = WinMM.JOY_RETURNALL;
                if (WinMM.joyGetPosEx(i, ref probe) != WinMM.JOYERR_NOERROR) continue; // not plugged in
                string name = (caps.szPname ?? "").Trim();
                if (name.Length == 0) name = "Generic joystick";
                list.Add(new DeviceInfo
                {
                    Id = "hid:" + i,
                    Label = name + "  (device " + (i + 1) + ", " + caps.wNumButtons + " buttons)"
                });
            }
            return list;
        }

        public static InputSource Open(string id)
        {
            if (id == null) return null;
            if (id.StartsWith("xinput:"))
            {
                int idx;
                if (!int.TryParse(id.Substring(7), out idx)) return null;
                XINPUT_STATE st;
                if (XInput.GetState(idx, out st) != 0) return null;
                return new XInputSource(idx);
            }
            if (id.StartsWith("hid:"))
            {
                uint devId;
                if (!uint.TryParse(id.Substring(4), out devId)) return null;
                JOYCAPS caps = new JOYCAPS();
                if (WinMM.joyGetDevCaps((UIntPtr)devId, ref caps, (uint)Marshal.SizeOf(typeof(JOYCAPS))) != WinMM.JOYERR_NOERROR)
                    return null;
                var src = new HidSource(devId, caps);
                if (!src.Poll()) return null;
                return src;
            }
            return null;
        }

        // auto mode: prefer a connected XInput pad, otherwise the first HID joystick.
        // Probes devices directly instead of going through Enumerate + Open, which
        // would query every slot twice; this runs continuously while unplugged.
        public static InputSource OpenAuto()
        {
            for (int i = 0; i < 4; i++)
            {
                XINPUT_STATE st;
                if (XInput.GetState(i, out st) == 0) return new XInputSource(i);
            }
            uint n = 0;
            try { n = WinMM.joyGetNumDevs(); }
            catch (DllNotFoundException) { return null; }
            for (uint i = 0; i < n && i < 16; i++)
            {
                JOYCAPS caps = new JOYCAPS();
                if (WinMM.joyGetDevCaps((UIntPtr)i, ref caps, (uint)Marshal.SizeOf(typeof(JOYCAPS))) != WinMM.JOYERR_NOERROR)
                    continue;
                var src = new HidSource(i, caps);
                if (src.Poll()) return src;
            }
            return null;
        }
    }

    // ---------------- engine ----------------

    class Engine
    {
        public List<MapEntry> Entries = new List<MapEntry>();
        public HashSet<string> Pressed = new HashSet<string>();
        public bool Connected;
        public int StickThreshold = 12000;  // raw, of 32767
        public int TriggerThreshold = 64;   // raw, of 255
        public byte BatteryType;            // 0 = none/unsupported
        public byte BatteryLevel;
        int lastBatteryCheck;
        bool batteryChecked;

        public InputSource Source;          // active device, null when nothing is connected
        public string PreferredId = "";     // "" = auto, otherwise a specific device id
        public bool MappingMismatch;        // mapping is written for a different device family
        int lastScan;

        public string SourceLabel
        {
            get { return Source != null ? Source.Label : "no device"; }
        }

        public string SourceShortLabel
        {
            get { return Source != null ? Source.ShortLabel : "no device"; }
        }

        public bool Poll()
        {
            bool changed = false;

            if (Source != null && !Source.Poll())
                Source = null;

            // rescan for a device at most twice a second: enumeration is not free
            if (Source == null && Environment.TickCount - lastScan > 500)
            {
                lastScan = Environment.TickCount;
                Source = PreferredId.Length > 0 ? InputSources.Open(PreferredId) : InputSources.OpenAuto();
                if (Source != null)
                {
                    batteryChecked = false;
                    RecheckMapping();
                    changed = true;
                }
            }

            bool got = Source != null;
            if (got != Connected) { Connected = got; changed = true; }

            if (!got)
            {
                BatteryType = 0;
                batteryChecked = false;
                ReleaseAll();
                foreach (var e in Entries) e.WasDown = false;
                if (Pressed.Count > 0) { Pressed.Clear(); changed = true; }
                return changed;
            }

            // battery: coarse 4-level reading where the device reports one, every 5s
            if (!batteryChecked || Environment.TickCount - lastBatteryCheck > 5000)
            {
                batteryChecked = true;
                lastBatteryCheck = Environment.TickCount;
                byte bt, bl;
                if (Source.TryBattery(out bt, out bl)) { BatteryType = bt; BatteryLevel = bl; }
                else BatteryType = 0;
            }

            foreach (var e in Entries)
            {
                bool down = Source.IsDown(e.Input, this);
                if (down != e.WasDown) changed = true;
                Step(e, down);
                e.WasDown = down;
                if (down) Pressed.Add(e.Input);
                else Pressed.Remove(e.Input);
            }
            return changed;
        }

        // flag mappings whose inputs the active device cannot produce (an XInput
        // mapping on a generic joystick, or vice versa)
        public void RecheckMapping()
        {
            MappingMismatch = false;
            if (Source == null || Entries.Count == 0) return;
            foreach (var e in Entries)
                if (Source.Knows(e.Input)) return;
            MappingMismatch = true;
        }

        void Step(MapEntry e, bool down)
        {
            if (e.Mode != "taphold" && e.Mode != "doubletap" && down && !e.WasDown && e.KeysText.Length > 0)
                KeySender.LastSent = e.KeysText.ToUpperInvariant();
            if (e.Mode == "doubletap")
            {
                // creator-confirmed V2 semantics: a second press within the
                // window fires the secondary; otherwise the single press fires
                // the primary once the window expires
                if (down && !e.WasDown)
                {
                    if (e.AwaitingSecond && Environment.TickCount - e.PressStart <= e.HoldMs)
                    {
                        KeySender.Tap(e.HoldMods, e.HoldVk);
                        KeySender.LastSent = e.HoldKeysText.ToUpperInvariant();
                        e.AwaitingSecond = false;
                    }
                    else
                    {
                        e.PressStart = Environment.TickCount;
                        e.AwaitingSecond = true;
                    }
                }
                else if (e.AwaitingSecond && Environment.TickCount - e.PressStart > e.HoldMs)
                {
                    KeySender.Tap(e.Mods, e.Vk);
                    KeySender.LastSent = e.KeysText.ToUpperInvariant();
                    e.AwaitingSecond = false;
                }
            }
            else if (e.Mode == "taphold")
            {
                // JoyToKey semantics: tap fires on release before the threshold,
                // hold fires once the moment the threshold is crossed
                if (down && !e.WasDown)
                {
                    e.PressStart = Environment.TickCount;
                    e.HoldFired = false;
                }
                else if (down && e.WasDown && !e.HoldFired
                    && Environment.TickCount - e.PressStart >= e.HoldMs)
                {
                    KeySender.Tap(e.HoldMods, e.HoldVk);
                    KeySender.LastSent = e.HoldKeysText.ToUpperInvariant();
                    e.HoldFired = true;
                }
                else if (!down && e.WasDown && !e.HoldFired)
                {
                    KeySender.Tap(e.Mods, e.Vk);
                    KeySender.LastSent = e.KeysText.ToUpperInvariant();
                }
            }
            else if (e.Mode == "hold")
            {
                if (down && !e.WasDown)
                {
                    KeySender.Press(e.Mods, e.Vk);
                    e.Holding = true;
                }
                else if (!down && e.WasDown && e.Holding)
                {
                    KeySender.Release(e.Mods, e.Vk);
                    e.Holding = false;
                }
            }
            else if (e.Mode == "tap")
            {
                if (down && !e.WasDown) KeySender.Tap(e.Mods, e.Vk);
            }
            else if (e.Mode == "repeat")
            {
                if (down)
                {
                    if (!e.WasDown)
                    {
                        KeySender.Tap(e.Mods, e.Vk);
                        e.NextRepeat = Environment.TickCount + e.RepeatMs;
                    }
                    else if (Environment.TickCount - e.NextRepeat >= 0)
                    {
                        KeySender.Tap(e.Mods, e.Vk);
                        e.NextRepeat = Environment.TickCount + e.RepeatMs;
                    }
                }
            }
        }

        public void ReleaseAll()
        {
            foreach (var e in Entries)
            {
                if (e.Holding)
                {
                    KeySender.Release(e.Mods, e.Vk);
                    e.Holding = false;
                }
            }
        }

    }

    // ---------------- app state ----------------

    static class AppState
    {
        public static int Layout = 0; // 0 = V1 board, 1 = V2 board
        public static bool NoUpdateCheck; // screenshot/test runs skip the launch check
    }

    // ---------------- dark dropdown menu rendering ----------------

    class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.Accent; } }
        public override Color MenuItemBorder { get { return Theme.Accent; } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(26, 27, 29); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(26, 27, 29); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(26, 27, 29); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(26, 27, 29); } }
        public override Color MenuBorder { get { return Color.FromArgb(70, 72, 76); } }
    }

    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.FromArgb(20, 22, 16) : Color.Gainsboro;
            base.OnRenderItemText(e);
        }
    }

    // ---------------- UI scale ----------------
    // One factor drives all geometry and fonts (pixel units), so layout and
    // text can never scale apart. Derived from display DPI, capped so the
    // window always fits the working area.

    static class Ui
    {
        public static float S = 1f;

        public static int X(float v)
        {
            return (int)Math.Round(v * S);
        }

        public static float F(float v)
        {
            return v * S;
        }

        public static void Init(float forced)
        {
            if (forced > 0.4f && forced < 4f) { S = forced; return; }
            float dpi;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
            float s = dpi / 96f;
            var wa = Screen.PrimaryScreen.WorkingArea;
            s = Math.Min(s, Math.Min(wa.Width / 720f, wa.Height / 650f));
            if (s < 0.8f) s = 0.8f;
            S = s;
        }
    }

    // ---------------- update checker (GitHub releases) ----------------

    class UpdateInfo
    {
        public string Tag;
        public string Body;
        public string ZipUrl;
    }

    static class Updater
    {
        const string ApiLatest = "https://api.github.com/repos/SenkoeUwU/golfdeck/releases/latest";

        public static UpdateInfo FetchLatest()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string json;
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "GolfDeck-updater");
                json = wc.DownloadString(ApiLatest);
            }
            var info = new UpdateInfo();
            info.Tag = JsonString(json, "tag_name");
            info.Body = JsonString(json, "body");
            info.ZipUrl = FindAssetUrl(json, "GolfDeck.exe");
            if (info.ZipUrl == null) info.ZipUrl = FindAssetUrl(json, ".zip");
            if (info.Tag == null || info.ZipUrl == null) return null;
            return info;
        }

        // pick the release asset whose download url ends with the given suffix
        static string FindAssetUrl(string json, string suffix)
        {
            int pos = 0;
            while (true)
            {
                int i = json.IndexOf("\"browser_download_url\"", pos, StringComparison.Ordinal);
                if (i < 0) return null;
                string v = JsonString(json.Substring(i), "browser_download_url");
                if (v != null && v.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return v;
                pos = i + 22;
            }
        }

        public static bool IsNewer(string tag, string current)
        {
            Version remote, local;
            if (!Version.TryParse(Normalize(tag), out remote)) return false;
            if (!Version.TryParse(Normalize(current), out local)) return false;
            return remote > local;
        }

        static string Normalize(string v)
        {
            v = v.Trim().TrimStart('v', 'V');
            return v.IndexOf('.') < 0 ? v + ".0" : v;
        }

        // minimal JSON string-field extractor (first occurrence of "key": "...")
        public static string JsonString(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf(':', i);
            if (i < 0) return null;
            i = json.IndexOf('"', i);
            if (i < 0) return null;
            var sb = new StringBuilder();
            for (int p = i + 1; p < json.Length; p++)
            {
                char c = json[p];
                if (c == '\\' && p + 1 < json.Length)
                {
                    char n = json[++p];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') { }
                    else if (n == 't') sb.Append('\t');
                    else if (n == 'u' && p + 4 < json.Length)
                    {
                        sb.Append((char)Convert.ToInt32(json.Substring(p + 1, 4), 16));
                        p += 4;
                    }
                    else sb.Append(n);
                }
                else if (c == '"') break;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // download the release zip, stage the new exe, swap it in via a helper
        // script after this process exits, then relaunch
        public static void Apply(UpdateInfo info)
        {
            string temp = Path.Combine(Path.GetTempPath(), "GolfDeckUpdate");
            Directory.CreateDirectory(temp);
            string cur = Application.ExecutablePath;
            string staged = cur + ".new";

            if (info.ZipUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                // bare exe asset: download straight to the staging path
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "GolfDeck-updater");
                    wc.DownloadFile(info.ZipUrl, staged);
                }
            }
            else
            {
                string zip = Path.Combine(temp, "GolfDeck.zip");
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "GolfDeck-updater");
                    wc.DownloadFile(info.ZipUrl, zip);
                }
                string extract = Path.Combine(temp, "extracted");
                if (Directory.Exists(extract)) Directory.Delete(extract, true);
                ZipFile.ExtractToDirectory(zip, extract);
                string[] found = Directory.GetFiles(extract, "GolfDeck.exe", SearchOption.AllDirectories);
                if (found.Length == 0) throw new Exception("GolfDeck.exe not found in the update package.");
                File.Copy(found[0], staged, true);
            }

            string script = Path.Combine(temp, "apply-update.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                "ping 127.0.0.1 -n 3 > nul\r\n" +
                "move /y \"" + cur + "\" \"" + cur + ".old\" > nul\r\n" +
                "move /y \"" + staged + "\" \"" + cur + "\" > nul\r\n" +
                "start \"\" \"" + cur + "\"\r\n" +
                "ping 127.0.0.1 -n 3 > nul\r\n" +
                "del \"" + cur + ".old\" > nul 2>&1\r\n");
            var psi = new ProcessStartInfo(script);
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
    }

    // ---------------- theme (matches the physical box's ordering options) ----------------

    static class Theme
    {
        public static int Trim = 0;   // letters & top trim: 0 green, 1 white, 2 yellow
        public static int Btn = 0;    // buttons: 0 black, 1 white, 2 yellow, 3 red
        public static int Board = 0;  // board: 0 black, 1 green jacket, 2 red, 3 blue
        public static bool Forced;    // set by --theme (screenshot testing): skip registry load

        public static Color BoardBg
        {
            get
            {
                if (Board == 1) return Color.FromArgb(32, 64, 46);
                if (Board == 2) return Color.FromArgb(158, 34, 38);
                if (Board == 3) return Color.FromArgb(40, 78, 160);
                return Color.FromArgb(26, 27, 29);
            }
        }

        public static Color Accent
        {
            get
            {
                if (Trim == 1) return Color.FromArgb(236, 236, 236);
                if (Trim == 2) return Color.FromArgb(250, 208, 60);
                return Color.FromArgb(158, 232, 112);
            }
        }

        public static Color AccentDim
        {
            get
            {
                if (Trim == 1) return Color.FromArgb(150, 150, 154);
                if (Trim == 2) return Color.FromArgb(164, 138, 46);
                return Color.FromArgb(96, 142, 74);
            }
        }

        public static Color BtnTop
        {
            get
            {
                if (Btn == 1) return Color.FromArgb(246, 246, 248);
                if (Btn == 2) return Color.FromArgb(252, 216, 84);
                if (Btn == 3) return Color.FromArgb(216, 62, 66);
                return Color.FromArgb(58, 58, 62);
            }
        }

        public static Color BtnBottom
        {
            get
            {
                if (Btn == 1) return Color.FromArgb(178, 178, 184);
                if (Btn == 2) return Color.FromArgb(186, 146, 30);
                if (Btn == 3) return Color.FromArgb(142, 22, 26);
                return Color.FromArgb(20, 20, 22);
            }
        }

        public static Color BtnBorder
        {
            get
            {
                if (Btn == 1) return Color.FromArgb(148, 148, 154);
                if (Btn == 2) return Color.FromArgb(158, 124, 38);
                if (Btn == 3) return Color.FromArgb(114, 26, 30);
                return Color.FromArgb(74, 74, 80);
            }
        }

        public static Color Glyph
        {
            get
            {
                if (Btn == 1) return Color.FromArgb(96, 96, 102);
                if (Btn == 2) return Color.FromArgb(104, 80, 22);
                if (Btn == 3) return Color.FromArgb(248, 218, 218);
                return AccentDim;
            }
        }

        // pressed face follows the button colour (brighter = lit); black buttons
        // light up in the trim accent like the original edition
        public static Color PressTop
        {
            get
            {
                if (Btn == 1) return Color.FromArgb(214, 214, 218);
                if (Btn == 2) return Color.FromArgb(255, 234, 130);
                if (Btn == 3) return Color.FromArgb(244, 104, 108);
                if (Trim == 1) return Color.FromArgb(214, 214, 218);
                if (Trim == 2) return Color.FromArgb(252, 214, 64);
                return Color.FromArgb(112, 205, 72);
            }
        }

        public static Color PressBottom
        {
            get
            {
                if (Btn == 1) return Color.FromArgb(96, 96, 102);
                if (Btn == 2) return Color.FromArgb(202, 158, 34);
                if (Btn == 3) return Color.FromArgb(164, 34, 38);
                if (Trim == 1) return Color.FromArgb(96, 96, 102);
                if (Trim == 2) return Color.FromArgb(170, 128, 18);
                return Color.FromArgb(38, 100, 22);
            }
        }

        public static Color PressGlyph { get { return Color.FromArgb(20, 22, 16); } }

        // status ink, kept readable on every board colour
        public static Color StatusBad
        {
            get
            {
                if (Board == 2) return Color.FromArgb(250, 244, 240); // red board: white
                return Color.FromArgb(225, 96, 92);
            }
        }

        public static Color StatusDim
        {
            get
            {
                if (Board == 0) return Color.FromArgb(140, 140, 145);
                return Color.FromArgb(210, 214, 218); // colored boards need lighter ink
            }
        }
    }

    // ---------------- board GUI ----------------

    class BoardPanel : Panel
    {
        public Engine Engine;

        class Slot
        {
            public string Label;      // labeled buttons: matched to mapping entries by label
            public string Sub = "";   // green secondary print on the physical box (cosmetic)
            public bool SubLeft;      // sub printed left of the button (C-up / C-down) vs below
            public string[] Inputs;   // arrow buttons: matched by input
            public float X, Y;
            public bool Arrow;
            public string ArrowGlyph = "";
        }

        static Color Green { get { return Theme.Accent; } }
        static Color GreenDim { get { return Theme.AccentDim; } }
        static Color BoardBg { get { return Theme.BoardBg; } }
        static readonly Color BtnRing = Color.FromArgb(10, 10, 11);

        List<Slot> slots = new List<Slot>();
        Font labelFont = MakeBoardFont(20f);
        Font keyFont = new Font("Consolas", Ui.F(12f), FontStyle.Bold, GraphicsUnit.Pixel);
        Font smallFont = new Font("Segoe UI", Ui.F(11.3f), FontStyle.Regular, GraphicsUnit.Pixel);
        Font markFont = MakeBoardFont(12.7f);

        // the physical box is printed in a DIN-style condensed industrial face;
        // Bahnschrift (ships with Win10+) is the closest stock match. SemiCondensed
        // first: the tighter cuts crush together at small sizes. Pixel units,
        // scaled by Ui.S, so text tracks the app's own geometry, not device DPI.
        static Font MakeBoardFont(float sizePx)
        {
            string[] candidates = { "Bahnschrift SemiBold SemiConden", "Bahnschrift SemiBold Condensed", "Bahnschrift" };
            foreach (var name in candidates)
            {
                try
                {
                    using (var fam = new FontFamily(name))
                        return new Font(name, Ui.F(sizePx), FontStyle.Bold, GraphicsUnit.Pixel);
                }
                catch (ArgumentException) { }
            }
            return new Font("Segoe UI", Ui.F(sizePx - 2f), FontStyle.Bold, GraphicsUnit.Pixel);
        }

        public BoardPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.FromArgb(13, 13, 14);

            SetLayout(AppState.Layout);
        }

        float glyphX = 0.500f, glyphY = 0.665f;

        // straight-on grids mirroring the printed boards: labels fixed to
        // physical positions, mapping entries attach by label
        public void SetLayout(int layout)
        {
            slots.Clear();
            if (layout == 1)
            {
                // V2 board (sub print placement matches the product photos:
                // C-up / C-down left of the button, S1 / S2 / tee subs below)
                AddBtn(0.150f, 0.235f, "SCORECARD", "C↑", true);
                AddBtn(0.383f, 0.235f, "FAST FWD");
                AddBtn(0.617f, 0.235f, "HEATMAP", "S1");
                AddBtn(0.850f, 0.235f, "HIDE OBJECT", "S2");
                AddBtn(0.150f, 0.560f, "SHOTCAM", "C↓", true);
                AddBtn(0.383f, 0.560f, "MULLIGAN");
                AddBtn(0.150f, 0.835f, "PUTT", "←T");
                AddBtn(0.383f, 0.835f, "FLYOVER", "T→");

                glyphX = 0.720f; glyphY = 0.660f;
                AddArrow(0.720f, 0.513f, "▲", V2Up);
                AddArrow(0.622f, 0.660f, "◀", V2Left);
                // WAKE is physically a button doubling as aim right
                var wake = new Slot(); wake.X = 0.818f; wake.Y = 0.660f; wake.Arrow = true;
                wake.ArrowGlyph = "▶"; wake.Sub = "WAKE";
                wake.Inputs = WakeInputs;
                slots.Add(wake);
                AddArrow(0.720f, 0.807f, "▼", V2Down);
            }
            else
            {
                // V1 board
                AddBtn(0.150f, 0.235f, "PUTT");
                AddBtn(0.383f, 0.235f, "HEATMAP");
                AddBtn(0.617f, 0.235f, "FLYOVER");
                AddBtn(0.850f, 0.235f, "SHOTCAM");
                AddBtn(0.150f, 0.560f, "CLUB UP");
                AddBtn(0.150f, 0.850f, "CLUB DOWN");
                AddBtn(0.850f, 0.560f, "AIM POINT");
                AddBtn(0.850f, 0.850f, "MULLIGAN");

                // equal-arm plus: every arrow 66px from the cluster center (700x478 window)
                glyphX = 0.500f; glyphY = 0.665f;
                AddArrow(0.500f, 0.518f, "▲", V1Up);
                AddArrow(0.402f, 0.665f, "◀", V1Left);
                AddArrow(0.598f, 0.665f, "▶", V1Right);
                AddArrow(0.500f, 0.812f, "▼", V1Down);
            }
            Invalidate();
        }

        void AddBtn(float x, float y, string label, string sub = "", bool subLeft = false)
        {
            var s = new Slot();
            s.X = x; s.Y = y; s.Label = label; s.Sub = sub; s.SubLeft = subLeft;
            slots.Add(s);
        }

        // An arrow lights for any input that can drive it, XInput or generic.
        // The generic button numbers are LAYOUT-SPECIFIC: on the wired V1 unit the
        // arrows are Btn9-12, but on the V2 unit Btn11/Btn12 are FLYOVER and PUTT,
        // so the lists must not be shared between layouts.
        static readonly string[] V1Up = { "LS_UP", "DPAD_UP", "AXIS2N", "POV_UP", "BTN10" };
        static readonly string[] V1Down = { "LS_DOWN", "DPAD_DOWN", "AXIS2P", "POV_DOWN", "BTN11" };
        static readonly string[] V1Left = { "LS_LEFT", "DPAD_LEFT", "AXIS1N", "POV_LEFT", "BTN9" };
        static readonly string[] V1Right = { "LS_RIGHT", "DPAD_RIGHT", "AXIS1P", "POV_RIGHT", "BTN12" };
        static readonly string[] V2Up = { "LS_UP", "DPAD_UP", "AXIS2N", "POV_UP" };
        static readonly string[] V2Down = { "LS_DOWN", "DPAD_DOWN", "AXIS2P", "POV_DOWN" };
        static readonly string[] V2Left = { "LS_LEFT", "DPAD_LEFT", "AXIS1N", "POV_LEFT" };
        // V2 only: aim-right is also wired to a physical button (the WAKE button).
        // Menu/Btn8 mean AIM POINT and MULLIGAN on V1, so V1 must not claim them.
        static readonly string[] WakeInputs = { "LS_RIGHT", "DPAD_RIGHT", "AXIS1P", "POV_RIGHT", "MENU", "BTN8" };

        void AddArrow(float x, float y, string glyph, params string[] inputs)
        {
            var s = new Slot(); s.X = x; s.Y = y; s.Arrow = true; s.ArrowGlyph = glyph; s.Inputs = inputs; slots.Add(s);
        }

        // ---- click-to-test: mouse press on a drawn button sends its mapped key ----

        public HashSet<string> MousePressed = new HashSet<string>();
        public event EventHandler OptionsClicked;
        Slot hoverSlot;
        MapEntry activeEntry;
        bool activeIsHold;
        int activeStart;
        System.Windows.Forms.Timer clickTimer;
        MapEntry pendingClick;

        // status line drawn in the board's top dead space
        string connText = "";
        Color connColor = Color.Gray;
        string infoText = "";
        string battText = "";
        Color battColor = Color.Gray;
        RectangleF optionsRect;
        bool hoverOptions;

        public void SetStatus(string conn, Color col, string info, string batt, Color battCol)
        {
            if (conn == connText && col == connColor && info == infoText
                && batt == battText && battCol == battColor) return;
            connText = conn;
            connColor = col;
            infoText = info;
            battText = batt;
            battColor = battCol;
            Invalidate();
        }

        Rectangle BoardRect
        {
            get { int pad = Ui.X(14); return new Rectangle(pad, pad, Width - pad * 2, Height - pad * 2); }
        }

        Slot HitTest(Point p)
        {
            var board = BoardRect;
            float r = board.Width * 0.053f;
            foreach (var s in slots)
            {
                float x = board.X + board.Width * s.X;
                float y = board.Y + board.Height * s.Y;
                float rr = s.Arrow ? r * 0.95f : r;
                float dx = p.X - x, dy = p.Y - y;
                if (dx * dx + dy * dy <= rr * rr) return s;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool overOpt = optionsRect.Contains(e.Location);
            var s = overOpt ? null : HitTest(e.Location);
            if (s != hoverSlot || overOpt != hoverOptions)
            {
                hoverSlot = s;
                hoverOptions = overOpt;
                Cursor = (s != null || overOpt) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverSlot != null || hoverOptions)
            {
                hoverSlot = null;
                hoverOptions = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
            EndClickPress();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (optionsRect.Contains(e.Location))
            {
                if (OptionsClicked != null) OptionsClicked(this, EventArgs.Empty);
                return;
            }
            var s = HitTest(e.Location);
            if (s == null) return;
            var entry = s.Arrow ? FindByInput(s.Inputs) : FindByLabel(s.Label);
            if (entry == null || entry.KeysText.Length == 0) return;
            activeEntry = entry;
            activeStart = Environment.TickCount;
            MousePressed.Add(entry.Input);
            if (entry.Mode == "hold")
            {
                activeIsHold = true;
                KeySender.Press(entry.Mods, entry.Vk);
                KeySender.LastSent = entry.KeysText.ToUpperInvariant();
            }
            else if (entry.Mode == "taphold")
            {
                activeIsHold = false; // decided on release by press duration
            }
            else if (entry.Mode == "doubletap")
            {
                activeIsHold = false;
                if (clickTimer == null)
                {
                    clickTimer = new System.Windows.Forms.Timer();
                    clickTimer.Tick += OnClickTimer;
                }
                if (pendingClick == entry)
                {
                    clickTimer.Stop();
                    pendingClick = null;
                    KeySender.Tap(entry.HoldMods, entry.HoldVk);
                    KeySender.LastSent = entry.HoldKeysText.ToUpperInvariant();
                }
                else
                {
                    OnClickTimer(null, null); // flush any other pending single click
                    pendingClick = entry;
                    clickTimer.Interval = Math.Max(50, entry.HoldMs);
                    clickTimer.Start();
                }
            }
            else
            {
                activeIsHold = false;
                KeySender.Tap(entry.Mods, entry.Vk);
                KeySender.LastSent = entry.KeysText.ToUpperInvariant();
            }
            Invalidate();
        }

        void OnClickTimer(object sender, EventArgs e)
        {
            if (clickTimer != null) clickTimer.Stop();
            if (pendingClick != null)
            {
                KeySender.Tap(pendingClick.Mods, pendingClick.Vk);
                KeySender.LastSent = pendingClick.KeysText.ToUpperInvariant();
                pendingClick = null;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            EndClickPress();
        }

        void EndClickPress()
        {
            if (activeEntry == null) return;
            if (activeIsHold)
            {
                KeySender.Release(activeEntry.Mods, activeEntry.Vk);
            }
            else if (activeEntry.Mode == "taphold")
            {
                if (Environment.TickCount - activeStart >= activeEntry.HoldMs)
                {
                    KeySender.Tap(activeEntry.HoldMods, activeEntry.HoldVk);
                    KeySender.LastSent = activeEntry.HoldKeysText.ToUpperInvariant();
                }
                else
                {
                    KeySender.Tap(activeEntry.Mods, activeEntry.Vk);
                    KeySender.LastSent = activeEntry.KeysText.ToUpperInvariant();
                }
            }
            MousePressed.Remove(activeEntry.Input);
            activeEntry = null;
            Invalidate();
        }

        MapEntry FindByLabel(string label)
        {
            if (Engine == null) return null;
            foreach (var e in Engine.Entries)
                if (e.Label.Length > 0 && string.Equals(e.Label, label, StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }

        MapEntry FindByInput(string[] inputs)
        {
            if (Engine == null) return null;
            foreach (var inp in inputs)
                foreach (var e in Engine.Entries)
                    if (e.Input == inp) return e;
            return null;
        }

        bool IsPressed(string[] inputs)
        {
            if (Engine == null) return false;
            foreach (var inp in inputs)
                if (Engine.Pressed.Contains(inp)) return true;
            return false;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float u = Ui.S;
            var board = BoardRect;
            using (var path = Rounded(board, Ui.X(26)))
            {
                using (var br = new SolidBrush(BoardBg)) g.FillPath(br, path);
                using (var vign = new PathGradientBrush(path))
                {
                    vign.CenterColor = Color.FromArgb(0, 0, 0, 0);
                    vign.SurroundColors = new Color[] { Color.FromArgb(80, 0, 0, 0) };
                    g.FillPath(vign, path);
                }
                using (var pen = new Pen(Green, 3f * u)) g.DrawPath(pen, path);
            }

            float r = board.Width * 0.053f;
            if (r < 16) r = 16;

            // center 4-way cross between the arrow buttons (drawn, stays crisp)
            DrawCross(g,
                board.X + board.Width * glyphX,
                board.Y + board.Height * glyphY,
                r * 0.62f, Color.FromArgb(230, Green));

            foreach (var s in slots)
            {
                float x = board.X + board.Width * s.X;
                float y = board.Y + board.Height * s.Y;
                MapEntry entry = s.Arrow ? FindByInput(s.Inputs) : FindByLabel(s.Label);
                bool pressed = s.Arrow
                    ? IsPressed(s.Inputs)
                    : (entry != null && Engine != null && Engine.Pressed.Contains(entry.Input));
                if (entry != null && MousePressed.Contains(entry.Input)) pressed = true;
                bool mapped = entry != null && entry.KeysText.Length > 0;

                float rr = s.Arrow ? r * 0.95f : r;
                var rect = new RectangleF(x - rr, y - rr, rr * 2, rr * 2);

                // press glow
                if (pressed)
                {
                    for (int i = 3; i >= 1; i--)
                    {
                        float go = i * 5 * u;
                        using (var glow = new SolidBrush(Color.FromArgb(22, Green)))
                            g.FillEllipse(glow, x - rr - go, y - rr - go, (rr + go) * 2, (rr + go) * 2);
                    }
                }

                // bezel ring + drop shadow
                using (var ring = new SolidBrush(BtnRing))
                    g.FillEllipse(ring, rect.X - 5 * u, rect.Y - 3 * u, rect.Width + 10 * u, rect.Height + 11 * u);

                // dome face
                using (var face = new LinearGradientBrush(
                    new RectangleF(rect.X, rect.Y - 2, rect.Width, rect.Height + 4),
                    pressed ? Theme.PressTop : Theme.BtnTop,
                    pressed ? Theme.PressBottom : Theme.BtnBottom,
                    LinearGradientMode.Vertical))
                    g.FillEllipse(face, rect);

                // top highlight
                using (var hl = new LinearGradientBrush(
                    new RectangleF(rect.X, rect.Y, rect.Width, rect.Height * 0.55f + 1),
                    Color.FromArgb(pressed ? 90 : 55, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical))
                    g.FillEllipse(hl, rect.X + rr * 0.22f, rect.Y + rr * 0.10f, rr * 1.56f, rr * 1.0f);

                using (var pen = new Pen(pressed ? Green : Theme.BtnBorder, 1.6f * u))
                    g.DrawEllipse(pen, rect);

                // hover ring (click-to-test affordance)
                if (s == hoverSlot && !pressed && mapped)
                {
                    using (var pen = new Pen(Color.FromArgb(150, Green), 2f * u))
                        g.DrawEllipse(pen, rect.X - 4 * u, rect.Y - 4 * u, rect.Width + 8 * u, rect.Height + 8 * u);
                }

                // arrow glyph (vector triangle, crisp at any size)
                if (s.Arrow)
                {
                    using (var br = new SolidBrush(pressed ? Theme.PressGlyph : Theme.Glyph))
                        DrawTriangle(g, x, y, rr * 0.40f, s.ArrowGlyph, br);
                }

                // label above (from the slot: physical board truth)
                if (!s.Arrow)
                {
                    using (var br = new SolidBrush(mapped ? Green : GreenDim))
                    {
                        var sz = g.MeasureString(s.Label, labelFont);
                        g.DrawString(s.Label, labelFont, br, x - sz.Width / 2f, y - rr - sz.Height - 6 * u);
                    }
                }

                // key caption below (labeled buttons only; arrows are self-evident)
                if (!s.Arrow)
                {
                    string cap = mapped ? entry.CaptionText.ToUpperInvariant() : "--";
                    using (var br = new SolidBrush(pressed ? Green : GreenDim))
                    {
                        var sz = g.MeasureString(cap, keyFont);
                        g.DrawString(cap, keyFont, br, x - sz.Width / 2f, y + rr + 7 * u);
                    }
                }

                // green secondary print (cosmetic, placed as printed on the box)
                if (s.Sub.Length > 0)
                {
                    using (var br = new SolidBrush(Color.FromArgb(150, 235, 100)))
                    {
                        var sz = g.MeasureString(s.Sub, markFont);
                        if (s.Arrow)
                            g.DrawString(s.Sub, markFont, br, x - sz.Width / 2f, y + rr + 5 * u);
                        else if (s.SubLeft)
                            g.DrawString(s.Sub, markFont, br, x - rr - sz.Width - 6 * u, y - sz.Height / 2f);
                        else
                            g.DrawString(s.Sub, markFont, br, x - sz.Width / 2f, y + rr + 23 * u);
                    }
                }
            }

            // stylized OPTIONS chip, top-right. Drawn before the status line so the
            // status knows how much width it may use and never runs underneath it.
            {
                var osz = g.MeasureString("OPTIONS", markFont);
                optionsRect = new RectangleF(board.Right - osz.Width - 34 * u, board.Y + 10 * u,
                    osz.Width + 22 * u, osz.Height + 8 * u);
                using (var cp = RoundedF(optionsRect, Ui.X(12)))
                {
                    using (var bg = new SolidBrush(hoverOptions ? Color.FromArgb(60, Green) : Color.FromArgb(60, 10, 10, 12)))
                        g.FillPath(bg, cp);
                    using (var pen = new Pen(hoverOptions ? Green : Color.FromArgb(190, Green), 1.4f * u))
                        g.DrawPath(pen, cp);
                }
                using (var br = new SolidBrush(hoverOptions ? Green : Color.FromArgb(220, Green)))
                    g.DrawString("OPTIONS", markFont, br, optionsRect.X + 11 * u, optionsRect.Y + 4 * u);
            }

            // status line top-left (replaces the old wordmark), clipped to the space
            // left of the OPTIONS chip
            float sx = board.X + 18 * u;
            float sy = board.Y + 12 * u;
            float statusLimit = optionsRect.X - 12 * u;
            if (connText.Length > 0)
            {
                string t = Ellipsize(g, connText, markFont, statusLimit - sx);
                using (var br = new SolidBrush(connColor))
                    g.DrawString(t, markFont, br, sx, sy);
                sx += g.MeasureString(t, markFont).Width + 18 * u;
            }
            if (infoText.Length > 0 && sx < statusLimit)
            {
                string t = Ellipsize(g, infoText, smallFont, statusLimit - sx);
                using (var br = new SolidBrush(Theme.StatusDim))
                    g.DrawString(t, smallFont, br, sx, sy + 1 * u);
                sx += g.MeasureString(t, smallFont).Width;
            }
            if (battText.Length > 0 && sx + 16 * u < statusLimit)
            {
                sx += 16 * u;
                string t = Ellipsize(g, battText, smallFont, statusLimit - sx);
                using (var br = new SolidBrush(battColor))
                    g.DrawString(t, smallFont, br, sx, sy + 1 * u);
                sx += g.MeasureString(t, smallFont).Width;
            }
        }

        // slim 4-way cross: four lines out of center with proper arrowheads
        // trim text with a trailing ellipsis so it fits the given width
        static string Ellipsize(Graphics g, string text, Font f, float maxWidth)
        {
            if (maxWidth <= 0) return "";
            if (g.MeasureString(text, f).Width <= maxWidth) return text;
            for (int len = text.Length - 1; len > 0; len--)
            {
                string t = text.Substring(0, len).TrimEnd() + "...";
                if (g.MeasureString(t, f).Width <= maxWidth) return t;
            }
            return "";
        }

        static void DrawCross(Graphics g, float cx, float cy, float ext, Color c)
        {
            using (var pen = new Pen(c, ext * 0.20f))
            using (var cap = new AdjustableArrowCap(2.4f, 2.0f, true))
            {
                pen.CustomEndCap = cap;
                pen.StartCap = LineCap.Round;
                g.DrawLine(pen, cx, cy, cx, cy - ext);
                g.DrawLine(pen, cx, cy, cx, cy + ext);
                g.DrawLine(pen, cx, cy, cx - ext, cy);
                g.DrawLine(pen, cx, cy, cx + ext, cy);
            }
        }

        // solid directional triangle for the arrow buttons
        static void DrawTriangle(Graphics g, float cx, float cy, float s, string dir, Brush br)
        {
            PointF[] pts;
            float b = s * 0.92f;   // half base width
            float a = s;           // apex distance
            float k = s * 0.62f;   // base distance
            if (dir == "▲") pts = new PointF[] { new PointF(cx, cy - a), new PointF(cx - b, cy + k), new PointF(cx + b, cy + k) };
            else if (dir == "▼") pts = new PointF[] { new PointF(cx, cy + a), new PointF(cx - b, cy - k), new PointF(cx + b, cy - k) };
            else if (dir == "◀") pts = new PointF[] { new PointF(cx - a, cy), new PointF(cx + k, cy - b), new PointF(cx + k, cy + b) };
            else pts = new PointF[] { new PointF(cx + a, cy), new PointF(cx - k, cy - b), new PointF(cx - k, cy + b) };
            g.FillPolygon(br, pts);
        }

        static GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, rad, rad, 180, 90);
            p.AddArc(r.Right - rad, r.Y, rad, rad, 270, 90);
            p.AddArc(r.Right - rad, r.Bottom - rad, rad, rad, 0, 90);
            p.AddArc(r.X, r.Bottom - rad, rad, rad, 90, 90);
            p.CloseFigure();
            return p;
        }

        static GraphicsPath RoundedF(RectangleF r, int rad)
        {
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, rad, rad, 180, 90);
            p.AddArc(r.Right - rad, r.Y, rad, rad, 270, 90);
            p.AddArc(r.Right - rad, r.Bottom - rad, rad, rad, 0, 90);
            p.AddArc(r.X, r.Bottom - rad, rad, rad, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ---------------- app icon (generated: green button on dark) ----------------

    static class AppIcon
    {
        static Icon icon;

        public static Icon Get()
        {
            if (icon != null) return icon;
            try
            {
                icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(20, 20, 22));
                using (var pen = new Pen(Color.FromArgb(158, 232, 112), 3f))
                    g.DrawEllipse(pen, 3, 3, 26, 26);
                using (var br = new SolidBrush(Color.FromArgb(120, 210, 80)))
                    g.FillEllipse(br, 9, 9, 14, 14);
                icon = Icon.FromHandle(bmp.GetHicon());
            }
            return icon;
        }
    }

    // ---------------- main form ----------------

    class MainForm : Form
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "GolfDeck";
        const string AppKey = @"Software\GolfDeck";
        public const string Version = "2.0.2";

        Engine engine = new Engine();
        BoardPanel board;
        System.Windows.Forms.Timer timer;
        NotifyIcon tray;
        bool closeToTray = true;
        Form optionsDlg; // active options window, if open (message boxes parent here)
        Form MsgOwner { get { return optionsDlg != null && !optionsDlg.IsDisposed ? optionsDlg : (Form)this; } }
        bool exiting;
        bool startMinimized;
        int statusTick = 999;
        int mapErrors;
        bool balloonShown;

        public MainForm(bool minimized)
        {
            startMinimized = minimized;
            Text = "GolfDeck";
            BackColor = Color.FromArgb(13, 13, 14);
            Font = new Font("Segoe UI", Ui.F(12f), FontStyle.Regular, GraphicsUnit.Pixel);
            ClientSize = new Size(Ui.X(700), Ui.X(478));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // restore last window position if it is still on a screen
            int wx = GetSetting("WinX", int.MinValue), wy = GetSetting("WinY", int.MinValue);
            if (wx != int.MinValue && wy != int.MinValue)
            {
                var vs = SystemInformation.VirtualScreen;
                if (wx > vs.Left - 100 && wx < vs.Right - 200 && wy > vs.Top - 20 && wy < vs.Bottom - 200)
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = new Point(wx, wy);
                }
            }

            Icon = AppIcon.Get();

            board = new BoardPanel();
            board.Engine = engine;
            board.Bounds = new Rectangle(0, 0, Ui.X(700), Ui.X(478));
            Controls.Add(board);

            // options: layout + colour choices, opened as a popup window
            if (!Theme.Forced)
            {
                Theme.Trim = GetSetting("TrimColor", 0);
                Theme.Btn = GetSetting("ButtonColor", 0);
                Theme.Board = GetSetting("BoardColor", 0);
            }
            closeToTray = GetSetting("CloseToTray", 1) != 0;
            engine.PreferredId = GetSettingString("InputDevice", "");
            board.OptionsClicked += delegate { ShowOptions(); };
            RefreshTheme();

            // restore saved send mode
            KeySender.Mode = GetSetting("SendMode", 0) == 1 ? SendMode.ScanCodes : SendMode.VirtualKeys;

            // tray
            tray = new NotifyIcon();
            tray.Icon = AppIcon.Get();
            tray.Text = "GolfDeck";
            tray.Visible = true;
            var menu = new ContextMenuStrip();
            var verItem = new ToolStripMenuItem("GolfDeck v" + Version);
            verItem.Enabled = false;
            menu.Items.Add(verItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Show", null, delegate { RestoreFromTray(); });
            menu.Items.Add("Options", null, delegate { RestoreFromTray(); ShowOptions(); });
            menu.Items.Add("Restart as administrator", null, delegate { RestartAsAdmin(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { exiting = true; Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { RestoreFromTray(); };

            LoadMapping(false);

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 10;
            timer.Tick += OnTick;
            timer.Start();
        }

        void SwitchLayout(int idx)
        {
            if (idx != AppState.Layout)
            {
                AppState.Layout = idx;
                SetSetting("Layout", idx);
                board.SetLayout(idx);
                if (MessageBox.Show(MsgOwner,
                    "Load the default GSPro mapping for the " + (idx == 1 ? "V2" : "V1") +
                    " board? This overwrites mapping.txt.",
                    "GolfDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    File.WriteAllText(Config.MappingPath, Config.DefaultFor(idx, engine.Source is HidSource));
                    LoadMapping(false);
                }
            }
            RefreshTheme();
        }

        void RefreshTheme()
        {
            board.Invalidate();
            UpdateStatus();
        }

        // ---- options popup ----

        Label AddOptLabel(Form f, string text, int x, int y, bool header)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(Ui.X(x), Ui.X(y));
            l.ForeColor = header ? Theme.Accent : Color.FromArgb(150, 150, 155);
            f.Controls.Add(l);
            return l;
        }

        // dropdown replacement: themed button opening a dark menu
        Button AddChoice(Form f, string label, string[] options, int selectedIndex, int x, int y, int w, Action<int> onPick)
        {
            var b = new Button();
            b.Tag = selectedIndex;
            b.Bounds = new Rectangle(Ui.X(x), Ui.X(y), Ui.X(w), Ui.X(26));
            b.TextAlign = ContentAlignment.MiddleLeft;
            StyleChoice(b, label, options[selectedIndex]);
            var menu = new ContextMenuStrip();
            f.FormClosed += delegate { menu.Dispose(); };
            menu.Renderer = new DarkMenuRenderer();
            menu.ShowImageMargin = false;
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var mi = new ToolStripMenuItem(options[i]);
                mi.ForeColor = Color.Gainsboro;
                mi.Click += delegate
                {
                    b.Tag = idx;
                    StyleChoice(b, label, options[idx]);
                    onPick(idx);
                };
                menu.Items.Add(mi);
            }
            b.Click += delegate { menu.Show(b, new Point(0, b.Height)); };
            b.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (var br = new SolidBrush(Theme.Accent))
                    pe.Graphics.FillRectangle(br, 2, b.Height - Ui.X(3), b.Width - 4, Ui.X(2));
            };
            f.Controls.Add(b);
            return b;
        }

        void StyleChoice(Button b, string label, string value)
        {
            b.Text = value + "   ▾";
            b.FlatStyle = FlatStyle.Flat;
            b.ForeColor = Color.Gainsboro;
            b.BackColor = Color.FromArgb(19, 19, 21);
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 33, 36);
        }

        // checkbox replacement: themed toggle
        Button AddToggle(Form f, string text, bool state, int x, int y, int w, Action<bool> onChange)
        {
            var b = new Button();
            b.Tag = state;
            b.Bounds = new Rectangle(Ui.X(x), Ui.X(y), Ui.X(w), Ui.X(26));
            StyleToggle(b, text);
            b.Click += delegate
            {
                b.Tag = !(bool)b.Tag;
                StyleToggle(b, text);
                onChange((bool)b.Tag);
            };
            f.Controls.Add(b);
            return b;
        }

        void StyleToggle(Button b, string text)
        {
            bool on = (bool)b.Tag;
            b.FlatStyle = FlatStyle.Flat;
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Text = (on ? "◉  " : "○  ") + text;
            b.BackColor = Color.FromArgb(19, 19, 21);
            b.ForeColor = on ? Theme.Accent : Color.Gainsboro;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 33, 36);
        }

        Button AddOptButton(Form f, string text, int x, int y, int w, int h)
        {
            var b = new Button();
            b.Text = text;
            b.Bounds = new Rectangle(Ui.X(x), Ui.X(y), Ui.X(w), Ui.X(h));
            b.FlatStyle = FlatStyle.Flat;
            b.ForeColor = Color.Gainsboro;
            b.BackColor = Color.FromArgb(36, 37, 40);
            b.FlatAppearance.BorderColor = Color.FromArgb(64, 66, 70);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 50, 54);
            f.Controls.Add(b);
            return b;
        }

        public Form BuildOptionsDialog()
        {
            var dlg = new Form();
            dlg.Text = "GolfDeck options";
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MaximizeBox = false;
            dlg.MinimizeBox = false;
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.BackColor = Color.FromArgb(19, 19, 21);
            dlg.Font = Font;
            dlg.Icon = AppIcon.Get();
            dlg.ClientSize = new Size(Ui.X(420), Ui.X(492));

            AddOptLabel(dlg, "Board layout", 16, 12, true);
            AddChoice(dlg, "Layout",
                new string[] { "V1  (PUTT top-left)", "V2  (SCORECARD top-left)" },
                AppState.Layout, 16, 34, 188,
                delegate(int idx) { if (idx != AppState.Layout) SwitchLayout(idx); });

            AddOptLabel(dlg, "Key send", 220, 12, true);
            AddChoice(dlg, "Key send",
                new string[] { "Virtual keys", "Scancodes" },
                KeySender.Mode == SendMode.ScanCodes ? 1 : 0, 220, 34, 184,
                delegate(int idx)
                {
                    KeySender.Mode = idx == 1 ? SendMode.ScanCodes : SendMode.VirtualKeys;
                    SetSetting("SendMode", idx);
                });

            // input device: Auto, or a specific pad / generic joystick
            AddOptLabel(dlg, "Input device", 16, 72, true);
            var devices = InputSources.Enumerate();
            var devLabels = new List<string>();
            var devIds = new List<string>();
            devLabels.Add("Auto  (Xbox pad if present)");
            devIds.Add("");
            foreach (var d in devices) { devLabels.Add(d.Label); devIds.Add(d.Id); }
            if (engine.PreferredId.Length > 0 && !devIds.Contains(engine.PreferredId))
            {
                devLabels.Add(engine.PreferredId + "  (not connected)");
                devIds.Add(engine.PreferredId);
            }
            int devSel = Math.Max(0, devIds.IndexOf(engine.PreferredId));
            AddChoice(dlg, "Device", devLabels.ToArray(), devSel, 16, 94, 264,
                delegate(int idx)
                {
                    engine.PreferredId = devIds[idx];
                    SetSettingString("InputDevice", engine.PreferredId);
                    engine.Source = null; // force a rescan onto the chosen device
                });
            var bMon = AddOptButton(dlg, "Input monitor", 288, 94, 116, 26);
            bMon.Click += delegate { ShowInputMonitor(dlg); };

            AddOptLabel(dlg, "Colours", 16, 132, true);
            AddOptLabel(dlg, "Board", 16, 154, false);
            AddOptLabel(dlg, "Buttons", 148, 154, false);
            AddOptLabel(dlg, "Letters && trim", 280, 154, false);
            Button cbBoard = null, cbBtn = null, cbTrim = null;
            string[] boards = { "Black", "Green Jacket", "Red", "Blue" };
            string[] btncols = { "Black", "White", "Yellow", "Red" };
            string[] trims = { "Green", "White", "Yellow" };
            Action applyColors = delegate
            {
                Theme.Board = (int)cbBoard.Tag;
                Theme.Btn = (int)cbBtn.Tag;
                Theme.Trim = (int)cbTrim.Tag;
                SetSetting("BoardColor", Theme.Board);
                SetSetting("ButtonColor", Theme.Btn);
                SetSetting("TrimColor", Theme.Trim);
                RefreshTheme();
            };
            cbBoard = AddChoice(dlg, "Board", boards, Theme.Board, 16, 174, 120, delegate(int i) { applyColors(); });
            cbBtn = AddChoice(dlg, "Buttons", btncols, Theme.Btn, 148, 174, 120, delegate(int i) { applyColors(); });
            cbTrim = AddChoice(dlg, "Letters", trims, Theme.Trim, 280, 174, 124, delegate(int i) { applyColors(); });

            AddOptLabel(dlg, "Edition presets", 16, 212, true);
            string[] pnames = { "Original", "Green Jacket", "Red && White", "Red, White && Blue" };
            int[][] pvals = { new int[] { 0, 0, 0 }, new int[] { 1, 1, 2 }, new int[] { 2, 1, 1 }, new int[] { 3, 1, 3 } };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var pb = AddOptButton(dlg, pnames[i], 16 + (i % 2) * 196, 234 + (i / 2) * 34, 188, 28);
                pb.Click += delegate
                {
                    cbBoard.Tag = pvals[idx][0];
                    cbTrim.Tag = pvals[idx][1];
                    cbBtn.Tag = pvals[idx][2];
                    StyleChoice(cbBoard, "Board", boards[pvals[idx][0]]);
                    StyleChoice(cbTrim, "Letters", trims[pvals[idx][1]]);
                    StyleChoice(cbBtn, "Buttons", btncols[pvals[idx][2]]);
                    applyColors();
                };
            }

            AddOptLabel(dlg, "Behaviour", 16, 310, true);
            AddToggle(dlg, "Start with Windows", GetAutostart(), 16, 332, 388,
                delegate(bool v) { SetAutostart(v); });
            AddToggle(dlg, "Close button hides to the tray instead of exiting", closeToTray, 16, 364, 388,
                delegate(bool v) { closeToTray = v; SetSetting("CloseToTray", v ? 1 : 0); });

            var lblStats = AddOptLabel(dlg, "Keys sent: " + KeySender.KeysSent, 16, 400, false);
            var statTimer = new System.Windows.Forms.Timer();
            statTimer.Interval = 500;
            statTimer.Tick += delegate
            {
                lblStats.Text = "Keys sent: " + KeySender.KeysSent
                    + (KeySender.LastSent.Length > 0 ? "      last: " + KeySender.LastSent : "");
            };
            statTimer.Start();
            dlg.FormClosed += delegate { statTimer.Stop(); statTimer.Dispose(); };

            var bEdit = AddOptButton(dlg, "Edit mapping", 16, 424, 120, 26);
            bEdit.Click += delegate { Process.Start("notepad.exe", "\"" + Config.MappingPath + "\""); };
            var bReload = AddOptButton(dlg, "Reload mapping", 144, 424, 126, 26);
            bReload.Click += delegate { LoadMapping(true); };
            var bFolder = AddOptButton(dlg, "Open folder", 278, 424, 126, 26);
            bFolder.Click += delegate { Process.Start("explorer.exe", "\"" + Config.Dir + "\""); };

            var bUpd = AddOptButton(dlg, "Check for updates", 16, 456, 150, 26);
            bUpd.Click += delegate { StartUpdateCheck(true); };
            var bDefaults = AddOptButton(dlg, "Load defaults", 174, 456, 116, 26);
            bDefaults.Click += delegate { LoadDefaultMapping(dlg); };
            var bClose = AddOptButton(dlg, "Close", 346, 456, 58, 26);
            bClose.Click += delegate { dlg.Close(); };
            bClose.DialogResult = DialogResult.Cancel;
            dlg.CancelButton = bClose; // Esc closes

            return dlg;
        }

        // live view of what the active device is reporting: the way to discover
        // button numbers on a generic joystick without any external tool
        void ShowInputMonitor(Form parent)
        {
            using (var dlg = BuildInputMonitor())
                dlg.ShowDialog(parent);
        }

        public Form BuildInputMonitor()
        {
            {
                var dlg = new Form();
                dlg.Text = "Input monitor";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.BackColor = Color.FromArgb(19, 19, 21);
                dlg.Font = Font;
                dlg.Icon = AppIcon.Get();
                dlg.ClientSize = new Size(Ui.X(430), Ui.X(210));

                var lblDev = AddOptLabel(dlg, "", 16, 12, true);
                var lblHint = AddOptLabel(dlg,
                    "Press each button on the box and note the number it reports,\r\nthen use those numbers in mapping.txt.", 16, 34, false);
                lblHint.MaximumSize = new Size(Ui.X(400), 0);

                var lblState = new Label();
                lblState.Bounds = new Rectangle(Ui.X(16), Ui.X(78), Ui.X(398), Ui.X(88));
                lblState.ForeColor = Color.Gainsboro;
                lblState.Font = new Font("Consolas", Ui.F(12f), FontStyle.Regular, GraphicsUnit.Pixel);
                dlg.Controls.Add(lblState);

                var bClose = AddOptButton(dlg, "Close", 346, 176, 58, 26);
                bClose.Click += delegate { dlg.Close(); };
                bClose.DialogResult = DialogResult.Cancel;
                dlg.CancelButton = bClose;

                EventHandler refresh = delegate
                {
                    var src = engine.Source;
                    if (src == null)
                    {
                        lblDev.Text = "No device connected";
                        lblState.Text = "";
                    }
                    else
                    {
                        lblDev.Text = src.Label;
                        lblState.Text = src.Describe();
                    }
                };
                var t = new System.Windows.Forms.Timer();
                t.Interval = 60;
                t.Tick += refresh;
                t.Start();
                dlg.FormClosed += delegate { t.Stop(); t.Dispose(); };
                refresh(null, EventArgs.Empty);
                return dlg;
            }
        }

        // rewrite mapping.txt with the defaults matching the current board and device
        void LoadDefaultMapping(Form parent)
        {
            bool generic = engine.Source is HidSource;
            string what = (AppState.Layout == 1 ? "V2" : "V1") + (generic ? " generic joystick" : " Xbox controller");
            if (MessageBox.Show(parent,
                "Replace mapping.txt with the default " + what + " mapping?\r\n\r\nYour current mapping will be overwritten.",
                "GolfDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            File.WriteAllText(Config.MappingPath, Config.DefaultFor(AppState.Layout, generic));
            LoadMapping(false);
        }

        void ShowOptions()
        {
            using (var dlg = BuildOptionsDialog())
            {
                optionsDlg = dlg;
                try { dlg.ShowDialog(this); }
                finally { optionsDlg = null; }
            }
        }

        public void DemoPress(string csv)
        {
            timer.Stop(); // freeze state so screenshot shows the demo presses
            foreach (var s in csv.Split(','))
                engine.Pressed.Add(s.Trim().ToUpperInvariant());
            UpdateStatus();
            board.Invalidate();
        }

        void LoadMapping(bool interactive)
        {
            engine.ReleaseAll();
            List<string> errors;
            engine.Entries = Config.Load(engine, out errors);
            mapErrors = errors.Count;
            engine.RecheckMapping();
            board.Invalidate();
            UpdateStatus();
            if (errors.Count > 0 && interactive)
                MessageBox.Show(MsgOwner, string.Join("\r\n", errors.ToArray()), "mapping.txt problems",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void OnTick(object sender, EventArgs e)
        {
            bool changed = engine.Poll();
            if (changed && Visible) board.Invalidate();

            statusTick++;
            if (statusTick >= 50) // every ~0.5s
            {
                statusTick = 0;
                UpdateStatus();
                OfferMatchingMapping();
            }
        }

        // Swapping a wired (generic) box for a wireless (Xbox) one, or the reverse,
        // leaves a mapping whose input names the new device cannot produce. Offer the
        // matching defaults once per device rather than silently doing nothing.
        void OfferMatchingMapping()
        {
            if (AppState.NoUpdateCheck || optionsDlg != null) return;
            if (!engine.MappingMismatch || engine.Source == null) return;
            if (mismatchPromptedFor == engine.Source.Id) return;
            mismatchPromptedFor = engine.Source.Id;

            bool generic = engine.Source is HidSource;
            string kind = generic ? "generic USB joystick" : "Xbox-compatible controller";
            string mapKind = generic ? "an Xbox controller" : "a generic joystick";

            // Running in the tray means a round may be in progress: a modal dialog
            // would steal focus from GSPro. Notify quietly and let the user come to it.
            if (!Visible)
            {
                if (tray != null)
                    tray.ShowBalloonTip(4000, "GolfDeck",
                        "A " + kind + " was connected, but the current mapping is for " + mapKind +
                        ". Open GolfDeck and use Options > Load defaults.", ToolTipIcon.Warning);
                return;
            }

            if (MessageBox.Show(MsgOwner,
                "Connected device: " + engine.Source.Label + "\r\n\r\n" +
                "This is a " + kind + ", but the current mapping is written for " + mapKind +
                ", so none of the buttons will work.\r\n\r\n" +
                "Load the default " + (AppState.Layout == 1 ? "V2" : "V1") + " mapping for this device?",
                "GolfDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            File.WriteAllText(Config.MappingPath, Config.DefaultFor(AppState.Layout, generic));
            LoadMapping(false);
        }

        string mismatchPromptedFor = "";

        void UpdateStatus()
        {
            if (board == null) return;
            string conn;
            Color col;
            if (engine.Connected)
            {
                conn = "●  " + engine.SourceShortLabel.ToUpperInvariant();
                col = Theme.Accent;
            }
            else
            {
                conn = "●  CONTROLLER NOT FOUND";
                col = Theme.StatusBad;
            }
            string info = "admin: " + (IsAdmin() ? "yes" : "no");
            if (KeySender.LastSent.Length > 0) info += "      last: " + KeySender.LastSent;
            if (mapErrors > 0) info = "mapping errors: " + mapErrors + " (fix in Options)      " + info;
            if (engine.MappingMismatch)
                info = "mapping does not match this device (Options > Load defaults)      " + info;

            // battery: only meaningful for wireless pads (alkaline / nimh)
            string batt = "";
            Color battCol = Theme.StatusDim;
            byte bType = engine.BatteryType;
            byte bLevel = engine.BatteryLevel;
            if (FakeBattery >= 0) { bType = 3; bLevel = (byte)FakeBattery; } // render testing
            if ((engine.Connected || FakeBattery >= 0) && (bType == 2 || bType == 3))
            {
                string[] lvls = { "empty", "LOW", "medium", "full" };
                int lv = bLevel <= 3 ? bLevel : 3;
                batt = "battery: " + lvls[lv];
                if (lv == 1) battCol = Color.FromArgb(250, 200, 80);
                else if (lv == 0) battCol = Theme.StatusBad;
                if (lv <= 1)
                {
                    if (!lowBatteryWarned && tray != null)
                    {
                        lowBatteryWarned = true;
                        tray.ShowBalloonTip(3000, "GolfDeck",
                            "Controller battery is " + lvls[lv] + ".", ToolTipIcon.Warning);
                    }
                }
                else lowBatteryWarned = false;
            }

            board.SetStatus(conn, col, info, batt, battCol);
            if (tray != null)
                tray.Text = (engine.Connected
                    ? "GolfDeck - controller connected"
                    : "GolfDeck - no controller")
                    + (batt.Length > 0 ? " - " + batt : "");
        }

        public static int FakeBattery = -1; // --battery N screenshot override
        bool lowBatteryWarned;

        static bool IsAdmin()
        {
            try
            {
                var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        void RestartAsAdmin()
        {
            if (IsAdmin()) { MessageBox.Show(MsgOwner, "Already running as administrator."); return; }
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                exiting = true;
                Close();
            }
            catch { /* UAC declined */ }
        }

        void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (startMinimized) Hide();
            if (!AppState.NoUpdateCheck && DateTime.UtcNow >= GetSnoozeUntil())
                StartUpdateCheck(false);
        }

        // ---- update check ----

        DateTime GetSnoozeUntil()
        {
            string s = GetSettingString("UpdateSnooze", "");
            DateTime dt;
            if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out dt))
                return dt;
            return DateTime.MinValue;
        }

        void StartUpdateCheck(bool manual)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null;
                bool failed = false;
                try { info = Updater.FetchLatest(); }
                catch { failed = true; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate { ShowUpdateResult(info, failed, manual); });
                }
                catch { /* form gone */ }
            });
        }

        void ShowUpdateResult(UpdateInfo info, bool failed, bool manual)
        {
            if (failed || info == null)
            {
                if (manual)
                    MessageBox.Show(MsgOwner, "Update check failed. No network, or the release feed is unavailable.",
                        "GolfDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Updater.IsNewer(info.Tag, Version))
            {
                if (manual)
                    MessageBox.Show(MsgOwner, "You are on the latest version (v" + Version + ").",
                        "GolfDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string summary = (info.Body ?? "").Trim();
            if (summary.Length > 600) summary = summary.Substring(0, 600) + "...";
            if (summary.Length == 0) summary = "(no release notes)";

            if (!Visible) RestoreFromTray();
            var res = MessageBox.Show(MsgOwner,
                "Update available: " + info.Tag + " (you have v" + Version + ")\r\n\r\n" +
                summary + "\r\n\r\nWould you like to update?",
                "GolfDeck update", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (res == DialogResult.Yes)
            {
                try
                {
                    Updater.Apply(info);
                    exiting = true;
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(MsgOwner, "Update failed: " + ex.Message +
                        "\r\n\r\nDownload manually from github.com/SenkoeUwU/golfdeck/releases",
                        "GolfDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                SetSettingString("UpdateSnooze", DateTime.UtcNow.AddDays(7).ToString("o"));
            }
        }

        static string GetSettingString(string name, string def)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(AppKey))
            {
                if (k == null) return def;
                object v = k.GetValue(name);
                return v is string ? (string)v : def;
            }
        }

        static void SetSettingString(string name, string val)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(AppKey))
                k.SetValue(name, val);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing && closeToTray)
            {
                e.Cancel = true;
                Hide();
                if (!balloonShown)
                {
                    balloonShown = true;
                    tray.ShowBalloonTip(2000, "GolfDeck", "Still running in the tray. Right-click the icon to exit.", ToolTipIcon.Info);
                }
                return;
            }
            if (WindowState == FormWindowState.Normal && Location.X > -2000)
            {
                SetSetting("WinX", Location.X);
                SetSetting("WinY", Location.Y);
            }
            engine.ReleaseAll();
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        // ---- settings / autostart ----

        static bool GetAutostart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunValue) != null;
        }

        static void SetAutostart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\" --minimized");
                else k.DeleteValue(RunValue, false);
            }
        }

        public static int GetSetting(string name, int def)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(AppKey))
            {
                if (k == null) return def;
                object v = k.GetValue(name);
                return v is int ? (int)v : def;
            }
        }

        public static void SetSetting(string name, int val)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(AppKey))
                k.SetValue(name, val);
        }
    }

    // ---------------- entry point ----------------

    static class Program
    {
        static int PromptLayout()
        {
            int[] choice = { 0 };
            using (var dlg = BuildLayoutPrompt(choice))
            {
                dlg.ShowDialog();
                return choice[0];
            }
        }

        public static Form BuildLayoutPrompt(int[] choiceOut)
        {
            {
                var dlg = new Form();
                dlg.Text = "GolfDeck";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.Font = new Font("Segoe UI", Ui.F(12f), FontStyle.Regular, GraphicsUnit.Pixel);
                dlg.ClientSize = new Size(Ui.X(430), Ui.X(184));
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.BackColor = Color.FromArgb(19, 19, 21);
                dlg.Icon = AppIcon.Get();

                var lbl = new Label();
                lbl.Text = "Which control box layout do you have?\r\n(You can change this later under Options.)";
                lbl.ForeColor = Color.Gainsboro;
                lbl.Bounds = new Rectangle(Ui.X(20), Ui.X(15), Ui.X(390), Ui.X(40));
                dlg.Controls.Add(lbl);

                // name what is actually plugged in, so the choice is informed
                var found = InputSources.Enumerate();
                var det = new Label();
                det.Text = found.Count > 0
                    ? "Detected: " + found[0].Label
                    : "No controller detected yet - you can still choose.";
                det.ForeColor = found.Count > 0 ? Color.FromArgb(158, 232, 112) : Color.FromArgb(220, 95, 90);
                det.AutoSize = true;
                det.Location = new Point(Ui.X(20), Ui.X(52));
                dlg.Controls.Add(det);

                Button b1 = new Button(), b2 = new Button();
                b1.Text = "V1\r\nPUTT top-left";
                b2.Text = "V2\r\nSCORECARD top-left";
                Button[] bs = { b1, b2 };
                for (int i = 0; i < 2; i++)
                {
                    int idx = i;
                    bs[i].Bounds = new Rectangle(Ui.X(20 + i * 200), Ui.X(90), Ui.X(190), Ui.X(70));
                    bs[i].FlatStyle = FlatStyle.Flat;
                    bs[i].ForeColor = Color.Gainsboro;
                    bs[i].BackColor = Color.FromArgb(36, 37, 40);
                    bs[i].FlatAppearance.BorderColor = Color.FromArgb(100, 160, 80);
                    bs[i].Click += delegate { choiceOut[0] = idx; dlg.DialogResult = DialogResult.OK; };
                    dlg.Controls.Add(bs[i]);
                }
                return dlg;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool minimized = false;
            // Ui.Init runs after arg parsing (below) so --scale can override
            string screenshot = null;
            string press = null;
            int layoutArg = -1;
            float forcedScale = 0f;
            bool shotOptions = false;
            bool shotMonitor = false;
            bool shotPrompt = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--minimized") minimized = true;
                else if (args[i] == "--shotoptions") shotOptions = true;
                else if (args[i] == "--shotmonitor") shotMonitor = true;
                else if (args[i] == "--shotprompt") shotPrompt = true;
                else if (args[i] == "--screenshot" && i + 1 < args.Length) screenshot = args[++i];
                else if (args[i] == "--press" && i + 1 < args.Length) press = args[++i];
                else if (args[i] == "--scale" && i + 1 < args.Length)
                    float.TryParse(args[++i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out forcedScale);
                else if (args[i] == "--battery" && i + 1 < args.Length)
                {
                    int bv;
                    if (int.TryParse(args[++i], out bv) && bv >= 0 && bv <= 3) MainForm.FakeBattery = bv;
                }
                else if (args[i] == "--layout" && i + 1 < args.Length)
                {
                    int lv;
                    if (int.TryParse(args[++i], out lv)) layoutArg = lv == 2 ? 1 : 0;
                }
                else if (args[i] == "--theme" && i + 1 < args.Length)
                {
                    string[] tb = args[++i].Split(',');
                    int tv, bv, ov;
                    if (tb.Length >= 2 && int.TryParse(tb[0], out tv) && int.TryParse(tb[1], out bv))
                    {
                        Theme.Trim = tv;
                        Theme.Btn = bv;
                        if (tb.Length >= 3 && int.TryParse(tb[2], out ov)) Theme.Board = ov;
                        Theme.Forced = true;
                    }
                }
            }

            // screenshot runs are transient and skip the single-instance guard
            Mutex mutex = null;
            if (screenshot == null)
            {
                bool created;
                mutex = new Mutex(true, "GolfDeck_SingleInstance", out created);
                if (!created)
                {
                    MessageBox.Show("GolfDeck is already running (check the tray).", "GolfDeck");
                    return;
                }
            }

            Ui.Init(forcedScale);

            // resolve board layout: arg > saved > first-launch prompt
            int layout = layoutArg;
            if (layout < 0)
            {
                layout = MainForm.GetSetting("Layout", -1);
                if (layout < 0)
                {
                    if (screenshot != null) layout = 0;
                    else
                    {
                        layout = PromptLayout();
                        MainForm.SetSetting("Layout", layout);
                    }
                }
            }
            AppState.Layout = layout;
            AppState.NoUpdateCheck = screenshot != null;

            Config.MigrateOldMapping();
            if (!File.Exists(Config.MappingPath))
            {
                // if the only thing plugged in is a generic joystick, start from
                // the generic template rather than the Xbox one
                bool anyXInput = false;
                for (int i = 0; i < 4 && !anyXInput; i++)
                {
                    XINPUT_STATE probe;
                    anyXInput = XInput.GetState(i, out probe) == 0;
                }
                bool generic = !anyXInput && InputSources.OpenAuto() is HidSource;
                File.WriteAllText(Config.MappingPath, Config.DefaultFor(layout, generic));
            }

            var form = new MainForm(minimized && screenshot == null);

            if (screenshot != null)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.Show();
                if (press != null) form.DemoPress(press);
                Application.DoEvents();
                Form target = form;
                if (shotOptions || shotMonitor || shotPrompt)
                {
                    target = shotPrompt ? BuildLayoutPrompt(new int[1])
                        : shotMonitor ? form.BuildInputMonitor() : form.BuildOptionsDialog();
                    target.StartPosition = FormStartPosition.Manual;
                    target.Location = new Point(-4000, -4000);
                    target.Show();
                    Application.DoEvents();
                }
                using (var full = new Bitmap(target.Width, target.Height))
                {
                    target.DrawToBitmap(full, new Rectangle(0, 0, target.Width, target.Height));
                    // crop to the client area: no window chrome in the capture
                    Point co = target.PointToScreen(Point.Empty);
                    var client = new Rectangle(co.X - target.Location.X, co.Y - target.Location.Y,
                        target.ClientSize.Width, target.ClientSize.Height);
                    using (var crop = full.Clone(client, full.PixelFormat))
                        crop.Save(screenshot, System.Drawing.Imaging.ImageFormat.Png);
                }
                GC.KeepAlive(mutex);
                return;
            }

            Application.Run(form);
            GC.KeepAlive(mutex);
        }
    }
}
