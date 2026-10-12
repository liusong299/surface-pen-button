// Core.cs
// Shared by the button program (PenButton.cs) and the settings window (PenButtonSettings.cs):
// the PenButton.ini model, action strings, key names, foreground-window info and Win32 calls.
// C# 5 / .NET Framework 4.x only.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class AppInfo
{
    public const string Version = "2.3.4";
    public const string Repo = "liusong299/surface-pen-button";   // GitHub owner/name, used for updates
    public const string MainExe = "PenButton.exe";
    public const string IniName = "PenButton.ini";
    public const string ClickExe = "PenClick.exe";
    public const string DoubleClickExe = "PenDoubleClick.exe";
    public const string HoldExe = "PenHold.exe";
}

// Very small UI-language helper: Chinese if Windows is set to Chinese, otherwise English.
static class L
{
    public static readonly bool SystemZh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
    public static bool Zh = SystemZh;
    public static string T(string zh, string en) { return Zh ? zh : en; }

    // Language setting in the ini: "zh", "en", or "auto" (follow Windows).
    public static void Apply(string setting)
    {
        string s = (setting ?? "").Trim().ToLowerInvariant();
        Zh = s == "zh" ? true : s == "en" ? false : SystemZh;
    }
}

// ---------------------------------------------------------------------------
// Gestures
// ---------------------------------------------------------------------------

static class Gestures
{
    public const string Click = "Click";
    public const string DoubleClick = "DoubleClick";
    public const string Hold = "Hold";
    public static readonly string[] All = { Click, DoubleClick, Hold };

    public static string Label(string g)
    {
        if (g == Click) return L.T("单击", "Click");
        if (g == DoubleClick) return L.T("双击", "Double-click");
        if (g == Hold) return L.T("长按", "Press and hold");
        return g;
    }

    // A command-line argument wins (handy for testing), otherwise the exe name.
    public static string Detect(string exeName, string[] args)
    {
        if (args != null && args.Length > 0)
        {
            string g = FromWord(args[0]);
            if (g != null) return g;
        }
        return FromWord(exeName);
    }

    public static string FromWord(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        s = s.ToLowerInvariant();
        if (s.Contains("settings")) return null;
        if (s.Contains("double")) return DoubleClick;
        if (s.Contains("hold") || s.Contains("long") || s.Contains("press")) return Hold;
        if (s.Contains("click") || s.Contains("single")) return Click;
        return null;
    }
}

// ---------------------------------------------------------------------------
// Actions as written in the ini: "keys:Ctrl+Enter", "snippaste", "run:notepad.exe" ...
// ---------------------------------------------------------------------------

class ActionSpec
{
    public const string None = "none";
    public const string Keys = "keys";
    public const string SnipPaste = "snippaste";
    public const string Snip = "snip";
    public const string Paste = "paste";
    public const string Run = "run";
    public const string Raw = "raw";       // anything we do not understand; kept as written

    public string Type = None;
    public string Arg = "";

    public static ActionSpec Parse(string s)
    {
        ActionSpec a = new ActionSpec();
        string t = (s ?? "").Trim();
        string lower = t.ToLowerInvariant();
        if (lower.Length == 0 || lower == "none") { a.Type = None; }
        else if (lower.StartsWith("keys:")) { a.Type = Keys; a.Arg = t.Substring(5).Trim(); }
        else if (lower == "snippaste") { a.Type = SnipPaste; }
        else if (lower == "snip") { a.Type = Snip; }
        else if (lower == "paste") { a.Type = Paste; }
        else if (lower.StartsWith("run:")) { a.Type = Run; a.Arg = t.Substring(4).Trim(); }
        else { a.Type = Raw; a.Arg = t; }
        return a;
    }

    public override string ToString()
    {
        if (Type == Keys) return "keys:" + Arg;
        if (Type == Run) return "run:" + Arg;
        if (Type == Raw) return Arg;
        return Type;
    }

