// PenButtonSettings.cs
// The settings window shown by PenButton.exe (WPF). Reads and writes PenButton.ini in the
// same folder, and writes the three small button programs next to it.
// The layout and styles are in SettingsWindow.xaml, embedded as a resource and loaded
// with XamlReader, so everything builds with the csc.exe that ships with Windows.
// C# 5 / .NET Framework 4.x only.
//
// PenButton.exe --render <folder> [--lang zh|en] [--dir <folder>]
//   writes a PNG of every page (used by CI to check the layout without a screen).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;

static class PenButtonSettings
{
    public static string ForcedLanguage;   // --lang zh|en (screenshots)
    public static bool JustUpdated;        // started by the updater

    public static int Run(string[] args)
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string render = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--render" && i + 1 < args.Length) render = args[++i];
            else if (args[i] == "--lang" && i + 1 < args.Length) ForcedLanguage = args[++i];
            else if (args[i] == "--dir" && i + 1 < args.Length) dir = args[++i];
            else if (args[i] == "--updated") JustUpdated = true;
            else if (args[i] == "--pretend-version" && i + 1 < args.Length) Updater.Override = new Version(args[++i]);
        }
        if (Array.IndexOf(args, "--update-selftest") >= 0) return Updater.SelfTest();

        Application app = new Application();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        try
        {
            SettingsUi ui = new SettingsUi(dir, render != null);
            if (render != null)
            {
                Directory.CreateDirectory(render);
                ui.RenderAll(render);
                return 0;
            }
            app.Run(ui.Win);
        }
        catch (Exception ex)
        {
            if (render != null)
            {
                Directory.CreateDirectory(render);
                File.WriteAllText(IOPath.Combine(render, "error.txt"), ex.ToString());
                Console.Error.WriteLine(ex.ToString());
            }
            else
            {
                MessageBox.Show(ex.ToString(), "PenButton");
            }
            return 1;
        }
        return 0;
    }
}

// ---------------------------------------------------------------------------
// Small UI helpers
// ---------------------------------------------------------------------------

static class Ui
{
    public static readonly Brush Accent = Hex("#4F46E5");
    public static readonly Brush AccentSoft = Hex("#EEF0FF");
    public static readonly Brush Text1 = Hex("#1C1C1E");
    public static readonly Brush Text2 = Hex("#6E6E73");
    public static readonly Brush Text3 = Hex("#A1A1A6");
    public static readonly Brush Line = Hex("#ECECF0");
    public static readonly Brush Field = Hex("#DCDCE2");
    public static readonly Brush Danger = Hex("#DC2626");
    public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas");
    public static FrameworkElement Root;   // for style lookups

    public static SolidColorBrush Hex(string s)
    {
        SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));
        b.Freeze();
        return b;
    }

    public static Style S(string key) { return (Style)Root.FindResource(key); }

    public static TextBlock Text(string s, string style)
    {
        TextBlock t = new TextBlock();
        t.Text = s;
        if (style != null) t.Style = S(style);
        return t;
    }

    public static TextBlock Icon(string glyph, double size, Brush fg)
    {
        TextBlock t = new TextBlock();
        t.Text = glyph;
        t.FontFamily = Icons;
        t.FontSize = size;
        t.Foreground = fg;
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    public static Button IconButton(string glyph, string tip, RoutedEventHandler click)
    {
        Button b = new Button();
        b.Style = S("IconButton");
        b.Content = glyph;
        b.ToolTip = tip;
        b.Click += click;
        return b;
    }

    public static Border RowBorder(bool first)
    {
        Border b = new Border();
        b.Style = S(first ? "FirstRow" : "Row");
        return b;
    }

    // One dot = click, two dots = double-click, a bar = press and hold.
    public static Border GestureIcon(string gesture, double box)
    {
        Border b = new Border();
        b.Width = box;
        b.Height = box;
        b.CornerRadius = new CornerRadius(box * 0.27);
        b.Background = Hex("#F1F1F5");
        StackPanel p = new StackPanel();
        p.Orientation = Orientation.Horizontal;
        p.HorizontalAlignment = HorizontalAlignment.Center;
        p.VerticalAlignment = VerticalAlignment.Center;
        double d = Math.Round(box * 0.22);
        if (gesture == Gestures.Hold)
        {
            Border bar = new Border();
            bar.Width = Math.Round(box * 0.55);
            bar.Height = d;
            bar.CornerRadius = new CornerRadius(d / 2);
            bar.Background = Accent;
            p.Children.Add(bar);
        }
        else
        {
            int n = gesture == Gestures.DoubleClick ? 2 : 1;
            for (int i = 0; i < n; i++)
            {
                Ellipse e = new Ellipse();
                e.Width = d;
                e.Height = d;
                e.Fill = Accent;
                if (i > 0) e.Margin = new Thickness(Math.Round(d * 0.5), 0, 0, 0);
                p.Children.Add(e);
            }
        }
        b.Child = p;
        return b;
    }

    public static string GestureHint(string g)
    {
        if (g == Gestures.Click) return L.T("轻按一下顶部按钮", "Press once");
        if (g == Gestures.DoubleClick) return L.T("快速按两下", "Press twice quickly");
        return L.T("按住约一秒", "Hold for about a second");
    }

    public static void NumbersOnly(TextBox t)
    {
        t.PreviewTextInput += delegate (object s, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text) if (c < '0' || c > '9') { e.Handled = true; return; }
        };
        DataObject.AddPastingHandler(t, delegate (object s, DataObjectPastingEventArgs e) { e.CancelCommand(); });
        InputMethod.SetIsInputMethodEnabled(t, false);
    }

    public static string KeyLabel(string k)
    {
        switch (k.ToLowerInvariant())
        {
            case "ralt": return L.T("右 Alt", "Right Alt");
            case "rctrl": return L.T("右 Ctrl", "Right Ctrl");
            case "rshift": return L.T("右 Shift", "Right Shift");
            case "left": return "←";
            case "right": return "→";
            case "up": return "↑";
            case "down": return "↓";
            case "space": return L.T("空格", "Space");
        }
        return k;
    }

    public static Border Keycap(string text)
    {
        Border b = new Border();
        b.Background = Brushes.White;
        b.BorderBrush = Hex("#D2D2D9");
        b.BorderThickness = new Thickness(1, 1, 1, 2);
        b.CornerRadius = new CornerRadius(5);
        b.Padding = new Thickness(7, 1, 7, 1);
        b.Margin = new Thickness(2, 0, 2, 0);
        b.VerticalAlignment = VerticalAlignment.Center;
        TextBlock t = new TextBlock();
        t.Text = text;
        t.FontSize = 12;
        t.FontWeight = FontWeights.SemiBold;
        t.Foreground = Hex("#3A3A3F");
        b.Child = t;
        return b;
    }

    public static Border Chip(string text, Brush bg, Brush fg)
    {
        Border b = new Border();
        b.Background = bg;
        b.CornerRadius = new CornerRadius(6);
        b.Padding = new Thickness(8, 3, 8, 3);
        TextBlock t = new TextBlock();
        t.Text = text;
        t.FontSize = 12;
        t.FontWeight = FontWeights.Medium;
        t.Foreground = fg;
        b.Child = t;
        return b;
    }
}

// ---------------------------------------------------------------------------
// A box that records a key or key combo (shown as keycaps)
// ---------------------------------------------------------------------------

class KeyRecorder : Border
{
    public static bool AnyRecording;
    readonly WrapPanel caps = new WrapPanel();
    readonly List<string> held = new List<string>();
    bool other;
    string combo = "";
    public event EventHandler Changed;

