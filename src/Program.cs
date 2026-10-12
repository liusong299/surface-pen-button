// Program.cs
// Entry point of PenButton.exe. One program does both jobs:
//   PenButton.exe                 opens the settings window, and creates the three button
//                                 programs next to itself (copies of itself)
//   PenClick.exe / PenDoubleClick.exe / PenHold.exe
//                                 run the action for that pen gesture (no window)
// The program tells the two apart by its own file name.

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
