// Updater.cs
// Checks GitHub Releases for a newer PenButton.exe, downloads it and swaps it in.
// Used only by PenButton.exe (the settings window); the button programs never go online.
//
// Flow: Check() reads https://api.github.com/repos/<AppInfo.Repo>/releases/latest.
// If its version is newer, Download() saves the PenButton.exe asset next to the running
// one as PenButton.exe.new and checks it (size, SHA-256 when GitHub gives one, that it is
// a .NET program of the expected version). ApplyAndRestart() renames the running exe to
// PenButton.exe.old (Windows allows renaming a running exe), moves the new file into
// place and starts it. The new instance deletes the .old file and refreshes the three
// button programs. C# 5 / .NET Framework 4.x only.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

class UpdateInfo
{
    public Version Version;
    public string Tag = "";
    public string DownloadUrl = "";
    public long Size;
    public string Sha256 = "";     // lower-case hex, empty if GitHub did not give one
    public string PageUrl = "";
}

static class Updater
{
    public const string AssetName = "PenButton.exe";

    public static Version Current
    {
        get { return Norm(new Version(AppInfo.Version)); }
    }

    // Major.Minor.Build only, so 2.3.0 and 2.3.0.0 compare equal.
    public static Version Norm(Version v)
    {
        if (v == null) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    // "v2.3.0" or "2.3.0" -> 2.3.0; null if it is not a version.
    public static Version ParseVersion(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        string t = tag.Trim().TrimStart('v', 'V');
        Version v;
        return Version.TryParse(t, out v) ? Norm(v) : null;
    }

    // Parses the JSON of a GitHub "release" object. Returns null if it has no usable PenButton.exe.
    public static UpdateInfo ParseRelease(string json)
    {
        JavaScriptSerializer js = new JavaScriptSerializer();
        js.MaxJsonLength = 16 * 1024 * 1024;
        Dictionary<string, object> rel = js.DeserializeObject(json) as Dictionary<string, object>;
        if (rel == null) return null;
        if (Bool(rel, "draft") || Bool(rel, "prerelease")) return null;

        UpdateInfo u = new UpdateInfo();
        u.Tag = Str(rel, "tag_name");
        u.Version = ParseVersion(u.Tag);
        u.PageUrl = Str(rel, "html_url");
        if (u.Version == null) return null;

        object assets;
        if (!rel.TryGetValue("assets", out assets) || !(assets is IEnumerable)) return null;
        foreach (object o in (IEnumerable)assets)
        {
            Dictionary<string, object> a = o as Dictionary<string, object>;
            if (a == null || !string.Equals(Str(a, "name"), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
            u.DownloadUrl = Str(a, "browser_download_url");
            object size;
            if (a.TryGetValue("size", out size) && size != null) u.Size = Convert.ToInt64(size);
            string digest = Str(a, "digest");
            if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) u.Sha256 = digest.Substring(7).ToLowerInvariant();
            break;
        }
        if (u.DownloadUrl.Length == 0 || !u.DownloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
        return u;
    }

    static string Str(Dictionary<string, object> d, string k)
    {
        object v;
        return d.TryGetValue(k, out v) && v != null ? v.ToString() : "";
    }

    static bool Bool(Dictionary<string, object> d, string k)
    {
        object v;
        return d.TryGetValue(k, out v) && v is bool && (bool)v;
    }

    static void EnableTls12()
    {
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch (Exception) { }
    }

    static HttpWebRequest Request(string url, string accept)
    {
        HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
        r.UserAgent = "PenButton/" + AppInfo.Version + " (+https://github.com/" + AppInfo.Repo + ")";
        r.Accept = accept;
        r.Timeout = 15000;
        r.ReadWriteTimeout = 30000;
        r.AllowAutoRedirect = true;
        return r;
    }

    // Latest release, or null when there is none or GitHub cannot be reached. error explains why.
    public static UpdateInfo CheckLatest(out string error)
    {
        error = null;
        EnableTls12();
        try
        {
            HttpWebRequest r = Request("https://api.github.com/repos/" + AppInfo.Repo + "/releases/latest", "application/vnd.github+json");
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                UpdateInfo u = ParseRelease(sr.ReadToEnd());
                if (u == null) error = L.T("最新版本里没有找到 PenButton.exe", "The latest release has no PenButton.exe");
                return u;
            }
        }
        catch (WebException ex)
        {
            HttpWebResponse resp = ex.Response as HttpWebResponse;
            if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
                error = L.T("GitHub 上还没有公开的版本", "No public release on GitHub yet");
            else
                error = L.T("连不上 GitHub：", "Could not reach GitHub: ") + ex.Message;
            return null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    public static bool IsNewer(UpdateInfo u)
    {
        return u != null && u.Version != null && u.Version > Current;
    }

    public static string SelfPath()
    {
        return Assembly.GetExecutingAssembly().Location;
    }

    // Downloads to PenButton.exe.new next to the running exe and checks it. Returns its path.
    public static string Download(UpdateInfo u)
    {
        EnableTls12();
        string self = SelfPath();
        string part = self + ".download";
        string ready = self + ".new";
        if (File.Exists(part)) File.Delete(part);

        HttpWebRequest r = Request(u.DownloadUrl, "application/octet-stream");
        using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
        using (Stream s = resp.GetResponseStream())
        using (FileStream fs = File.Create(part))
        {
            s.CopyTo(fs);
        }

        string problem = Verify(part, u);
        if (problem != null)
        {
            try { File.Delete(part); } catch (Exception) { }
            throw new InvalidDataException(problem);
        }
        if (File.Exists(ready)) File.Delete(ready);
        File.Move(part, ready);
        return ready;
    }

    // Returns null when the file looks like the expected PenButton.exe.
    public static string Verify(string path, UpdateInfo u)
    {
        FileInfo fi = new FileInfo(path);
        if (u.Size > 0 && fi.Length != u.Size) return L.T("下载的文件大小不对", "The download has the wrong size");
        byte[] head = new byte[2];
        using (FileStream fs = File.OpenRead(path)) fs.Read(head, 0, 2);
        if (head[0] != 'M' || head[1] != 'Z') return L.T("下载的不是程序文件", "The download is not a program");
        if (u.Sha256.Length > 0)
        {
            string got;
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                got = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (got != u.Sha256) return L.T("下载的文件校验不通过", "The download failed its checksum");
        }
        try
        {
            AssemblyName an = AssemblyName.GetAssemblyName(path);   // reads metadata, does not run it
            if (an.Version == null || Norm(an.Version) < u.Version) return L.T("下载的版本号不对", "The download has the wrong version");
        }
        catch (Exception) { return L.T("下载的文件无法识别", "The download could not be read"); }
        return null;
    }

    // Puts newFile in place of the running exe, keeping the old one as .old. Returns false (and
    // leaves everything as it was) if it could not.
    public static bool Swap(string self, string newFile, out string error)
    {
        error = null;
        string old = self + ".old";
        try
        {
            if (File.Exists(old)) File.Delete(old);
        }
        catch (Exception)
        {
            old = self + "." + DateTime.Now.Ticks + ".old";   // an older .old is still in use
        }
        try
        {
            File.Move(self, old);
        }
        catch (Exception ex) { error = ex.Message; return false; }
        try
        {
            File.Move(newFile, self);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try { File.Move(old, self); } catch (Exception) { }
            return false;
        }
    }

    public static bool ApplyAndRestart(string newFile, out string error)
    {
        string self = SelfPath();
        if (!Swap(self, newFile, out error)) return false;
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(self, "--updated");
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(self);
            Process.Start(psi);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    // Removes PenButton.exe.old (and leftovers) from an earlier update. The old process may
    // still be closing, so retry for a few seconds in the background.
    public static void CleanupOld()
    {
        string self = SelfPath();
        string dir = Path.GetDirectoryName(self);
        string name = Path.GetFileName(self);
        ThreadPool.QueueUserWorkItem(delegate
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                bool left = false;
                try
                {
                    foreach (string f in Directory.GetFiles(dir, name + "*.old"))
                    {
                        try { File.Delete(f); } catch (Exception) { left = true; }
                    }
                    string part = self + ".download";
                    if (File.Exists(part)) { try { File.Delete(part); } catch (Exception) { left = true; } }
                }
                catch (Exception) { return; }
                if (!left) return;
                Thread.Sleep(500);
            }
        });
    }

    // A PenButton.exe.new from an earlier download that is newer than this exe.
    public static string PendingDownload()
    {
        string ready = SelfPath() + ".new";
        if (!File.Exists(ready)) return null;
        try
        {
            AssemblyName an = AssemblyName.GetAssemblyName(ready);
            if (an.Version != null && Norm(an.Version) > Current) return ready;
        }
        catch (Exception) { }
        try { File.Delete(ready); } catch (Exception) { }
        return null;
    }
}
