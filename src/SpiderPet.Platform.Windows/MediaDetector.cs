using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SpiderPet.Core;

namespace SpiderPet.Platform.Windows;

/// <summary>What <see cref="MediaDetector"/> found in the foreground.</summary>
/// <param name="Active">A game, video, photo or similar is being looked at.</param>
/// <param name="Centre">The middle of that window (screen pixels), or null.</param>
/// <param name="Reason">Why it counts ("audio", "player", "game"...), for debugging. Empty when not active.</param>
public readonly record struct MediaResult(bool Active, Vec2? Centre, string Reason);

/// <summary>
/// Decides whether the window in front is "media": something the user is watching or playing and the spider should
/// keep out of the way of. It does not rely on any one site or program; it combines several signals, in this order:
/// <list type="number">
/// <item><b>Full screen</b>: the window fills the screen and has no title bar (games, full-screen video).</item>
/// <item><b>Known players and photo viewers</b>, by program (VLC, mpv, Movies &amp; TV, Photos, IrfanView ...).</item>
/// <item><b>Games</b>, by where the program lives (Steam, Epic, GOG, Riot, Xbox, EA, Ubisoft ... folders).</item>
/// <item><b>The window title</b>: site names, "Episode 12", "S01E04", a video or image file name.</item>
/// <item><b>Sound</b>: the foreground program (or a process it started, which is how browsers play audio) is
/// making sound through Windows' audio mixer. This is what catches a video on any site, however obscure, and
/// anime sites that embed players from several sources: the title says nothing, but the browser is playing sound.
/// Short sounds (notification dings, clicks) are ignored, a pause of up to ten seconds does not end it.</item>
/// </list>
/// Music players, call/chat programs and code editors never count (a song or a call is not something to watch,
/// and a full-screen IDE is not a game).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MediaDetector : IDisposable
{
    // ---- tuning (all public, all plain data)

    /// <summary>Use the audio mixer as a signal. Turn off to rely on the other signals only.</summary>
    public bool UseAudio { get; set; } = true;

    /// <summary>A mixer peak (0..1) above this counts as sound.</summary>
    public double AudioThreshold { get; set; } = 0.003;

    /// <summary>Sound must go on this long (seconds) before it counts, so dings and clicks do not.</summary>
    public double AudioStartSeconds { get; set; } = 4.0;

    /// <summary>Once it counts, silence shorter than this (seconds) does not end it (paused, buffering, a quiet scene).</summary>
    public double AudioHoldSeconds { get; set; } = 10.0;

    /// <summary>Programs that never count: music, calls and chat, recording, code editors.</summary>
    public HashSet<string> IgnoredPrograms { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "spotify.exe", "foobar2000.exe", "winamp.exe", "musicbee.exe", "itunes.exe", "aimp.exe", "tidal.exe",
        "discord.exe", "teams.exe", "ms-teams.exe", "zoom.exe", "slack.exe", "skype.exe", "telegram.exe", "whatsapp.exe",
        "obs64.exe", "obs32.exe",
        "rider64.exe", "idea64.exe", "pycharm64.exe", "webstorm64.exe", "clion64.exe", "goland64.exe", "phpstorm64.exe",
        "rubymine64.exe", "datagrip64.exe", "devenv.exe", "code.exe", "code - insiders.exe", "sublime_text.exe",
        "notepad++.exe", "windowsterminal.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
    };

    /// <summary>Video players and streaming apps: when one is in front it counts, playing or not.</summary>
    public HashSet<string> Players { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "vlc.exe", "mpv.exe", "mpvnet.exe", "mpc-hc.exe", "mpc-hc64.exe", "mpc-be.exe", "mpc-be64.exe",
        "potplayer.exe", "potplayermini.exe", "potplayermini64.exe", "potplayer64.exe", "wmplayer.exe", "video.ui.exe",
        "kodi.exe", "plex.exe", "smplayer.exe", "stremio.exe", "gomplayer.exe", "kmplayer.exe", "bsplayer.exe",
        "netflix.exe", "disneyplus.exe", "primevideo.exe", "crunchyroll.exe", "twitch.exe",
    };

    /// <summary>Picture viewers: when one is in front it counts.</summary>
    public HashSet<string> PhotoViewers { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft.photos.exe", "i_view32.exe", "i_view64.exe", "imageglass.exe", "xnviewmp.exe", "xnview.exe",
        "fsviewer.exe", "honeyview.exe", "nomacs.exe",
    };

    /// <summary>A program that lives under one of these folders is a game.</summary>
    public string[] GameFolders { get; set; } =
    {
        @"\steamapps\common\", @"\epic games\", @"\gog games\", @"\gog galaxy\games\", @"\riot games\", @"\xboxgames\",
        @"\ubisoft game launcher\games\", @"\ea games\", @"\origin games\", @"\rockstar games\", @"\itch\apps\",
    };

    /// <summary>Words in the window title that mean a video or a gallery (the name of a site or game).</summary>
    public string[] TitleKeywords { get; set; } =
    {
        "YouTube", "Netflix", "Twitch", "Prime Video", "Disney+", "Crunchyroll", "Hulu", "Plex", "Vimeo", "Dailymotion",
        "Bilibili", "HBO Max", "Funimation", "Minecraft",
        "Pinterest", "Instagram", "Flickr", "Imgur", "DeviantArt", "ArtStation", "Google Photos", "Unsplash", "Pixiv",
    };

    /// <summary>Patterns in the window title: episodes and seasons, subtitles, and the file name of a video or picture.</summary>
    public Regex[] TitlePatterns { get; set; } =
    {
        new(@"\b(episode|ep\.?)\s*\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bs\d{1,2}\s?e\d{1,3}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bseason\s*\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(eng(lish)?\s+)?(sub(bed|titled)?|dub(bed)?)\b.*\b(episode|ep|anime)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\.(mp4|mkv|avi|mov|webm|wmv|flv|m4v|mpg|mpeg)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\.(jpe?g|png|gif|webp|bmp|tiff?|heic|avif)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    };

    /// <summary>The program name and the reason for the last answer, for the debug display.</summary>
    public string LastProgram { get; private set; } = "";
    public string LastReason { get; private set; } = "";

    // ---- state

    private readonly StringBuilder _text = new(512);
    private readonly Dictionary<uint, string> _paths = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private double _deviceAge;
    private uint _audioProgram;
    private double _audioStreak, _audioSilence;
    private bool _audioHeld;

    public void Dispose()
    {
        (_device as IDisposable)?.Dispose();
        _device = null;
        _enumerator?.Dispose();
        _enumerator = null;
    }

    /// <summary>Looks at the window in front. Call it a couple of times a second.</summary>
    /// <param name="dt">Seconds since the previous call.</param>
    /// <param name="title">The title of that window, if known (empty otherwise).</param>
    public MediaResult Check(double dt, IntPtr foreground, IntPtr ignoredWindow, string title)
    {
        LastProgram = "";
        LastReason = "";
        if (foreground == IntPtr.Zero || foreground == ignoredWindow)
            return Nothing();

        NativeMethods.GetWindowThreadProcessId(foreground, out uint windowPid);
        if (windowPid == (uint)Environment.ProcessId)
            return Nothing();

        _text.Clear();
        NativeMethods.GetClassName(foreground, _text, _text.Capacity);
        string windowClass = _text.ToString();
        if (windowClass is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return Nothing();

        if (!NativeMethods.GetWindowRect(foreground, out RECT r))
            return Nothing();

        Dictionary<uint, (uint Parent, string Exe)> processes = SnapshotProcesses();

        // A Store app is shown in a frame window that belongs to ApplicationFrameHost; the app itself is a child window.
        uint appPid = windowPid;
        string exe = NameOf(processes, appPid);
        if (exe.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            IntPtr core = FindWindowEx(foreground, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
            if (core != IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(core, out uint corePid);
                if (corePid != 0)
                {
                    appPid = corePid;
                    exe = NameOf(processes, corePid);
                }
            }
        }
        LastProgram = exe;

        if (IgnoredPrograms.Contains(exe))
        {
            ResetAudio(appPid);
            return Nothing();
        }

        // sound, tracked all the time so that "going on for a few seconds" makes sense
        bool audio = UseAudio && AudioCounts(dt, processes, appPid, windowPid);

        int screenW = NativeMethods.GetSystemMetrics(0), screenH = NativeMethods.GetSystemMetrics(1);
        long style = NativeMethods.GetWindowLongPtr(foreground, NativeMethods.GWL_STYLE);
        bool fullScreen = r.Left <= 0 && r.Top <= 0 && r.Right >= screenW && r.Bottom >= screenH
                          && (style & NativeMethods.WS_CAPTION) == 0;

        string reason = "";
        if (fullScreen) reason = "full screen";
        else if (Players.Contains(exe)) reason = "player";
        else if (PhotoViewers.Contains(exe)) reason = "photo viewer";
        else if (IsGame(appPid)) reason = "game";
        else if (TitleMatches(title)) reason = "title";
        else if (audio) reason = "sound";

        if (reason.Length == 0)
            return Nothing();

        LastReason = reason;
        return new MediaResult(true, new Vec2((r.Left + r.Right) / 2.0, (r.Top + r.Bottom) / 2.0), reason);
    }

    private static MediaResult Nothing() => new(false, null, "");

    // ---- title

    private bool TitleMatches(string title)
    {
        if (string.IsNullOrEmpty(title))
            return false;
        foreach (string k in TitleKeywords)
            if (title.Contains(k, StringComparison.OrdinalIgnoreCase))
                return true;
        foreach (Regex p in TitlePatterns)
            if (p.IsMatch(title))
                return true;
        return false;
    }

    // ---- games

    private bool IsGame(uint pid)
    {
        string path = PathOf(pid).ToLowerInvariant();
        if (path.Length == 0)
            return false;
        foreach (string folder in GameFolders)
            if (path.Contains(folder, StringComparison.Ordinal))
                return true;
        return false;
    }

    private string PathOf(uint pid)
    {
        if (_paths.TryGetValue(pid, out string? known))
            return known;

        string path = "";
        IntPtr handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var name = new StringBuilder(1024);
                uint size = (uint)name.Capacity;
                if (QueryFullProcessImageName(handle, 0, name, ref size))
                    path = name.ToString();
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        if (_paths.Count > 128)
            _paths.Clear();
        _paths[pid] = path;
        return path;
    }

    // ---- sound

    private void ResetAudio(uint program)
    {
        _audioProgram = program;
        _audioStreak = 0.0;
        _audioSilence = 0.0;
        _audioHeld = false;
    }

    // True while the program in front has been making sound for a while (and shortly after it stopped).
    private bool AudioCounts(double dt, Dictionary<uint, (uint Parent, string Exe)> processes, uint appPid, uint windowPid)
    {
        if (_audioProgram != appPid)
            ResetAudio(appPid);

        bool now = IsMakingSound(processes, appPid, windowPid);
        if (now)
        {
            _audioSilence = 0.0;
            _audioStreak += dt;
            if (_audioStreak >= AudioStartSeconds)
                _audioHeld = true;
        }
        else
        {
            _audioSilence += dt;
            if (_audioSilence > 1.5)
                _audioStreak = 0.0;
            if (_audioSilence >= AudioHoldSeconds)
                _audioHeld = false;
        }
        return _audioHeld;
    }

    // Does any audio session of the program, or of a process it started (browsers play sound from a helper
    // process), have sound coming out right now?
    private bool IsMakingSound(Dictionary<uint, (uint Parent, string Exe)> processes, uint appPid, uint windowPid)
    {
        try
        {
            _deviceAge += 0.5;
            if (_device is null || _deviceAge > 20.0)
            {
                // the default output can change (headphones plugged in), so now and then look again
                (_device as IDisposable)?.Dispose();
                _enumerator ??= new MMDeviceEnumerator();
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _deviceAge = 0.0;
            }

            AudioSessionManager manager = _device.AudioSessionManager;
            manager.RefreshSessions();
            SessionCollection sessions = manager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSessionControl session = sessions[i];
                if (session.IsSystemSoundsSession)
                    continue;
                if (session.State != AudioSessionState.AudioSessionStateActive)
                    continue;
                if (!BelongsTo(processes, session.GetProcessID, appPid, windowPid))
                    continue;
                if (session.AudioMeterInformation.MasterPeakValue > AudioThreshold)
                    return true;
            }
        }
        catch (Exception)
        {
            _device = null;   // no sound device, or it changed under us: try again next time
        }
        return false;
    }

    private static bool BelongsTo(Dictionary<uint, (uint Parent, string Exe)> processes, uint pid, uint appPid, uint windowPid)
    {
        for (int hop = 0; hop < 8 && pid != 0; hop++)
        {
            if (pid == appPid || pid == windowPid)
                return true;
            if (!processes.TryGetValue(pid, out var info))
                return false;
            pid = info.Parent;
        }
        return false;
    }

    // ---- processes

    private static Dictionary<uint, (uint Parent, string Exe)> SnapshotProcesses()
    {
        var result = new Dictionary<uint, (uint, string)>();
        IntPtr snapshot = CreateToolhelp32Snapshot(0x2 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return result;

        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    result[entry.th32ProcessID] = (entry.th32ParentProcessID, entry.szExeFile ?? "");
                }
                while (Process32NextW(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return result;
    }

    private static string NameOf(Dictionary<uint, (uint Parent, string Exe)> processes, uint pid) =>
        processes.TryGetValue(pid, out var info) ? info.Exe : "";

    // ---- native

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowTitle);
}
