// Program.cs
// Entry point of PenButton.exe: opens the settings window, and writes the three small
// button programs (PenClick.exe, PenDoubleClick.exe, PenHold.exe; grey icon) next to itself.
// Those are built from GestureProgram.cs and embedded here as a resource.
// If PenButton.exe itself is renamed to a gesture name, it runs that gesture too.

using System;
using System.IO;
using System.Reflection;

[assembly: AssemblyTitle("PenButton")]
[assembly: AssemblyDescription("Configurable actions for the Surface Pen top button")]
[assembly: AssemblyProduct("PenButton")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion(AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(AppInfo.Version + ".0")]

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string exeName = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
        string gesture = Gestures.Detect(exeName, args);
        if (gesture != null) return PenButton.RunGesture(gesture);
        return PenButtonSettings.Run(args);
    }
}
