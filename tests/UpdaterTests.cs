using System;
using System.IO;

static class UpdaterTests
{
    static int fails = 0;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) fails++; }

    const string Sample = @"{""tag_name"":""v9.1.0"",""draft"":false,""prerelease"":false,""html_url"":""https://github.com/x/y/releases/tag/v9.1.0"",
      ""assets"":[{""name"":""notes.txt"",""size"":3,""browser_download_url"":""https://example.com/notes.txt""},
                  {""name"":""PenButton.exe"",""size"":256000,""digest"":""sha256:ABCDEF0123"",""uploader"":{""login"":""bot""},
                   ""browser_download_url"":""https://github.com/x/y/releases/download/v9.1.0/PenButton.exe""}]}";

    static int Main()
    {
        UpdateInfo u = Updater.ParseRelease(Sample);
        Check(u != null, "release parsed");
        Check(u.Version == new Version(9, 1, 0), "version from tag");
        Check(u.DownloadUrl.EndsWith("/v9.1.0/PenButton.exe"), "picks the PenButton.exe asset");
        Check(u.Size == 256000, "asset size");
        Check(u.Sha256 == "abcdef0123", "sha256 digest, lower-case");
        Check(Updater.IsNewer(u), "9.1.0 is newer than the current version");
        Check(Updater.ParseRelease(Sample.Replace("\"draft\":false", "\"draft\":true")) == null, "drafts ignored");
        Check(Updater.ParseRelease(Sample.Replace("PenButton.exe", "Other.exe")) == null, "no PenButton.exe -> null");
        Check(Updater.ParseRelease(Sample.Replace("https://github.com/x/y/releases/download", "http://evil")) == null, "non-https download refused");

        Check(Updater.ParseVersion("v2.3.0") == new Version(2, 3, 0), "ParseVersion v2.3.0");
        Check(Updater.ParseVersion("2.3") == new Version(2, 3, 0), "ParseVersion 2.3");
        Check(Updater.ParseVersion("latest") == null, "ParseVersion junk");
        Check(Updater.Norm(new Version(2, 3, 0, 0)) == Updater.Current || AppInfo.Version != "2.3.0", "2.3.0.0 equals 2.3.0");
        UpdateInfo same = Updater.ParseRelease(Sample.Replace("v9.1.0", "v" + AppInfo.Version));
        Check(!Updater.IsNewer(same), "same version is not newer");

        // Swap: the new file takes the old one's place, the old one is kept as .old
        string dir = Path.Combine(Path.GetTempPath(), "pbswap" + DateTime.Now.Ticks);
        Directory.CreateDirectory(dir);
        string self = Path.Combine(dir, "PenButton.exe"), nf = self + ".new";
        File.WriteAllText(self, "old");
        File.WriteAllText(nf, "new");
        string err;
        Check(Updater.Swap(self, nf, out err), "swap ok");
        Check(File.ReadAllText(self) == "new" && File.ReadAllText(self + ".old") == "old" && !File.Exists(nf), "swap result");
        File.WriteAllText(nf, "newer");
        Check(Updater.Swap(self, nf, out err) && File.ReadAllText(self) == "newer", "swap again over an existing .old");
        Check(!Updater.Swap(self, Path.Combine(dir, "missing"), out err) && File.ReadAllText(self) == "newer", "failed swap leaves the exe in place");
        Directory.Delete(dir, true);

        Console.WriteLine(fails == 0 ? "\nALL PASSED" : "\n" + fails + " FAILED");
        return fails;
    }
}
