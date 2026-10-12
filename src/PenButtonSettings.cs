// PenButtonSettings.cs
// Settings window for PenButton. Reads and writes PenButton.ini in the same folder.
// Built by build.bat together with Core.cs. C# 5 / .NET Framework 4.x only.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

static class PenButtonSettings
{
    [STAThread]
    static void Main()
    {
        Native.EnableDpiAwareness(false);   // system DPI aware: crisp on the Surface screen
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        Application.Run(new MainForm(dir));
    }

    public static Font UiFont()
    {
        return new Font(L.Zh ? "Microsoft YaHei UI" : "Segoe UI", 9f);
    }

    public static Label Text(string s, int maxWidth)
    {
        Label l = new Label();
        l.Text = s;
        l.AutoSize = true;
        if (maxWidth > 0) l.MaximumSize = new Size(maxWidth, 0);
        l.Margin = new Padding(3, 6, 3, 6);
        return l;
    }

    public static Button Btn(string s, EventHandler onClick)
    {
        Button b = new Button();
        b.Text = s;
        b.AutoSize = true;
        b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        b.MinimumSize = new Size(84, 28);
        b.Padding = new Padding(6, 0, 6, 0);
        b.UseVisualStyleBackColor = true;
        if (onClick != null) b.Click += onClick;
        return b;
    }

    public static TableLayoutPanel Table(int cols)
    {
        TableLayoutPanel t = new TableLayoutPanel();
        t.ColumnCount = cols;
        t.AutoSize = true;
        t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        t.Dock = DockStyle.Top;
        return t;
    }
}

// ---------------------------------------------------------------------------
// A box that records a key or key combo when you press it
// ---------------------------------------------------------------------------

class KeyBox : TextBox
{
    readonly List<string> heldMods = new List<string>();
    bool otherPressed;
    public event EventHandler ComboChanged;

    public KeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ImeMode = ImeMode.Disable;
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
        Width = 150;
    }

    protected override bool IsInputKey(Keys keyData) { return true; }

    static bool Down(int vk)
    {
        try { return (Native.GetKeyState(vk) & 0x8000) != 0; } catch (Exception) { return false; }
    }

    static string ModifierName(Keys k)
    {
        if (k == Keys.ControlKey || k == Keys.LControlKey || k == Keys.RControlKey) return Down(0xA3) ? "RCtrl" : "Ctrl";
        if (k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey) return "Shift";
        if (k == Keys.Menu || k == Keys.LMenu || k == Keys.RMenu) return Down(0xA5) ? "RAlt" : "Alt";
        if (k == Keys.LWin || k == Keys.RWin) return "Win";
        return null;
    }

    static string KeyName(Keys k)
    {
        if (k >= Keys.A && k <= Keys.Z) return k.ToString();
        if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return ((char)('0' + (k - Keys.NumPad0))).ToString();
        if (k >= Keys.F1 && k <= Keys.F24) return "F" + (1 + (k - Keys.F1));
        switch (k)
        {
            case Keys.Return: return "Enter";
            case Keys.Tab: return "Tab";
            case Keys.Escape: return "Esc";
            case Keys.Space: return "Space";
            case Keys.Back: return "Backspace";
            case Keys.Delete: return "Delete";
            case Keys.Insert: return "Insert";
            case Keys.Home: return "Home";
            case Keys.End: return "End";
            case Keys.PageUp: return "PageUp";
            case Keys.PageDown: return "PageDown";
            case Keys.Left: return "Left";
            case Keys.Right: return "Right";
            case Keys.Up: return "Up";
            case Keys.Down: return "Down";
            case Keys.Apps: return "Apps";
            case Keys.PrintScreen: return "PrintScreen";
            case Keys.CapsLock: return "CapsLock";
        }
        return null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        string mod = ModifierName(e.KeyCode);
        if (mod != null)
        {
            if (heldMods.Count == 0) otherPressed = false;
            if (!heldMods.Contains(mod)) heldMods.Add(mod);
            return;
        }

        string name = KeyName(e.KeyCode);
        if (name == null) return;   // a key we cannot send; ignore it

        List<string> parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Shift) parts.Add("Shift");
        if (e.Alt) parts.Add("Alt");
        if (Down(0x5B) || Down(0x5C)) parts.Add("Win");
        parts.Add(name);
        otherPressed = true;
        SetCombo(string.Join("+", parts.ToArray()));
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (ModifierName(e.KeyCode) == null) return;

        bool anyDown = e.Control || e.Shift || e.Alt || Down(0x5B) || Down(0x5C);
        if (anyDown) return;
        if (!otherPressed && heldMods.Count > 0) SetCombo(string.Join("+", heldMods.ToArray()));   // e.g. RAlt alone
        heldMods.Clear();
        otherPressed = false;
    }

    protected override void OnKeyPress(KeyPressEventArgs e) { e.Handled = true; }

    void SetCombo(string combo)
    {
        Text = combo;
        if (ComboChanged != null) ComboChanged(this, EventArgs.Empty);
    }
}

// ---------------------------------------------------------------------------
// Pick an action: type + key box / program path / raw text
// ---------------------------------------------------------------------------

class ActionEditor : UserControl
{
    class Item
    {
        public string Key, Text;
        public Item(string k, string t) { Key = k; Text = t; }
        public override string ToString() { return Text; }
    }

    const string Inherit = "inherit";
    readonly ComboBox type = new ComboBox();
    readonly KeyBox keys = new KeyBox();
    readonly TextBox runBox = new TextBox();
    readonly Button browse;
    readonly TextBox rawBox = new TextBox();
    readonly bool allowInherit;
    bool loading;
    public event EventHandler Changed;

    public ActionEditor(bool allowInherit)
    {
        this.allowInherit = allowInherit;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0);

        FlowLayoutPanel flow = new FlowLayoutPanel();
        flow.AutoSize = true;
        flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        flow.WrapContents = false;
        flow.Margin = new Padding(0);