    public KeyRecorder()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Height = 34;
        MinWidth = 130;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(6, 0, 6, 0);
        HorizontalAlignment = HorizontalAlignment.Right;
        caps.VerticalAlignment = VerticalAlignment.Center;
        caps.HorizontalAlignment = HorizontalAlignment.Center;
        Child = caps;
        InputMethod.SetIsInputMethodEnabled(this, false);
        ToolTip = L.T("点一下，然后在键盘上按想要的键", "Click, then press the key or combo");
        MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { Focus(); Keyboard.Focus(this); e.Handled = true; };
        GotKeyboardFocus += delegate { AnyRecording = true; held.Clear(); other = false; Refresh(); };
        LostKeyboardFocus += delegate { AnyRecording = false; Refresh(); };
        Refresh();
    }

    public string Combo
    {
        get { return combo; }
        set { combo = value ?? ""; Refresh(); }
    }

    bool Recording { get { return IsKeyboardFocused; } }

    void Refresh()
    {
        caps.Children.Clear();
        if (Recording)
        {
            Background = Ui.AccentSoft;
            BorderBrush = Ui.Accent;
            TextBlock t = new TextBlock();
            t.Text = L.T("请按下按键…", "Press keys…");
            t.Foreground = Ui.Accent;
            t.Margin = new Thickness(6, 0, 6, 0);
            caps.Children.Add(t);
            return;
        }
        Background = Ui.Hex("#F7F7F9");
        BorderBrush = Ui.Field;
        if (combo.Length == 0)
        {
            TextBlock t = new TextBlock();
            t.Text = L.T("点击录制按键", "Click to set keys");
            t.Foreground = Ui.Text3;
            t.Margin = new Thickness(6, 0, 6, 0);
            caps.Children.Add(t);
            return;
        }
        foreach (string part in combo.Split('+')) caps.Children.Add(Ui.Keycap(Ui.KeyLabel(part.Trim())));
    }

    static Key Real(KeyEventArgs e)
    {
        if (e.Key == Key.System) return e.SystemKey;
        if (e.Key == Key.ImeProcessed) return e.ImeProcessedKey;
        return e.Key;
    }

    static string ModName(Key k)
    {
        switch (k)
        {
            case Key.LeftCtrl: return "Ctrl";
            case Key.RightCtrl: return "RCtrl";
            case Key.LeftShift:
            case Key.RightShift: return "Shift";
            case Key.LeftAlt: return "Alt";
            case Key.RightAlt: return "RAlt";
            case Key.LWin:
            case Key.RWin: return "Win";
        }
        return null;
    }

    static string KeyName(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return k.ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return ((char)('0' + (k - Key.NumPad0))).ToString();
        if (k >= Key.F1 && k <= Key.F24) return "F" + (1 + (k - Key.F1));
        switch (k)
        {
            case Key.Enter: return "Enter";
            case Key.Tab: return "Tab";
            case Key.Escape: return "Esc";
            case Key.Space: return "Space";
            case Key.Back: return "Backspace";
            case Key.Delete: return "Delete";
            case Key.Insert: return "Insert";
            case Key.Home: return "Home";
            case Key.End: return "End";
            case Key.PageUp: return "PageUp";
            case Key.PageDown: return "PageDown";
            case Key.Left: return "Left";
            case Key.Right: return "Right";
            case Key.Up: return "Up";
            case Key.Down: return "Down";
            case Key.Apps: return "Apps";
            case Key.PrintScreen: return "PrintScreen";
            case Key.CapsLock: return "CapsLock";
        }
        return null;
    }

    static bool AnyModifierDown(Key except)
    {
        Key[] mods = { Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin };
        foreach (Key m in mods) if (m != except && Keyboard.IsKeyDown(m)) return true;
        return false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!Recording) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;
        Key k = Real(e);
        string mod = ModName(k);
        if (mod != null)
        {
            if (held.Count == 0) other = false;
            if (!held.Contains(mod)) held.Add(mod);
            return;
        }
        string name = KeyName(k);
        if (name == null) return;

        List<string> parts = new List<string>();
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) parts.Add("Ctrl");
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) parts.Add("Shift");
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) parts.Add("Alt");
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) parts.Add("Win");
        parts.Add(name);
        other = true;
        Finish(string.Join("+", parts.ToArray()));
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (!Recording) { base.OnPreviewKeyUp(e); return; }
        e.Handled = true;
        Key k = Real(e);
        if (ModName(k) == null) return;
        if (AnyModifierDown(k)) return;
        if (!other && held.Count > 0) Finish(string.Join("+", held.ToArray()));   // e.g. right Alt alone
        held.Clear();
        other = false;
    }

    void Finish(string c)
    {
        combo = c;
        if (Changed != null) Changed(this, EventArgs.Empty);
        // Stop recording, so the next key press is not captured by accident.
        DependencyObject scope = FocusManager.GetFocusScope(this);
        FocusManager.SetFocusedElement(scope, null);
        Keyboard.ClearFocus();
        Refresh();
    }
}

// ---------------------------------------------------------------------------
// Pick an action: drop-down + key recorder / program path / raw text
// ---------------------------------------------------------------------------

class ActionPicker : StackPanel
{
    const string Inherit = "inherit";
    readonly ComboBox type = new ComboBox();
    readonly Grid param = new Grid();
    readonly KeyRecorder keys = new KeyRecorder();
    readonly StackPanel run = new StackPanel();
    readonly TextBox runBox = new TextBox();
    readonly TextBox rawBox = new TextBox();
    readonly bool allowInherit;
    bool loading;
    public event EventHandler Changed;

    public ActionPicker(bool allowInherit, double paramWidth)
    {
        this.allowInherit = allowInherit;
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;

        param.Width = paramWidth;
        param.Margin = new Thickness(0, 0, 10, 0);
        keys.Changed += delegate { Fire(); };
        runBox.Width = paramWidth - 40;
        runBox.Tag = L.T("选择程序…", "Choose a program…");
        runBox.TextChanged += delegate { Fire(); };
        run.Orientation = Orientation.Horizontal;
        run.HorizontalAlignment = HorizontalAlignment.Right;
        run.Children.Add(runBox);
        Button browse = Ui.IconButton("", L.T("浏览", "Browse"), OnBrowse);
        browse.Margin = new Thickness(6, 0, 0, 0);
        run.Children.Add(browse);
        rawBox.Tag = L.T("ini 里的原文", "Text as in the ini");
        rawBox.TextChanged += delegate { Fire(); };
        param.Children.Add(keys);
        param.Children.Add(run);
        param.Children.Add(rawBox);

        type.Width = 168;
        if (allowInherit) Add(Inherit, L.T("沿用默认", "Use default"));
        Add(ActionSpec.Keys, L.T("按键", "Press keys"));
        Add(ActionSpec.SnipPaste, L.T("截图/粘贴交替", "Snip, then paste"));
        Add(ActionSpec.Snip, L.T("只截图", "Snip only"));
        Add(ActionSpec.Paste, L.T("粘贴", "Paste"));
        Add(ActionSpec.Run, L.T("运行程序", "Run a program"));
        Add(ActionSpec.None, L.T("无动作", "Nothing"));
        Add(ActionSpec.Raw, L.T("自定义（ini 原文）", "Custom (ini text)"));
        type.SelectionChanged += delegate { UpdateVisible(); Fire(); };

        Children.Add(param);
        Children.Add(type);
        type.SelectedIndex = 0;
        UpdateVisible();
    }

    void Add(string key, string text)
    {
        ComboBoxItem it = new ComboBoxItem();
        it.Content = text;
        it.Tag = key;
        type.Items.Add(it);
    }

    void Fire() { if (!loading && Changed != null) Changed(this, EventArgs.Empty); }

    string SelectedKey
    {
        get { ComboBoxItem it = type.SelectedItem as ComboBoxItem; return it == null ? ActionSpec.None : (string)it.Tag; }
    }

    void SelectKey(string key)
    {
        for (int i = 0; i < type.Items.Count; i++)
            if ((string)((ComboBoxItem)type.Items[i]).Tag == key) { type.SelectedIndex = i; return; }
    }

