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

    static class XInput
    {
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern int GetState14(int idx, out XINPUT_STATE state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern int GetState910(int idx, out XINPUT_STATE state);

        static int mode = 0; // 0 = try 1_4, 2 = fall back to 9_1_0

        public static int GetState(int idx, out XINPUT_STATE state)
        {
            if (mode != 2)
            {
                try { return GetState14(idx, out state); }
                catch (DllNotFoundException) { mode = 2; }
            }
            return GetState910(idx, out state);
        }
    }

    // ---------------- SendInput ----------------

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
                    File.Move(old, MappingPath);
            }
            catch { /* keep the old file in place on any failure */ }
        }

        public static string DefaultFor(int layout)
        {
            return layout == 1 ? DefaultMappingV2 : DefaultMappingV1;
        }

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

                if (!validInputs.Contains(leftUp))
                {
                    errors.Add("line " + (ln + 1) + ": unknown input '" + left + "'");
                    continue;
                }
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

    // ---------------- engine ----------------

    class Engine
    {
        public List<MapEntry> Entries = new List<MapEntry>();
        public HashSet<string> Pressed = new HashSet<string>();
        public bool Connected;
        public int ControllerIndex = -1;
        public int StickThreshold = 12000;  // raw, of 32767
        public int TriggerThreshold = 64;   // raw, of 255

        public bool Poll()
        {
            XINPUT_STATE st = new XINPUT_STATE();
            bool got = false;
            if (ControllerIndex >= 0)
                got = XInput.GetState(ControllerIndex, out st) == 0;
            if (!got)
            {
                ControllerIndex = -1;
                for (int i = 0; i < 4; i++)
                {
                    if (XInput.GetState(i, out st) == 0) { ControllerIndex = i; got = true; break; }
                }
            }

            bool changed = false;
            if (got != Connected) { Connected = got; changed = true; }

            if (!got)
            {
                ReleaseAll();
                foreach (var e in Entries) e.WasDown = false;
                if (Pressed.Count > 0) { Pressed.Clear(); changed = true; }
                return changed;
            }

            foreach (var e in Entries)
            {
                bool down = ReadInput(e.Input, ref st);
                if (down != e.WasDown) changed = true;
                Step(e, down);
                e.WasDown = down;
                if (down) Pressed.Add(e.Input);
                else Pressed.Remove(e.Input);
            }
            return changed;
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
                    if (e.PressStart != 0 && Environment.TickCount - e.PressStart <= e.HoldMs)
                    {
                        KeySender.Tap(e.HoldMods, e.HoldVk);
                        KeySender.LastSent = e.HoldKeysText.ToUpperInvariant();
                        e.PressStart = 0;
                    }
                    else
                    {
                        e.PressStart = Environment.TickCount;
                    }
                }
                else if (e.PressStart != 0 && Environment.TickCount - e.PressStart > e.HoldMs)
                {
                    KeySender.Tap(e.Mods, e.Vk);
                    KeySender.LastSent = e.KeysText.ToUpperInvariant();
                    e.PressStart = 0;
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

        bool ReadInput(string input, ref XINPUT_STATE st)
        {
            ushort b = st.Gamepad.wButtons;
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
                case "LT": return st.Gamepad.bLeftTrigger > TriggerThreshold;
                case "RT": return st.Gamepad.bRightTrigger > TriggerThreshold;
                case "LS_UP": return st.Gamepad.sThumbLY > StickThreshold;
                case "LS_DOWN": return st.Gamepad.sThumbLY < -StickThreshold;
                case "LS_LEFT": return st.Gamepad.sThumbLX < -StickThreshold;
                case "LS_RIGHT": return st.Gamepad.sThumbLX > StickThreshold;
                case "RS_UP": return st.Gamepad.sThumbRY > StickThreshold;
                case "RS_DOWN": return st.Gamepad.sThumbRY < -StickThreshold;
                case "RS_LEFT": return st.Gamepad.sThumbRX < -StickThreshold;
                case "RS_RIGHT": return st.Gamepad.sThumbRX > StickThreshold;
            }
            return false;
        }
    }

    // ---------------- app state ----------------

    static class AppState
    {
        public static int Layout = 0; // 0 = V1 board, 1 = V2 board
        public static bool NoUpdateCheck; // screenshot/test runs skip the launch check
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
                AddArrow(0.720f, 0.513f, "▲", "LS_UP", "DPAD_UP");
                AddArrow(0.622f, 0.660f, "◀", "LS_LEFT", "DPAD_LEFT");
                // WAKE is physically the Menu button doubling as aim right
                var wake = new Slot(); wake.X = 0.818f; wake.Y = 0.660f; wake.Arrow = true;
                wake.ArrowGlyph = "▶"; wake.Sub = "WAKE";
                wake.Inputs = new string[] { "MENU", "LS_RIGHT", "DPAD_RIGHT" };
                slots.Add(wake);
                AddArrow(0.720f, 0.807f, "▼", "LS_DOWN", "DPAD_DOWN");
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
                AddArrow(0.500f, 0.518f, "▲", "LS_UP", "DPAD_UP");
                AddArrow(0.402f, 0.665f, "◀", "LS_LEFT", "DPAD_LEFT");
                AddArrow(0.598f, 0.665f, "▶", "LS_RIGHT", "DPAD_RIGHT");
                AddArrow(0.500f, 0.812f, "▼", "LS_DOWN", "DPAD_DOWN");
            }
            Invalidate();
        }

        void AddBtn(float x, float y, string label, string sub = "", bool subLeft = false)
        {
            var s = new Slot();
            s.X = x; s.Y = y; s.Label = label; s.Sub = sub; s.SubLeft = subLeft;
            slots.Add(s);
        }

        void AddArrow(float x, float y, string glyph, params string[] inputs)
        {
            var s = new Slot(); s.X = x; s.Y = y; s.Arrow = true; s.ArrowGlyph = glyph; s.Inputs = inputs; slots.Add(s);
        }

        // ---- click-to-test: mouse press on a drawn button sends its mapped key ----

        public HashSet<string> MousePressed = new HashSet<string>();
        Slot hoverSlot;
        MapEntry activeEntry;
        bool activeIsHold;
        int activeStart;
        System.Windows.Forms.Timer clickTimer;
        MapEntry pendingClick;

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
            var s = HitTest(e.Location);
            if (s != hoverSlot)
            {
                hoverSlot = s;
                Cursor = s != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverSlot != null) { hoverSlot = null; Cursor = Cursors.Default; Invalidate(); }
            EndClickPress();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
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

            var used = new List<MapEntry>();

            foreach (var s in slots)
            {
                float x = board.X + board.Width * s.X;
                float y = board.Y + board.Height * s.Y;
                MapEntry entry;
                if (s.Arrow)
                {
                    // claim every entry matching any of the slot's inputs so
                    // alternates (stick + button aim) don't fall out as chips
                    entry = null;
                    foreach (var inp in s.Inputs)
                        foreach (var en in Engine != null ? Engine.Entries : new List<MapEntry>())
                            if (en.Input == inp)
                            {
                                if (entry == null) entry = en;
                                if (!used.Contains(en)) used.Add(en);
                            }
                }
                else
                {
                    entry = FindByLabel(s.Label);
                    if (entry != null && !used.Contains(entry)) used.Add(entry);
                }
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

            // wordmark top-left
            using (var br = new SolidBrush(Color.FromArgb(130, GreenDim)))
                g.DrawString("GOLFDECK", markFont, br, board.X + 18 * u, board.Y + 12 * u);


            // entries not attached to any board slot as chips along the top edge
            if (Engine != null)
            {
                float tx = board.Right - 16 * u;
                foreach (var e in Engine.Entries)
                {
                    if (used.Contains(e)) continue;
                    string txt = e.Input + "  =  " + e.CaptionText + (e.Mode == "repeat" ? "  (repeat)" : "");
                    bool on = Engine.Pressed.Contains(e.Input);
                    var sz = g.MeasureString(txt, smallFont);
                    var chip = new RectangleF(tx - sz.Width - 18 * u, board.Y + 12 * u, sz.Width + 18 * u, sz.Height + 7 * u);
                    using (var cp = RoundedF(chip, Ui.X(10)))
                    {
                        using (var bg = new SolidBrush(on ? Color.FromArgb(70, 158, 232, 112) : Color.FromArgb(34, 35, 37)))
                            g.FillPath(bg, cp);
                        using (var pen = new Pen(on ? Green : Color.FromArgb(58, 60, 62), 1f * u))
                            g.DrawPath(pen, cp);
                    }
                    using (var br = new SolidBrush(on ? Green : GreenDim))
                        g.DrawString(txt, smallFont, br, chip.X + 9 * u, chip.Y + 3.5f * u);
                    tx = chip.X - 8 * u;
                }
            }
        }

        // slim 4-way cross: four lines out of center with proper arrowheads
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
        public const string Version = "1.7";

        Engine engine = new Engine();
        BoardPanel board;
        System.Windows.Forms.Timer timer;
        NotifyIcon tray;
        CheckBox chkAutostart;
        RadioButton rbVk, rbScan;
        Label lblConn, lblInfo;
        bool closeToTray = true;
        bool exiting;
        bool startMinimized;
        bool suppressModeEvents;
        int statusTick = 999;
        int mapErrors;
        bool balloonShown;

        public MainForm(bool minimized)
        {
            startMinimized = minimized;
            Text = "GolfDeck";
            BackColor = Color.FromArgb(13, 13, 14);
            Font = new Font("Segoe UI", Ui.F(12f), FontStyle.Regular, GraphicsUnit.Pixel);
            ClientSize = new Size(Ui.X(700), Ui.X(590));
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

            var bottom = new Panel();
            bottom.Bounds = new Rectangle(0, Ui.X(478), Ui.X(700), Ui.X(112));
            bottom.BackColor = Color.FromArgb(19, 19, 21);
            bottom.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(44, 46, 48), 1f))
                    e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };
            Controls.Add(bottom);

            chkAutostart = new CheckBox();
            chkAutostart.Text = "Start with Windows";
            chkAutostart.FlatStyle = FlatStyle.Flat;
            chkAutostart.ForeColor = Color.Gainsboro;
            chkAutostart.Location = new Point(Ui.X(18), Ui.X(12));
            chkAutostart.AutoSize = true;
            chkAutostart.Checked = GetAutostart();
            chkAutostart.CheckedChanged += delegate { SetAutostart(chkAutostart.Checked); SyncControlAccents(); };
            bottom.Controls.Add(chkAutostart);

            var lblMode = new Label();
            lblMode.Text = "Key send:";
            lblMode.ForeColor = Color.FromArgb(150, 150, 155);
            lblMode.Location = new Point(Ui.X(18), Ui.X(45));
            lblMode.AutoSize = true;
            bottom.Controls.Add(lblMode);

            rbVk = MakeRadio("Virtual keys", Ui.X(98), Ui.X(43));
            rbScan = MakeRadio("Scancodes", Ui.X(212), Ui.X(43));
            bottom.Controls.Add(rbVk);
            bottom.Controls.Add(rbScan);

            var btnEdit = MakeButton("Edit mapping", 500, 8);
            btnEdit.Click += delegate { Process.Start("notepad.exe", "\"" + Config.MappingPath + "\""); };
            bottom.Controls.Add(btnEdit);

            var btnReload = MakeButton("Reload mapping", 500, 40);
            btnReload.Click += delegate { LoadMapping(true); };
            bottom.Controls.Add(btnReload);
            // (MakeButton scales x/y internally)

            // options: layout + colour choices, opened as a popup window
            if (!Theme.Forced)
            {
                Theme.Trim = GetSetting("TrimColor", 0);
                Theme.Btn = GetSetting("ButtonColor", 0);
                Theme.Board = GetSetting("BoardColor", 0);
            }
            closeToTray = GetSetting("CloseToTray", 1) != 0;
            var btnOptions = MakeButton("Options", 500, 72);
            btnOptions.Click += delegate { ShowOptions(); };
            bottom.Controls.Add(btnOptions);
            RefreshTheme();

            lblConn = new Label();
            lblConn.Location = new Point(Ui.X(18), Ui.X(82));
            lblConn.AutoSize = true;
            lblConn.Text = "● starting...";
            lblConn.ForeColor = Color.Gray;
            bottom.Controls.Add(lblConn);

            lblInfo = new Label();
            lblInfo.AutoSize = false;
            lblInfo.TextAlign = ContentAlignment.MiddleLeft;
            lblInfo.AutoEllipsis = true;
            lblInfo.Bounds = new Rectangle(Ui.X(200), Ui.X(80), Ui.X(300), Ui.X(20));
            lblInfo.Text = "";
            lblInfo.ForeColor = Color.FromArgb(130, 130, 135);
            bottom.Controls.Add(lblInfo);

            // restore saved send mode
            int mode = GetSetting("SendMode", 0);
            suppressModeEvents = true;
            if (mode == 1)
            {
                KeySender.Mode = SendMode.ScanCodes;
                rbScan.Checked = true;
            }
            else
            {
                KeySender.Mode = SendMode.VirtualKeys;
                rbVk.Checked = true;
            }
            suppressModeEvents = false;

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
                if (MessageBox.Show(this,
                    "Load the default GSPro mapping for the " + (idx == 1 ? "V2" : "V1") +
                    " board? This overwrites mapping.txt.",
                    "GolfDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    File.WriteAllText(Config.MappingPath, Config.DefaultFor(idx));
                    LoadMapping(false);
                }
            }
            RefreshTheme();
        }

        void RefreshTheme()
        {
            SyncControlAccents();
            board.Invalidate();
            UpdateStatus();
        }

        // checked controls light up in the active trim accent
        void SyncControlAccents()
        {
            if (chkAutostart == null) return;
            chkAutostart.ForeColor = chkAutostart.Checked ? Theme.Accent : Color.Gainsboro;
            rbVk.ForeColor = rbVk.Checked ? Theme.Accent : Color.Gainsboro;
            rbScan.ForeColor = rbScan.Checked ? Theme.Accent : Color.Gainsboro;
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

        RadioButton AddOptRadio(Form f, string text, int x, int y)
        {
            var r = new RadioButton();
            r.Text = text;
            r.AutoSize = true;
            r.Location = new Point(Ui.X(x), Ui.X(y));
            r.FlatStyle = FlatStyle.Flat;
            r.ForeColor = Color.Gainsboro;
            r.CheckedChanged += delegate { r.ForeColor = r.Checked ? Theme.Accent : Color.Gainsboro; };
            f.Controls.Add(r);
            return r;
        }

        ComboBox AddOptCombo(Form f, string[] items, int x, int y, int w)
        {
            var c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var it in items) c.Items.Add(it);
            c.Bounds = new Rectangle(Ui.X(x), Ui.X(y), Ui.X(w), Ui.X(24));
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Color.FromArgb(36, 37, 40);
            c.ForeColor = Color.Gainsboro;
            c.DrawMode = DrawMode.OwnerDrawFixed;
            c.DrawItem += OptComboDrawItem;
            f.Controls.Add(c);
            return c;
        }

        void OptComboDrawItem(object sender, DrawItemEventArgs e)
        {
            var c = (ComboBox)sender;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? Theme.Accent : Color.FromArgb(36, 37, 40)))
                e.Graphics.FillRectangle(bg, e.Bounds);
            if (e.Index >= 0)
            {
                using (var br = new SolidBrush(sel ? Color.FromArgb(20, 22, 16) : Color.Gainsboro))
                    e.Graphics.DrawString(c.Items[e.Index].ToString(), c.Font, br, e.Bounds.X + 4, e.Bounds.Y + 2);
            }
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
            dlg.ClientSize = new Size(Ui.X(420), Ui.X(300));

            bool[] init = { true };

            AddOptLabel(dlg, "Board layout", 16, 12, true);
            var rbL1 = AddOptRadio(dlg, "V1  (PUTT top-left)", 16, 34);
            var rbL2 = AddOptRadio(dlg, "V2  (SCORECARD top-left)", 180, 34);
            if (AppState.Layout == 1) rbL2.Checked = true; else rbL1.Checked = true;
            rbL1.CheckedChanged += delegate { if (!init[0] && rbL1.Checked) SwitchLayout(0); };
            rbL2.CheckedChanged += delegate { if (!init[0] && rbL2.Checked) SwitchLayout(1); };

            AddOptLabel(dlg, "Colours", 16, 68, true);
            AddOptLabel(dlg, "Board", 16, 90, false);
            AddOptLabel(dlg, "Buttons", 148, 90, false);
            AddOptLabel(dlg, "Letters && trim", 280, 90, false);
            var cbBoard = AddOptCombo(dlg, new string[] { "Black", "Green Jacket", "Red", "Blue" }, 16, 110, 120);
            var cbBtn = AddOptCombo(dlg, new string[] { "Black", "White", "Yellow", "Red" }, 148, 110, 120);
            var cbTrim = AddOptCombo(dlg, new string[] { "Green", "White", "Yellow" }, 280, 110, 120);
            cbBoard.SelectedIndex = Theme.Board;
            cbBtn.SelectedIndex = Theme.Btn;
            cbTrim.SelectedIndex = Theme.Trim;
            EventHandler colorChange = delegate
            {
                if (init[0]) return;
                Theme.Board = cbBoard.SelectedIndex;
                Theme.Btn = cbBtn.SelectedIndex;
                Theme.Trim = cbTrim.SelectedIndex;
                SetSetting("BoardColor", Theme.Board);
                SetSetting("ButtonColor", Theme.Btn);
                SetSetting("TrimColor", Theme.Trim);
                RefreshTheme();
            };
            cbBoard.SelectedIndexChanged += colorChange;
            cbBtn.SelectedIndexChanged += colorChange;
            cbTrim.SelectedIndexChanged += colorChange;

            AddOptLabel(dlg, "Edition presets", 16, 148, true);
            string[] pnames = { "Original", "Green Jacket", "Red && White", "Red, White && Blue" };
            int[][] pvals = { new int[] { 0, 0, 0 }, new int[] { 1, 1, 2 }, new int[] { 2, 1, 1 }, new int[] { 3, 1, 3 } };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var pb = AddOptButton(dlg, pnames[i], 16 + (i % 2) * 196, 170 + (i / 2) * 34, 188, 28);
                pb.Click += delegate
                {
                    init[0] = true;
                    cbBoard.SelectedIndex = pvals[idx][0];
                    cbTrim.SelectedIndex = pvals[idx][1];
                    cbBtn.SelectedIndex = pvals[idx][2];
                    init[0] = false;
                    colorChange(null, null);
                };
            }

            var chkTray = new CheckBox();
            chkTray.Text = "Close button hides to the tray instead of exiting";
            chkTray.AutoSize = true;
            chkTray.Location = new Point(Ui.X(16), Ui.X(242));
            chkTray.FlatStyle = FlatStyle.Flat;
            chkTray.ForeColor = Color.Gainsboro;
            chkTray.Checked = closeToTray;
            chkTray.ForeColor = chkTray.Checked ? Theme.Accent : Color.Gainsboro;
            chkTray.CheckedChanged += delegate
            {
                closeToTray = chkTray.Checked;
                SetSetting("CloseToTray", closeToTray ? 1 : 0);
                chkTray.ForeColor = chkTray.Checked ? Theme.Accent : Color.Gainsboro;
            };
            dlg.Controls.Add(chkTray);

            var bUpd = AddOptButton(dlg, "Check for updates", 16, 268, 150, 26);
            bUpd.Click += delegate { StartUpdateCheck(true); };
            var bFolder = AddOptButton(dlg, "Open mapping folder", 174, 268, 164, 26);
            bFolder.Click += delegate { Process.Start("explorer.exe", "\"" + Config.Dir + "\""); };
            var bClose = AddOptButton(dlg, "Close", 346, 268, 58, 26);
            bClose.Click += delegate { dlg.Close(); };

            init[0] = false;
            return dlg;
        }

        void ShowOptions()
        {
            using (var dlg = BuildOptionsDialog())
                dlg.ShowDialog(this);
        }

        public void DemoPress(string csv)
        {
            timer.Stop(); // freeze state so screenshot shows the demo presses
            foreach (var s in csv.Split(','))
                engine.Pressed.Add(s.Trim().ToUpperInvariant());
            UpdateStatus();
            board.Invalidate();
        }

        RadioButton MakeRadio(string text, int x, int y)
        {
            var r = new RadioButton();
            r.Text = text;
            r.FlatStyle = FlatStyle.Flat;
            r.ForeColor = Color.Gainsboro;
            r.Location = new Point(x, y);
            r.AutoSize = true;
            r.CheckedChanged += OnModeChanged;
            r.CheckedChanged += delegate { SyncControlAccents(); };
            return r;
        }

        void OnModeChanged(object sender, EventArgs e)
        {
            if (suppressModeEvents) return;
            var rb = (RadioButton)sender;
            if (!rb.Checked) return;

            if (rb == rbScan)
            {
                KeySender.Mode = SendMode.ScanCodes;
                SetSetting("SendMode", 1);
            }
            else
            {
                KeySender.Mode = SendMode.VirtualKeys;
                SetSetting("SendMode", 0);
            }
            statusTick = 999;
        }

        Button MakeButton(string text, int x, int y)
        {
            var b = new Button();
            b.Text = text;
            b.Bounds = new Rectangle(Ui.X(x), Ui.X(y), Ui.X(180), Ui.X(28));
            b.FlatStyle = FlatStyle.Flat;
            b.ForeColor = Color.Gainsboro;
            b.BackColor = Color.FromArgb(36, 37, 40);
            b.FlatAppearance.BorderColor = Color.FromArgb(64, 66, 70);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 50, 54);
            return b;
        }

        void LoadMapping(bool interactive)
        {
            engine.ReleaseAll();
            List<string> errors;
            engine.Entries = Config.Load(engine, out errors);
            mapErrors = errors.Count;
            board.Invalidate();
            UpdateStatus();
            if (errors.Count > 0 && interactive)
                MessageBox.Show(this, string.Join("\r\n", errors.ToArray()), "mapping.txt problems",
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
            }
        }

        void UpdateStatus()
        {
            if (lblConn == null) return;
            if (engine.Connected)
            {
                lblConn.Text = "●  Controller connected (P" + (engine.ControllerIndex + 1) + ")";
                lblConn.ForeColor = Theme.Accent;
            }
            else
            {
                lblConn.Text = "●  Controller not found";
                lblConn.ForeColor = Color.FromArgb(220, 95, 90);
            }
            int infoLeft = lblConn.Right + Ui.X(14);
            lblInfo.SetBounds(infoLeft, Ui.X(80), Ui.X(500 - 12) - infoLeft, Ui.X(20));
            string info = "admin: " + (IsAdmin() ? "yes" : "no")
                + "      mode: " + (KeySender.Mode == SendMode.ScanCodes ? "scancodes" : "virtual keys")
                + "      keys sent: " + KeySender.KeysSent;
            if (KeySender.LastSent.Length > 0) info += "      last: " + KeySender.LastSent;
            if (mapErrors > 0) info = "mapping errors: " + mapErrors + " (Edit mapping)      " + info;
            lblInfo.Text = info;
            lblInfo.ForeColor = mapErrors > 0 ? Color.FromArgb(220, 95, 90) : Color.FromArgb(130, 130, 135);
            if (tray != null)
                tray.Text = engine.Connected
                    ? "GolfDeck - controller connected"
                    : "GolfDeck - no controller";
        }

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
            if (IsAdmin()) { MessageBox.Show(this, "Already running as administrator."); return; }
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
                    MessageBox.Show(this, "Update check failed. No network, or the release feed is unavailable.",
                        "GolfDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Updater.IsNewer(info.Tag, Version))
            {
                if (manual)
                    MessageBox.Show(this, "You are on the latest version (v" + Version + ").",
                        "GolfDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string summary = (info.Body ?? "").Trim();
            if (summary.Length > 600) summary = summary.Substring(0, 600) + "...";
            if (summary.Length == 0) summary = "(no release notes)";

            if (!Visible) RestoreFromTray();
            var res = MessageBox.Show(this,
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
                    MessageBox.Show(this, "Update failed: " + ex.Message +
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
            using (var dlg = new Form())
            {
                dlg.Text = "GolfDeck";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.Font = new Font("Segoe UI", Ui.F(12f), FontStyle.Regular, GraphicsUnit.Pixel);
                dlg.ClientSize = new Size(Ui.X(430), Ui.X(160));
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.BackColor = Color.FromArgb(19, 19, 21);
                dlg.Icon = AppIcon.Get();

                var lbl = new Label();
                lbl.Text = "Which control box layout do you have?\r\n(You can change this later under Options.)";
                lbl.ForeColor = Color.Gainsboro;
                lbl.Bounds = new Rectangle(Ui.X(20), Ui.X(15), Ui.X(390), Ui.X(40));
                dlg.Controls.Add(lbl);

                int choice = 0;
                Button b1 = new Button(), b2 = new Button();
                b1.Text = "V1\r\nPUTT top-left";
                b2.Text = "V2\r\nSCORECARD top-left";
                Button[] bs = { b1, b2 };
                for (int i = 0; i < 2; i++)
                {
                    int idx = i;
                    bs[i].Bounds = new Rectangle(Ui.X(20 + i * 200), Ui.X(65), Ui.X(190), Ui.X(70));
                    bs[i].FlatStyle = FlatStyle.Flat;
                    bs[i].ForeColor = Color.Gainsboro;
                    bs[i].BackColor = Color.FromArgb(36, 37, 40);
                    bs[i].FlatAppearance.BorderColor = Color.FromArgb(100, 160, 80);
                    bs[i].Click += delegate { choice = idx; dlg.DialogResult = DialogResult.OK; };
                    dlg.Controls.Add(bs[i]);
                }
                dlg.ShowDialog();
                return choice;
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
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--minimized") minimized = true;
                else if (args[i] == "--shotoptions") shotOptions = true;
                else if (args[i] == "--screenshot" && i + 1 < args.Length) screenshot = args[++i];
                else if (args[i] == "--press" && i + 1 < args.Length) press = args[++i];
                else if (args[i] == "--scale" && i + 1 < args.Length)
                    float.TryParse(args[++i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out forcedScale);
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
                File.WriteAllText(Config.MappingPath, Config.DefaultFor(layout));

            var form = new MainForm(minimized && screenshot == null);

            if (screenshot != null)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.Show();
                if (press != null) form.DemoPress(press);
                Application.DoEvents();
                Form target = form;
                if (shotOptions)
                {
                    target = form.BuildOptionsDialog();
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
