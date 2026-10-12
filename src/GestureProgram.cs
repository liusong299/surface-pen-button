// GestureProgram.cs
// Entry point of the small button program (grey icon). PenButton.exe carries it as an
// embedded resource and writes it out as PenClick.exe, PenDoubleClick.exe and PenHold.exe.
// It reads its own file name to know which pen gesture fired, runs that action and exits.
// No window, no WPF. Built by build.bat from GestureProgram.cs, Core.cs and PenButton.cs.

using System;
using System.IO;
using System.Reflection;

[assembly: AssemblyTitle("PenButton pen-button action")]
[assembly: AssemblyDescription("Runs the PenButton action for one Surface Pen gesture")]
[assembly: AssemblyProduct("PenButton")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion(AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(AppInfo.Version + ".0")]

static class GestureProgram
{
    [STAThread]
    static int Main(string[] args)
    {
        string exeName = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
        string gesture = Gestures.Detect(exeName, args);
        if (gesture == null) return 2;   // renamed to something we do not know
        return PenButton.RunGesture(gesture);
    }
}