    void UpdateVisible()
    {
        string k = SelectedKey;
        keys.Visibility = k == ActionSpec.Keys ? Visibility.Visible : Visibility.Collapsed;
        run.Visibility = k == ActionSpec.Run ? Visibility.Visible : Visibility.Collapsed;
        rawBox.Visibility = k == ActionSpec.Raw ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnBrowse(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog();
        d.Filter = L.T("程序", "Programs") + " (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|" + L.T("所有文件", "All files") + " (*.*)|*.*";
        if (d.ShowDialog(Window.GetWindow(this)) == true)
            runBox.Text = d.FileName.Contains(" ") ? "\"" + d.FileName + "\"" : d.FileName;
    }

    // null means "use default" (only when allowInherit)
    public string Value
    {
        get
        {
            string k = SelectedKey;
            if (k == Inherit) return null;
            ActionSpec a = new ActionSpec();
            a.Type = k;
            if (k == ActionSpec.Keys) a.Arg = keys.Combo.Trim();
            else if (k == ActionSpec.Run) a.Arg = runBox.Text.Trim();
            else if (k == ActionSpec.Raw) a.Arg = rawBox.Text.Trim();
            return a.ToString();
        }
        set
        {
            loading = true;
            try
            {
                keys.Combo = "";
                runBox.Text = "";
                rawBox.Text = "";
                if (value == null)
                {
                    SelectKey(allowInherit ? Inherit : ActionSpec.None);
                }
                else
                {
                    ActionSpec a = ActionSpec.Parse(value);
                    SelectKey(a.Type);
                    if (a.Type == ActionSpec.Keys) keys.Combo = a.Arg;
                    else if (a.Type == ActionSpec.Run) runBox.Text = a.Arg;
                    else if (a.Type == ActionSpec.Raw) rawBox.Text = a.Arg;
                }
                UpdateVisible();
            }
            finally { loading = false; }
        }
    }

    public string Problem()
    {
        string v = Value;
        return v == null ? null : ActionSpec.Parse(v).Problem();
    }
}

// ---------------------------------------------------------------------------
// The window
// ---------------------------------------------------------------------------

class SettingsUi
{
    public readonly Window Win;
    readonly FrameworkElement root;
    readonly string dir, iniPath;
    readonly bool rendering;
    Config cfg;
    bool dirty, loading;
    bool closingWithoutPrompt;
    int page;
    string updateFile;          // downloaded PenButton.exe.new, ready to switch to
    UpdateInfo updateInfo;
    bool checking;

    readonly Dictionary<string, ActionPicker> defaults = new Dictionary<string, ActionPicker>();
    readonly Dictionary<string, ActionPicker> editorPickers = new Dictionary<string, ActionPicker>();
    int editingIndex = -1;          // -1 = new rule
    WindowInfo lastPicked;
    string lastKind = "app";
    readonly DispatcherTimer countdown = new DispatcherTimer();
    int secondsLeft;
    List<WindowInfo> pickerWindows = new List<WindowInfo>();

    static readonly string[][] Pages =
    {
        new string[] { "按钮动作", "Actions",
            "笔顶部按钮的三种按法各做什么。在“按程序设置”里没有单独设置的程序，都用这里的动作。",
            "What each way of pressing the pen’s top button does. Used everywhere except where a per-app rule says otherwise." },
        new string[] { "按程序设置", "Per app",
            "在这些程序或窗口里，用规则里的动作代替默认动作。从上往下查找，先匹配到的优先。",
            "In these programs or windows, a rule’s actions replace the defaults. Rules are checked from top to bottom; the first match wins." },
        new string[] { "截图与提示", "Snip & label",
            "截图/粘贴交替这个动作的细节。",
            "Details of the snip-then-paste action." },
        new string[] { "高级", "Advanced",
            "一般不需要改。",
            "You rarely need to change these." },
        new string[] { "绑定到笔", "Pen setup",
            "第一次使用时绑定一次，以后改设置都不用再绑定。",
            "Bind once. Later changes here need no re-binding." },
    };

    public SettingsUi(string dir, bool rendering) : this(dir, rendering, null, false, -1) { }

    // carried / wasDirty / startAt: used when switching language, to keep unsaved edits and the page.
    SettingsUi(string dir, bool rendering, Config carried, bool wasDirty, int startAt)
    {
        this.dir = dir;
        this.rendering = rendering;
        iniPath = IOPath.Combine(dir, AppInfo.IniName);

        cfg = carried ?? Config.Load(iniPath, false);
        if (PenButtonSettings.ForcedLanguage != null) L.Apply(PenButtonSettings.ForcedLanguage);
        else L.Apply(cfg.GetString("Language", "auto"));

        Win = LoadWindow();
        root = (FrameworkElement)Win.Content;
        Ui.Root = root;
        Win.Title = L.T("PenButton 设置", "PenButton Settings");
        Get<TextBlock>("VersionText").Text = "v" + AppInfo.Version;

        for (int i = 0; i < Pages.Length; i++)
        {
            int n = i;
            Get<RadioButton>("Nav" + i).Checked += delegate { ShowPage(n); };
        }

        string exeError;
        bool created = ButtonExes.Ensure(dir, out exeError);
        if (!rendering) ButtonExes.RefreshIcons(dir, created);
        SetAppIcon();

        BuildGestureRows();
        BuildEditorRows();
        BuildBindPage();
        WireControls();

        if (rendering) AddSampleRule();
        LoadIntoUi();
        if (rendering) SetUpdateStatus(L.T("已是最新版本", "You have the latest version"));
        ShowPage(0);
        SetStatus(File.Exists(iniPath)
            ? L.T("已读取 ", "Loaded ") + iniPath
            : L.T("还没有 PenButton.ini，保存后会新建", "No PenButton.ini yet; saving will create it"), false);
        if (cfg.Warnings.Count > 0)
            SetStatus(L.T("ini 里有 ", "The ini has ") + cfg.Warnings.Count +
                      L.T(" 行无法识别，保存时会被去掉", " unreadable line(s); they will be dropped on save"), true);
        int startPage = 0;
        if (created && !rendering)
        {
            SetStatus(L.T("已在本文件夹生成三个按钮程序，请到“绑定到笔”页完成绑定", "Created the three button programs in this folder. See Pen setup to bind them."), false);
            startPage = 4;
        }
        if (exeError != null) { SetStatus(exeError, true); startPage = 4; }
        else if (ButtonExes.LooksTemporary(dir))
        {
            SetStatus(L.T("PenButton.exe 现在在临时文件夹里。请先把它放到一个固定的文件夹（例如 C:\\Tools\\PenButton）再运行",
                          "PenButton.exe is in a temporary folder. Move it to a folder that will stay put (e.g. C:\\Tools\\PenButton) and run it again."), true);
            startPage = 4;
        }

        if (startAt >= 0) startPage = startAt;
        if (wasDirty) { dirty = true; UpdateDirty(); }
        if (PenButtonSettings.JustUpdated && carried == null)
            SetStatus(L.T("已更新到 v", "Updated to v") + AppInfo.Version, false);

        // Pick the first page once the window has loaded; WPF resets the radio buttons on load.
        Win.Loaded += delegate { ShowPage(startPage); };
        if (!rendering) Win.Loaded += delegate { StartUpdates(carried != null); };
        Win.SourceInitialized += delegate { OnSourceInitialized(); };
        Win.Closing += OnClosing;
        countdown.Interval = TimeSpan.FromSeconds(1);
        countdown.Tick += delegate { Tick(); };
    }

    static Window LoadWindow()
    {
        string xaml;
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SettingsWindow.xaml"))
        using (StreamReader r = new StreamReader(s, Encoding.UTF8))
            xaml = r.ReadToEnd();
        xaml = Regex.Replace(xaml, "\\{\\{(.*?)§(.*?)\\}\\}",
            delegate (Match m) { return SecurityElement.Escape(L.Zh ? m.Groups[1].Value : m.Groups[2].Value); });
        return (Window)XamlReader.Parse(xaml);
    }

    void SetAppIcon()
    {
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PenButton.ico"))
            {
                IconBitmapDecoder dec = new IconBitmapDecoder(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapFrame best = dec.Frames[0];
                foreach (BitmapFrame f in dec.Frames) if (f.PixelWidth > best.PixelWidth) best = f;
                Image img = Get<Image>("AppIcon");
                img.Source = best;
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            }
        }
        catch (Exception) { }
    }

    T Get<T>(string name) where T : class
    {
        T x = Win.FindName(name) as T;
        if (x == null) throw new InvalidOperationException("Missing element: " + name);
        return x;
    }

