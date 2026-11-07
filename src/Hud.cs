using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

sealed class Track
{
    public string Title, Artist, Album, Cover;
    public long Position, Duration;
    public bool Playing;
    public string Key { get { return Title + "\t" + Artist + "\t" + Album + "\t" + Duration + "\t" + Cover; } }
    public string LeadArtist { get { return Artist.Split(new[] { ", " }, StringSplitOptions.None)[0]; } }
}

sealed class Line
{
    public long? at_ms { get; set; }
    public string text { get; set; }
}

sealed class Lyrics
{
    public List<Line> lines { get; set; }
    public bool synced { get; set; }
    public bool instrumental { get; set; }
    public string Source { get; set; }
}

sealed class Cache { public Lyrics found { get; set; } }
sealed class Play { public HistoryTrack track { get; set; } }
sealed class HistoryTrack
{
    public string id { get; set; }
    public string uri { get; set; }
    public string name { get; set; }
    public long duration_ms { get; set; }
    public Artist[] artists { get; set; }
    public Album album { get; set; }
}
sealed class Artist { public string name { get; set; } }
sealed class Album
{
    public string name { get; set; }
    public Cover[] images { get; set; }
}
sealed class Cover { public string url { get; set; } }
sealed class Lrc
{
    public string trackName { get; set; }
    public string artistName { get; set; }
    public string albumName { get; set; }
    public double duration { get; set; }
    public bool instrumental { get; set; }
    public string syncedLyrics { get; set; }
}

static class Spotifast
{
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static readonly string Root = Path.Combine(Local, @"paolino\spotifast");
    static readonly string Executable = Path.Combine(Local, @"Programs\Spotifast\spotifast.exe");

    public static Track Read()
    {
        if (!File.Exists(Executable)) return null;
        var start = new ProcessStartInfo(Executable, "now-playing --raw")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8
        };
        using (var process = Process.Start(start))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(1500))
            {
                try { process.Kill(); }
                catch (InvalidOperationException) { }
                return null;
            }
            if (process.ExitCode != 0 || !output.Wait(200)) return null;
            var fields = output.Result.TrimEnd('\r', '\n').Split('\t');
            long position, duration;
            if (fields.Length < 10 || (fields[0] != "playing" && fields[0] != "paused")
                || !long.TryParse(fields[4], out position) || !long.TryParse(fields[5], out duration)) return null;
            return new Track
            {
                Playing = fields[0] == "playing", Title = fields[1], Artist = fields[2],
                Album = fields[3], Position = position, Duration = duration, Cover = fields[9]
            };
        }
    }
}

static class Lookup
{
    static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    static readonly Regex Stamp = new Regex(@"\[(\d{1,4}):([0-5]\d)(?:[.:](\d{1,3}))?\]");
    static readonly Regex Offset = new Regex(@"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
    static readonly Regex TrackId = new Regex(@"^[a-zA-Z0-9]{22}$");
    static readonly Regex Whitespace = new Regex(@"\s+");

    static Lookup()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Spotifast-Lyrics-HUD/1.0");
    }

    static JavaScriptSerializer Json()
    {
        return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
    }

    public static async Task<Lyrics> Find(Track track, CancellationToken token)
    {
        var local = ReadLocal(track);
        if (local != null || string.IsNullOrWhiteSpace(track.Artist) || track.Duration <= 0) return local;

        string query = "track_name=" + Uri.EscapeDataString(track.Title)
            + "&artist_name=" + Uri.EscapeDataString(track.LeadArtist);
        var exact = await Get<Lrc>("/get?" + query + "&album_name="
            + Uri.EscapeDataString(track.Album) + "&duration="
            + (track.Duration / 1000.0).ToString("0", CultureInfo.InvariantCulture), token).ConfigureAwait(false);
        var first = ConvertRecord(exact);
        if (first != null) return first;

        var records = await Get<Lrc[]>("/search?" + query, token).ConfigureAwait(false);
        return (records ?? new Lrc[0])
            .Where(r => Math.Abs(r.duration * 1000 - track.Duration) <= 5000
                && Same(r.trackName, track.Title)
                && (Same(r.artistName, track.Artist) || Same(r.artistName, track.LeadArtist)))
            .OrderBy(r => !Same(Whitespace.Replace(r.albumName ?? "", ""), Whitespace.Replace(track.Album, "")))
            .ThenBy(r => Math.Abs(r.duration * 1000 - track.Duration))
            .Select(ConvertRecord).FirstOrDefault(r => r != null);
    }