    public string Describe()
    {
        if (Type == None) return L.T("无动作", "Nothing");
        if (Type == Keys) return L.T("按键 ", "Keys ") + Arg;
        if (Type == SnipPaste) return L.T("截图/粘贴交替", "Snip / paste");
        if (Type == Snip) return L.T("截图", "Snip");
        if (Type == Paste) return L.T("粘贴", "Paste");
        if (Type == Run) return L.T("运行 ", "Run ") + Path.GetFileName(Arg.Trim('"'));
        return Arg;
    }

    // Returns null when the action is valid.
    public string Problem()
    {
        if (Type == Keys)
        {
            if (Arg.Length == 0) return L.T("还没有设置按键", "No key set");
            return KeyNames.Validate(Arg);
        }
        if (Type == Run && Arg.Length == 0) return L.T("还没有选择程序", "No program chosen");
        if (Type == Raw) return L.T("无法识别的动作：", "Unknown action: ") + Arg;
        return null;
    }
}

// ---------------------------------------------------------------------------
// Key names used in "keys:" actions
// ---------------------------------------------------------------------------

struct KeyDef
{
    public byte Vk;
    public bool Extended;
    public byte Scan;   // 0 = ask Windows (MapVirtualKey)
    public KeyDef(byte vk, bool ext) { Vk = vk; Extended = ext; Scan = 0; }
    public KeyDef(byte vk, bool ext, byte scan) { Vk = vk; Extended = ext; Scan = scan; }

    public bool IsModifier
    {
        get { return Vk == 0x10 || Vk == 0x11 || Vk == 0x12 || (Vk >= 0xA0 && Vk <= 0xA5) || Vk == 0x5B || Vk == 0x5C; }
    }
}

static class KeyNames
{
    public static readonly Dictionary<string, KeyDef> Map = Build();

    static Dictionary<string, KeyDef> Build()
    {
        Dictionary<string, KeyDef> d = new Dictionary<string, KeyDef>(StringComparer.OrdinalIgnoreCase);
        d["Ctrl"] = new KeyDef(0x11, false); d["Control"] = d["Ctrl"];
        d["LCtrl"] = new KeyDef(0xA2, false); d["RCtrl"] = new KeyDef(0xA3, true);
        d["Shift"] = new KeyDef(0x10, false); d["LShift"] = new KeyDef(0xA0, false, 0x2A); d["RShift"] = new KeyDef(0xA1, false, 0x36);
        d["Alt"] = new KeyDef(0x12, false); d["LAlt"] = new KeyDef(0xA4, false); d["RAlt"] = new KeyDef(0xA5, true);
        d["Win"] = new KeyDef(0x5B, true); d["LWin"] = d["Win"]; d["RWin"] = new KeyDef(0x5C, true);
        d["Enter"] = new KeyDef(0x0D, false); d["Return"] = d["Enter"];
        d["Tab"] = new KeyDef(0x09, false);
        d["Esc"] = new KeyDef(0x1B, false); d["Escape"] = d["Esc"];
        d["Space"] = new KeyDef(0x20, false);
        d["Backspace"] = new KeyDef(0x08, false);
        d["Delete"] = new KeyDef(0x2E, true); d["Del"] = d["Delete"];
        d["Insert"] = new KeyDef(0x2D, true); d["Ins"] = d["Insert"];
        d["Home"] = new KeyDef(0x24, true); d["End"] = new KeyDef(0x23, true);
        d["PageUp"] = new KeyDef(0x21, true); d["PgUp"] = d["PageUp"];
        d["PageDown"] = new KeyDef(0x22, true); d["PgDn"] = d["PageDown"];
        d["Left"] = new KeyDef(0x25, true); d["Up"] = new KeyDef(0x26, true);
        d["Right"] = new KeyDef(0x27, true); d["Down"] = new KeyDef(0x28, true);
        d["Apps"] = new KeyDef(0x5D, true); d["Menu"] = d["Apps"];
        d["PrintScreen"] = new KeyDef(0x2C, true);
        d["CapsLock"] = new KeyDef(0x14, false);
        for (int i = 1; i <= 24; i++) d["F" + i] = new KeyDef((byte)(0x70 + i - 1), false);
        for (char ch = 'A'; ch <= 'Z'; ch++) d[ch.ToString()] = new KeyDef((byte)ch, false);
        for (char ch = '0'; ch <= '9'; ch++) d[ch.ToString()] = new KeyDef((byte)ch, false);
        return d;
    }