    // Title bar in the window's color (Windows 11), and no window menu on a lone Alt while recording keys.
    void OnSourceInitialized()
    {
        IntPtr h = new WindowInteropHelper(Win).Handle;
        try
        {
            int caption = 0x00F5F2F2;   // COLORREF of #F2F2F5
            Native.DwmSetWindowAttribute(h, 35, ref caption, 4);   // DWMWA_CAPTION_COLOR
        }
        catch (Exception) { }
        HwndSource src = HwndSource.FromHwnd(h);
        if (src != null)
            src.AddHook(delegate (IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
            {
                if (msg == 0x0112 && ((int)wp & 0xFFF0) == 0xF100 && KeyRecorder.AnyRecording) handled = true;   // SC_KEYMENU
                return IntPtr.Zero;
            });
    }

    // ----- pages -----

    void ShowPage(int n)
    {
        page = n;
        for (int i = 0; i < Pages.Length; i++)
            Get<ScrollViewer>("Page" + i).Visibility = i == n ? Visibility.Visible : Visibility.Collapsed;
        Get<TextBlock>("PageTitle").Text = L.Zh ? Pages[n][0] : Pages[n][1];
        Get<TextBlock>("PageSub").Text = L.Zh ? Pages[n][2] : Pages[n][3];
        RadioButton nav = Get<RadioButton>("Nav" + n);
        if (nav.IsChecked != true) nav.IsChecked = true;
    }

    void BuildGestureRows()
    {
        StackPanel host = Get<StackPanel>("GestureRows");
        bool first = true;
        foreach (string g in Gestures.All)
        {
            Border row = Ui.RowBorder(first);
            row.Padding = new Thickness(20, 16, 20, 16);
            first = false;
            DockPanel dp = new DockPanel();
            ActionPicker ap = new ActionPicker(false, 220);
            ap.Changed += delegate { MarkDirty(); };
            defaults[g] = ap;
            DockPanel.SetDock(ap, Dock.Right);
            dp.Children.Add(ap);

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(Ui.GestureIcon(g, 38));
            StackPanel txt = new StackPanel();
            txt.Margin = new Thickness(14, 0, 12, 0);
            txt.VerticalAlignment = VerticalAlignment.Center;
            txt.Children.Add(Ui.Text(Gestures.Label(g), "RowTitle"));
            TextBlock hint = Ui.Text(Ui.GestureHint(g), "RowDesc");
            hint.TextWrapping = TextWrapping.NoWrap;
            hint.TextTrimming = TextTrimming.CharacterEllipsis;
            txt.Children.Add(hint);
            left.Children.Add(txt);
            dp.Children.Add(left);
            row.Child = dp;
            host.Children.Add(row);
        }
    }

    void BuildEditorRows()
    {
        StackPanel host = Get<StackPanel>("EditorGestures");
        bool first = true;
        foreach (string g in Gestures.All)
        {
            Border row = new Border();
            row.Padding = new Thickness(0, 10, 0, 10);
            row.BorderBrush = Ui.Line;
            row.BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0);
            first = false;
            DockPanel dp = new DockPanel();
            ActionPicker ap = new ActionPicker(true, 190);
            editorPickers[g] = ap;
            DockPanel.SetDock(ap, Dock.Right);
            dp.Children.Add(ap);
            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(Ui.GestureIcon(g, 30));
            TextBlock t = Ui.Text(Gestures.Label(g), "RowTitle");
            t.Margin = new Thickness(10, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(t);
            dp.Children.Add(left);
            row.Child = dp;
            host.Children.Add(row);
        }
    }

    void BuildBindPage()
    {
        StackPanel steps = Get<StackPanel>("Steps");
        string[] lines =
        {
            L.T("把 PenButton.exe 放在一个固定的文件夹里，例如 C:\\Tools\\PenButton。运行它时，它会在旁边自动生成下面三个按钮程序。",
                "Keep PenButton.exe in a folder that will stay put, e.g. C:\\Tools\\PenButton. When it runs, it creates the three button programs below next to itself."),
            L.T("打开 设置 > 蓝牙和其他设备 > 笔和 Windows Ink > 笔快捷方式。",
                "Open Settings > Bluetooth & devices > Pen & Windows Ink > Pen shortcuts."),
            L.T("单击、双击、长按分别选“打开程序”，再选下面对应的程序。",
                "For single click, double-click and press and hold, choose “Open a program” and pick the matching program below."),
            L.T("PowerPoint 放映时会自己处理笔按钮（按一下下一页，长按上一页）。想让 PenButton 的设置在所有程序里都生效，就关掉同一页里的“允许应用替代快捷方式按钮行为”。",
                "In a slide show, PowerPoint handles the pen button itself (press for the next slide, hold for the previous one). To have PenButton's actions everywhere, turn off “Allow apps to override the shortcut button behavior” on the same page."),
        };
        for (int i = 0; i < lines.Length; i++)
        {
            DockPanel dp = new DockPanel();
            dp.Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 0);
            Border num = new Border();
            num.Width = 24;
            num.Height = 24;
            num.CornerRadius = new CornerRadius(12);
            num.Background = Ui.Hex("#18181B");
            num.VerticalAlignment = VerticalAlignment.Top;
            TextBlock nt = new TextBlock();
            nt.Text = (i + 1).ToString();
            nt.Foreground = Brushes.White;
            nt.FontSize = 12;
            nt.FontWeight = FontWeights.SemiBold;
            nt.HorizontalAlignment = HorizontalAlignment.Center;
            nt.VerticalAlignment = VerticalAlignment.Center;
            num.Child = nt;
            DockPanel.SetDock(num, Dock.Left);
            dp.Children.Add(num);
            TextBlock t = new TextBlock();
            t.Text = lines[i];
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(12, 2, 0, 0);
            dp.Children.Add(t);
            steps.Children.Add(dp);
        }

        StackPanel rows = Get<StackPanel>("ExeRows");
        string[][] exes =
        {
            new string[] { Gestures.Click, AppInfo.ClickExe },
            new string[] { Gestures.DoubleClick, AppInfo.DoubleClickExe },
            new string[] { Gestures.Hold, AppInfo.HoldExe },
        };
        bool first = true;
        foreach (string[] e in exes)
        {
            string full = IOPath.Combine(dir, e[1]);
            bool exists = File.Exists(full);
            Border row = Ui.RowBorder(first);
            first = false;
            DockPanel dp = new DockPanel();
            Button copy = new Button();
            copy.Style = Ui.S("Secondary");
            copy.Height = 30;
            copy.Padding = new Thickness(12, 0, 12, 0);
            copy.FontWeight = FontWeights.Normal;
            StackPanel cp = new StackPanel();
            cp.Orientation = Orientation.Horizontal;
            TextBlock ci = Ui.Icon("", 12, Ui.Text2);
            ci.Margin = new Thickness(0, 1, 7, 0);
            cp.Children.Add(ci);
            cp.Children.Add(Ui.Text(L.T("复制路径", "Copy path"), null));
            copy.Content = cp;
            string path = full;
            copy.Click += delegate
            {
                try { Clipboard.SetText(path); SetStatus(L.T("已复制：", "Copied: ") + path, false); } catch (Exception) { }
            };
            copy.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(copy, Dock.Right);
            dp.Children.Add(copy);

            StackPanel left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.Children.Add(Ui.GestureIcon(e[0], 34));
            StackPanel txt = new StackPanel();
            txt.Margin = new Thickness(14, 0, 12, 0);
            txt.VerticalAlignment = VerticalAlignment.Center;
            StackPanel title = new StackPanel();
            title.Orientation = Orientation.Horizontal;
            title.Children.Add(Ui.Text(Gestures.Label(e[0]), "RowTitle"));
            TextBlock exe = Ui.Text(e[1], "RowTitle");
            exe.FontFamily = Ui.Mono;
            exe.FontSize = 13;
            exe.Foreground = Ui.Text2;
            exe.Margin = new Thickness(10, 1, 0, 0);
            title.Children.Add(exe);
            txt.Children.Add(title);
            TextBlock p = Ui.Text(exists ? full : L.T("未能生成，请确认这个文件夹可以写入", "Could not be created. Check that this folder is writable."), "RowDesc");
            if (!exists) p.Foreground = Ui.Danger;
            p.TextTrimming = TextTrimming.CharacterEllipsis;
            txt.Children.Add(p);
            left.Children.Add(txt);
            dp.Children.Add(left);
            row.Child = dp;
            rows.Children.Add(row);
        }
    }