        type.DropDownStyle = ComboBoxStyle.DropDownList;
        type.Width = 170;
        if (allowInherit) type.Items.Add(new Item(Inherit, L.T("（沿用默认）", "(use default)")));
        type.Items.Add(new Item(ActionSpec.Keys, L.T("按键", "Press keys")));
        type.Items.Add(new Item(ActionSpec.SnipPaste, L.T("截图/粘贴交替", "Snip, then paste")));
        type.Items.Add(new Item(ActionSpec.Snip, L.T("只截图", "Snip only")));
        type.Items.Add(new Item(ActionSpec.Paste, L.T("粘贴", "Paste")));
        type.Items.Add(new Item(ActionSpec.Laser, L.T("激光笔（开/关）", "Laser pointer (on/off)")));
        type.Items.Add(new Item(ActionSpec.Run, L.T("运行程序", "Run a program")));
        type.Items.Add(new Item(ActionSpec.None, L.T("无动作", "Nothing")));
        type.Items.Add(new Item(ActionSpec.Raw, L.T("自定义（ini 原文）", "Custom (ini text)")));
        type.SelectedIndexChanged += delegate { UpdateVisible(); Fire(); };

        keys.ComboChanged += delegate { Fire(); };
        runBox.Width = 230;
        runBox.TextChanged += delegate { Fire(); };
        browse = PenButtonSettings.Btn(L.T("浏览…", "Browse..."), OnBrowse);
        browse.MinimumSize = new Size(60, 0);
        rawBox.Width = 230;
        rawBox.TextChanged += delegate { Fire(); };

