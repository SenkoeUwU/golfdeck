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

        public const string DefaultMapping = @"# GolfDeck mapping (defaults = GSPro standard shortcuts)
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

    // ---------------- board GUI ----------------

    class BoardPanel : Panel
    {
        public Engine Engine;

        class Slot
        {
            public string Label;      // labeled buttons: matched to mapping entries by label
            public string[] Inputs;   // arrow buttons: matched by input
            public float X, Y;
            public bool Arrow;
            public string ArrowGlyph = "";
        }

        static readonly Color Green = Color.FromArgb(158, 232, 112);
        static readonly Color GreenDim = Color.FromArgb(96, 142, 74);
        static readonly Color BoardBg = Color.FromArgb(26, 27, 29);
        static readonly Color BtnRing = Color.FromArgb(10, 10, 11);

        List<Slot> slots = new List<Slot>();
        Font labelFont = new Font("Segoe UI", 11f, FontStyle.Bold | FontStyle.Italic);
        Font keyFont = new Font("Consolas", 9f, FontStyle.Bold);
        Font smallFont = new Font("Segoe UI", 8.5f);
        Font markFont = new Font("Segoe UI", 9f, FontStyle.Bold | FontStyle.Italic);

        public BoardPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.FromArgb(13, 13, 14);

            // straight-on grid mirroring the printed board: labels fixed to
            // physical positions, mapping entries attach by label
            AddBtn(0.150f, 0.235f, "PUTT");
            AddBtn(0.383f, 0.235f, "HEATMAP");
            AddBtn(0.617f, 0.235f, "FLYOVER");
            AddBtn(0.850f, 0.235f, "SHOTCAM");
            AddBtn(0.150f, 0.560f, "CLUB UP");
            AddBtn(0.150f, 0.850f, "CLUB DOWN");
            AddBtn(0.850f, 0.560f, "AIM POINT");
            AddBtn(0.850f, 0.850f, "MULLIGAN");

            AddArrow(0.500f, 0.475f, "▲", "LS_UP", "DPAD_UP");
            AddArrow(0.410f, 0.665f, "◀", "LS_LEFT", "DPAD_LEFT");
            AddArrow(0.590f, 0.665f, "▶", "LS_RIGHT", "DPAD_RIGHT");
            AddArrow(0.500f, 0.850f, "▼", "LS_DOWN", "DPAD_DOWN");
        }

        void AddBtn(float x, float y, string label)
        {
            var s = new Slot(); s.X = x; s.Y = y; s.Label = label; slots.Add(s);
        }

        void AddArrow(float x, float y, string glyph, params string[] inputs)
        {
            var s = new Slot(); s.X = x; s.Y = y; s.Arrow = true; s.ArrowGlyph = glyph; s.Inputs = inputs; slots.Add(s);
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
                using (var pen = new Pen(Green, 3f)) g.DrawPath(pen, path);
            }

            float r = board.Width * 0.053f;
            if (r < 16) r = 16;

            // center 4-way glyph between the arrow buttons
            using (var br = new SolidBrush(Color.FromArgb(70, 105, 55)))
            using (var f = new Font("Segoe UI Symbol", r * 0.34f, FontStyle.Bold))
            {
                var gsz = g.MeasureString("✥", f);
                float gx = board.X + board.Width * 0.500f;
                float gy = board.Y + board.Height * 0.665f;
                g.DrawString("✥", f, br, gx - gsz.Width / 2f, gy - gsz.Height / 2f);
            }

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
                    pressed ? Color.FromArgb(112, 205, 72) : Color.FromArgb(58, 58, 62),
                    pressed ? Color.FromArgb(38, 100, 22) : Color.FromArgb(20, 20, 22),
                    LinearGradientMode.Vertical))
                    g.FillEllipse(face, rect);

                // top highlight
                using (var hl = new LinearGradientBrush(
                    new RectangleF(rect.X, rect.Y, rect.Width, rect.Height * 0.55f + 1),
                    Color.FromArgb(pressed ? 90 : 55, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical))
                    g.FillEllipse(hl, rect.X + rr * 0.22f, rect.Y + rr * 0.10f, rr * 1.56f, rr * 1.0f);

                using (var pen = new Pen(pressed ? Green : Color.FromArgb(74, 74, 80), 1.6f))
                    g.DrawEllipse(pen, rect);

                // arrow glyph
                if (s.Arrow)
                {
                    using (var br = new SolidBrush(pressed ? Color.FromArgb(16, 24, 10) : GreenDim))
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
            }

            // wordmark top-left
            using (var br = new SolidBrush(Color.FromArgb(60, 88, 48)))
                g.DrawString("GOLFDECK", markFont, br, board.X + 18, board.Y + 12);

            // entries not attached to any board slot as chips top-right
            if (Engine != null)
            {
                float ty = board.Y + 12;
                foreach (var e in Engine.Entries)
                {
                    if (used.Contains(e)) continue;
                    string txt = e.Input + "  =  " + e.KeysText + (e.Mode == "repeat" ? "  (repeat)" : "");
                    bool on = Engine.Pressed.Contains(e.Input);
                    var sz = g.MeasureString(txt, smallFont);
                    var chip = new RectangleF(board.Right - sz.Width - 18 - 16, ty, sz.Width + 18, 22);
                    using (var cp = RoundedF(chip, 10))
                    {
                        using (var bg = new SolidBrush(on ? Color.FromArgb(70, 158, 232, 112) : Color.FromArgb(34, 35, 37)))
                            g.FillPath(bg, cp);
                        using (var pen = new Pen(on ? Green : Color.FromArgb(58, 60, 62), 1f))
                            g.DrawPath(pen, cp);
                    }
                    using (var br = new SolidBrush(on ? Green : GreenDim))
                        g.DrawString(txt, smallFont, br, chip.X + 9, chip.Y + 3);
                    ty += 28;
                }
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

        Engine engine = new Engine();
        BoardPanel board;
        System.Windows.Forms.Timer timer;
        NotifyIcon tray;
        CheckBox chkAutostart;
        RadioButton rbVk, rbScan;
        Label lblConn, lblInfo;
        bool exiting;
        bool startMinimized;
        bool suppressModeEvents;
        int statusTick = 999;

        public MainForm(bool minimized)
        {
            startMinimized = minimized;
            Text = "GolfDeck";
            BackColor = Color.FromArgb(13, 13, 14);
            ClientSize = new Size(700, 590);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

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

            var btnEdit = MakeButton("Edit mapping", 500, 10);
            btnEdit.Click += delegate { Process.Start("notepad.exe", "\"" + Config.MappingPath + "\""); };
            bottom.Controls.Add(btnEdit);

            var btnReload = MakeButton("Reload mapping", 500, 44);
            btnReload.Click += delegate { LoadMapping(true); };
            bottom.Controls.Add(btnReload);

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
            board.Invalidate();
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
            if (engine.Connected)
            {
                lblConn.Text = "●  Controller connected (P" + (engine.ControllerIndex + 1) + ")";
                lblConn.ForeColor = Color.FromArgb(158, 232, 112);
            }
            else
            {
                lblConn.Text = "●  Controller not found";
                lblConn.ForeColor = Color.FromArgb(220, 95, 90);
            }
            lblInfo.Left = lblConn.Right + 16;
            lblInfo.Text = "admin: " + (IsAdmin() ? "yes" : "no")
                + "      mode: " + (KeySender.Mode == SendMode.ScanCodes ? "scancodes" : "virtual keys")
                + "      keys sent: " + KeySender.KeysSent;
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
                return;
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

        static int GetSetting(string name, int def)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(AppKey))
            {
                if (k == null) return def;
                object v = k.GetValue(name);
                return v is int ? (int)v : def;
            }
        }

        static void SetSetting(string name, int val)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(AppKey))
                k.SetValue(name, val);
        }
    }

    // ---------------- entry point ----------------

    static class Program
    {
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
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--minimized") minimized = true;
                else if (args[i] == "--screenshot" && i + 1 < args.Length) screenshot = args[++i];
                else if (args[i] == "--press" && i + 1 < args.Length) press = args[++i];
            }

            if (!File.Exists(Config.MappingPath))
                File.WriteAllText(Config.MappingPath, Config.DefaultMapping);

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
