// PenButton.cs
// The program the Surface Pen top button starts.
//
// Windows lets each pen gesture (single click, double click, press and hold)
// start one .exe, without arguments. So this program is built under three names
// and reads its own file name to know which gesture fired:
//   PenClick.exe        single click
//   PenDoubleClick.exe  double click
//   PenHold.exe         press and hold
// What each gesture does comes from PenButton.ini next to the exe
// (edit it with PenButtonSettings.exe or Notepad).
//
// Built by build.bat together with Core.cs. C# 5 / .NET Framework 4.x only.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class PenButton
{
    [STAThread]
    static int Main(string[] args)
    {
        Native.EnableDpiAwareness(true);

        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string exeName = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
        string gesture = Gestures.Detect(exeName, args);

        Config cfg = Config.Load(Path.Combine(exeDir, AppInfo.IniName), true);
        Log log = new Log(cfg.GetBool("Log", false), exeDir);
        foreach (string w in cfg.Warnings) log.Write("ini: " + w);

        if (gesture == null)
        {
            log.Write("Unknown gesture. The exe should be named PenClick, PenDoubleClick or PenHold (was '" + exeName + "').");
            return 2;
        }

        // Let the pen-button event finish before we look at the foreground window or send keys.
        Thread.Sleep(Math.Max(0, cfg.GetInt("PreDelayMs", 100)));

        WindowInfo fg = WindowInfo.Foreground();
        string rule;
        string action = cfg.Resolve(gesture, fg, out rule);
        log.Write(gesture + "  app=" + fg.App + "  class=" + fg.ClassName + "  title=" + fg.Title +
                  "  rule=[" + rule + "]  action=" + action);

        try
        {
            Actions.Run(ActionSpec.Parse(action), cfg, fg, log);
        }
        catch (Exception ex)
        {
            log.Write("Error: " + ex.Message);
            return 1;
        }
        return 0;
    }
}

static class Actions
{
    public static void Run(ActionSpec a, Config cfg, WindowInfo fg, Log log)
    {
        int holdMs = Math.Max(0, cfg.GetInt("KeyHoldMs", 40));

        if (a.Type == ActionSpec.None) return;
        if (a.Type == ActionSpec.Keys) { KeySender.Press(a.Arg, holdMs); return; }
        if (a.Type == ActionSpec.Paste)
        {
            KeySender.Press("Ctrl+V", holdMs);
            ShowToast(cfg, false);
            return;
        }
        if (a.Type == ActionSpec.Snip)
        {
            if (Snipper.Capture(cfg, fg)) ShowToast(cfg, true);
            return;
        }
        if (a.Type == ActionSpec.SnipPaste) { SnipPaste(cfg, fg, holdMs); return; }
        if (a.Type == ActionSpec.Run) { Launch(a.Arg); return; }
        log.Write("Unknown action '" + a.Arg + "'.");
    }

    // 1st press: screenshot, 2nd press: paste, 3rd press: screenshot, ...
    static void SnipPaste(Config cfg, WindowInfo fg, int holdMs)
    {
        if (SnipState.NextIsPaste(cfg.GetInt("SnipResetSeconds", 0)))
        {
            KeySender.Press("Ctrl+V", holdMs);
            SnipState.Save(false);
            ShowToast(cfg, false);
            return;
        }
        if (Snipper.Capture(cfg, fg))
        {
            SnipState.Save(true);   // before the toast, in case the next press comes quickly
            ShowToast(cfg, true);
        }
    }

    static void ShowToast(Config cfg, bool snip)
    {
        if (!cfg.GetBool("Toast", true)) return;
        string text = snip
            ? cfg.GetString("ToastSnipText", L.T("已截图", "Captured"))
            : cfg.GetString("ToastPasteText", L.T("已粘贴", "Pasted"));
        Toast.Show(text, snip, Math.Max(200, cfg.GetInt("ToastMs", 800)));
    }