    static async Task<T> Get<T>(string path, CancellationToken token) where T : class
    {
        using (var response = await Http.GetAsync("https://lrclib.net/api" + path, token).ConfigureAwait(false))
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return Json().Deserialize<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
    }

    static bool Same(string a, string b)
    {
        return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    static Lyrics ReadLocal(Track track)
    {
        var history = ReadJson<Play[]>(Path.Combine(Spotifast.Root, @"data\history.json"));
        var matching = (history ?? new Play[0]).Select(p => p.track)
            .Where(t => t != null && t.album != null && t.artists != null
                && t.name == track.Title && t.album.name == track.Album && t.duration_ms == track.Duration
                && string.Join(", ", t.artists.Select(a => a.name)) == track.Artist
                && (string.IsNullOrEmpty(track.Cover)
                    || (t.album.images ?? new Cover[0]).Any(i => i.url == track.Cover)))
            .ToList();
        string cache = Path.Combine(Spotifast.Root, @"cache\lyrics");
        var ids = matching.Where(t => t.id != null && TrackId.IsMatch(t.id)
            && t.uri == "spotify:track:" + t.id).Select(t => t.id).Distinct().ToList();
        if (ids.Count == 1)
        {
            foreach (string prefix in new[] { "spotify-v2-", "spotify-" })
            {
                var found = ReadCached(Path.Combine(cache, prefix + ids[0] + ".json"), "Spotifast 缓存 · Spotify");
                if (found != null) return found;
            }
        }

        var artist = matching.Select(t => t.artists.FirstOrDefault()).Where(a => a != null)
            .Select(a => a.name).FirstOrDefault() ?? track.LeadArtist;
        var key = string.Join("|", artist, track.Title, track.Album, track.Duration.ToString(CultureInfo.InvariantCulture));
        string hash;
        using (var sha = SHA1.Create())
            hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant();
        return ReadCached(Path.Combine(cache, hash + ".json"), "Spotifast 缓存 · LRCLIB");
    }

    static Lyrics ReadCached(string path, string source)
    {
        var record = ReadJson<Cache>(path);
        var lyrics = record == null ? null : record.found;
        if (lyrics == null || lyrics.lines == null) return null;
        if (!lyrics.instrumental && (!lyrics.synced || lyrics.lines.Count == 0 || lyrics.lines.Any(l => !l.at_ms.HasValue)))
            return null;
        lyrics.lines = lyrics.lines.OrderBy(l => l.at_ms).ToList();
        lyrics.Source = source;
        return lyrics;
    }

    static T ReadJson<T>(string path) where T : class
    {
        try
        {
            if (File.Exists(path)) return Json().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        return null;
    }

    static Lyrics ConvertRecord(Lrc record)
    {
        if (record == null) return null;
        var lines = new List<Line>();
        if (!string.IsNullOrWhiteSpace(record.syncedLyrics))
        {
            int offset;
            int.TryParse(Offset.Match(record.syncedLyrics).Groups[1].Value, out offset);
            foreach (var row in record.syncedLyrics.Split('\n'))
            {
                var stamps = Stamp.Matches(row);
                if (stamps.Count == 0) continue;
                var last = stamps[stamps.Count - 1];
                var content = row.Substring(last.Index + last.Length).Trim();
                foreach (Match stamp in stamps)
                {
                    long minutes = long.Parse(stamp.Groups[1].Value);
                    long seconds = long.Parse(stamp.Groups[2].Value);
                    long fraction = long.Parse(stamp.Groups[3].Value.PadRight(3, '0'));
                    lines.Add(new Line
                    {
                        at_ms = Math.Max(0, minutes * 60000 + seconds * 1000 + fraction - offset),
                        text = content
                    });
                }
            }
        }
        bool timed = lines.Any(l => !string.IsNullOrWhiteSpace(l.text));
        if (!timed && !record.instrumental) return null;
        return new Lyrics
        {
            Source = "LRCLIB", synced = timed, instrumental = record.instrumental,
            lines = lines.OrderBy(l => l.at_ms).ToList()
        };
    }
}

sealed class Hud : Window
{
    readonly TextBlock title, source, current, next;
    readonly DispatcherTimer poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer render = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
    readonly Stopwatch clock = new Stopwatch();
    Track track;
    Lyrics lyrics;
    string status = "";
    CancellationTokenSource lookup;
    bool reading, closed;
    int misses;
    DateTime retryAt = DateTime.MaxValue;

    static Brush Color(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }

    public Hud()
    {
        Title = "Spotifast 歌词浮窗";
        Width = Math.Min(700, SystemParameters.WorkArea.Width - 32);
        Height = 160;
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = SystemParameters.WorkArea.Bottom - Height - 26;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = true;
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");

        var card = new Border
        {
            Background = Color("#ED151B22"), BorderBrush = Color("#FF34404A"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(19, 10, 13, 10)
        };
        Content = card;
        var layout = new Grid();
        card.Child = layout;
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(29) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

        var header = new DockPanel { LastChildFill = true };
        layout.Children.Add(header);
        var close = new Button
        {
            Content = "×", FontSize = 21, Width = 30, Height = 27, Padding = new Thickness(0),
            BorderThickness = new Thickness(0), Background = Color("#FF2B3540"),
            Foreground = Color("#FFE0E8F0"), Cursor = Cursors.Hand, ToolTip = "关闭浮窗"
        };
        close.Click += (sender, args) => Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);

        source = Text(11, "#FF9EACBA");
        source.Margin = new Thickness(10, 0, 11, 0);
        DockPanel.SetDock(source, Dock.Right);
        header.Children.Add(source);
        title = Text(12, "#FF9AE5BD");
        title.Cursor = Cursors.SizeAll;
        title.ToolTip = "按住拖动浮窗";
        title.Background = Brushes.Transparent;
        title.MouseLeftButtonDown += (sender, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        header.Children.Add(title);

        current = Text(27, "#FFF6FAFC");
        current.FontWeight = FontWeights.SemiBold;
        current.TextAlignment = TextAlignment.Center;
        current.TextWrapping = TextWrapping.Wrap;
        current.LineHeight = 32;
        current.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        current.MaxHeight = 65;
        current.ClipToBounds = true;
        Grid.SetRow(current, 1);
        layout.Children.Add(current);

        next = Text(15, "#FFA6B2BF");
        next.TextAlignment = TextAlignment.Center;
        Grid.SetRow(next, 2);
        layout.Children.Add(next);

        poll.Tick += async (sender, args) => await RefreshTrack();
        render.Tick += (sender, args) => RenderLyrics();
        Loaded += async (sender, args) =>
        {
            RenderLyrics();
            poll.Start();
            render.Start();
            await RefreshTrack();
        };
        Closed += (sender, args) =>
        {
            closed = true;
            poll.Stop();
            render.Stop();
            if (lookup != null) lookup.Cancel();
        };
    }

    static TextBlock Text(double size, string color)
    {
        return new TextBlock
        {
            FontSize = size, Foreground = Color(color),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
    }

    async Task RefreshTrack()
    {
        if (reading || closed) return;
        reading = true;
        Track latest = null;
        try { latest = await Task.Run(() => Spotifast.Read()); }
        catch (System.ComponentModel.Win32Exception) { }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        catch (AggregateException) { }
        finally { reading = false; }
        if (closed) return;
        if (latest == null && track != null && ++misses < 2) return;
        if (latest != null) misses = 0;
        bool changed = (latest == null ? null : latest.Key) != (track == null ? null : track.Key);
        track = latest;
        clock.Restart();
        if (changed)
        {
            if (lookup != null) lookup.Cancel();
            lyrics = null;
            status = "";
            retryAt = DateTime.MaxValue;
        }
        if (latest != null && (changed || DateTime.UtcNow >= retryAt))
            await RefreshLyrics(latest);
        RenderLyrics();
    }

    async Task RefreshLyrics(Track latest)
    {
        var request = new CancellationTokenSource();
        lookup = request;
        status = "正在查找歌词…";
        retryAt = DateTime.MaxValue;
        RenderLyrics();
        try
        {
            var result = await Task.Run(() => Lookup.Find(latest, request.Token));
            if (closed || request.IsCancellationRequested) return;
            lyrics = result;
            status = "没有找到同步歌词";
            if (result == null) retryAt = DateTime.UtcNow.AddSeconds(45);
        }
        catch (Exception)
        {
            if (closed || request.IsCancellationRequested) return;
            status = "暂时无法获取歌词";
            retryAt = DateTime.UtcNow.AddSeconds(20);
        }
        finally
        {
            if (lookup == request) lookup = null;
            request.Dispose();
        }
    }

    public static int ActiveLine(List<Line> lines, long position)
    {
        int low = 0, high = lines.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (lines[middle].at_ms <= position) low = middle + 1;
            else high = middle;
        }
        return low - 1;
    }

    void RenderLyrics()
    {
        string trackText = track == null ? "SPOTIFAST · 桌面歌词" : track.Title + "  ·  " + track.Artist;
        string sourceText = track == null ? "" : (!track.Playing ? "已暂停" : (lyrics == null ? "" : lyrics.Source));
        string currentText = track == null ? "等待 Spotifast 播放" : status;
        string nextText = "";
        if (track != null && lyrics != null)
        {
            if (lyrics.instrumental) currentText = "纯音乐";
            else if (lyrics.synced && lyrics.lines.Count > 0)
            {
                long position = track.Position + (track.Playing ? Math.Min(850, clock.ElapsedMilliseconds) : 0);
                if (track.Duration > 0) position = Math.Min(track.Duration, position);
                var lines = lyrics.lines;
                int active = ActiveLine(lines, position);
                currentText = active < 0 || string.IsNullOrWhiteSpace(lines[active].text) ? "♪" : lines[active].text;
                for (int i = active + 1; i < lines.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i].text)) continue;
                    nextText = lines[i].text;
                    break;
                }
            }
        }

        if (title.Text != trackText) title.Text = trackText;
        if (source.Text != sourceText) source.Text = sourceText;
        if (current.Text != currentText) current.Text = currentText;
        if (next.Text != nextText) next.Text = nextText;
    }
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--check")
        {
            try
            {
                var track = Spotifast.Read();
                if (track == null) { File.WriteAllText(args[1], "No Spotifast track"); return 1; }
                var lyrics = Lookup.Find(track, CancellationToken.None).GetAwaiter().GetResult();
                int active = lyrics == null || !lyrics.synced ? -1 : Hud.ActiveLine(lyrics.lines, track.Position);
                File.WriteAllText(args[1], string.Format(
                    "{0} | {1} | playing={2} | position={3} | source={4} | synced={5} | lines={6} | activeIndex={7}",
                    track.Title, track.Artist, track.Playing, track.Position,
                    lyrics == null ? "none" : lyrics.Source, lyrics != null && lyrics.synced,
                    lyrics == null ? 0 : lyrics.lines.Count, active));
                return lyrics != null && lyrics.synced ? 0 : 2;
            }
            catch (Exception error) { File.WriteAllText(args[1], error.ToString()); return 3; }
        }

        bool first;
        using (var instance = new Mutex(true, "Local\\SpotifastLyricsHud", out first))
        {
            if (!first) return 0;
            return new Application().Run(new Hud());
        }
    }
}