    // Returns null if every part of the combo is a known key name.
    public static string Validate(string combo)
    {
        if (string.IsNullOrEmpty(combo)) return L.T("按键为空", "Empty key");
        foreach (string part in combo.Split('+'))
        {
            string p = part.Trim();
            if (p.Length == 0) return L.T("按键组合里有空位：", "Empty key name in ") + combo;
            if (!Map.ContainsKey(p)) return L.T("不认识的按键：", "Unknown key: ") + p;
        }
        return null;
    }

    public static List<KeyDef> ParseCombo(string combo)
    {
        string err = Validate(combo);
        if (err != null) throw new ArgumentException(err);
        List<KeyDef> keys = new List<KeyDef>();
        foreach (string part in combo.Split('+')) keys.Add(Map[part.Trim()]);
        return keys;
    }
}

// ---------------------------------------------------------------------------
// PenButton.ini
// ---------------------------------------------------------------------------

class Rule
{
    public string Kind = "app";   // "app", "class", "title" or "default"
    public string[] Patterns = new string[0];
    public string Comment = "";
    public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Name
    {
        get { return Kind == "default" ? "Default" : Kind + ":" + string.Join("|", Patterns); }
    }

    public static string KindLabel(string kind)
    {
        if (kind == "app") return L.T("程序名", "Program");
        if (kind == "class") return L.T("窗口类名", "Window class");
        if (kind == "title") return L.T("窗口标题包含", "Title contains");
        return kind;
    }

