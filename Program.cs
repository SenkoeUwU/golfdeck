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

    static class KeySender
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint code, uint mapType);

        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_SCANCODE = 0x0008;

        public static bool UseScanCodes = false;
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
            if (UseScanCodes)
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

        static void Emit(List<INPUT> seq)
        {
            if (seq.Count == 0) return;
            SendInput((uint)seq.Count, seq.ToArray(), Marshal.SizeOf(typeof(INPUT)));
            KeysSent += seq.Count;
        }

        public static void Press(List<ushort> mods, ushort vk)
        {
            var seq = new List<INPUT>();
            foreach (var m in mods) seq.Add(Make(m, true));
            seq.Add(Make(vk, true));
            Emit(seq);
        }

        public static void Release(List<ushort> mods, ushort vk)
        {
            var seq = new List<INPUT>();
            seq.Add(Make(vk, false));
            for (int i = mods.Count - 1; i >= 0; i--) seq.Add(Make(mods[i], false));
            Emit(seq);
        }

        public static void Tap(List<ushort> mods, ushort vk)
        {
            var seq = new List<INPUT>();
            foreach (var m in mods) seq.Add(Make(m, true));
            seq.Add(Make(vk, true));
            seq.Add(Make(vk, false));
            for (int i = mods.Count - 1; i >= 0; i--) seq.Add(Make(mods[i], false));
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

        public const string DefaultMapping = @"# GolfDeck mapping
#
# format:   input = keys | label | mode | repeat_ms
#   keys:   single key or combo with +   (K, Ctrl+M, Shift+F5, ')
#   label:  text shown on the board GUI
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

A       = K       | PUTT       | tap
B       = I       | HEATMAP    | tap
X       = U       | FLYOVER    | tap
Y       = Y       | SHOTCAM    | tap
LB      = O       | CLUB UP    | tap
RB      = J       | CLUB DOWN  | tap
Menu    = A       | AIM POINT  | tap
LT      = Ctrl+M  | MULLIGAN   | tap
RT      = '       |            | repeat | 170

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
                // special case: the key itself might be '+' or contain it; not supported, keep simple
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
            public string[] Inputs;
            public float X, Y;
            public bool LabelAbove = true;
            public bool Arrow;
            public string ArrowGlyph = "";
            public Slot(float x, float y, params string[] inputs) { X = x; Y = y; Inputs = inputs; }
        }

        static readonly Color Green = Color.FromArgb(150, 235, 100);
        static readonly Color GreenDim = Color.FromArgb(95, 150, 70);
        static readonly Color BoardBg = Color.FromArgb(24, 24, 26);
        static readonly Color BtnFace = Color.FromArgb(38, 38, 40);
        static readonly Color BtnRing = Color.FromArgb(12, 12, 12);

        List<Slot> slots = new List<Slot>();
        Font labelFont = new Font("Segoe UI", 10.5f, FontStyle.Bold | FontStyle.Italic);
        Font keyFont = new Font("Consolas", 8.5f, FontStyle.Bold);
        Font smallFont = new Font("Segoe UI", 8f);

        public BoardPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.FromArgb(14, 14, 15);

            slots.Add(new Slot(0.155f, 0.235f, "A"));
            slots.Add(new Slot(0.385f, 0.235f, "B"));
            slots.Add(new Slot(0.615f, 0.235f, "X"));
            slots.Add(new Slot(0.845f, 0.235f, "Y"));
            slots.Add(new Slot(0.145f, 0.535f, "LB"));
            slots.Add(new Slot(0.125f, 0.835f, "RB"));
            slots.Add(new Slot(0.855f, 0.535f, "MENU"));
            slots.Add(new Slot(0.875f, 0.835f, "LT"));

            var up = new Slot(0.50f, 0.46f, "LS_UP", "DPAD_UP"); up.Arrow = true; up.ArrowGlyph = "▲"; slots.Add(up);
            var lf = new Slot(0.385f, 0.625f, "LS_LEFT", "DPAD_LEFT"); lf.Arrow = true; lf.ArrowGlyph = "◀"; slots.Add(lf);
            var rt = new Slot(0.615f, 0.625f, "LS_RIGHT", "DPAD_RIGHT"); rt.Arrow = true; rt.ArrowGlyph = "▶"; slots.Add(rt);
            var dn = new Slot(0.50f, 0.79f, "LS_DOWN", "DPAD_DOWN"); dn.Arrow = true; dn.ArrowGlyph = "▼"; slots.Add(dn);
        }

        MapEntry FindEntry(string[] inputs)
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

            int pad = 12;
            var board = new Rectangle(pad, pad, Width - pad * 2, Height - pad * 2);
            using (var path = Rounded(board, 22))
            {
                using (var br = new SolidBrush(BoardBg)) g.FillPath(br, path);
                using (var pen = new Pen(Green, 3f)) g.DrawPath(pen, path);
            }

            float r = board.Width * 0.048f;
            if (r < 16) r = 16;

            // center cross glyph
            float cx = board.X + board.Width * 0.50f;
            float cy = board.Y + board.Height * 0.625f;
            using (var br = new SolidBrush(GreenDim))
            using (var f = new Font("Segoe UI Symbol", r * 0.55f, FontStyle.Bold))
            {
                var sz = g.MeasureString("✥", f);
                g.DrawString("✥", f, br, cx - sz.Width / 2f, cy - sz.Height / 2f);
            }

            foreach (var s in slots)
            {
                float x = board.X + board.Width * s.X;
                float y = board.Y + board.Height * s.Y;
                var entry = FindEntry(s.Inputs);
                bool pressed = IsPressed(s.Inputs);
                bool mapped = entry != null && entry.KeysText.Length > 0;

                // button
                float rr = s.Arrow ? r * 0.85f : r;
                var rect = new RectangleF(x - rr, y - rr, rr * 2, rr * 2);
                using (var ring = new SolidBrush(BtnRing))
                    g.FillEllipse(ring, rect.X - 5, rect.Y - 4, rect.Width + 10, rect.Height + 10);
                using (var face = new LinearGradientBrush(rect,
                    pressed ? Color.FromArgb(90, 190, 60) : Color.FromArgb(52, 52, 55),
                    pressed ? Color.FromArgb(40, 110, 25) : Color.FromArgb(22, 22, 24), 70f))
                    g.FillEllipse(face, rect);
                using (var pen = new Pen(pressed ? Green : Color.FromArgb(70, 70, 74), 2f))
                    g.DrawEllipse(pen, rect);

                // arrow glyph on center-cluster buttons
                if (s.Arrow)
                {
                    using (var br = new SolidBrush(pressed ? Color.Black : GreenDim))
                    using (var f = new Font("Segoe UI Symbol", rr * 0.5f, FontStyle.Bold))
                    {
                        var sz = g.MeasureString(s.ArrowGlyph, f);
                        g.DrawString(s.ArrowGlyph, f, br, x - sz.Width / 2f, y - sz.Height / 2f + 1);
                    }
                }

                // label above
                if (!s.Arrow)
                {
                    string label = entry != null && entry.Label.Length > 0 ? entry.Label : "";
                    if (label.Length > 0)
                    {
                        using (var br = new SolidBrush(Green))
                        {
                            var sz = g.MeasureString(label.ToUpperInvariant(), labelFont);
                            g.DrawString(label.ToUpperInvariant(), labelFont, br, x - sz.Width / 2f, y - rr - sz.Height - 4);
                        }
                    }
                }

                // key caption below
                string cap = mapped ? entry.KeysText.ToUpperInvariant() : "--";
                using (var br = new SolidBrush(pressed ? Green : GreenDim))
                {
                    var sz = g.MeasureString(cap, keyFont);
                    g.DrawString(cap, keyFont, br, x - sz.Width / 2f, y + rr + 4);
                }
            }

            // entries with no slot on the board (e.g. RT)
            if (Engine != null)
            {
                var slotInputs = new HashSet<string>();
                foreach (var s in slots) foreach (var i in s.Inputs) slotInputs.Add(i);
                float ty = board.Bottom - 22;
                foreach (var e in Engine.Entries)
                {
                    if (slotInputs.Contains(e.Input)) continue;
                    string txt = e.Input + " = " + e.KeysText + (e.Mode == "repeat" ? "  (repeat " + e.RepeatMs + "ms)" : "");
                    bool on = Engine.Pressed.Contains(e.Input);
                    using (var br = new SolidBrush(on ? Green : GreenDim))
                        g.DrawString(txt, smallFont, br, board.X + 14, ty);
                    ty -= 16;
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
        CheckBox chkScan;
        Label lblStatus;
        bool exiting;
        bool startMinimized;
        int statusTick;

        public MainForm(bool minimized)
        {
            startMinimized = minimized;
            Text = "GolfDeck";
            BackColor = Color.FromArgb(14, 14, 15);
            ClientSize = new Size(680, 560);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            board = new BoardPanel();
            board.Engine = engine;
            board.Bounds = new Rectangle(0, 0, 680, 470);
            Controls.Add(board);

            var bottom = new Panel();
            bottom.Bounds = new Rectangle(0, 470, 680, 90);
            bottom.BackColor = Color.FromArgb(20, 20, 22);
            Controls.Add(bottom);

            chkAutostart = new CheckBox();
            chkAutostart.Text = "Start with Windows";
            chkAutostart.ForeColor = Color.Gainsboro;
            chkAutostart.Location = new Point(16, 10);
            chkAutostart.AutoSize = true;
            chkAutostart.Checked = GetAutostart();
            chkAutostart.CheckedChanged += delegate { SetAutostart(chkAutostart.Checked); };
            bottom.Controls.Add(chkAutostart);

            chkScan = new CheckBox();
            chkScan.Text = "Send as scancodes (try if game ignores keys)";
            chkScan.ForeColor = Color.Gainsboro;
            chkScan.Location = new Point(16, 34);
            chkScan.AutoSize = true;
            chkScan.Checked = GetSetting("ScanCodes", 0) != 0;
            KeySender.UseScanCodes = chkScan.Checked;
            chkScan.CheckedChanged += delegate
            {
                KeySender.UseScanCodes = chkScan.Checked;
                SetSetting("ScanCodes", chkScan.Checked ? 1 : 0);
            };
            bottom.Controls.Add(chkScan);

            var btnEdit = MakeButton("Edit mapping", 470, 8);
            btnEdit.Click += delegate { Process.Start("notepad.exe", "\"" + Config.MappingPath + "\""); };
            bottom.Controls.Add(btnEdit);

            var btnReload = MakeButton("Reload mapping", 470, 38);
            btnReload.Click += delegate { LoadMapping(true); };
            bottom.Controls.Add(btnReload);

            lblStatus = new Label();
            lblStatus.ForeColor = Color.Gray;
            lblStatus.Location = new Point(16, 62);
            lblStatus.AutoSize = true;
            lblStatus.Text = "starting...";
            bottom.Controls.Add(lblStatus);

            // tray
            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Application;
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

        Button MakeButton(string text, int x, int y)
        {
            var b = new Button();
            b.Text = text;
            b.Bounds = new Rectangle(x, y, 180, 26);
            b.FlatStyle = FlatStyle.Flat;
            b.ForeColor = Color.Gainsboro;
            b.BackColor = Color.FromArgb(38, 38, 42);
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 74);
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
                string s = engine.Connected
                    ? "Controller: connected (P" + (engine.ControllerIndex + 1) + ")"
                    : "Controller: NOT FOUND";
                s += "   |   admin: " + (IsAdmin() ? "yes" : "no");
                s += "   |   keys sent: " + KeySender.KeysSent;
                lblStatus.Text = s;
                lblStatus.ForeColor = engine.Connected ? Color.FromArgb(150, 235, 100) : Color.IndianRed;
            }
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
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--minimized") minimized = true;
                else if (args[i] == "--screenshot" && i + 1 < args.Length) screenshot = args[++i];
            }

            if (!File.Exists(Config.MappingPath))
                File.WriteAllText(Config.MappingPath, Config.DefaultMapping);

            var form = new MainForm(minimized && screenshot == null);

            if (screenshot != null)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                form.Show();
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