    void WireControls()
    {
        Get<Button>("Save").Click += delegate { SaveNow(); };
        Get<RadioButton>(L.Zh ? "LangZh" : "LangEn").IsChecked = true;
        Get<RadioButton>("LangZh").Checked += delegate { SwitchLanguage("zh"); };
        Get<RadioButton>("LangEn").Checked += delegate { SwitchLanguage("en"); };
        Get<TextBlock>("CurrentVersion").Text = L.T("当前版本 v", "Version ") + AppInfo.Version;
        Get<Button>("CheckUpdate").Click += delegate { StartCheck(true); };
        Get<Button>("UpdateNow").Click += delegate { RestartToUpdate(); };
        Get<Button>("UpdateNotes").Click += delegate { OpenReleasePage(); };
        Get<CheckBox>("AutoUpdate").Checked += delegate { MarkDirty(); };
        Get<CheckBox>("AutoUpdate").Unchecked += delegate { MarkDirty(); };
        Get<Button>("AddRule").Click += delegate { OpenEditor(-1); };
        Get<Button>("OpenLog").Click += delegate { OpenInNotepad(IOPath.Combine(dir, "PenButton.log")); };
        Get<Button>("OpenIni").Click += delegate { OpenIni(); };
        Get<Button>("Restore").Click += delegate { RestoreDefaults(); };
        Get<Button>("OpenPen").Click += delegate { try { Process.Start("ms-settings:pen"); } catch (Exception) { } };

        foreach (string n in new string[] { "SnipWindow", "SnipScreen" })
            Get<RadioButton>(n).Checked += delegate { MarkDirty(); };
        foreach (string n in new string[] { "SnipSound", "ToastOn", "LogOn" })
        {
            Get<CheckBox>(n).Checked += delegate { MarkDirty(); };
            Get<CheckBox>(n).Unchecked += delegate { MarkDirty(); };
        }
        foreach (string n in new string[] { "ResetSec", "ToastMs", "PreDelay", "KeyHold" })
        {
            TextBox t = Get<TextBox>(n);
            Ui.NumbersOnly(t);
            t.TextChanged += delegate { MarkDirty(); };
        }
        Get<TextBox>("ToastSnip").Tag = L.T("已截图", "Captured");
        Get<TextBox>("ToastPaste").Tag = L.T("已粘贴", "Pasted");
        Get<TextBox>("ToastSnip").TextChanged += delegate { MarkDirty(); };
        Get<TextBox>("ToastPaste").TextChanged += delegate { MarkDirty(); };

        Get<Button>("EditorCancel").Click += delegate { CloseEditor(); };
        Get<Button>("EditorOk").Click += delegate { EditorOk(); };
        Get<Button>("PickWindow").Click += delegate { OpenPicker(); };
        Get<Button>("GrabWindow").Click += delegate { StartCountdown(); };
        foreach (string n in new string[] { "KindApp", "KindClass", "KindTitle" })
            Get<RadioButton>(n).Checked += delegate { OnKindChanged(); };
        Get<Button>("PickerCancel").Click += delegate { Get<Grid>("PickerOverlay").Visibility = Visibility.Collapsed; };
        Get<Button>("PickerOk").Click += delegate { PickerChoose(); };
        Get<ListBox>("PickerList").MouseDoubleClick += delegate { PickerChoose(); };

        Win.PreviewKeyDown += delegate (object s, KeyEventArgs e)
        {
            if (KeyRecorder.AnyRecording) return;
            if (e.Key == Key.Escape)
            {
                if (Get<Grid>("PickerOverlay").Visibility == Visibility.Visible) { Get<Grid>("PickerOverlay").Visibility = Visibility.Collapsed; e.Handled = true; }
                else if (Get<Grid>("EditorOverlay").Visibility == Visibility.Visible) { CloseEditor(); e.Handled = true; }
            }
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { SaveNow(); e.Handled = true; }
        };
    }

    // ----- load / save -----

    void LoadIntoUi()
    {
        loading = true;
        try
        {
            foreach (string g in Gestures.All)
            {
                string v;
                defaults[g].Value = cfg.Default.Values.TryGetValue(g, out v) ? v : ActionSpec.None;
            }
            RefreshRules();
            bool screen = cfg.GetString("SnipMode", "window").ToLowerInvariant() == "screen";
            Get<RadioButton>("SnipScreen").IsChecked = screen;
            Get<RadioButton>("SnipWindow").IsChecked = !screen;
            Get<CheckBox>("SnipSound").IsChecked = cfg.GetBool("SnipSound", false);
            Get<TextBox>("ResetSec").Text = cfg.GetInt("SnipResetSeconds", 0).ToString();
            Get<CheckBox>("ToastOn").IsChecked = cfg.GetBool("Toast", true);
            Get<TextBox>("ToastMs").Text = cfg.GetInt("ToastMs", 800).ToString();
            Get<TextBox>("ToastSnip").Text = cfg.GetString("ToastSnipText", "", true);
            Get<TextBox>("ToastPaste").Text = cfg.GetString("ToastPasteText", "", true);
            Get<TextBox>("PreDelay").Text = cfg.GetInt("PreDelayMs", 100).ToString();
            Get<TextBox>("KeyHold").Text = cfg.GetInt("KeyHoldMs", 40).ToString();
            Get<CheckBox>("LogOn").IsChecked = cfg.GetBool("Log", false);
            Get<CheckBox>("AutoUpdate").IsChecked = cfg.GetBool("AutoUpdate", true);
        }
        finally { loading = false; }
        dirty = false;
        UpdateDirty();
    }

    static string Clamp(string s, int min, int max, int def)
    {
        int n;
        if (!int.TryParse(s, out n)) n = def;
        return Math.Max(min, Math.Min(max, n)).ToString();
    }

    void ReadFromUi()
    {
        foreach (string g in Gestures.All) cfg.Default.Values[g] = defaults[g].Value;
        cfg.Settings["SnipMode"] = Get<RadioButton>("SnipScreen").IsChecked == true ? "screen" : "window";
        cfg.Settings["SnipSound"] = Get<CheckBox>("SnipSound").IsChecked == true ? "true" : "false";
        cfg.Settings["SnipResetSeconds"] = Clamp(Get<TextBox>("ResetSec").Text, 0, 86400, 0);
        cfg.Settings["Toast"] = Get<CheckBox>("ToastOn").IsChecked == true ? "true" : "false";
        cfg.Settings["ToastMs"] = Clamp(Get<TextBox>("ToastMs").Text, 200, 5000, 800);
        cfg.Settings["ToastSnipText"] = Get<TextBox>("ToastSnip").Text.Trim();
        cfg.Settings["ToastPasteText"] = Get<TextBox>("ToastPaste").Text.Trim();
        cfg.Settings["PreDelayMs"] = Clamp(Get<TextBox>("PreDelay").Text, 0, 2000, 100);
        cfg.Settings["KeyHoldMs"] = Clamp(Get<TextBox>("KeyHold").Text, 0, 2000, 40);
        cfg.Settings["Log"] = Get<CheckBox>("LogOn").IsChecked == true ? "true" : "false";
        cfg.Settings["AutoUpdate"] = Get<CheckBox>("AutoUpdate").IsChecked == true ? "true" : "false";
    }

    bool SaveNow()
    {
        foreach (string g in Gestures.All)
        {
            string pr = defaults[g].Problem();
            if (pr != null) { ShowPage(0); SetStatus(Gestures.Label(g) + L.T("：", ": ") + pr, true); return false; }
        }
        foreach (Rule r in cfg.Rules)
            foreach (KeyValuePair<string, string> kv in r.Values)
            {
                string pr = ActionSpec.Parse(kv.Value).Problem();
                if (pr != null)
                {
                    ShowPage(1);
                    SetStatus(string.Join(" | ", r.Patterns) + "  " + Gestures.Label(kv.Key) + L.T("：", ": ") + pr, true);
                    return false;
                }
            }

        ReadFromUi();
        try { cfg.Save(iniPath); }
        catch (Exception ex)
        {
            SetStatus(L.T("保存失败：", "Could not save: ") + ex.Message, true);
            return false;
        }
        LoadIntoUi();   // show what was written (e.g. clamped numbers)
        SetStatus(L.T("已保存 ", "Saved at ") + DateTime.Now.ToString("HH:mm:ss") +
                  L.T("，下次按笔就会生效", ". Takes effect on the next press."), false);
        return true;
    }

    void MarkDirty()
    {
        if (loading) return;
        dirty = true;
        UpdateDirty();
    }