    public static void Launch(string cmd)
    {
        cmd = (cmd ?? "").Trim();
        if (cmd.Length == 0) return;
        string file, args = "";
        if (cmd[0] == '"')
        {
            int end = cmd.IndexOf('"', 1);
            if (end < 0) file = cmd.Trim('"');
            else { file = cmd.Substring(1, end - 1); args = cmd.Substring(end + 1).Trim(); }
        }
        else if (File.Exists(cmd))
        {
            file = cmd;
        }
        else
        {
            int sp = cmd.IndexOf(' ');
            file = sp < 0 ? cmd : cmd.Substring(0, sp);
            args = sp < 0 ? "" : cmd.Substring(sp + 1).Trim();
        }
        ProcessStartInfo psi = new ProcessStartInfo(file, args);
        psi.UseShellExecute = true;
        Process.Start(psi);
    }
}

static class KeySender
{
    // "Ctrl+Enter": press Ctrl, press Enter, wait holdMs, release Enter, release Ctrl.
    public static void Press(string combo, int holdMs)
    {
        List<KeyDef> keys = KeyNames.ParseCombo(combo);
        for (int i = 0; i < keys.Count; i++)
        {
            Send(keys[i], false);
            if (i < keys.Count - 1) Thread.Sleep(15);
        }
        Thread.Sleep(holdMs);
        for (int i = keys.Count - 1; i >= 0; i--)
        {
            Send(keys[i], true);
            if (i > 0) Thread.Sleep(15);
        }
    }

    static void Send(KeyDef k, bool up)
    {
        byte scan = (byte)Native.MapVirtualKey(k.Vk, 0);
        uint flags = 0;
        if (k.Extended) flags |= Native.KEYEVENTF_EXTENDEDKEY;
        if (up) flags |= Native.KEYEVENTF_KEYUP;
        Native.keybd_event(k.Vk, scan, flags, UIntPtr.Zero);
    }
}

static class Snipper
{
    // Returns true if the image is on the clipboard.
    public static bool Capture(Config cfg, WindowInfo fg)
    {
        string mode = cfg.GetString("SnipMode", "window").ToLowerInvariant();
        Rectangle area = CaptureArea(fg, mode);
        if (area.Width <= 0 || area.Height <= 0) return false;

        bool ok = false;
        using (Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
                g.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size, CopyPixelOperation.SourceCopy);

            // The clipboard can be briefly locked by another app, so retry a few times.
            // SetImage keeps the image on the clipboard after this program exits.
            for (int i = 0; i < 10; i++)
            {
                try { Clipboard.SetImage(bmp); ok = true; break; }
                catch (ExternalException) { Thread.Sleep(100); }
            }
        }

        if (ok && cfg.GetBool("SnipSound", false))
        {
            System.Media.SystemSounds.Asterisk.Play();
            Thread.Sleep(300);
        }
        return ok;
    }

    static Rectangle CaptureArea(WindowInfo fg, string mode)
    {
        IntPtr h = fg.Handle;
        Screen monitor = (h != IntPtr.Zero) ? Screen.FromHandle(h) : Screen.PrimaryScreen;

        if (mode != "window" || h == IntPtr.Zero || Native.IsIconic(h) || fg.IsShell())
            return monitor.Bounds;

        Native.RECT r;
        // Visible window bounds without the invisible resize border / shadow
        if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0)
            Native.GetWindowRect(h, out r);

        Rectangle area = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        area.Intersect(SystemInformation.VirtualScreen);   // drop parts that are off screen
        if (area.Width <= 0 || area.Height <= 0) return monitor.Bounds;
        return area;
    }
}

// Remembers whether the next "snippaste" should paste.
// The file exists only between a screenshot and the following paste,
// and holds the screenshot time (UTC ticks).
static class SnipState
{
    public static string FilePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PenButton");
        return Path.Combine(dir, "PenSnip.state");
    }

    public static bool NextIsPaste(int resetSeconds)
    {
        try
        {
            string p = FilePath();
            if (!File.Exists(p)) return false;
            if (resetSeconds <= 0) return true;

            long ticks;
            if (!long.TryParse(File.ReadAllText(p).Trim(), out ticks)) return true;
            TimeSpan age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            return age.TotalSeconds <= resetSeconds;
        }
        catch (Exception) { return false; }   // if anything goes wrong, take a screenshot
    }

    public static void Save(bool nextIsPaste)
    {
        try
        {
            string p = FilePath();
            if (nextIsPaste)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (File.Exists(p))
            {
                File.Delete(p);
            }
        }
        catch (Exception) { }
    }
}