    public bool Matches(WindowInfo w)
    {
        if (Kind == "default") return true;
        foreach (string p in Patterns)
        {
            if (p.Length == 0) continue;
            if (Kind == "app")
            {
                string a = w.App ?? "";
                if (string.Equals(a, p, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(a, p + ".exe", StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (Kind == "class")
            {
                if (string.Equals(w.ClassName ?? "", p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (Kind == "title")
            {
                if ((w.Title ?? "").IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
        }
        return false;
    }

    public Rule Clone()
    {
        Rule r = new Rule();
        r.Kind = Kind;
        r.Patterns = (string[])Patterns.Clone();
        r.Comment = Comment;
        foreach (KeyValuePair<string, string> kv in Values) r.Values[kv.Key] = kv.Value;
        return r;
    }
}

class SettingDef
{
    public string Key, Default, Comment;
    public SettingDef(string key, string def, string comment) { Key = key; Default = def; Comment = comment; }
}

class Config
{
    public Dictionary<string, string> Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public List<Rule> Rules = new List<Rule>();   // in file order, without [Default]
    public Rule Default;
    public List<string> Warnings = new List<string>();

    public static readonly SettingDef[] KnownSettings =
    {
        new SettingDef("PreDelayMs", "100", "Wait before acting, so the pen-button event can finish (ms)"),
        new SettingDef("KeyHoldMs", "40", "How long keys are held down (ms). Raise it if an app misses short presses."),
        new SettingDef("SnipMode", "window", "window = the window you are working in, screen = the whole monitor"),
        new SettingDef("SnipSound", "false", "Play a short system sound after a screenshot"),
        new SettingDef("SnipResetSeconds", "0", "If the last screenshot is older than this (seconds), the next snippaste takes a new\n" +
                                               "screenshot instead of pasting. 0 = always alternate."),
        new SettingDef("Toast", "true", "Small label near the pen after a screenshot or paste"),
        new SettingDef("ToastMs", "800", "How long the label stays (ms)"),
        new SettingDef("ToastSnipText", "", "Label text. Leave empty to follow the Windows language (Chinese or English)."),
        new SettingDef("ToastPasteText", "", null),
        new SettingDef("Language", "auto", "auto = follow Windows, zh = 中文, en = English"),
        new SettingDef("AutoUpdate", "true", "Check GitHub for a new PenButton.exe when the settings window opens, and download it"),
        new SettingDef("Log", "false", "Write every press (program, window class, title, action) to PenButton.log"),
    };

    const string Header =
@"; PenButton settings. Edit with PenButton.exe, or with Notepad.
; Changes apply on the next pen-button press; no rebuild needed.
; Lines that start with ; are comments.
;
; ACTIONS
;   keys:<combo>    press a key or combo, e.g. keys:Enter  keys:Ctrl+Enter  keys:RAlt  keys:Right
;   snippaste       1st press: screenshot to clipboard, 2nd press: paste (Ctrl+V), and so on
;   snip            screenshot to clipboard every time
;   paste           paste (Ctrl+V)
;   run:<program>   start a program, e.g. run:notepad.exe
;   none            do nothing
;
; GESTURES (keys inside a section): Click  DoubleClick  Hold
;
; RULES
;   Sections are checked from top to bottom. The first one that matches the window you
;   are working in AND sets that gesture is used. Otherwise [Default] is used.
;   [app:OUTLOOK.EXE]       match by program name (several: [app:a.exe|b.exe])
;   [class:screenClass]     match by window class name
;   [title:Gmail]           match when the window title contains this text
;
;   Example, for apps that send with Ctrl+Enter:
;     [app:OUTLOOK.EXE|olk.exe]
;     Hold=keys:Ctrl+Enter
";

    public static Config CreateDefault()
    {
        Config c = new Config();
        foreach (SettingDef s in KnownSettings) c.Settings[s.Key] = s.Default;

        // No per-app rules by default. (PowerPoint has its own pen-button support in slide
        // shows: click = next, hold = previous, while Windows lets apps override the button.)

        c.Default = new Rule();
        c.Default.Kind = "default";
        c.Default.Values[Gestures.Click] = "keys:RAlt";
        c.Default.Values[Gestures.DoubleClick] = "snippaste";
        c.Default.Values[Gestures.Hold] = "keys:Enter";
        return c;
    }

    public static string DefaultText() { return CreateDefault().ToIni(); }

    // Reads the ini. If it is missing, writes the default one (when create is true).
    public static Config Load(string path, bool create)
    {
        string text = null;
        try { if (File.Exists(path)) text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception) { }

        if (text == null)
        {
            Config d = CreateDefault();
            if (create) { try { d.Save(path); } catch (Exception) { } }
            return d;
        }
        return Parse(text);
    }

    public void Save(string path)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, ToIni(), new UTF8Encoding(true));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    public static Config Parse(string text)
    {
        Config c = new Config();
        Rule current = null;
        bool inSettings = false;
        List<string> pendingComments = new List<string>();

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) { pendingComments.Clear(); continue; }
            if (line[0] == ';' || line[0] == '#')
            {
                pendingComments.Add(line.Substring(1).Trim());
                continue;
            }

            if (line[0] == '[' && line[line.Length - 1] == ']')
            {
                string name = line.Substring(1, line.Length - 2).Trim();
                current = null;
                inSettings = false;
                string comment = string.Join("\n", pendingComments.ToArray());
                pendingComments.Clear();

                if (name.Equals("Settings", StringComparison.OrdinalIgnoreCase)) { inSettings = true; continue; }
                if (name.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    if (c.Default == null) { c.Default = new Rule(); c.Default.Kind = "default"; }
                    current = c.Default;
                    continue;
                }

                int colon = name.IndexOf(':');
                string kind = colon > 0 ? name.Substring(0, colon).Trim().ToLowerInvariant() : "";
                if (kind != "app" && kind != "class" && kind != "title")
                {
                    c.Warnings.Add("Line " + (i + 1) + ": unknown section [" + name + "], ignored.");
                    continue;
                }
                Rule r = new Rule();
                r.Kind = kind;
                r.Comment = comment;
                List<string> pats = new List<string>();
                foreach (string p in name.Substring(colon + 1).Split('|'))
                    if (p.Trim().Length > 0) pats.Add(p.Trim());
                r.Patterns = pats.ToArray();
                c.Rules.Add(r);
                current = r;
                continue;
            }

            pendingComments.Clear();
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                c.Warnings.Add("Line " + (i + 1) + ": not 'key=value', ignored.");
                continue;
            }
            string key = line.Substring(0, eq).Trim();
            string val = line.Substring(eq + 1).Trim();

            if (inSettings) c.Settings[key] = val;
            else if (current != null) current.Values[key] = val;
        }

        if (c.Default == null) c.Default = CreateDefault().Default;
        c.DropRemovedFeatures();
        return c;
    }

    // Version 2.0 briefly had a laser pointer. Its action and settings are no longer known;
    // drop them so old ini files load and save without errors (the gesture falls back to [Default]).
    void DropRemovedFeatures()
    {
        List<Rule> all = new List<Rule>(Rules);
        if (Default != null) all.Add(Default);
        foreach (Rule r in all)
            foreach (string g in Gestures.All)
            {
                string v;
                if (r.Values.TryGetValue(g, out v) && v.Trim().Equals("laser", StringComparison.OrdinalIgnoreCase)) r.Values.Remove(g);
            }
        List<string> drop = new List<string>();
        foreach (string k in Settings.Keys) if (k.StartsWith("Laser", StringComparison.OrdinalIgnoreCase)) drop.Add(k);
        foreach (string k in drop) Settings.Remove(k);
        foreach (Rule r in all)
            if (r.Comment != null && r.Comment.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0)
                r.Comment = r.Comment.Replace(",\npress and hold = laser pointer on/off", "").Replace("\npress and hold = laser pointer on/off", "").Replace(", press and hold = laser pointer on/off", "").TrimEnd(',', ' ');
    }

    public string ToIni()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(Header.Replace("\r\n", "\n"));
        sb.Append("\n[Settings]\n");
        HashSet<string> written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SettingDef s in KnownSettings)
        {
            if (s.Comment != null) AppendComment(sb, s.Comment);
            sb.Append(s.Key).Append('=').Append(GetString(s.Key, s.Default, true)).Append('\n');
            written.Add(s.Key);
        }
        foreach (KeyValuePair<string, string> kv in Settings)
            if (!written.Contains(kv.Key)) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');

        foreach (Rule r in Rules)
        {
            if (r.Patterns.Length == 0) continue;
            sb.Append('\n');
            if (!string.IsNullOrEmpty(r.Comment)) AppendComment(sb, r.Comment);
            AppendRule(sb, r);
        }
        sb.Append('\n');
        AppendRule(sb, Default ?? CreateDefault().Default);
        return sb.ToString().Replace("\n", "\r\n");
    }

    static void AppendComment(StringBuilder sb, string comment)
    {
        foreach (string l in comment.Replace("\r\n", "\n").Split('\n')) sb.Append("; ").Append(l).Append('\n');
    }

    static void AppendRule(StringBuilder sb, Rule r)
    {
        sb.Append('[').Append(r.Name).Append("]\n");
        foreach (string g in Gestures.All)
        {
            string v;
            if (r.Values.TryGetValue(g, out v)) sb.Append(g).Append('=').Append(v).Append('\n');
        }
        foreach (KeyValuePair<string, string> kv in r.Values)
            if (Array.IndexOf(Gestures.All, kv.Key) < 0) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
    }

    // First rule (top to bottom) that matches the window and sets this gesture wins,
    // otherwise [Default]. Returns "none" if nothing is set.
    public string Resolve(string gesture, WindowInfo w, out string ruleName)
    {
        string v;
        foreach (Rule r in Rules)
        {
            if (r.Matches(w) && r.Values.TryGetValue(gesture, out v))
            {
                ruleName = r.Name;
                return v;
            }
        }
        if (Default != null && Default.Values.TryGetValue(gesture, out v))
        {
            ruleName = "Default";
            return v;
        }
        ruleName = "none";
        return "none";
    }

    public string GetString(string key, string def) { return GetString(key, def, false); }

    public string GetString(string key, string def, bool allowEmpty)
    {
        string v;
        if (Settings.TryGetValue(key, out v) && (allowEmpty || v.Length > 0)) return v;
        return def;
    }

    public int GetInt(string key, int def)
    {
        string v;
        int n;
        if (Settings.TryGetValue(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
            return n;
        return def;
    }

    public bool GetBool(string key, bool def)
    {
        string v;
        if (!Settings.TryGetValue(key, out v)) return def;
        v = v.Trim().ToLowerInvariant();
        if (v == "true" || v == "yes" || v == "1" || v == "on") return true;
        if (v == "false" || v == "no" || v == "0" || v == "off") return false;
        return def;
    }
}

// ---------------------------------------------------------------------------
// Windows
// ---------------------------------------------------------------------------

class WindowInfo
{
    public IntPtr Handle;
    public string App = "";        // process name with .exe, e.g. POWERPNT.EXE
    public string ClassName = "";
    public string Title = "";

    public static WindowInfo Foreground()
    {
        IntPtr h = IntPtr.Zero;
        try { h = Native.GetForegroundWindow(); } catch (Exception) { }
        return FromHandle(h);
    }

    public static WindowInfo FromHandle(IntPtr h)
    {
        WindowInfo w = new WindowInfo();
        w.Handle = h;
        if (h == IntPtr.Zero) return w;
        try
        {
            StringBuilder sb = new StringBuilder(256);
            Native.GetClassName(h, sb, sb.Capacity);
            w.ClassName = sb.ToString();

            StringBuilder tb = new StringBuilder(512);
            Native.GetWindowText(h, tb, tb.Capacity);
            w.Title = tb.ToString();

            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            using (Process p = Process.GetProcessById((int)pid))
                w.App = p.ProcessName + ".exe";
        }
        catch (Exception) { }
        return w;
    }

    // Visible top-level windows that have a title (for the settings window's picker).
    public static List<WindowInfo> ListOpen()
    {
        List<WindowInfo> list = new List<WindowInfo>();
        int me = Process.GetCurrentProcess().Id;
        try
        {
            Native.EnumWindows(delegate (IntPtr h, IntPtr lp)
            {
                if (!Native.IsWindowVisible(h)) return true;
                if (Native.GetWindowTextLength(h) == 0) return true;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if ((int)pid == me) return true;
                WindowInfo w = FromHandle(h);
                if (!w.IsShell()) list.Add(w);
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception) { }
        return list;
    }

    public bool IsShell()
    {
        string c = ClassName;
        return c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd";
    }
}

static class Native
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT rect, int size);
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // Surface screens use 150-200% scaling. Without this, coordinates would be
    // scaled and screenshots cropped or blurry.
    public static void EnableDpiAwareness(bool perMonitor)
    {
        bool ok = false;
        if (perMonitor) { try { ok = SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (Exception) { } }
        if (!ok) { try { SetProcessDPIAware(); } catch (Exception) { } }
    }

    public static float DpiAt(Point p)
    {
        try
        {
            POINT pt; pt.X = p.X; pt.Y = p.Y;
            IntPtr mon = MonitorFromPoint(pt, 2);   // MONITOR_DEFAULTTONEAREST
            uint dx, dy;
            if (GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx;
        }
        catch (Exception) { }
        try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX; }
        catch (Exception) { return 96f; }
    }
}