        foreach (Control c in new Control[] { type, keys, runBox, browse, rawBox })
        {
            c.Margin = new Padding(3, 3, 3, 3);
            flow.Controls.Add(c);
        }
        Controls.Add(flow);
        type.SelectedIndex = 0;
        UpdateVisible();
    }

    void Fire()
    {
        if (!loading && Changed != null) Changed(this, EventArgs.Empty);
    }

    string SelectedKey { get { Item it = type.SelectedItem as Item; return it == null ? ActionSpec.None : it.Key; } }

    void Select(string key)
    {
        for (int i = 0; i < type.Items.Count; i++)
            if (((Item)type.Items[i]).Key == key) { type.SelectedIndex = i; return; }
    }

    void UpdateVisible()
    {
        string k = SelectedKey;
        keys.Visible = k == ActionSpec.Keys;
        runBox.Visible = browse.Visible = k == ActionSpec.Run;
        rawBox.Visible = k == ActionSpec.Raw;
    }

    void OnBrowse(object sender, EventArgs e)
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Filter = L.T("程序", "Programs") + " (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|" + L.T("所有文件", "All files") + " (*.*)|*.*";
            if (d.ShowDialog(FindForm()) == DialogResult.OK)
                runBox.Text = d.FileName.Contains(" ") ? "\"" + d.FileName + "\"" : d.FileName;
        }
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
            if (k == ActionSpec.Keys) a.Arg = keys.Text.Trim();
            else if (k == ActionSpec.Run) a.Arg = runBox.Text.Trim();
            else if (k == ActionSpec.Raw) a.Arg = rawBox.Text.Trim();
            return a.ToString();
        }
        set
        {
            loading = true;
            try
            {
                keys.Text = ""; runBox.Text = ""; rawBox.Text = "";
                if (value == null)
                {
                    Select(allowInherit ? Inherit : ActionSpec.None);
                }
                else
                {
                    ActionSpec a = ActionSpec.Parse(value);
                    Select(a.Type);
                    if (a.Type == ActionSpec.Keys) keys.Text = a.Arg;
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
// Main window
// ---------------------------------------------------------------------------

class MainForm : Form
{
    readonly string dir;
    readonly string iniPath;
    Config cfg;
    bool dirty;
    bool loading;
    float scale = 1f;

    readonly Dictionary<string, ActionEditor> defaults = new Dictionary<string, ActionEditor>();
    ListView rulesView;
    RadioButton snipWindow, snipScreen;
    CheckBox snipSound, toastOn, logOn;
    NumericUpDown resetSec, toastMs, preDelay, keyHold, laserSize, laserHide, laserOff;
    Button laserColorBtn;
    Color laserColor;
    TextBox toastSnip, toastPaste;
    Label status;

    const int WM_SYSCOMMAND = 0x0112;
    const int SC_KEYMENU = 0xF100;

    public MainForm(string dir)
    {
        this.dir = dir;
        iniPath = Path.Combine(dir, AppInfo.IniName);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = PenButtonSettings.UiFont();
        Text = L.T("PenButton 设置", "PenButton Settings") + "  v" + AppInfo.Version;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 540);
        MinimumSize = new Size(640, 460);
        try { using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f; } catch (Exception) { }

        TabControl tabs = new TabControl();
        tabs.Dock = DockStyle.Fill;
        tabs.Padding = new Point(12, 5);
        tabs.TabPages.Add(BuildDefaultsTab());
        tabs.TabPages.Add(BuildRulesTab());
        tabs.TabPages.Add(BuildSnipTab());
        tabs.TabPages.Add(BuildLaserTab());
        tabs.TabPages.Add(BuildAdvancedTab());
        tabs.TabPages.Add(BuildBindTab());

        Panel bottom = new Panel();
        bottom.Dock = DockStyle.Bottom;
        bottom.Height = 48;
        bottom.Padding = new Padding(10, 8, 10, 8);
        FlowLayoutPanel btns = new FlowLayoutPanel();
        btns.Dock = DockStyle.Right;
        btns.AutoSize = true;
        btns.WrapContents = false;
        Button save = PenButtonSettings.Btn(L.T("保存", "Save"), delegate { SaveNow(); });
        Button close = PenButtonSettings.Btn(L.T("关闭", "Close"), delegate { Close(); });
        btns.Controls.Add(save);
        btns.Controls.Add(close);
        status = new Label();
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.ForeColor = SystemColors.GrayText;
        bottom.Controls.Add(status);
        bottom.Controls.Add(btns);

        Controls.Add(tabs);
        Controls.Add(bottom);
        AcceptButton = null;
        ResumeLayout(false);
        PerformLayout();

        cfg = Config.Load(iniPath, false);
        LoadIntoUi();
        status.Text = File.Exists(iniPath)
            ? L.T("已读取 ", "Loaded ") + iniPath
            : L.T("还没有 PenButton.ini，保存后会新建。", "No PenButton.ini yet. Saving will create it.");
        if (cfg.Warnings.Count > 0)
            status.Text = L.T("ini 里有 ", "The ini has ") + cfg.Warnings.Count + L.T(" 行无法识别，保存时会被去掉。", " line(s) that could not be read; they will be dropped on save.");
    }

    TabPage Page(string title)
    {
        TabPage p = new TabPage(title);
        p.Padding = new Padding(14);
        p.AutoScroll = true;
        p.UseVisualStyleBackColor = true;
        return p;
    }

    // Controls in a TabPage dock from the bottom of the list up, so add them in reverse.
    static void Stack(Control parent, params Control[] items)
    {
        for (int i = items.Length - 1; i >= 0; i--)
        {
            if (items[i].Dock == DockStyle.None) items[i].Dock = DockStyle.Top;
            parent.Controls.Add(items[i]);
        }
    }

    TabPage BuildDefaultsTab()
    {
        TabPage p = Page(L.T("按钮动作", "Actions"));
        Label intro = PenButtonSettings.Text(L.T(
            "笔顶部按钮的三种按法各做什么。下一页“按程序设置”里没有单独设置的程序，都用这里的动作。",
            "What each way of pressing the pen's top button does. Used everywhere except where the next tab says otherwise."), 680);

        TableLayoutPanel t = PenButtonSettings.Table(2);
        t.Padding = new Padding(0, 6, 0, 6);
        foreach (string g in Gestures.All)
        {
            Label l = PenButtonSettings.Text(Gestures.Label(g), 0);
            l.Anchor = AnchorStyles.Left;
            l.MinimumSize = new Size(90, 0);
            ActionEditor ed = new ActionEditor(false);
            ed.Changed += delegate { MarkDirty(); };
            defaults[g] = ed;
            t.Controls.Add(l);
            t.Controls.Add(ed);
        }

        Label hint = PenButtonSettings.Text(L.T(
            "设置按键：点一下按键框，再在键盘上按想要的键或组合键，比如 Enter 或 Ctrl+Enter。只按右边的 Alt 会记成 RAlt（可用来唤起 Typeless 等语音输入）。",
            "To set keys: click the key box, then press the key or combo on your keyboard, e.g. Enter or Ctrl+Enter. The right Alt key alone is recorded as RAlt (handy for voice-typing apps)."), 680);
        hint.ForeColor = SystemColors.GrayText;
        Label hint2 = PenButtonSettings.Text(L.T(
            "截图/粘贴交替：第一次截取当前窗口到剪贴板，第二次粘贴，第三次又截图，依此类推。",
            "Snip, then paste: the 1st press copies a screenshot of the current window, the 2nd press pastes it, the 3rd snips again, and so on."), 680);
        hint2.ForeColor = SystemColors.GrayText;

        Stack(p, intro, t, hint, hint2);
        return p;
    }

    TabPage BuildRulesTab()
    {
        TabPage p = Page(L.T("按程序设置", "Per program"));
        Label intro = PenButtonSettings.Text(L.T(
            "在这些程序或窗口里，用这里的动作代替默认动作。从上往下查找，先匹配到的优先；某个按法没有设置时用默认动作。",
            "In these programs or windows, these actions replace the defaults. Checked from top to bottom; the first match wins. A gesture left on \"use default\" falls back to the Actions tab."), 680);
        intro.Dock = DockStyle.Top;

        rulesView = new ListView();
        rulesView.View = View.Details;
        rulesView.FullRowSelect = true;
        rulesView.HideSelection = false;
        rulesView.MultiSelect = false;
        rulesView.Dock = DockStyle.Fill;
        rulesView.Columns.Add(L.T("匹配方式", "Match by"), S(110));
        rulesView.Columns.Add(L.T("匹配内容", "Match"), S(170));
        rulesView.Columns.Add(Gestures.Label(Gestures.Click), S(115));
        rulesView.Columns.Add(Gestures.Label(Gestures.DoubleClick), S(115));
        rulesView.Columns.Add(Gestures.Label(Gestures.Hold), S(115));
        rulesView.DoubleClick += delegate { EditRule(); };

        FlowLayoutPanel side = new FlowLayoutPanel();
        side.FlowDirection = FlowDirection.TopDown;
        side.Dock = DockStyle.Right;
        side.AutoSize = true;
        side.WrapContents = false;
        side.Padding = new Padding(8, 0, 0, 0);
        side.Controls.Add(PenButtonSettings.Btn(L.T("添加…", "Add..."), delegate { AddRule(); }));
        side.Controls.Add(PenButtonSettings.Btn(L.T("编辑…", "Edit..."), delegate { EditRule(); }));
        side.Controls.Add(PenButtonSettings.Btn(L.T("删除", "Delete"), delegate { DeleteRule(); }));
        side.Controls.Add(PenButtonSettings.Btn(L.T("上移", "Move up"), delegate { MoveRule(-1); }));
        side.Controls.Add(PenButtonSettings.Btn(L.T("下移", "Move down"), delegate { MoveRule(1); }));

        Panel body = new Panel();
        body.Dock = DockStyle.Fill;
        body.Padding = new Padding(0, 6, 0, 0);
        body.Controls.Add(rulesView);
        body.Controls.Add(side);

        p.Controls.Add(body);
        p.Controls.Add(intro);
        p.AutoScroll = false;
        return p;
    }

    TabPage BuildSnipTab()
    {
        TabPage p = Page(L.T("截图与提示", "Snip & label"));

        GroupBox g1 = new GroupBox();
        g1.Text = L.T("截图", "Screenshot");
        g1.AutoSize = true;
        g1.Padding = new Padding(10);
        TableLayoutPanel t1 = PenButtonSettings.Table(1);
        snipWindow = new RadioButton(); snipWindow.Text = L.T("截取当前窗口", "Capture the current window"); snipWindow.AutoSize = true;
        snipScreen = new RadioButton(); snipScreen.Text = L.T("截取整个屏幕", "Capture the whole screen"); snipScreen.AutoSize = true;
        snipSound = new CheckBox(); snipSound.Text = L.T("截图后播放提示音", "Play a sound after a screenshot"); snipSound.AutoSize = true;
        resetSec = Num(0, 86400);
        FlowLayoutPanel resetRow = Row(PenButtonSettings.Text(L.T("截图后超过", "If not pasted within"), 0), resetSec,
            PenButtonSettings.Text(L.T("秒还没粘贴，下次重新截图（0 = 一直交替）", "seconds, the next press snips again (0 = always alternate)"), 0));
        foreach (Control c in new Control[] { snipWindow, snipScreen, snipSound, resetRow }) t1.Controls.Add(c);
        snipWindow.CheckedChanged += delegate { MarkDirty(); };
        snipSound.CheckedChanged += delegate { MarkDirty(); };
        resetSec.ValueChanged += delegate { MarkDirty(); };
        g1.Controls.Add(t1);

        GroupBox g2 = new GroupBox();
        g2.Text = L.T("小提示", "Label");
        g2.AutoSize = true;
        g2.Padding = new Padding(10);
        TableLayoutPanel t2 = PenButtonSettings.Table(1);
        toastOn = new CheckBox(); toastOn.Text = L.T("截图或粘贴后，在笔尖旁边短暂显示“已截图”/“已粘贴”", "Briefly show \"Captured\" / \"Pasted\" next to the pen"); toastOn.AutoSize = true;
        toastMs = Num(200, 5000);
        toastMs.Increment = 100;
        toastSnip = new TextBox(); toastSnip.Width = 120;
        toastPaste = new TextBox(); toastPaste.Width = 120;
        t2.Controls.Add(toastOn);
        t2.Controls.Add(Row(PenButtonSettings.Text(L.T("显示时长（毫秒）", "Show for (ms)"), 0), toastMs));
        t2.Controls.Add(Row(PenButtonSettings.Text(L.T("截图提示文字", "Snip text"), 0), toastSnip,
                            PenButtonSettings.Text(L.T("粘贴提示文字", "Paste text"), 0), toastPaste));
        Label hint = PenButtonSettings.Text(L.T("文字留空时跟随 Windows 语言。", "Leave the text empty to follow the Windows language."), 0);
        hint.ForeColor = SystemColors.GrayText;
        t2.Controls.Add(hint);
        toastOn.CheckedChanged += delegate { MarkDirty(); };
        toastMs.ValueChanged += delegate { MarkDirty(); };
        toastSnip.TextChanged += delegate { MarkDirty(); };
        toastPaste.TextChanged += delegate { MarkDirty(); };
        g2.Controls.Add(t2);

        Panel gap = new Panel(); gap.Height = 10;
        Stack(p, g1, gap, g2);
        return p;
    }

    TabPage BuildLaserTab()
    {
        TabPage p = Page(L.T("激光笔", "Laser"));
        Label intro = PenButtonSettings.Text(L.T(
            "把某个按法设成“激光笔（开/关）”：按一次打开，屏幕上出现一个发光的点跟着笔尖走；再按一次关闭。" +
            "笔要悬停在屏幕上方（大约 1 到 2 厘米以内）才能被追踪。共享整个屏幕时（Teams、Zoom 等）对方也能看到。",
            "Set a gesture to \"Laser pointer (on/off)\": press once and a glowing dot follows the pen tip; press again to turn it off. " +
            "The pen is tracked while it hovers just above the screen (about 1 to 2 cm). People see it when you share your whole screen in Teams, Zoom and similar."), 680);

        laserColorBtn = PenButtonSettings.Btn(L.T("选择颜色…", "Choose color..."), delegate { PickLaserColor(); });
        laserSize = Num(6, 120);
        laserHide = Num(0, 60000);
        laserHide.Increment = 500;
        laserOff = Num(0, 600);
        laserSize.ValueChanged += delegate { MarkDirty(); };
        laserHide.ValueChanged += delegate { MarkDirty(); };
        laserOff.ValueChanged += delegate { MarkDirty(); };

        TableLayoutPanel t = PenButtonSettings.Table(1);
        t.Padding = new Padding(0, 8, 0, 0);
        t.Controls.Add(Row(PenButtonSettings.Text(L.T("颜色", "Color"), 0), laserColorBtn));
        t.Controls.Add(Row(PenButtonSettings.Text(L.T("大小（像素，100% 缩放时）", "Size (pixels at 100% scaling)"), 0), laserSize));
        t.Controls.Add(Row(PenButtonSettings.Text(L.T("笔停住超过", "Fade out after"), 0), laserHide,
                           PenButtonSettings.Text(L.T("毫秒后淡出，一动就重新出现（0 = 不淡出）", "ms without movement; it comes back when the pen moves (0 = never)"), 0)));
        t.Controls.Add(Row(PenButtonSettings.Text(L.T("笔停住超过", "Turn off after"), 0), laserOff,
                           PenButtonSettings.Text(L.T("分钟后自动关闭（0 = 不自动关闭）", "minutes without movement (0 = never)"), 0)));
        Label hint = Hint(L.T("提示：可以在“按程序设置”里只让 PowerPoint 放映时的某个按法打开激光笔。",
                              "Tip: on the Per program tab you can use the laser only in a PowerPoint slide show."));
        hint.MaximumSize = new Size(680, 0);
        t.Controls.Add(hint);
        Stack(p, intro, t);
        return p;
    }

    void PickLaserColor()
    {
        using (ColorDialog d = new ColorDialog())
        {
            d.Color = laserColor;
            d.FullOpen = true;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            laserColor = d.Color;
            ShowLaserColor();
            MarkDirty();
        }
    }

    void ShowLaserColor()
    {
        laserColorBtn.BackColor = laserColor;
        laserColorBtn.ForeColor = (laserColor.R * 299 + laserColor.G * 587 + laserColor.B * 114) / 1000 > 140 ? Color.Black : Color.White;
        laserColorBtn.Text = ColorHex(laserColor);
    }

    static string ColorHex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

    static Color ParseHex(string s, Color def)
    {
        try
        {
            s = (s ?? "").Trim().TrimStart('#');
            if (s.Length != 6) return def;
            int v = int.Parse(s, System.Globalization.NumberStyles.HexNumber);
            return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
        }
        catch (Exception) { return def; }
    }

    TabPage BuildAdvancedTab()
    {
        TabPage p = Page(L.T("高级", "Advanced"));
        preDelay = Num(0, 2000);
        keyHold = Num(0, 2000);
        logOn = new CheckBox();
        logOn.Text = L.T("记录日志：每次按笔时记下程序名、窗口类名、标题和执行的动作（PenButton.log）",
                         "Log every press: program, window class, title and action (PenButton.log)");
        logOn.AutoSize = true;
        preDelay.ValueChanged += delegate { MarkDirty(); };
        keyHold.ValueChanged += delegate { MarkDirty(); };
        logOn.CheckedChanged += delegate { MarkDirty(); };

        TableLayoutPanel t = PenButtonSettings.Table(1);
        t.Controls.Add(Row(PenButtonSettings.Text(L.T("动作前等待（毫秒）", "Wait before acting (ms)"), 0), preDelay,
                           Hint(L.T("让笔按钮事件先结束，一般不用改", "lets the pen event finish; rarely needs changing"))));
        t.Controls.Add(Row(PenButtonSettings.Text(L.T("按键按住时长（毫秒）", "Hold keys for (ms)"), 0), keyHold,
                           Hint(L.T("某个程序收不到按键时调大，例如 300", "raise it (e.g. 300) if an app misses the key"))));
        t.Controls.Add(logOn);
        t.Controls.Add(Row(
            PenButtonSettings.Btn(L.T("打开日志", "Open log"), delegate { OpenFile(Path.Combine(dir, "PenButton.log")); }),
            PenButtonSettings.Btn(L.T("用记事本打开 ini", "Open ini in Notepad"), delegate { OpenIni(); }),
            PenButtonSettings.Btn(L.T("恢复默认设置", "Restore defaults"), delegate { RestoreDefaults(); })));
        Stack(p, t);
        return p;
    }

    TabPage BuildBindTab()
    {
        TabPage p = Page(L.T("绑定到笔", "Pen setup"));
        Label steps = PenButtonSettings.Text(L.T(
            "第一次使用时需要绑定一次，以后改设置都不用再绑定：\n" +
            "1. 打开 设置 → 蓝牙和其他设备 → 笔和 Windows Ink → 笔快捷方式。\n" +
            "2. 单击、双击、长按分别选“打开程序”，再选下面对应的 exe。\n" +
            "3. 如果 PowerPoint 等程序里笔按钮被程序自己接管，可以关掉同一页里的“允许应用替代快捷方式按钮行为”。",
            "Bind once; later changes here need no re-binding:\n" +
            "1. Open Settings > Bluetooth & devices > Pen & Windows Ink > Pen shortcuts.\n" +
            "2. For single click, double-click and press and hold, choose \"Open a program\" and pick the matching exe below.\n" +
            "3. If an app such as PowerPoint takes over the pen button, turn off \"Allow apps to override the shortcut button behavior\" on the same page."), 680);

        TableLayoutPanel t = PenButtonSettings.Table(3);
        t.Padding = new Padding(0, 8, 0, 8);
        string[][] rows =
        {
            new string[] { Gestures.Label(Gestures.Click), AppInfo.ClickExe },
            new string[] { Gestures.Label(Gestures.DoubleClick), AppInfo.DoubleClickExe },
            new string[] { Gestures.Label(Gestures.Hold), AppInfo.HoldExe },
        };
        foreach (string[] r in rows)
        {
            string full = Path.Combine(dir, r[1]);
            Label l = PenButtonSettings.Text(r[0], 0);
            l.Anchor = AnchorStyles.Left;
            TextBox box = new TextBox();
            box.ReadOnly = true;
            box.Width = 430;
            box.Text = File.Exists(full) ? full : full + L.T("  （未找到，请先运行 build.bat）", "  (not found, run build.bat first)");
            box.Anchor = AnchorStyles.Left;
            string copy = full;
            Button b = PenButtonSettings.Btn(L.T("复制路径", "Copy path"), delegate
            {
                try { Clipboard.SetText(copy); status.Text = L.T("已复制：", "Copied: ") + copy; } catch (Exception) { }
            });
            t.Controls.Add(l);
            t.Controls.Add(box);
            t.Controls.Add(b);
        }

        FlowLayoutPanel row = Row(PenButtonSettings.Btn(L.T("打开笔设置", "Open pen settings"), delegate
        {
            try { Process.Start("ms-settings:pen"); } catch (Exception) { }
        }));
        Stack(p, steps, t, row);
        return p;
    }

    int S(int px) { return (int)(px * scale); }

    static NumericUpDown Num(int min, int max)
    {
        NumericUpDown n = new NumericUpDown();
        n.Minimum = min;
        n.Maximum = max;
        n.Width = 80;
        n.Margin = new Padding(3, 3, 3, 3);
        return n;
    }

    static Label Hint(string s)
    {
        Label l = PenButtonSettings.Text(s, 0);
        l.ForeColor = SystemColors.GrayText;
        return l;
    }

    static FlowLayoutPanel Row(params Control[] items)
    {
        FlowLayoutPanel f = new FlowLayoutPanel();
        f.AutoSize = true;
        f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        f.WrapContents = false;
        f.Margin = new Padding(0, 2, 0, 2);
        foreach (Control c in items) f.Controls.Add(c);
        return f;
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
            RefreshRules(-1);

            bool screen = cfg.GetString("SnipMode", "window").ToLowerInvariant() == "screen";
            snipScreen.Checked = screen;
            snipWindow.Checked = !screen;
            snipSound.Checked = cfg.GetBool("SnipSound", false);
            SetNum(resetSec, cfg.GetInt("SnipResetSeconds", 0));
            toastOn.Checked = cfg.GetBool("Toast", true);
            SetNum(toastMs, cfg.GetInt("ToastMs", 800));
            toastSnip.Text = cfg.GetString("ToastSnipText", "", true);
            toastPaste.Text = cfg.GetString("ToastPasteText", "", true);
            SetNum(preDelay, cfg.GetInt("PreDelayMs", 100));
            SetNum(keyHold, cfg.GetInt("KeyHoldMs", 40));
            logOn.Checked = cfg.GetBool("Log", false);
            laserColor = ParseHex(cfg.GetString("LaserColor", "#FF3B30"), Color.FromArgb(255, 59, 48));
            ShowLaserColor();
            SetNum(laserSize, cfg.GetInt("LaserSize", 22));
            SetNum(laserHide, cfg.GetInt("LaserHideMs", 2500));
            SetNum(laserOff, cfg.GetInt("LaserAutoOffMinutes", 10));
        }
        finally { loading = false; }
        dirty = false;
        UpdateTitle();
    }

    static void SetNum(NumericUpDown n, int v)
    {
        n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v));
    }

    void ReadFromUi()
    {
        foreach (string g in Gestures.All) cfg.Default.Values[g] = defaults[g].Value;
        cfg.Settings["SnipMode"] = snipScreen.Checked ? "screen" : "window";
        cfg.Settings["SnipSound"] = snipSound.Checked ? "true" : "false";
        cfg.Settings["SnipResetSeconds"] = ((int)resetSec.Value).ToString();
        cfg.Settings["Toast"] = toastOn.Checked ? "true" : "false";
        cfg.Settings["ToastMs"] = ((int)toastMs.Value).ToString();
        cfg.Settings["ToastSnipText"] = toastSnip.Text.Trim();
        cfg.Settings["ToastPasteText"] = toastPaste.Text.Trim();
        cfg.Settings["PreDelayMs"] = ((int)preDelay.Value).ToString();
        cfg.Settings["KeyHoldMs"] = ((int)keyHold.Value).ToString();
        cfg.Settings["Log"] = logOn.Checked ? "true" : "false";
        cfg.Settings["LaserColor"] = ColorHex(laserColor);
        cfg.Settings["LaserSize"] = ((int)laserSize.Value).ToString();
        cfg.Settings["LaserHideMs"] = ((int)laserHide.Value).ToString();
        cfg.Settings["LaserAutoOffMinutes"] = ((int)laserOff.Value).ToString();
    }

    bool SaveNow()
    {
        List<string> problems = new List<string>();
        foreach (string g in Gestures.All)
        {
            string pr = defaults[g].Problem();
            if (pr != null) problems.Add(Gestures.Label(g) + L.T("：", ": ") + pr);
        }
        foreach (Rule r in cfg.Rules)
            foreach (KeyValuePair<string, string> kv in r.Values)
            {
                string pr = ActionSpec.Parse(kv.Value).Problem();
                if (pr != null) problems.Add("[" + r.Name + "] " + Gestures.Label(kv.Key) + L.T("：", ": ") + pr);
            }
        if (problems.Count > 0)
        {
            MessageBox.Show(this, L.T("请先修改这些地方：\n\n", "Please fix these first:\n\n") + string.Join("\n", problems.ToArray()),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        ReadFromUi();
        try
        {
            cfg.Save(iniPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.T("保存失败：", "Could not save: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        dirty = false;
        UpdateTitle();
        status.Text = L.T("已保存 ", "Saved ") + DateTime.Now.ToString("HH:mm:ss") + L.T("，下次按笔就会生效。", ". Takes effect on the next press.");
        return true;
    }

    void MarkDirty()
    {
        if (loading) return;
        dirty = true;
        UpdateTitle();
    }

    void UpdateTitle()
    {
        string t = L.T("PenButton 设置", "PenButton Settings") + "  v" + AppInfo.Version;
        Text = dirty ? t + L.T("  （未保存）", "  (unsaved)") : t;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!dirty || e.Cancel) return;
        DialogResult r = MessageBox.Show(this, L.T("有修改还没保存，要保存吗？", "Save your changes?"), Text,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) e.Cancel = true;
        else if (r == DialogResult.Yes && !SaveNow()) e.Cancel = true;
    }

    // Pressing Alt alone in a key box should not open the window menu.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_KEYMENU && ActiveControl is KeyBox) return;
        base.WndProc(ref m);
    }

    // ----- rules -----

    static string Cell(Rule r, string g)
    {
        string v;
        if (!r.Values.TryGetValue(g, out v)) return L.T("默认", "default");
        return ActionSpec.Parse(v).Describe();
    }

    void RefreshRules(int select)
    {
        rulesView.BeginUpdate();
        rulesView.Items.Clear();
        foreach (Rule r in cfg.Rules)
        {
            ListViewItem it = new ListViewItem(Rule.KindLabel(r.Kind));
            it.SubItems.Add(string.Join(" | ", r.Patterns));
            foreach (string g in Gestures.All) it.SubItems.Add(Cell(r, g));
            if (!string.IsNullOrEmpty(r.Comment)) it.ToolTipText = r.Comment;
            rulesView.Items.Add(it);
        }
        rulesView.ShowItemToolTips = true;
        if (select >= 0 && select < rulesView.Items.Count)
        {
            rulesView.Items[select].Selected = true;
            rulesView.Items[select].EnsureVisible();
        }
        rulesView.EndUpdate();
    }

    int Selected { get { return rulesView.SelectedIndices.Count > 0 ? rulesView.SelectedIndices[0] : -1; } }

    void AddRule()
    {
        Rule r = new Rule();
        using (RuleDialog d = new RuleDialog(r, true))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            cfg.Rules.Add(d.Result);
        }
        RefreshRules(cfg.Rules.Count - 1);
        MarkDirty();
    }

    void EditRule()
    {
        int i = Selected;
        if (i < 0) return;
        using (RuleDialog d = new RuleDialog(cfg.Rules[i], false))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            cfg.Rules[i] = d.Result;
        }
        RefreshRules(i);
        MarkDirty();
    }

    void DeleteRule()
    {
        int i = Selected;
        if (i < 0) return;
        if (MessageBox.Show(this, L.T("删除这条设置？", "Delete this rule?") + "\n\n[" + cfg.Rules[i].Name + "]", Text,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        cfg.Rules.RemoveAt(i);
        RefreshRules(Math.Min(i, cfg.Rules.Count - 1));
        MarkDirty();
    }

    void MoveRule(int delta)
    {
        int i = Selected, j = i + delta;
        if (i < 0 || j < 0 || j >= cfg.Rules.Count) return;
        Rule r = cfg.Rules[i];
        cfg.Rules[i] = cfg.Rules[j];
        cfg.Rules[j] = r;
        RefreshRules(j);
        MarkDirty();
    }

    // ----- misc -----

    void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            status.Text = L.T("还没有这个文件：", "File does not exist yet: ") + path;
            return;
        }
        try { Process.Start("notepad.exe", "\"" + path + "\""); } catch (Exception) { }
    }

    void OpenIni()
    {
        if (dirty && MessageBox.Show(this, L.T("先保存当前修改，再打开 ini？", "Save your changes before opening the ini?"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            if (!SaveNow()) return;
        }
        if (!File.Exists(iniPath)) { ReadFromUi(); try { cfg.Save(iniPath); } catch (Exception) { } }
        OpenFile(iniPath);
    }

    void RestoreDefaults()
    {
        if (MessageBox.Show(this, L.T("恢复成默认设置？（点“保存”后才会写入 ini）", "Restore the default settings? (Written to the ini only when you click Save.)"),
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        cfg = Config.CreateDefault();
        LoadIntoUi();
        dirty = true;
        UpdateTitle();
    }
}

// ---------------------------------------------------------------------------
// Add / edit one per-program rule
// ---------------------------------------------------------------------------

class RuleDialog : Form
{
    class Kind
    {
        public string Key;
        public Kind(string k) { Key = k; }
        public override string ToString() { return Rule.KindLabel(Key); }
    }

    readonly ComboBox kind = new ComboBox();
    readonly TextBox pattern = new TextBox();
    readonly TextBox comment = new TextBox();
    readonly Label picked;
    readonly Dictionary<string, ActionEditor> eds = new Dictionary<string, ActionEditor>();
    readonly Timer countdown = new Timer();
    readonly Button grabBtn;
    int secondsLeft;
    WindowInfo lastPicked;
    string lastKind;
    readonly Rule original;
    public Rule Result;

    public RuleDialog(Rule r, bool isNew)
    {
        original = r;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = PenButtonSettings.UiFont();
        Text = isNew ? L.T("添加按程序设置", "Add rule") : L.T("编辑按程序设置", "Edit rule");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0, 0, 12, 12);

        TableLayoutPanel t = new TableLayoutPanel();
        t.ColumnCount = 2;
        t.AutoSize = true;
        t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        t.Location = new Point(12, 12);

        kind.DropDownStyle = ComboBoxStyle.DropDownList;
        kind.Width = 170;
        kind.Items.Add(new Kind("app"));
        kind.Items.Add(new Kind("class"));
        kind.Items.Add(new Kind("title"));
        pattern.Width = 330;
        comment.Width = 330;

        grabBtn = PenButtonSettings.Btn(L.T("3 秒后读取当前窗口", "Read the active window in 3 s"), delegate { StartCountdown(); });
        Button listBtn = PenButtonSettings.Btn(L.T("从打开的窗口中选…", "Pick an open window..."), delegate { PickFromList(); });
        picked = PenButtonSettings.Text("", 460);
        picked.ForeColor = SystemColors.GrayText;

        AddRow(t, L.T("匹配方式", "Match by"), kind);
        AddRow(t, L.T("匹配内容", "Match"), pattern);
        AddRow(t, "", Hint(L.T("多个用 | 隔开，例如 OUTLOOK.EXE|olk.exe", "Separate several with |, e.g. OUTLOOK.EXE|olk.exe")));
        AddRow(t, "", Flow(listBtn, grabBtn));
        AddRow(t, "", picked);
        AddRow(t, L.T("说明（可不填）", "Note (optional)"), comment);
        Panel gap = new Panel(); gap.Height = 6; gap.Width = 1;
        AddRow(t, "", gap);
        foreach (string g in Gestures.All)
        {
            ActionEditor ed = new ActionEditor(true);
            eds[g] = ed;
            AddRow(t, Gestures.Label(g), ed);
        }

        Button ok = PenButtonSettings.Btn(L.T("确定", "OK"), delegate { OnOk(); });
        Button cancel = PenButtonSettings.Btn(L.T("取消", "Cancel"), null);
        cancel.DialogResult = DialogResult.Cancel;
        CancelButton = cancel;
        FlowLayoutPanel btns = Flow(ok, cancel);
        btns.Anchor = AnchorStyles.Right;
        t.Controls.Add(new Label() { AutoSize = true });
        t.Controls.Add(btns);

        Controls.Add(t);
        ResumeLayout(false);
        PerformLayout();

        // Load
        string k = r.Kind == "class" || r.Kind == "title" ? r.Kind : "app";
        for (int i = 0; i < kind.Items.Count; i++) if (((Kind)kind.Items[i]).Key == k) kind.SelectedIndex = i;
        lastKind = k;
        pattern.Text = string.Join("|", r.Patterns);
        comment.Text = (r.Comment ?? "").Replace("\n", " ");
        foreach (string g in Gestures.All)
        {
            string v;
            eds[g].Value = r.Values.TryGetValue(g, out v) ? v : null;
        }
        kind.SelectedIndexChanged += delegate { OnKindChanged(); };

        countdown.Interval = 1000;
        countdown.Tick += delegate { Tick(); };
    }

    static Label Hint(string s)
    {
        Label l = PenButtonSettings.Text(s, 0);
        l.ForeColor = SystemColors.GrayText;
        l.Margin = new Padding(3, 0, 3, 3);
        return l;
    }

    static FlowLayoutPanel Flow(params Control[] items)
    {
        FlowLayoutPanel f = new FlowLayoutPanel();
        f.AutoSize = true;
        f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        f.WrapContents = false;
        f.Margin = new Padding(0);
        foreach (Control c in items) f.Controls.Add(c);
        return f;
    }

    static void AddRow(TableLayoutPanel t, string label, Control c)
    {
        Label l = PenButtonSettings.Text(label, 0);
        l.Anchor = AnchorStyles.Left;
        t.Controls.Add(l);
        c.Anchor = AnchorStyles.Left;
        t.Controls.Add(c);
    }

    string SelectedKind { get { Kind k = kind.SelectedItem as Kind; return k == null ? "app" : k.Key; } }

    static string ValueFor(WindowInfo w, string k)
    {
        if (k == "class") return w.ClassName;
        if (k == "title") return w.Title;
        return w.App;
    }

    void OnKindChanged()
    {
        string k = SelectedKind;
        // If the box still holds what we filled in from a picked window, switch it to the new kind.
        if (lastPicked != null && pattern.Text == ValueFor(lastPicked, lastKind)) pattern.Text = ValueFor(lastPicked, k);
        lastKind = k;
    }

    void UsePicked(WindowInfo w)
    {
        lastPicked = w;
        lastKind = SelectedKind;
        pattern.Text = ValueFor(w, lastKind);
        picked.Text = L.T("程序：", "Program: ") + w.App + "    " + L.T("类名：", "Class: ") + w.ClassName + "\n" +
                      L.T("标题：", "Title: ") + w.Title;
    }

    void StartCountdown()
    {
        secondsLeft = 3;
        grabBtn.Enabled = false;
        picked.Text = L.T("请在 3 秒内切换到要设置的窗口（例如正在放映的 PowerPoint）…", "Switch to the window you want within 3 seconds (e.g. a running slide show)...");
        grabBtn.Text = "3";
        countdown.Start();
    }

    void Tick()
    {
        secondsLeft--;
        if (secondsLeft > 0) { grabBtn.Text = secondsLeft.ToString(); return; }
        countdown.Stop();
        grabBtn.Enabled = true;
        grabBtn.Text = L.T("3 秒后读取当前窗口", "Read the active window in 3 s");

        WindowInfo w = WindowInfo.Foreground();
        uint pid = 0;
        try { Native.GetWindowThreadProcessId(w.Handle, out pid); } catch (Exception) { }
        if (w.Handle == IntPtr.Zero || (int)pid == Process.GetCurrentProcess().Id)
        {
            picked.Text = L.T("读到的还是本窗口。请点按钮后切换到别的窗口。", "That was this window. Click the button, then switch to another window.");
        }
        else
        {
            UsePicked(w);
        }
        try { Activate(); } catch (Exception) { }
    }

    void PickFromList()
    {
        using (WindowPicker d = new WindowPicker())
        {
            if (d.ShowDialog(this) == DialogResult.OK && d.Picked != null) UsePicked(d.Picked);
        }
    }

    void OnOk()
    {
        List<string> pats = new List<string>();
        foreach (string p in pattern.Text.Split('|')) if (p.Trim().Length > 0) pats.Add(p.Trim());
        if (pats.Count == 0)
        {
            MessageBox.Show(this, L.T("请填写匹配内容。", "Please fill in what to match."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        List<string> problems = new List<string>();
        foreach (string g in Gestures.All)
        {
            string pr = eds[g].Problem();
            if (pr != null) problems.Add(Gestures.Label(g) + L.T("：", ": ") + pr);
        }
        if (problems.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", problems.ToArray()), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Rule r = original.Clone();
        r.Kind = SelectedKind;
        r.Patterns = pats.ToArray();
        r.Comment = comment.Text.Trim();
        foreach (string g in Gestures.All)
        {
            string v = eds[g].Value;
            if (v == null) r.Values.Remove(g); else r.Values[g] = v;
        }
        Result = r;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) countdown.Dispose();
        base.Dispose(disposing);
    }
}

// ---------------------------------------------------------------------------
// List of open windows to pick from
// ---------------------------------------------------------------------------

class WindowPicker : Form
{
    readonly ListView list = new ListView();
    readonly List<WindowInfo> windows;
    public WindowInfo Picked;

    public WindowPicker()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = PenButtonSettings.UiFont();
        Text = L.T("选择窗口", "Pick a window");
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(700, 420);
        Padding = new Padding(10);

        float scale = 1f;
        try { using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f; } catch (Exception) { }

        Label hint = PenButtonSettings.Text(L.T(
            "PowerPoint 放映窗口要先开始放映，才会出现在这里。也可以用“3 秒后读取当前窗口”。",
            "A PowerPoint slide show only appears here while it is running. You can also use \"Read the active window in 3 s\"."), 660);
        hint.Dock = DockStyle.Top;
        hint.ForeColor = SystemColors.GrayText;

        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.Dock = DockStyle.Fill;
        list.Columns.Add(L.T("程序", "Program"), (int)(150 * scale));
        list.Columns.Add(L.T("窗口类名", "Window class"), (int)(170 * scale));
        list.Columns.Add(L.T("标题", "Title"), (int)(340 * scale));
        list.DoubleClick += delegate { Choose(); };

        windows = WindowInfo.ListOpen();
        windows.Sort(delegate (WindowInfo a, WindowInfo b) { return string.Compare(a.App, b.App, StringComparison.OrdinalIgnoreCase); });
        foreach (WindowInfo w in windows)
        {
            ListViewItem it = new ListViewItem(w.App);
            it.SubItems.Add(w.ClassName);
            it.SubItems.Add(w.Title);
            list.Items.Add(it);
        }

        FlowLayoutPanel btns = new FlowLayoutPanel();
        btns.Dock = DockStyle.Bottom;
        btns.FlowDirection = FlowDirection.RightToLeft;
        btns.AutoSize = true;
        btns.Padding = new Padding(0, 8, 0, 0);
        Button cancel = PenButtonSettings.Btn(L.T("取消", "Cancel"), null);
        cancel.DialogResult = DialogResult.Cancel;
        Button ok = PenButtonSettings.Btn(L.T("选择", "Select"), delegate { Choose(); });
        btns.Controls.Add(cancel);
        btns.Controls.Add(ok);
        CancelButton = cancel;

        Controls.Add(list);
        Controls.Add(hint);
        Controls.Add(btns);
        ResumeLayout(false);
        PerformLayout();
    }

    void Choose()
    {
        if (list.SelectedIndices.Count == 0) return;
        Picked = windows[list.SelectedIndices[0]];
        DialogResult = DialogResult.OK;
        Close();
    }
}