// A small label near the pen. It never takes focus and lets clicks pass through.
class Toast : Form
{
    readonly string text;
    readonly Color dot;
    readonly Font font;
    readonly float scale;
    readonly int lifeMs;
    readonly System.Windows.Forms.Timer timer;
    readonly Stopwatch clock = new Stopwatch();
    const double MaxOpacity = 0.94;
    const int FadeMs = 220;

    public static void Show(string text, bool snip, int lifeMs)
    {
        try
        {
            using (Toast t = new Toast(text, snip, lifeMs))
                Application.Run(t);
        }
        catch (Exception) { }
    }

    Toast(string text, bool snip, int lifeMs)
    {
        this.text = text;
        this.lifeMs = lifeMs;
        dot = snip ? Color.FromArgb(76, 194, 125) : Color.FromArgb(96, 165, 250);

        Point at = Cursor.Position;
        scale = Native.DpiAt(at) / 96f;
        font = new Font(L.Zh ? "Microsoft YaHei UI" : "Segoe UI", 14f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(32, 32, 32);
        Opacity = MaxOpacity;
        DoubleBuffered = true;

        Size ts = TextRenderer.MeasureText(text, font);
        int padX = (int)(14 * scale), padY = (int)(8 * scale), dotD = (int)(8 * scale), gap = (int)(8 * scale);
        Size = new Size(padX + dotD + gap + ts.Width + padX, Math.Max(ts.Height, dotD) + padY * 2);

        // Below-right of the pen, kept inside the screen.
        Rectangle wa = Screen.FromPoint(at).WorkingArea;
        int x = at.X + (int)(18 * scale), y = at.Y + (int)(18 * scale);
        if (x + Width > wa.Right) x = at.X - Width - (int)(10 * scale);
        if (y + Height > wa.Bottom) y = at.Y - Height - (int)(10 * scale);
        x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
        y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height));
        Location = new Point(x, y);

        timer = new System.Windows.Forms.Timer();
        timer.Interval = 15;
        timer.Tick += OnTick;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int round = Native.DWMWCP_ROUND;   // rounded corners on Windows 11
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }
        catch (Exception) { }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        clock.Start();
        timer.Start();
    }

    void OnTick(object sender, EventArgs e)
    {
        long t = clock.ElapsedMilliseconds;
        if (t >= lifeMs) { timer.Stop(); Close(); return; }
        long fadeStart = lifeMs - FadeMs;
        if (t > fadeStart) Opacity = MaxOpacity * (lifeMs - t) / (double)FadeMs;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int padX = (int)(14 * scale), dotD = (int)(8 * scale), gap = (int)(8 * scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (SolidBrush b = new SolidBrush(dot))
            g.FillEllipse(b, padX, (Height - dotD) / 2f, dotD, dotD);
        Rectangle tr = new Rectangle(padX + dotD + gap, 0, Width - (padX + dotD + gap), Height);
        TextRenderer.DrawText(g, text, font, tr, Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); font.Dispose(); }
        base.Dispose(disposing);
    }
}

// Only when Log=true: PenButton.log next to the exe.
class Log
{
    readonly bool on;
    readonly string path;

    public Log(bool on, string exeDir)
    {
        this.on = on;
        path = Path.Combine(exeDir, "PenButton.log");
    }

    public void Write(string msg)
    {
        if (!on) return;
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) +
                      "  " + msg + Environment.NewLine;
        try { File.AppendAllText(path, line, Encoding.UTF8); }
        catch (Exception)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PenButton");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "PenButton.log"), line, Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }
}
