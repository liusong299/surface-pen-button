using System;
using System.Collections.Generic;

static class CoreTests
{
    static int fails = 0;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }
    static WindowInfo W(string app, string cls, string title) { WindowInfo w = new WindowInfo(); w.App = app; w.ClassName = cls; w.Title = title; return w; }

    static int Main()
    {
        string rule;
        // Gestures from exe names
        Check(Gestures.Detect("PenClick", null) == Gestures.Click, "PenClick -> Click");
        Check(Gestures.Detect("PenDoubleClick", null) == Gestures.DoubleClick, "PenDoubleClick -> DoubleClick");
        Check(Gestures.Detect("PenHold", null) == Gestures.Hold, "PenHold -> Hold");
        Check(Gestures.Detect("PenButtonSettings", null) == null, "Settings exe is not a gesture");
        Check(Gestures.Detect("PenClick", new string[] { "hold" }) == Gestures.Hold, "argument overrides name");

        // Defaults
        Config d = Config.CreateDefault();
        Check(d.Resolve(Gestures.Click, W("WINWORD.EXE", "OpusApp", "Doc"), out rule) == "keys:RAlt", "default click = RAlt");
        Check(d.Resolve(Gestures.DoubleClick, W("WINWORD.EXE", "OpusApp", "Doc"), out rule) == "snippaste", "default double = snippaste");
        Check(d.Resolve(Gestures.Hold, W("WINWORD.EXE", "OpusApp", "Doc"), out rule) == "keys:Enter", "default hold = Enter");
        Check(d.Resolve(Gestures.Click, W("POWERPNT.EXE", "screenClass", "Slide Show"), out rule) == "keys:Right", "slideshow click = Right");
        Check(d.Resolve(Gestures.DoubleClick, W("POWERPNT.EXE", "PodiumParent", "Presenter"), out rule) == "keys:Left", "presenter double = Left");
        Check(d.Resolve(Gestures.Hold, W("POWERPNT.EXE", "screenClass", "Slide Show"), out rule) == "keys:Enter" && rule == "Default", "slideshow hold falls back to default");
        Check(d.Resolve(Gestures.Click, W("POWERPNT.EXE", "PPTFrameClass", "Edit"), out rule) == "keys:RAlt", "PowerPoint editing uses default");

        // Round trip
        string ini = d.ToIni();
        Config r = Config.Parse(ini);
        Check(r.ToIni() == ini, "ToIni -> Parse -> ToIni is stable");
        Check(r.Warnings.Count == 0, "default ini has no warnings");
        Check(r.Rules.Count == 1 && r.Rules[0].Comment.StartsWith("PowerPoint"), "rule comment kept");

        // Hand-written ini with app/title rules, order and fallbacks
        string hand =
            "[Settings]\nKeyHoldMs=300\nCustomThing=abc\n\n" +
            "[app:OUTLOOK|olk.exe]\nHold=keys:Ctrl+Enter\n\n" +
            "[title:Gmail]\nHold=keys:Ctrl+Enter\nClick=none\n\n" +
            "[app:chrome.exe]\nHold=keys:Shift+Enter\n\n" +
            "[weird]\nx=1\n" +
            "[Default]\nClick=keys:F9\n";
        Config h = Config.Parse(hand);
        Check(h.GetInt("KeyHoldMs", 40) == 300, "setting read");
        Check(h.Resolve(Gestures.Hold, W("OUTLOOK.EXE", "rctrl_renwnd32", "Inbox"), out rule) == "keys:Ctrl+Enter", "app match without .exe");
        Check(h.Resolve(Gestures.Hold, W("olk.exe", "x", "Mail"), out rule) == "keys:Ctrl+Enter", "second pattern matches");
        Check(h.Resolve(Gestures.Hold, W("chrome.exe", "Chrome_WidgetWin_1", "Inbox - Gmail"), out rule) == "keys:Ctrl+Enter", "first matching rule wins (title before app)");
        Check(h.Resolve(Gestures.Hold, W("chrome.exe", "Chrome_WidgetWin_1", "YouTube"), out rule) == "keys:Shift+Enter", "later rule when earlier does not match");
        Check(h.Resolve(Gestures.Click, W("chrome.exe", "c", "Inbox - gmail"), out rule) == "none", "title match is case-insensitive");
        Check(h.Resolve(Gestures.DoubleClick, W("a.exe", "c", "t"), out rule) == "none", "gesture not set anywhere -> none");
        Check(h.Warnings.Count == 1, "unknown section warned");
        string h2 = h.ToIni();
        Check(h2.Contains("CustomThing=abc"), "unknown setting kept on save");
        Check(h2.Contains("[app:OUTLOOK|olk.exe]"), "rule name kept on save");
        Check(Config.Parse(h2).Resolve(Gestures.Click, W("x.exe", "c", "t"), out rule) == "keys:F9", "default kept on save");

        // Missing [Default] falls back to built-in default
        Config nd = Config.Parse("[Settings]\nLog=true\n");
        Check(nd.Resolve(Gestures.Hold, W("x.exe", "c", "t"), out rule) == "keys:Enter", "no [Default] -> built-in default");
        Check(nd.GetBool("Log", false), "bool setting");

        // Actions
        Check(ActionSpec.Parse("keys:Ctrl+Enter").Problem() == null, "valid combo");
        Check(ActionSpec.Parse("keys:Ctrl+Entr").Problem() != null, "typo in key is reported");
        Check(ActionSpec.Parse("keys:").Problem() != null, "empty keys reported");
        Check(ActionSpec.Parse("snippaste").Type == ActionSpec.SnipPaste, "snippaste parsed");
        Check(ActionSpec.Parse("run:\"C:\\A B\\x.exe\" -y").ToString() == "run:\"C:\\A B\\x.exe\" -y", "run round trip");
        Check(ActionSpec.Parse("frobnicate").Type == ActionSpec.Raw && ActionSpec.Parse("frobnicate").ToString() == "frobnicate", "unknown action kept as raw");
        List<KeyDef> k = KeyNames.ParseCombo("Ctrl+Shift+Right");
        Check(k.Count == 3 && k[0].Vk == 0x11 && k[2].Vk == 0x27 && k[2].Extended, "combo parsed with extended flag");
        Check(KeyNames.ParseCombo("RAlt")[0].Vk == 0xA5 && KeyNames.ParseCombo("RAlt")[0].Extended, "RAlt is extended 0xA5");
        Check(KeyNames.ParseCombo("enter")[0].Vk == 0x0D, "key names are case-insensitive");

        Console.WriteLine(fails == 0 ? "\nALL PASSED" : "\n" + fails + " FAILED");
        return fails;
    }
}
