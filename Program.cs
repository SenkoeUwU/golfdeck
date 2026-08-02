using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
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

        public bool WasDown;
        public int NextRepeat;
        public bool Holding;
    }

    static class Config
    {
        public static string Dir
        {
            get { return Path.GetDirectoryName(Application.ExecutablePath); }
        }

        public static string MappingPath
        {
            get { return Path.Combine(Dir, "mapping.txt"); }
        }

        public static string DefaultFor(int layout)
        {
            return layout == 1 ? DefaultMappingV2 : DefaultMappingV1;
        }

        public const string DefaultMappingV2 = @"# GolfDeck mapping - V2 board (defaults = GSPro standard shortcuts)
#
# GSPro keys: T scorecard, Space fast forward, Y heat map, H hide UI,
#             J shot cam, Ctrl+M mulligan, U putt toggle, O flyover,
#             arrow keys aim
# green print on the box (C/S/T/WAKE) marks hardware secondary functions
#
# format:   input = keys | label | mode | repeat_ms
#   (see the V1 template or README for details)
#
# settings (percent):
stick_threshold = 37
trigger_threshold = 25

X       = T       | SCORECARD   | tap
Y       = Space   | FAST FWD    | tap
LB      = Y       | HEATMAP     | tap
RB      = H       | HIDE OBJECT | tap
B       = J       | SHOTCAM     | tap
Menu    = Ctrl+M  | MULLIGAN    | tap
A       = U       | PUTT        | tap
LT      = O       | FLYOVER     | tap

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
#   mode:   hold   = key held down while button held (default)
#           tap    = one keypress per button press
#           repeat = keypress repeats every repeat_ms while held
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
                    if (int.TryParse(parts[3].Trim(), out ms) && ms >= 20) e.RepeatMs = ms;
                }
                if (e.Mode != "hold" && e.Mode != "tap" && e.Mode != "repeat")
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
            if (down && !e.WasDown && e.KeysText.Length > 0)
                KeySender.LastSent = e.KeysText.ToUpperInvariant();
            if (e.Mode == "hold")
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
        Font labelFont = MakeBoardFont(15f);
        Font keyFont = new Font("Consolas", 9f, FontStyle.Bold);
        Font smallFont = new Font("Segoe UI", 8.5f);
        Font markFont = MakeBoardFont(9.5f);

        // the physical box is printed in a DIN-style condensed industrial face;
        // Bahnschrift (ships with Win10+) is the closest stock match. SemiCondensed
        // first: the tighter cuts crush together at small sizes.
        static Font MakeBoardFont(float size)
        {
            string[] candidates = { "Bahnschrift SemiBold SemiConden", "Bahnschrift SemiBold Condensed", "Bahnschrift" };
            foreach (var name in candidates)
            {
                try
                {
                    using (var fam = new FontFamily(name))
                        return new Font(name, size, FontStyle.Bold);
                }
                catch (ArgumentException) { }
            }
            return new Font("Segoe UI", size - 1.5f, FontStyle.Bold);
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
                var wake = new Slot(); wake.X = 0.818f; wake.Y = 0.660f; wake.Arrow = true;
                wake.ArrowGlyph = "▶"; wake.Sub = "WAKE";
                wake.Inputs = new string[] { "LS_RIGHT", "DPAD_RIGHT" };
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

        Rectangle BoardRect
        {
            get { int pad = 14; return new Rectangle(pad, pad, Width - pad * 2, Height - pad * 2); }
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
            MousePressed.Add(entry.Input);
            if (entry.Mode == "hold")
            {
                activeIsHold = true;
                KeySender.Press(entry.Mods, entry.Vk);
            }
            else
            {
                activeIsHold = false;
                KeySender.Tap(entry.Mods, entry.Vk);
            }
            KeySender.LastSent = entry.KeysText.ToUpperInvariant();
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            EndClickPress();
        }

        void EndClickPress()
        {
            if (activeEntry == null) return;
            if (activeIsHold) KeySender.Release(activeEntry.Mods, activeEntry.Vk);
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

            int pad = 14;
            var board = new Rectangle(pad, pad, Width - pad * 2, Height - pad * 2);
            using (var path = Rounded(board, 26))
            {
                using (var br = new SolidBrush(BoardBg)) g.FillPath(br, path);
                using (var vign = new PathGradientBrush(path))
                {
                    vign.CenterColor = Color.FromArgb(0, 0, 0, 0);
                    vign.SurroundColors = new Color[] { Color.FromArgb(80, 0, 0, 0) };
                    g.FillPath(vign, path);
                }
                using (var pen = new Pen(Green, 3f)) g.DrawPath(pen, path);
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
                var entry = s.Arrow ? FindByInput(s.Inputs) : FindByLabel(s.Label);
                if (entry != null) used.Add(entry);
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
                        using (var glow = new SolidBrush(Color.FromArgb(22, Green)))
                            g.FillEllipse(glow, x - rr - i * 5, y - rr - i * 5, (rr + i * 5) * 2, (rr + i * 5) * 2);
                    }
                }

                // bezel ring + drop shadow
                using (var ring = new SolidBrush(BtnRing))
                    g.FillEllipse(ring, rect.X - 5, rect.Y - 3, rect.Width + 10, rect.Height + 11);

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

                using (var pen = new Pen(pressed ? Green : Theme.BtnBorder, 1.6f))
                    g.DrawEllipse(pen, rect);

                // hover ring (click-to-test affordance)
                if (s == hoverSlot && !pressed && mapped)
                {
                    using (var pen = new Pen(Color.FromArgb(150, Green), 2f))
                        g.DrawEllipse(pen, rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8);
                }

                // arrow glyph
                if (s.Arrow)
                {
                    using (var br = new SolidBrush(pressed ? Theme.PressGlyph : Theme.Glyph))
                    using (var f = new Font("Segoe UI Symbol", rr * 0.52f, FontStyle.Bold))
                    {
                        var sz = g.MeasureString(s.ArrowGlyph, f);
                        g.DrawString(s.ArrowGlyph, f, br, x - sz.Width / 2f, y - sz.Height / 2f + 1);
                    }
                }

                // label above (from the slot: physical board truth)
                if (!s.Arrow)
                {
                    using (var br = new SolidBrush(mapped ? Green : GreenDim))
                    {
                        var sz = g.MeasureString(s.Label, labelFont);
                        g.DrawString(s.Label, labelFont, br, x - sz.Width / 2f, y - rr - sz.Height - 6);
                    }
                }

                // key caption below (labeled buttons only; arrows are self-evident)
                if (!s.Arrow)
                {
                    string cap = mapped ? entry.KeysText.ToUpperInvariant() : "--";
                    using (var br = new SolidBrush(pressed ? Green : GreenDim))
                    {
                        var sz = g.MeasureString(cap, keyFont);
                        g.DrawString(cap, keyFont, br, x - sz.Width / 2f, y + rr + 7);
                    }
                }

                // green secondary print (cosmetic, placed as printed on the box)
                if (s.Sub.Length > 0)
                {
                    using (var br = new SolidBrush(Color.FromArgb(150, 235, 100)))
                    {
                        var sz = g.MeasureString(s.Sub, markFont);
                        if (s.Arrow)
                            g.DrawString(s.Sub, markFont, br, x - sz.Width / 2f, y + rr + 5);
                        else if (s.SubLeft)
                            g.DrawString(s.Sub, markFont, br, x - rr - sz.Width - 6, y - sz.Height / 2f);
                        else
                            g.DrawString(s.Sub, markFont, br, x - sz.Width / 2f, y + rr + 23);
                    }
                }
            }

            // wordmark top-left
            using (var br = new SolidBrush(Color.FromArgb(130, GreenDim)))
                g.DrawString("GOLFDECK", markFont, br, board.X + 18, board.Y + 12);


            // entries not attached to any board slot as chips along the top edge
            if (Engine != null)
            {
                float tx = board.Right - 16;
                foreach (var e in Engine.Entries)
                {
                    if (used.Contains(e)) continue;
                    string txt = e.Input + "  =  " + e.KeysText + (e.Mode == "repeat" ? "  (repeat)" : "");
                    bool on = Engine.Pressed.Contains(e.Input);
                    var sz = g.MeasureString(txt, smallFont);
                    var chip = new RectangleF(tx - sz.Width - 18, board.Y + 12, sz.Width + 18, 22);
                    using (var cp = RoundedF(chip, 10))
                    {
                        using (var bg = new SolidBrush(on ? Color.FromArgb(70, 158, 232, 112) : Color.FromArgb(34, 35, 37)))
                            g.FillPath(bg, cp);
                        using (var pen = new Pen(on ? Green : Color.FromArgb(58, 60, 62), 1f))
                            g.DrawPath(pen, cp);
                    }
                    using (var br = new SolidBrush(on ? Green : GreenDim))
                        g.DrawString(txt, smallFont, br, chip.X + 9, chip.Y + 3);
                    tx = chip.X - 8;
                }
            }
        }

        static void DrawCross(Graphics g, float cx, float cy, float ext, Color c)
        {
            using (var br = new SolidBrush(c))
            {
                float w = ext * 0.26f;    // shaft half-width
                float head = ext * 0.46f; // arrowhead length
                float hw = ext * 0.52f;   // arrowhead half-width
                float shaft = ext - head;
                g.FillRectangle(br, cx - w, cy - shaft, w * 2, shaft * 2);
                g.FillRectangle(br, cx - shaft, cy - w, shaft * 2, w * 2);
                g.FillPolygon(br, new PointF[] { new PointF(cx, cy - ext), new PointF(cx - hw, cy - shaft), new PointF(cx + hw, cy - shaft) });
                g.FillPolygon(br, new PointF[] { new PointF(cx, cy + ext), new PointF(cx - hw, cy + shaft), new PointF(cx + hw, cy + shaft) });
                g.FillPolygon(br, new PointF[] { new PointF(cx - ext, cy), new PointF(cx - shaft, cy - hw), new PointF(cx - shaft, cy + hw) });
                g.FillPolygon(br, new PointF[] { new PointF(cx + ext, cy), new PointF(cx + shaft, cy - hw), new PointF(cx + shaft, cy + hw) });
            }
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
        public const string Version = "1.3";

        Engine engine = new Engine();
        BoardPanel board;
        System.Windows.Forms.Timer timer;
        NotifyIcon tray;
        CheckBox chkAutostart;
        RadioButton rbVk, rbScan;
        Label lblConn, lblInfo;
        ToolStripMenuItem trimMenu, btnColMenu, boardMenu, layoutMenu;
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
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(700, 590);
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
            board.Bounds = new Rectangle(0, 0, 700, 478);
            Controls.Add(board);

            var bottom = new Panel();
            bottom.Bounds = new Rectangle(0, 478, 700, 112);
            bottom.BackColor = Color.FromArgb(19, 19, 21);
            bottom.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(44, 46, 48), 1f))
                    e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
            };
            Controls.Add(bottom);

            chkAutostart = new CheckBox();
            chkAutostart.Text = "Start with Windows";
            chkAutostart.ForeColor = Color.Gainsboro;
            chkAutostart.Location = new Point(18, 12);
            chkAutostart.AutoSize = true;
            chkAutostart.Checked = GetAutostart();
            chkAutostart.CheckedChanged += delegate { SetAutostart(chkAutostart.Checked); };
            bottom.Controls.Add(chkAutostart);

            var lblMode = new Label();
            lblMode.Text = "Key send:";
            lblMode.ForeColor = Color.FromArgb(150, 150, 155);
            lblMode.Location = new Point(18, 45);
            lblMode.AutoSize = true;
            bottom.Controls.Add(lblMode);

            rbVk = MakeRadio("Virtual keys", 98, 43);
            rbScan = MakeRadio("Scancodes", 205, 43);
            bottom.Controls.Add(rbVk);
            bottom.Controls.Add(rbScan);

            var btnEdit = MakeButton("Edit mapping", 500, 8);
            btnEdit.Click += delegate { Process.Start("notepad.exe", "\"" + Config.MappingPath + "\""); };
            bottom.Controls.Add(btnEdit);

            var btnReload = MakeButton("Reload mapping", 500, 40);
            btnReload.Click += delegate { LoadMapping(true); };
            bottom.Controls.Add(btnReload);

            // options: layout + colour choices matching the physical box editions
            if (!Theme.Forced)
            {
                Theme.Trim = GetSetting("TrimColor", 0);
                Theme.Btn = GetSetting("ButtonColor", 0);
                Theme.Board = GetSetting("BoardColor", 0);
            }
            var btnOptions = MakeButton("Options", 500, 72);
            var optMenu = new ContextMenuStrip();

            layoutMenu = new ToolStripMenuItem("Board layout");
            string[] layouts = { "V1", "V2" };
            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                var li = new ToolStripMenuItem(layouts[i]);
                li.Click += delegate { SwitchLayout(idx); };
                layoutMenu.DropDownItems.Add(li);
            }

            var presetMenu = new ToolStripMenuItem("Edition presets");
            AddPreset(presetMenu, "Original (black / green)", 0, 0, 0);
            AddPreset(presetMenu, "Green Jacket", 1, 1, 2);
            AddPreset(presetMenu, "Red && White", 2, 1, 1);
            AddPreset(presetMenu, "Red, White && Blue", 3, 1, 3);

            trimMenu = new ToolStripMenuItem("Letters && top trim colour");
            btnColMenu = new ToolStripMenuItem("Button colour");
            boardMenu = new ToolStripMenuItem("Board colour");
            string[] trims = { "Green", "White", "Yellow" };
            string[] btncols = { "Black", "White", "Yellow", "Red" };
            string[] boards = { "Black", "Green Jacket", "Red", "Blue" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var t = new ToolStripMenuItem(trims[i]);
                t.Click += delegate { Theme.Trim = idx; SetSetting("TrimColor", idx); RefreshTheme(); };
                trimMenu.DropDownItems.Add(t);
            }
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = new ToolStripMenuItem(btncols[i]);
                b.Click += delegate { Theme.Btn = idx; SetSetting("ButtonColor", idx); RefreshTheme(); };
                btnColMenu.DropDownItems.Add(b);
                var bo = new ToolStripMenuItem(boards[i]);
                bo.Click += delegate { Theme.Board = idx; SetSetting("BoardColor", idx); RefreshTheme(); };
                boardMenu.DropDownItems.Add(bo);
            }
            optMenu.Items.Add(layoutMenu);
            optMenu.Items.Add(presetMenu);
            optMenu.Items.Add(new ToolStripSeparator());
            optMenu.Items.Add(boardMenu);
            optMenu.Items.Add(btnColMenu);
            optMenu.Items.Add(trimMenu);
            btnOptions.Click += delegate { optMenu.Show(btnOptions, new Point(0, btnOptions.Height)); };
            bottom.Controls.Add(btnOptions);
            RefreshTheme();

            lblConn = new Label();
            lblConn.Location = new Point(18, 82);
            lblConn.AutoSize = true;
            lblConn.Text = "● starting...";
            lblConn.ForeColor = Color.Gray;
            bottom.Controls.Add(lblConn);

            lblInfo = new Label();
            lblInfo.Location = new Point(180, 82);
            lblInfo.AutoSize = true;
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

        void AddPreset(ToolStripMenuItem parent, string name, int boardCol, int trim, int btn)
        {
            var p = new ToolStripMenuItem(name);
            p.Click += delegate
            {
                Theme.Board = boardCol; Theme.Trim = trim; Theme.Btn = btn;
                SetSetting("BoardColor", boardCol); SetSetting("TrimColor", trim); SetSetting("ButtonColor", btn);
                RefreshTheme();
            };
            parent.DropDownItems.Add(p);
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
            for (int i = 0; i < 3; i++)
                ((ToolStripMenuItem)trimMenu.DropDownItems[i]).Checked = Theme.Trim == i;
            for (int i = 0; i < 4; i++)
            {
                ((ToolStripMenuItem)btnColMenu.DropDownItems[i]).Checked = Theme.Btn == i;
                ((ToolStripMenuItem)boardMenu.DropDownItems[i]).Checked = Theme.Board == i;
            }
            for (int i = 0; i < 2; i++)
                ((ToolStripMenuItem)layoutMenu.DropDownItems[i]).Checked = AppState.Layout == i;
            board.Invalidate();
            UpdateStatus();
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
            r.ForeColor = Color.Gainsboro;
            r.Location = new Point(x, y);
            r.AutoSize = true;
            r.CheckedChanged += OnModeChanged;
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
            b.Bounds = new Rectangle(x, y, 180, 28);
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
            lblInfo.Left = lblConn.Right + 16;
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
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
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
                dlg.AutoScaleDimensions = new SizeF(96F, 96F);
                dlg.AutoScaleMode = AutoScaleMode.Dpi;
                dlg.ClientSize = new Size(430, 160);
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.BackColor = Color.FromArgb(19, 19, 21);
                dlg.Icon = AppIcon.Get();

                var lbl = new Label();
                lbl.Text = "Which control box layout do you have?\r\n(You can change this later under Options.)";
                lbl.ForeColor = Color.Gainsboro;
                lbl.Bounds = new Rectangle(20, 15, 390, 40);
                dlg.Controls.Add(lbl);

                int choice = 0;
                Button b1 = new Button(), b2 = new Button();
                b1.Text = "V1\r\nPUTT top-left";
                b2.Text = "V2\r\nSCORECARD top-left";
                Button[] bs = { b1, b2 };
                for (int i = 0; i < 2; i++)
                {
                    int idx = i;
                    bs[i].Bounds = new Rectangle(20 + i * 200, 65, 190, 70);
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
            bool created;
            var mutex = new Mutex(true, "GolfDeck_SingleInstance", out created);
            if (!created)
            {
                MessageBox.Show("GolfDeck is already running (check the tray).", "GolfDeck");
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool minimized = false;
            string screenshot = null;
            string press = null;
            int layoutArg = -1;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--minimized") minimized = true;
                else if (args[i] == "--screenshot" && i + 1 < args.Length) screenshot = args[++i];
                else if (args[i] == "--press" && i + 1 < args.Length) press = args[++i];
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
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(screenshot, System.Drawing.Imaging.ImageFormat.Png);
                }
                GC.KeepAlive(mutex);
                return;
            }

            Application.Run(form);
            GC.KeepAlive(mutex);
        }
    }
}