    void UpdateDirty()
    {
        Get<Ellipse>("DirtyDot").Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        if (dirty) SetStatus(L.T("有未保存的修改", "Unsaved changes"), false);
    }

    void SetStatus(string s, bool error)
    {
        TextBlock t = Get<TextBlock>("Status");
        t.Text = s;
        t.Foreground = error ? Ui.Danger : Ui.Text2;
    }

    void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!dirty || closingWithoutPrompt) return;
        MessageBoxResult r = MessageBox.Show(Win, L.T("有修改还没保存，要保存吗？", "Save your changes?"), Win.Title,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) e.Cancel = true;
        else if (r == MessageBoxResult.Yes && !SaveNow()) e.Cancel = true;
    }

    // ----- rules list -----

    static string KindShort(string kind)
    {
        if (kind == "class") return L.T("窗口类名", "Window class");
        if (kind == "title") return L.T("标题包含", "Title contains");
        return L.T("程序", "Program");
    }

    void RefreshRules()
    {
        StackPanel list = Get<StackPanel>("RulesList");
        list.Children.Clear();
        for (int i = 0; i < cfg.Rules.Count; i++) list.Children.Add(RuleCard(cfg.Rules[i], i));
        Get<Border>("RulesEmpty").Visibility = cfg.Rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Get<TextBlock>("RuleCount").Text = cfg.Rules.Count == 0 ? "" :
            L.T("共 " + cfg.Rules.Count + " 条规则", cfg.Rules.Count + (cfg.Rules.Count == 1 ? " rule" : " rules"));
    }

    Border RuleCard(Rule r, int index)
    {
        Border card = new Border();
        card.Style = Ui.S("Section");
        card.Padding = new Thickness(18, 14, 12, 12);
        card.Margin = new Thickness(0, 0, 0, 10);
        card.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { if (e.ClickCount == 2) OpenEditor(index); };

        DockPanel dp = new DockPanel();
        StackPanel tools = new StackPanel();
        tools.Orientation = Orientation.Horizontal;
        tools.VerticalAlignment = VerticalAlignment.Top;
        Button up = Ui.IconButton("", L.T("上移", "Move up"), delegate { MoveRule(index, -1); });
        Button down = Ui.IconButton("", L.T("下移", "Move down"), delegate { MoveRule(index, 1); });
        up.IsEnabled = index > 0;
        down.IsEnabled = index < cfg.Rules.Count - 1;
        tools.Children.Add(up);
        tools.Children.Add(down);
        tools.Children.Add(Ui.IconButton("", L.T("编辑", "Edit"), delegate { OpenEditor(index); }));
        tools.Children.Add(Ui.IconButton("", L.T("删除", "Delete"), delegate { DeleteRule(index); }));
        DockPanel.SetDock(tools, Dock.Right);
        dp.Children.Add(tools);

        StackPanel body = new StackPanel();
        StackPanel head = new StackPanel();
        head.Orientation = Orientation.Horizontal;
        Border kind = Ui.Chip(KindShort(r.Kind), Ui.AccentSoft, Ui.Accent);
        kind.Margin = new Thickness(0, 0, 10, 0);
        kind.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(kind);
        TextBlock pat = Ui.Text(string.Join("  |  ", r.Patterns), "RowTitle");
        pat.VerticalAlignment = VerticalAlignment.Center;
        pat.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(pat);
        body.Children.Add(head);
        if (!string.IsNullOrEmpty(r.Comment))
        {
            TextBlock c = Ui.Text(r.Comment.Replace("\n", " "), "RowDesc");
            c.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(c);
        }
        WrapPanel acts = new WrapPanel();
        acts.Margin = new Thickness(0, 10, 0, 0);
        foreach (string g in Gestures.All)
        {
            string v;
            bool set = r.Values.TryGetValue(g, out v);
            Border chip = new Border();
            chip.Background = Ui.Hex("#F4F4F7");
            chip.CornerRadius = new CornerRadius(7);
            chip.Padding = new Thickness(5, 4, 10, 4);
            chip.Margin = new Thickness(0, 0, 8, 2);
            StackPanel cp = new StackPanel();
            cp.Orientation = Orientation.Horizontal;
            Border gi = Ui.GestureIcon(g, 20);
            gi.Background = Brushes.White;
            cp.Children.Add(gi);
            TextBlock t = new TextBlock();
            t.Margin = new Thickness(7, 0, 0, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.FontSize = 12.5;
            t.Text = set ? ActionLabel(v) : L.T("默认", "Default");
            t.Foreground = set ? Ui.Text1 : Ui.Text3;
            cp.Children.Add(t);
            chip.Child = cp;
            chip.ToolTip = Gestures.Label(g);
            acts.Children.Add(chip);
        }
        body.Children.Add(acts);
        dp.Children.Add(body);
        card.Child = dp;
        return card;
    }

    // Like ActionSpec.Describe, but keys shown the way the keycaps show them (arrows, "Right Alt").
    static string ActionLabel(string v)
    {
        ActionSpec a = ActionSpec.Parse(v);
        if (a.Type != ActionSpec.Keys) return a.Describe();
        List<string> parts = new List<string>();
        foreach (string p in a.Arg.Split('+')) parts.Add(Ui.KeyLabel(p.Trim()));
        return L.T("按键 ", "Keys ") + string.Join(" + ", parts.ToArray());
    }

    void MoveRule(int i, int delta)
    {
        int j = i + delta;
        if (j < 0 || j >= cfg.Rules.Count) return;
        Rule t = cfg.Rules[i];
        cfg.Rules[i] = cfg.Rules[j];
        cfg.Rules[j] = t;
        RefreshRules();
        MarkDirty();
    }

    void DeleteRule(int i)
    {
        if (MessageBox.Show(Win, L.T("删除这条规则？", "Delete this rule?") + "\n\n" + string.Join(" | ", cfg.Rules[i].Patterns),
                Win.Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        cfg.Rules.RemoveAt(i);
        RefreshRules();
        MarkDirty();
    }

    void AddSampleRule()
    {
        Rule r = new Rule();
        r.Kind = "app";
        r.Patterns = new string[] { "OUTLOOK.EXE", "olk.exe" };
        r.Comment = L.T("Outlook 里长按发送邮件", "In Outlook, hold sends the email");
        r.Values[Gestures.Hold] = "keys:Ctrl+Enter";
        cfg.Rules.Add(r);
    }

    // ----- rule editor -----

    string EditorKind
    {
        get
        {
            if (Get<RadioButton>("KindClass").IsChecked == true) return "class";
            if (Get<RadioButton>("KindTitle").IsChecked == true) return "title";
            return "app";
        }
        set
        {
            Get<RadioButton>(value == "class" ? "KindClass" : value == "title" ? "KindTitle" : "KindApp").IsChecked = true;
        }
    }

    void OpenEditor(int index)
    {
        editingIndex = index;
        Rule r = index >= 0 ? cfg.Rules[index] : new Rule();
        Get<TextBlock>("EditorTitle").Text = index >= 0 ? L.T("编辑规则", "Edit rule") : L.T("添加规则", "Add rule");
        lastPicked = null;
        EditorKind = r.Kind;
        lastKind = EditorKind;
        Get<TextBox>("Pattern").Text = string.Join("|", r.Patterns);
        Get<TextBox>("Comment").Text = (r.Comment ?? "").Replace("\n", " ");
        Get<TextBlock>("Picked").Visibility = Visibility.Collapsed;
        Get<TextBlock>("EditorError").Visibility = Visibility.Collapsed;
        foreach (string g in Gestures.All)
        {
            string v;
            editorPickers[g].Value = r.Values.TryGetValue(g, out v) ? v : null;
        }
        Get<Grid>("EditorOverlay").Visibility = Visibility.Visible;
        if (!rendering) Get<TextBox>("Pattern").Focus();
    }

    void CloseEditor()
    {
        countdown.Stop();
        ResetGrabButton();
        Get<Grid>("EditorOverlay").Visibility = Visibility.Collapsed;
    }

    void EditorError(string s)
    {
        TextBlock t = Get<TextBlock>("EditorError");
        t.Text = s;
        t.Visibility = Visibility.Visible;
    }

    void EditorOk()
    {
        List<string> pats = new List<string>();
        foreach (string p in Get<TextBox>("Pattern").Text.Split('|')) if (p.Trim().Length > 0) pats.Add(p.Trim());
        if (pats.Count == 0) { EditorError(L.T("请填写要匹配的程序名、类名或标题。", "Please fill in what to match.")); return; }
        foreach (string g in Gestures.All)
        {
            string pr = editorPickers[g].Problem();
            if (pr != null) { EditorError(Gestures.Label(g) + L.T("：", ": ") + pr); return; }
        }
        Rule r = editingIndex >= 0 ? cfg.Rules[editingIndex].Clone() : new Rule();
        r.Kind = EditorKind;
        r.Patterns = pats.ToArray();
        r.Comment = Get<TextBox>("Comment").Text.Trim();
        foreach (string g in Gestures.All)
        {
            string v = editorPickers[g].Value;
            if (v == null) r.Values.Remove(g); else r.Values[g] = v;
        }
        if (editingIndex >= 0) cfg.Rules[editingIndex] = r; else cfg.Rules.Add(r);
        CloseEditor();
        RefreshRules();
        MarkDirty();
    }

    static string ValueFor(WindowInfo w, string kind)
    {
        if (kind == "class") return w.ClassName;
        if (kind == "title") return w.Title;
        return w.App;
    }

    void OnKindChanged()
    {
        string k = EditorKind;
        TextBox pat = Get<TextBox>("Pattern");
        if (lastPicked != null && pat.Text == ValueFor(lastPicked, lastKind)) pat.Text = ValueFor(lastPicked, k);
        lastKind = k;
    }

    void UsePicked(WindowInfo w)
    {
        lastPicked = w;
        lastKind = EditorKind;
        Get<TextBox>("Pattern").Text = ValueFor(w, lastKind);
        TextBlock t = Get<TextBlock>("Picked");
        t.Text = L.T("程序：", "Program: ") + w.App + "    " + L.T("类名：", "Class: ") + w.ClassName + "\n" +
                 L.T("标题：", "Title: ") + w.Title;
        t.Visibility = Visibility.Visible;
    }

    void StartCountdown()
    {
        secondsLeft = 3;
        Button b = Get<Button>("GrabWindow");
        b.IsEnabled = false;
        b.Content = "3";
        TextBlock t = Get<TextBlock>("Picked");
        t.Text = L.T("请在 3 秒内切换到要设置的窗口，例如正在放映的 PowerPoint…",
                     "Switch to the window you want within 3 seconds, for example a running slide show…");
        t.Visibility = Visibility.Visible;
        countdown.Start();
    }

    void ResetGrabButton()
    {
        Button b = Get<Button>("GrabWindow");
        b.IsEnabled = true;
        b.Content = L.T("3 秒后读取当前窗口", "Read the active window in 3 s");
    }

    void Tick()
    {
        secondsLeft--;
        if (secondsLeft > 0) { Get<Button>("GrabWindow").Content = secondsLeft.ToString(); return; }
        countdown.Stop();
        ResetGrabButton();
        WindowInfo w = WindowInfo.Foreground();
        uint pid = 0;
        try { Native.GetWindowThreadProcessId(w.Handle, out pid); } catch (Exception) { }
        if (w.Handle == IntPtr.Zero || (int)pid == Process.GetCurrentProcess().Id)
        {
            Get<TextBlock>("Picked").Text = L.T("读到的还是本窗口。点按钮后，请切换到别的窗口。",
                                                "That was this window. Click the button, then switch to another window.");
        }
        else UsePicked(w);
        try { Win.Activate(); } catch (Exception) { }
    }

    void OpenPicker()
    {
        ListBox list = Get<ListBox>("PickerList");
        list.Items.Clear();
        pickerWindows = WindowInfo.ListOpen();
        pickerWindows.Sort(delegate (WindowInfo a, WindowInfo b) { return string.Compare(a.App, b.App, StringComparison.OrdinalIgnoreCase); });
        if (rendering && pickerWindows.Count < 3) pickerWindows.AddRange(SampleWindows());
        foreach (WindowInfo w in pickerWindows) list.Items.Add(PickerItem(w));
        Get<Grid>("PickerOverlay").Visibility = Visibility.Visible;
    }

    static List<WindowInfo> SampleWindows()
    {
        List<WindowInfo> l = new List<WindowInfo>();
        string[][] s =
        {
            new string[] { "OUTLOOK.EXE", "rctrl_renwnd32", "Inbox - Outlook" },
            new string[] { "POWERPNT.EXE", "screenClass", "PowerPoint Slide Show - Lecture 5" },
            new string[] { "WINWORD.EXE", "OpusApp", "Manuscript draft.docx - Word" },
        };
        foreach (string[] x in s) { WindowInfo w = new WindowInfo(); w.App = x[0]; w.ClassName = x[1]; w.Title = x[2]; l.Add(w); }
        return l;
    }

    static UIElement PickerItem(WindowInfo w)
    {
        StackPanel p = new StackPanel();
        StackPanel head = new StackPanel();
        head.Orientation = Orientation.Horizontal;
        TextBlock app = new TextBlock();
        app.Text = w.App;
        app.FontWeight = FontWeights.SemiBold;
        head.Children.Add(app);
        TextBlock cls = new TextBlock();
        cls.Text = w.ClassName;
        cls.Foreground = Ui.Text3;
        cls.FontSize = 12;
        cls.Margin = new Thickness(10, 0, 0, 0);
        cls.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(cls);
        p.Children.Add(head);
        TextBlock title = new TextBlock();
        title.Text = w.Title;
        title.Foreground = Ui.Text2;
        title.FontSize = 12.5;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = new Thickness(0, 2, 0, 0);
        p.Children.Add(title);
        return p;
    }

    void PickerChoose()
    {
        int i = Get<ListBox>("PickerList").SelectedIndex;
        if (i < 0 || i >= pickerWindows.Count) return;
        UsePicked(pickerWindows[i]);
        Get<Grid>("PickerOverlay").Visibility = Visibility.Collapsed;
    }

    // ----- language -----

    void SwitchLanguage(string code)
    {
        if (loading || (code == "zh") == L.Zh) return;
        // Remember the choice right away, without saving other unsaved changes.
        try
        {
            Config disk = Config.Load(iniPath, false);
            disk.Settings["Language"] = code;
            disk.Save(iniPath);
        }
        catch (Exception) { }
        ReadFromUi();                       // keep what is on screen, saved or not
        cfg.Settings["Language"] = code;
        L.Apply(code);

        SettingsUi next = new SettingsUi(dir, false, cfg, dirty, page);
        next.Win.WindowStartupLocation = WindowStartupLocation.Manual;
        next.Win.Left = Win.Left;
        next.Win.Top = Win.Top;
        next.Win.Width = Win.Width;
        next.Win.Height = Win.Height;
        next.Win.WindowState = Win.WindowState;
        next.updateFile = updateFile;
        next.updateInfo = updateInfo;
        Application.Current.MainWindow = next.Win;
        next.Win.Show();
        closingWithoutPrompt = true;
        Win.Close();
    }

    // ----- updates -----

    void StartUpdates(bool afterLanguageSwitch)
    {
        Updater.CleanupOld();
        if (updateFile == null) updateFile = Updater.PendingDownload();
        if (updateFile != null) { ShowUpdateReady(); return; }
        if (afterLanguageSwitch) return;
        if (cfg.GetBool("AutoUpdate", true)) StartCheck(false);
        else SetUpdateStatus(L.T("自动更新已关闭", "Automatic updates are off"));
    }

    void SetUpdateStatus(string s) { Get<TextBlock>("UpdateStatus").Text = s; }

    void StartCheck(bool manual)
    {
        if (checking) return;
        if (updateFile != null) { ShowUpdateReady(); return; }
        checking = true;
        Get<Button>("CheckUpdate").IsEnabled = false;
        SetUpdateStatus(L.T("正在检查更新…", "Checking for updates…"));
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            string error;
            UpdateInfo info = Updater.CheckLatest(out error);
            string file = null;
            if (Updater.IsNewer(info))
            {
                try { file = Updater.Download(info); }
                catch (Exception ex) { error = L.T("下载新版本失败：", "Could not download the new version: ") + ex.Message; }
            }
            Win.Dispatcher.BeginInvoke(new Action(delegate { CheckDone(manual, info, file, error); }));
        });
    }

    void CheckDone(bool manual, UpdateInfo info, string file, string error)
    {
        checking = false;
        Get<Button>("CheckUpdate").IsEnabled = true;
        string checkedAt = "  (" + DateTime.Now.ToString("HH:mm") + ")";
        if (file != null)
        {
            updateInfo = info;
            updateFile = file;
            ShowUpdateReady();
            return;
        }
        if (error != null)
        {
            SetUpdateStatus(error + checkedAt);
            if (manual) SetStatus(error, true);
            return;
        }
        SetUpdateStatus(L.T("已是最新版本", "You have the latest version") + checkedAt);
        if (manual) SetStatus(L.T("已是最新版本", "You have the latest version"), false);
    }

    void ShowUpdateReady()
    {
        string v = updateInfo != null ? updateInfo.Tag : L.T("新版本", "A new version");
        if (updateInfo == null)
        {
            try { v = "v" + Updater.Norm(AssemblyName.GetAssemblyName(updateFile).Version); } catch (Exception) { }
        }
        Get<TextBlock>("UpdateText").Text = L.T("新版本 " + v + " 已经下载好，重启 PenButton 就能用上", v + " is downloaded. Restart PenButton to use it.");
        Get<Button>("UpdateNotes").Visibility = updateInfo != null && updateInfo.PageUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Get<Border>("UpdateBar").Visibility = Visibility.Visible;
        SetUpdateStatus(L.T("新版本 " + v + " 已下载", v + " downloaded"));
    }

    void RestartToUpdate()
    {
        if (updateFile == null) return;
        if (dirty)
        {
            MessageBoxResult r = MessageBox.Show(Win, L.T("更新前先保存当前修改吗？", "Save your changes before updating?"), Win.Title,
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes && !SaveNow()) return;
        }
        string error;
        if (!Updater.ApplyAndRestart(updateFile, out error))
        {
            SetStatus(L.T("更新失败：", "Update failed: ") + error, true);
            return;
        }
        closingWithoutPrompt = true;
        Application.Current.Shutdown();
    }

    void OpenReleasePage()
    {
        string url = updateInfo != null && updateInfo.PageUrl.Length > 0 ? updateInfo.PageUrl : "https://github.com/" + AppInfo.Repo + "/releases";
        try { Process.Start(url); } catch (Exception) { }
    }

    // ----- misc -----

    void OpenInNotepad(string path)
    {
        if (!File.Exists(path)) { SetStatus(L.T("还没有这个文件：", "File does not exist yet: ") + path, true); return; }
        try { Process.Start("notepad.exe", "\"" + path + "\""); } catch (Exception) { }
    }

    void OpenIni()
    {
        if (dirty)
        {
            MessageBoxResult r = MessageBox.Show(Win, L.T("先保存当前修改，再打开 ini？", "Save your changes before opening the ini?"),
                Win.Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes && !SaveNow()) return;
        }
        if (!File.Exists(iniPath)) { ReadFromUi(); try { cfg.Save(iniPath); } catch (Exception) { } }
        OpenInNotepad(iniPath);
    }

    void RestoreDefaults()
    {
        if (MessageBox.Show(Win, L.T("恢复成默认设置？点“保存”后才会写入 ini。", "Restore the default settings? They are written to the ini only when you click Save."),
                Win.Title, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        cfg = Config.CreateDefault();
        LoadIntoUi();
        dirty = true;
        UpdateDirty();
    }

    // ----- render to PNG (CI) -----

    public void RenderAll(string outDir)
    {
        string[] names = { "1-actions", "2-per-app", "3-snip", "4-advanced", "5-pen-setup" };
        for (int i = 0; i < Pages.Length; i++)
        {
            ShowPage(i);
            Snap(IOPath.Combine(outDir, names[i] + ".png"));
        }
        ShowPage(1);
        OpenEditor(0);
        Snap(IOPath.Combine(outDir, "6-edit-rule.png"));
        OpenPicker();
        Snap(IOPath.Combine(outDir, "7-pick-window.png"));
        Get<Grid>("PickerOverlay").Visibility = Visibility.Collapsed;
        CloseEditor();

        // What the update bar looks like (sample version number)
        updateInfo = new UpdateInfo();
        updateInfo.Tag = "v9.9.0";
        updateInfo.PageUrl = "https://github.com/" + AppInfo.Repo + "/releases";
        updateFile = "sample";
        ShowUpdateReady();
        ShowPage(0);
        Snap(IOPath.Combine(outDir, "8-update.png"));
    }

    void Snap(string file)
    {
        Size size = new Size(960, 640);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        Win.Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.Background);
        RenderTargetBitmap bmp = new RenderTargetBitmap(1920, 1280, 192, 192, PixelFormats.Pbgra32);
        bmp.Render(root);
        PngBitmapEncoder enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (FileStream fs = File.Create(file)) enc.Save(fs);
    }
}

// ---------------------------------------------------------------------------
// The three button programs: a small program (grey icon) embedded in PenButton.exe as the
// resource "PenGesture.exe". Write it out under the three names, and again whenever the
// files on disk differ from it (for example after PenButton.exe was updated).
// ---------------------------------------------------------------------------

static class ButtonExes
{
    public static readonly string[] Names = { AppInfo.ClickExe, AppInfo.DoubleClickExe, AppInfo.HoldExe };

    static byte[] Embedded()
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("PenGesture.exe"))
        {
            if (s == null) return null;
            using (MemoryStream m = new MemoryStream())
            {
                s.CopyTo(m);
                return m.ToArray();
            }
        }
    }

    // Returns true if any file was created or replaced. error is set when one could not be written.
    public static bool Ensure(string dir, out string error)
    {
        error = null;
        bool changed = false;
        byte[] exe;
        try { exe = Embedded(); }
        catch (Exception ex) { error = ex.Message; return false; }
        if (exe == null) { error = L.T("这个 PenButton.exe 里没有按钮程序，请重新下载或编译", "This PenButton.exe has no button program inside. Download or build it again."); return false; }

        foreach (string name in Names)
        {
            string target = IOPath.Combine(dir, name);
            if (Same(target, exe)) continue;
            string problem = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    string tmp = target + ".new";
                    File.WriteAllBytes(tmp, exe);
                    if (File.Exists(target)) File.Replace(tmp, target, null);
                    else File.Move(tmp, target);
                    problem = null;
                    changed = true;
                    break;
                }
                catch (UnauthorizedAccessException ex) { problem = ex.Message; break; }
                catch (IOException ex) { problem = ex.Message; System.Threading.Thread.Sleep(200); }   // busy: a press may be running
            }
            try { if (File.Exists(target + ".new")) File.Delete(target + ".new"); } catch (Exception) { }
            if (problem != null && error == null)
                error = L.T("无法生成 ", "Could not create ") + name + L.T("：", ": ") + problem;
        }
        return changed;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

    // Ask Explorer to redraw the files' icons; it otherwise keeps showing a cached old icon.
    // After the files were rewritten, also announce a global icon change, which makes every
    // Explorer window drop its cached icons (the per-file notice alone is not always enough).
    public static void RefreshIcons(string dir, bool filesChanged)
    {
        try
        {
            foreach (string name in Names)
            {
                string p = IOPath.Combine(dir, name);
                if (File.Exists(p)) SHChangeNotify(0x00002000, 0x0005, p, IntPtr.Zero);   // SHCNE_UPDATEITEM, SHCNF_PATHW
            }
            if (filesChanged) SHChangeNotify(0x08000000, 0x0000, null, IntPtr.Zero);       // SHCNE_ASSOCCHANGED, SHCNF_IDLIST
        }
        catch (Exception) { }
    }

    static bool Same(string path, byte[] exe)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length != exe.Length) return false;
            byte[] other = File.ReadAllBytes(path);
            for (int i = 0; i < exe.Length; i++) if (other[i] != exe[i]) return false;
            return true;
        }
        catch (Exception) { return false; }
    }

    public static bool LooksTemporary(string dir)
    {
        try
        {
            string d = IOPath.GetFullPath(dir).TrimEnd('\\') + "\\";
            string t = IOPath.GetFullPath(IOPath.GetTempPath()).TrimEnd('\\') + "\\";
            return d.StartsWith(t, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }
}
