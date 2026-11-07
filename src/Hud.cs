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
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

sealed class Track
{
    public string Title, Artist, Album, Cover;
    public long Position, Duration;
    public bool Playing;
    string simplifiedLabel;
    public string Key { get { return Title + "\t" + Artist + "\t" + Album + "\t" + Duration + "\t" + Cover; } }
    public string LeadArtist { get { return Artist.Split(new[] { ", " }, StringSplitOptions.None)[0]; } }

    public string DisplayLabel(bool simplify)
    {
        string label = Title + "  ·  " + Artist;
        if (!simplify) return label;
        return simplifiedLabel ?? (simplifiedLabel = ChineseText.Simplify(label));
    }
}

sealed class PlaybackTimeline
{
    double anchor, sampledAt, advancedAt, rate;
    long previousPosition;
    bool ready, playing;

    public double Position(double now)
    {
        return anchor + (playing ? Math.Min(1200, Math.Max(0, now - sampledAt)) * rate : 0);
    }

    public void Update(Track track, double now, bool changed)
    {
        if (track == null)
        {
            ready = false;
            return;
        }

        double predicted = Position(now);
        double error = track.Position - predicted;
        bool advanced = track.Position != previousPosition;
        bool jumped = advanced && (track.Position < previousPosition - 120 || Math.Abs(error) > 450);
        bool reset = !ready || changed || playing != track.Playing || jumped;
        if (reset || advanced) advancedAt = now;

        anchor = reset || !track.Playing ? track.Position : predicted;
        // Correct small discrepancies by changing speed briefly so the colour edge never moves backwards.
        double targetRate = 1 + Math.Max(-0.15, Math.Min(0.15, error / 2200));
        if (reset) rate = 1;
        else if (now - advancedAt > 950) rate = 0;
        else rate = rate == 0 ? targetRate : (rate + targetRate) / 2;
        sampledAt = now;
        previousPosition = track.Position;
        playing = track.Playing;
        ready = true;
    }
}

sealed class Line
{
    string simplified;
    int[] simplifiedOffsets;
    public long? at_ms { get; set; }
    public string text { get; set; }
    public List<Segment> Segments { get; set; }

    public string DisplayText(bool simplify)
    {
        if (!simplify) return text;
        return simplified ?? (simplified = ChineseText.Simplify(text, Segments != null, out simplifiedOffsets));
    }

    public int DisplayOffset(int sourceOffset, bool simplify)
    {
        return simplify && simplifiedOffsets != null ? simplifiedOffsets[sourceOffset] : sourceOffset;
    }
}

static class ChineseText
{
    static readonly Lazy<DictionaryData> OpenCc = new Lazy<DictionaryData>(() => new DictionaryData());

    public static string Simplify(string text)
    {
        int[] unused;
        return Simplify(text, false, out unused);
    }

    public static string Simplify(string text, bool keepOffsets, out int[] offsets)
    {
        offsets = null;
        if (string.IsNullOrEmpty(text) || !text.Any(IsHanCandidate)) return text ?? "";
        var dictionary = OpenCc.Value;
        var result = new StringBuilder(text.Length);
        if (keepOffsets) offsets = new int[text.Length + 1];
        for (int i = 0; i < text.Length;)
        {
            string mapped = null;
            int length = 1;
            if (IsHanCandidate(text[i]))
            {
                for (int size = Math.Min(dictionary.MaxKeyLength, text.Length - i); size >= 1; size--)
                {
                    if (!dictionary.Values.TryGetValue(text.Substring(i, size), out mapped)) continue;
                    length = size;
                    break;
                }
            }
            if (offsets != null)
            {
                int replacementLength = mapped == null ? length : mapped.Length;
                for (int j = 0; j < length; j++)
                    offsets[i + j] = result.Length + j * replacementLength / length;
            }
            if (mapped == null) result.Append(text[i]);
            else result.Append(mapped);
            i += length;
        }
        if (offsets != null) offsets[text.Length] = result.Length;
        return result.ToString();
    }

    static bool IsHanCandidate(char c)
    {
        return (c >= '\u3400' && c <= '\u9FFF') || (c >= '\uF900' && c <= '\uFAFF') || char.IsHighSurrogate(c);
    }

    sealed class DictionaryData
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly int MaxKeyLength;

        public DictionaryData()
        {
            // OpenCC 1.2.0 t2s dictionaries, with phrase matches preferred over single characters.
            foreach (var name in new[] { "OpenCC.TSCharacters", "OpenCC.TSPhrases" })
            {
                using (var stream = typeof(ChineseText).Assembly.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string row;
                    while ((row = reader.ReadLine()) != null)
                    {
                        if (row.Length == 0 || row[0] == '#') continue;
                        int tab = row.IndexOf('\t');
                        if (tab < 1 || tab == row.Length - 1) continue;
                        var key = row.Substring(0, tab);
                        int nextValue = row.IndexOf(' ', tab + 1);
                        var value = nextValue < 0 ? row.Substring(tab + 1) : row.Substring(tab + 1, nextValue - tab - 1);
                        Values[key] = value;
                        MaxKeyLength = Math.Max(MaxKeyLength, key.Length);
                    }
                }
            }
        }
    }
}

sealed class Segment
{
    public long At;
    public int Start;
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
    public string plainLyrics { get; set; }
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
    static readonly Regex InlineStamp = new Regex(@"<(\d{1,4}):([0-5]\d)(?:[.:](\d{1,3}))?>");
    static readonly Regex Offset = new Regex(@"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
    static readonly Regex TrackId = new Regex(@"^[a-zA-Z0-9]{22}$");
    static readonly Regex Whitespace = new Regex(@"\s+");
    static readonly Regex BilingualTitle = new Regex(@"^(?<title>.*[\u3400-\u9FFF].*?)\s*\((?<alias>[A-Za-z][A-Za-z0-9 &'’.,!?-]*)\)$");

    static Lookup()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Spotifast-Lyrics-HUD/1.0");
    }

    static JavaScriptSerializer Json()
    {
        return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
    }

    public static async Task<Lyrics> Find(Track track, CancellationToken token, IProgress<Lyrics> progress = null)
    {
        var available = ReadLocal(track);
        if (IsTimed(available) || string.IsNullOrWhiteSpace(track.Artist) || track.Duration <= 0) return available;
        if (available == null) available = await FindExact(track, token).ConfigureAwait(false);
        if (available == null || IsTimed(available) || string.IsNullOrWhiteSpace(track.Album)) return available;

        if (progress != null) progress.Report(available);
        try
        {
            return await FindAlternate(track, available, token).ConfigureAwait(false) ?? available;
        }
        catch (HttpRequestException) { return available; }
        catch (TaskCanceledException) { token.ThrowIfCancellationRequested(); return available; }
    }

    static bool IsTimed(Lyrics lyrics)
    {
        return lyrics != null && (lyrics.synced || lyrics.instrumental);
    }

    static async Task<Lyrics> FindExact(Track track, CancellationToken token)
    {
        string query = "track_name=" + Uri.EscapeDataString(track.Title)
            + "&artist_name=" + Uri.EscapeDataString(track.LeadArtist);
        var exact = await Get<Lrc>("/get?" + query + "&album_name="
            + Uri.EscapeDataString(track.Album) + "&duration="
            + (track.Duration / 1000.0).ToString("0", CultureInfo.InvariantCulture), token).ConfigureAwait(false);
        var first = ConvertRecord(exact);
        if (first != null && (first.synced || first.instrumental)) return first;

        Lrc[] records;
        try { records = await Get<Lrc[]>("/search?" + query, token).ConfigureAwait(false); }
        catch (HttpRequestException) { if (first != null) return first; throw; }
        catch (TaskCanceledException) { if (first != null && !token.IsCancellationRequested) return first; throw; }
        var candidates = (records ?? new Lrc[0])
            .Where(r => Math.Abs(r.duration * 1000 - track.Duration) <= 5000
                && Same(r.trackName, track.Title)
                && (Same(r.artistName, track.Artist) || Same(r.artistName, track.LeadArtist)))
            .OrderBy(r => !Same(Whitespace.Replace(r.albumName ?? "", ""), Whitespace.Replace(track.Album, "")))
            .ThenBy(r => Math.Abs(r.duration * 1000 - track.Duration))
            .Select(ConvertRecord).Where(r => r != null).ToList();
        return candidates.FirstOrDefault(r => r.synced) ?? first ?? candidates.FirstOrDefault();
    }

    static async Task<Lyrics> FindAlternate(Track track, Lyrics plain, CancellationToken token)
    {
        string title = ShortTitle(track.Title);
        string normalizedTitle = Normalized(title);
        string album = Whitespace.Replace(track.Album, "");
        var candidates = await Get<Lrc[]>("/search?track_name=" + Uri.EscapeDataString(title), token).ConfigureAwait(false);
        return (candidates ?? new Lrc[0])
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.syncedLyrics)
                && Math.Abs(candidate.duration * 1000 - track.Duration) <= 2000
                && Same(Whitespace.Replace(candidate.albumName ?? "", ""), album)
                && Normalized(ShortTitle(candidate.trackName)) == normalizedTitle)
            .OrderBy(candidate => !Same(candidate.artistName, track.LeadArtist))
            .ThenBy(candidate => Math.Abs(candidate.duration * 1000 - track.Duration))
            .Select(ConvertRecord)
            .FirstOrDefault(candidate => candidate != null && candidate.synced && LyricsAgree(plain, candidate));
    }

    static string ShortTitle(string title)
    {
        var match = BilingualTitle.Match(title ?? "");
        return match.Success ? match.Groups["title"].Value.Trim() : (title ?? "").Trim();
    }

    static string Normalized(string text)
    {
        string simplified = ChineseText.Simplify(text);
        return new string(simplified.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    static bool LyricsAgree(Lyrics plain, Lyrics timed)
    {
        var original = Trigrams(plain);
        var candidate = Trigrams(timed);
        int total = Math.Max(original.Values.Sum(), candidate.Values.Sum());
        if (total < 30) return false;
        int matching = 0;
        foreach (var entry in original)
        {
            int count;
            if (candidate.TryGetValue(entry.Key, out count)) matching += Math.Min(count, entry.Value);
        }
        return matching >= total * 0.75;
    }

    static Dictionary<string, int> Trigrams(Lyrics lyrics)
    {
        var text = Normalized(string.Concat(lyrics.lines.Select(line => line.text)));
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i + 2 < text.Length; i++)
        {
            string gram = text.Substring(i, 3);
            int count;
            counts.TryGetValue(gram, out count);
            counts[gram] = count + 1;
        }
        return counts;
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

    public static Lyrics ReadLocal(Track track)
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
        Lyrics plain = null;
        if (ids.Count == 1)
        {
            foreach (string prefix in new[] { "spotify-v2-", "spotify-" })
            {
                var found = ReadCached(Path.Combine(cache, prefix + ids[0] + ".json"), "Spotifast 缓存 · Spotify");
                if (found != null && (found.synced || found.instrumental)) return found;
                plain = plain ?? found;
            }
        }

        var artist = matching.Select(t => t.artists.FirstOrDefault()).Where(a => a != null)
            .Select(a => a.name).FirstOrDefault() ?? track.LeadArtist;
        var key = string.Join("|", artist, track.Title, track.Album, track.Duration.ToString(CultureInfo.InvariantCulture));
        string hash;
        using (var sha = SHA1.Create())
            hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant();
        var cached = ReadCached(Path.Combine(cache, hash + ".json"), "Spotifast 缓存 · LRCLIB");
        return cached != null && (cached.synced || cached.instrumental) ? cached : plain ?? cached;
    }

    static Lyrics ReadCached(string path, string source)
    {
        var record = ReadJson<Cache>(path);
        var lyrics = record == null ? null : record.found;
        if (lyrics == null || lyrics.lines == null) return null;
        if (!lyrics.instrumental && !lyrics.lines.Any(l => !string.IsNullOrWhiteSpace(l.text))) return null;
        lyrics.synced = lyrics.synced && lyrics.lines.All(l => l.at_ms.HasValue);
        if (lyrics.synced) lyrics.lines = lyrics.lines.OrderBy(l => l.at_ms).ToList();
        else lyrics.lines = lyrics.lines.Where(l => !string.IsNullOrWhiteSpace(l.text)).ToList();
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
        var synced = ParseLrc(record.syncedLyrics, "LRCLIB", record.instrumental);
        if (synced != null || string.IsNullOrWhiteSpace(record.plainLyrics)) return synced;
        return new Lyrics
        {
            Source = "LRCLIB",
            lines = record.plainLyrics.Split('\n').Where(row => !string.IsNullOrWhiteSpace(row))
                .Select(row => new Line { text = row.Trim() }).ToList()
        };
    }

    public static Lyrics ParseLrc(string lrc, string source, bool instrumental = false)
    {
        var lines = new List<Line>();
        if (!string.IsNullOrWhiteSpace(lrc))
        {
            int offset;
            int.TryParse(Offset.Match(lrc).Groups[1].Value, out offset);
            foreach (var row in lrc.Split('\n'))
            {
                var stamps = new List<Match>();
                int headerEnd = 0;
                for (var stamp = Stamp.Match(row); stamp.Success && stamp.Index == headerEnd; stamp = stamp.NextMatch())
                {
                    stamps.Add(stamp);
                    headerEnd += stamp.Length;
                }
                if (stamps.Count == 0) continue;
                var last = stamps[stamps.Count - 1];
                var content = row.Substring(last.Index + last.Length).Trim();
                var inline = InlineStamp.Matches(content);
                var segments = new List<Segment>();
                var plain = new StringBuilder();
                int from = 0;
                foreach (Match tag in inline)
                {
                    plain.Append(content, from, tag.Index - from);
                    segments.Add(new Segment { At = Timestamp(tag, offset), Start = plain.Length });
                    from = tag.Index + tag.Length;
                }
                plain.Append(content, from, content.Length - from);
                foreach (Match stamp in stamps)
                {
                    lines.Add(new Line
                    {
                        at_ms = Timestamp(stamp, offset), text = plain.ToString(),
                        Segments = stamps.Count == 1 && segments.Count > 0
                            && segments.SequenceEqual(segments.OrderBy(s => s.At)) ? segments : null
                    });
                }
            }
        }
        bool timed = lines.Any(l => !string.IsNullOrWhiteSpace(l.text));
        if (!timed && !instrumental) return null;
        return new Lyrics
        {
            Source = source, synced = timed, instrumental = instrumental,
            lines = lines.OrderBy(l => l.at_ms).ToList()
        };
    }

    static long Timestamp(Match stamp, int offset)
    {
        long minutes = long.Parse(stamp.Groups[1].Value);
        long seconds = long.Parse(stamp.Groups[2].Value);
        long fraction = long.Parse(stamp.Groups[3].Value.PadRight(3, '0'));
        return Math.Max(0, minutes * 60000 + seconds * 1000 + fraction - offset);
    }
}

sealed class Preferences
{
    public static string LoadError;
    static readonly string DirectoryPath = Path.GetDirectoryName(typeof(Preferences).Assembly.Location);
    static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    static readonly string LegacyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotifastLyricsHud", "settings.json");

    static Preferences() { }

    public int Background { get; set; }
    public int FontSize { get; set; }
    public int OffsetMs { get; set; }
    public bool Karaoke { get; set; }
    public bool Preview { get; set; }
    public string BackgroundColor { get; set; }
    public string TextColor { get; set; }
    public string HighlightColor { get; set; }
    public string BorderColor { get; set; }
    public string FontName { get; set; }
    public int BorderWidth { get; set; }
    public int CornerRadius { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int TitleFontSize { get; set; }
    public bool SimplifiedLyrics { get; set; }

    public Preferences()
    {
        Background = 93;
        FontSize = 27;
        Karaoke = true;
        Preview = true;
        BackgroundColor = "#151B22";
        TextColor = "#F6FAFC";
        HighlightColor = "#9AE5BD";
        BorderColor = "#34404A";
        FontName = "Microsoft YaHei UI";
        BorderWidth = 1;
        CornerRadius = 16;
        WindowWidth = 700;
        TitleFontSize = 16;
        SimplifiedLyrics = true;
    }

    public static Preferences Load()
    {
        LoadError = null;
        Preferences loaded = null;
        try
        {
            if (File.Exists(FilePath))
                return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new Preferences();
            if (File.Exists(LegacyPath))
            {
                loaded = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(LegacyPath));
                if (loaded != null)
                {
                    loaded.Save();
                    return loaded;
                }
            }
        }
        catch (IOException error) { LoadError = error.ToString(); }
        catch (UnauthorizedAccessException error) { LoadError = error.ToString(); }
        catch (ArgumentException error) { LoadError = error.ToString(); }
        catch (InvalidOperationException error) { LoadError = error.ToString(); }
        return loaded ?? new Preferences();
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else
            {
                try { File.Move(temp, FilePath); }
                catch (IOException)
                {
                    if (!File.Exists(FilePath)) throw;
                    File.Replace(temp, FilePath, null);
                }
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

sealed class Hud : Window
{
    readonly TextBlock title, source, current, next;
    readonly Border card;
    readonly Border settingsFrame;
    readonly StackPanel settings = new StackPanel();
    readonly StackPanel playbackSettings = new StackPanel();
    readonly StackPanel appearanceSettings = new StackPanel { Visibility = Visibility.Collapsed };
    readonly Button playbackTab, appearanceTab;
    readonly Thumb resizeGrip;
    readonly StackPanel plainNavigation;
    readonly TextBlock settingsNotice;
    readonly Preferences preferences;
    readonly SolidColorBrush highlight = new SolidColorBrush();
    readonly DispatcherTimer save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
    readonly DispatcherTimer poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer render = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly PlaybackTimeline timeline = new PlaybackTimeline();
    Track track;
    Lyrics lyrics;
    Lyrics imported;
    string importedKey;
    string displayedLine;
    bool displayedHighlight;
    LinearGradientBrush[] glyphPaint;
    System.Windows.Media.Color lyricColor;
    string segmentedText;
    int[] textElements;
    string status = "";
    CancellationTokenSource lookup;
    bool reading, closed;
    int misses;
    int plainIndex;
    DateTime retryAt = DateTime.MaxValue;
    DateTime cacheCheckAt = DateTime.MaxValue;

    static Brush Color(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }

    public Hud(Preferences startupPreferences = null)
    {
        preferences = startupPreferences ?? Preferences.Load();
        Title = "Spotifast 歌词浮窗";
        Width = ClampWidth(preferences.WindowWidth);
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

        card = new Border
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
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });

        var header = new DockPanel { LastChildFill = true };
        layout.Children.Add(header);
        var close = ButtonFor("×", "关闭浮窗", 30, 21);
        close.Click += (sender, args) => Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var gear = ButtonFor("⚙", "歌词设置", 30, 18);
        gear.Margin = new Thickness(0, 0, 6, 0);
        gear.Click += (sender, args) => ToggleSettings();
        DockPanel.SetDock(gear, Dock.Right);
        header.Children.Add(gear);

        plainNavigation = new StackPanel
        {
            Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 5, 0)
        };
        var previousLine = ButtonFor("‹", "上一句歌词", 24, 18);
        var followingLine = ButtonFor("›", "下一句歌词", 24, 18);
        previousLine.Click += (sender, args) => MovePlain(-1);
        followingLine.Click += (sender, args) => MovePlain(1);
        plainNavigation.Children.Add(previousLine);
        plainNavigation.Children.Add(followingLine);
        DockPanel.SetDock(plainNavigation, Dock.Right);
        header.Children.Add(plainNavigation);

        source = Text(11, "#FF9EACBA");
        source.Margin = new Thickness(10, 0, 11, 0);
        DockPanel.SetDock(source, Dock.Right);
        header.Children.Add(source);
        title = Text(12, "#FF9AE5BD");
        title.FontWeight = FontWeights.SemiBold;
        title.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8
        };
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
        current.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        current.ClipToBounds = true;
        current.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 5, ShadowDepth = 1, Opacity = 0.9
        };
        current.Background = Brushes.Transparent;
        current.MouseWheel += (sender, args) => { if (MovePlain(args.Delta > 0 ? -1 : 1)) args.Handled = true; };
        Grid.SetRow(current, 1);
        layout.Children.Add(current);

        next = Text(15, "#FFA6B2BF");
        next.TextAlignment = TextAlignment.Center;
        Grid.SetRow(next, 2);
        layout.Children.Add(next);
        resizeGrip = CreateResizeGrip();
        Grid.SetRowSpan(resizeGrip, 3);
        layout.Children.Add(resizeGrip);

        settings.Margin = new Thickness(10, 5, 10, 5);
        settingsFrame = new Border
        {
            Child = settings, Background = Color("#F026303A"), CornerRadius = new CornerRadius(8),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(settingsFrame, 3);
        layout.Children.Add(settingsFrame);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
        settings.Children.Add(tabs);
        playbackTab = ButtonFor("歌词", "歌词与同步设置", 72, 12);
        appearanceTab = ButtonFor("外观", "字体、颜色与边框设置", 72, 12);
        appearanceTab.Margin = new Thickness(6, 0, 0, 0);
        playbackTab.Click += (sender, args) => SelectTab(false);
        appearanceTab.Click += (sender, args) => SelectTab(true);
        playbackTab.Background = Color("#FF38634F");
        tabs.Children.Add(playbackTab);
        tabs.Children.Add(appearanceTab);
        settings.Children.Add(playbackSettings);
        settings.Children.Add(appearanceSettings);

        preferences.FontSize = AddSlider(playbackSettings, "歌词字号", 16, 52, 1, preferences.FontSize, "",
            value => preferences.FontSize = value);
        preferences.TitleFontSize = AddSlider(playbackSettings, "歌名字号", 10, 32, 1, preferences.TitleFontSize, "",
            value => preferences.TitleFontSize = value);
        preferences.OffsetMs = AddSlider(playbackSettings, "歌词提前量", -2000, 2000, 50, preferences.OffsetMs, " ms",
            value => preferences.OffsetMs = value);

        var options = new WrapPanel { Margin = new Thickness(0, 6, 0, 3) };
        playbackSettings.Children.Add(options);
        options.Children.Add(AddOption("卡拉 OK 高亮", preferences.Karaoke, value => preferences.Karaoke = value));
        options.Children.Add(AddOption("显示下一句", preferences.Preview, value => preferences.Preview = value));
        options.Children.Add(AddOption("显示简体中文", preferences.SimplifiedLyrics, value => preferences.SimplifiedLyrics = value));
        var import = ButtonFor("导入当前曲目 LRC", "支持带字词时间戳的增强 LRC", 118, 12);
        import.Click += (sender, args) => ImportLyrics();
        options.Children.Add(import);
        var reset = ButtonFor("清除导入", "恢复自动匹配歌词", 72, 12);
        reset.Margin = new Thickness(6, 0, 0, 0);
        reset.Click += (sender, args) =>
        {
            imported = null;
            importedKey = null;
            settingsNotice.Text = "已恢复自动匹配";
            RenderLyrics();
        };
        options.Children.Add(reset);
        settingsNotice = Text(11, "#FFD0DCE5");
        settingsNotice.Margin = new Thickness(0, 2, 0, 3);
        settingsNotice.Text = Preferences.LoadError == null
            ? "正值让歌词提前。收起设置后可拖动右下角调整窗口。"
            : "配置读写失败，请检查程序目录权限。";
        if (Preferences.LoadError != null) settingsNotice.ToolTip = Preferences.LoadError;
        playbackSettings.Children.Add(settingsNotice);
        BuildAppearance();

        save.Tick += (sender, args) => SavePreferences();
        ApplyPreferences();
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
            if (save.IsEnabled) SavePreferences();
        };
    }

    static Button ButtonFor(string label, string tip, double width, double size)
    {
        return new Button
        {
            Content = label, FontSize = size, Width = width, Height = 27, Padding = new Thickness(0),
            BorderThickness = new Thickness(0), Background = Color("#FF2B3540"),
            Foreground = Color("#FFE0E8F0"), Cursor = Cursors.Hand, ToolTip = tip
        };
    }

    static double ClampWidth(double width)
    {
        double limit = Math.Min(1600, SystemParameters.WorkArea.Width - 16);
        return Math.Max(Math.Min(330, limit), Math.Min(limit, width));
    }

    double MinimumHudHeight()
    {
        return Math.Max(29, preferences.TitleFontSize + 10)
            + current.LineHeight + (preferences.Preview ? next.FontSize + 13 : 0)
            + 22 + preferences.BorderWidth * 2;
    }

    Thumb CreateResizeGrip()
    {
        var grip = new Thumb
        {
            Width = 22, Height = 22, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -8, -5),
            ToolTip = "拖动调整浮窗宽高", Foreground = highlight
        };
        System.Windows.Automation.AutomationProperties.SetName(grip, "调整浮窗大小");
        var hitArea = new FrameworkElementFactory(typeof(Border));
        hitArea.SetValue(Border.BackgroundProperty, Color("#55303842"));
        hitArea.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var mark = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        mark.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 5,17 L 17,5 M 10,17 L 17,10 M 15,17 L 17,15"));
        mark.SetBinding(System.Windows.Shapes.Path.StrokeProperty, new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        mark.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.4);
        hitArea.AppendChild(mark);
        grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = hitArea };

        Point start = new Point();
        Size original = new Size();
        Matrix fromPixels = Matrix.Identity;
        grip.DragStarted += (sender, args) =>
        {
            fromPixels = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
            start = fromPixels.Transform(PointToScreen(Mouse.GetPosition(this)));
            original = new Size(Width, Height);
        };
        grip.DragDelta += (sender, args) =>
        {
            var delta = fromPixels.Transform(PointToScreen(Mouse.GetPosition(this))) - start;
            Width = ClampWidth(original.Width + delta.X);
            Height = Math.Max(MinimumHudHeight(), Math.Min(SystemParameters.WorkArea.Height - 16, original.Height + delta.Y));
        };
        grip.DragCompleted += (sender, args) =>
        {
            if (args.Canceled)
            {
                Width = original.Width;
                Height = original.Height;
                return;
            }
            if (Math.Abs(Width - original.Width) < 1 && Math.Abs(Height - original.Height) < 1) return;
            preferences.WindowWidth = (int)Math.Round(Width);
            preferences.WindowHeight = (int)Math.Round(Height);
            ScheduleSave();
        };
        return grip;
    }

    void BuildAppearance()
    {
        var fontRow = new DockPanel { Height = 30 };
        appearanceSettings.Children.Add(fontRow);
        var fontLabel = Text(12, "#FFF0F5FA");
        fontLabel.Text = "歌词字体";
        fontLabel.Width = 108;
        fontRow.Children.Add(fontLabel);
        var names = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct()
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (!names.Contains(preferences.FontName)) preferences.FontName = "Microsoft YaHei UI";
        if (!names.Contains(preferences.FontName)) names.Insert(0, preferences.FontName);
        var fonts = new ComboBox
        {
            ItemsSource = names, SelectedItem = preferences.FontName,
            MaxDropDownHeight = 240, Height = 24, IsTextSearchEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        System.Windows.Automation.AutomationProperties.SetName(fonts, "歌词字体");
        fonts.SelectionChanged += (sender, args) =>
        {
            if (fonts.SelectedItem == null) return;
            preferences.FontName = (string)fonts.SelectedItem;
            PreferencesChanged();
        };
        fontRow.Children.Add(fonts);

        var colors = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 4) };
        appearanceSettings.Children.Add(colors);
        preferences.BackgroundColor = AddColor(colors, "背景色", preferences.BackgroundColor, "#151B22", value => preferences.BackgroundColor = value);
        preferences.TextColor = AddColor(colors, "歌词色", preferences.TextColor, "#F6FAFC", value => preferences.TextColor = value);
        preferences.HighlightColor = AddColor(colors, "高亮色", preferences.HighlightColor, "#9AE5BD", value => preferences.HighlightColor = value);
        preferences.BorderColor = AddColor(colors, "边框色", preferences.BorderColor, "#34404A", value => preferences.BorderColor = value);

        preferences.Background = AddSlider(appearanceSettings, "背景不透明度", 0, 100, 1, preferences.Background, "%",
            value => preferences.Background = value);
        preferences.BorderWidth = AddSlider(appearanceSettings, "边框粗细", 0, 8, 1, preferences.BorderWidth, " px",
            value => preferences.BorderWidth = value);
        preferences.CornerRadius = AddSlider(appearanceSettings, "边框圆角", 0, 36, 1, preferences.CornerRadius, " px",
            value => preferences.CornerRadius = value);
    }

    string AddColor(Panel parent, string label, string initial, string fallback, Action<string> set)
    {
        var value = initial != null && Regex.IsMatch(initial, @"^#[0-9a-fA-F]{6}$") ? initial : fallback;
        var button = ButtonFor(label, "点击选择" + label, double.NaN, 12);
        button.Margin = new Thickness(0, 1, 5, 2);
        var contents = new StackPanel { Orientation = Orientation.Horizontal };
        var swatch = new Border
        {
            Background = Color(value), Width = 15, Height = 15,
            BorderThickness = new Thickness(1), BorderBrush = Color("#FFA6B2BF"),
            CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 8, 0)
        };
        var caption = new TextBlock { Text = label + "  " + value };
        contents.Children.Add(swatch);
        contents.Children.Add(caption);
        button.Content = contents;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (sender, args) =>
        {
            using (var picker = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true, AnyColor = true, SolidColorOnly = true,
                Color = System.Drawing.ColorTranslator.FromHtml(value)
            })
            {
                var owner = new DialogOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                if (picker.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return;
                var chosen = picker.Color;
                value = string.Format("#{0:X2}{1:X2}{2:X2}", chosen.R, chosen.G, chosen.B);
            }
            swatch.Background = Color(value);
            caption.Text = label + "  " + value;
            set(value);
            PreferencesChanged();
        };
        parent.Children.Add(button);
        return value;
    }

    sealed class DialogOwner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; private set; }
        public DialogOwner(IntPtr handle) { Handle = handle; }
    }

    int AddSlider(Panel parent, string label, int min, int max, int step, int initial, string unit, Action<int> set)
    {
        int value = Math.Max(min, Math.Min(max, initial));
        var row = new DockPanel { Height = 29 };
        parent.Children.Add(row);
        var name = Text(12, "#FFF0F5FA");
        name.Width = 108;
        name.Text = label;
        row.Children.Add(name);
        var readout = Text(12, "#FF9AE5BD");
        readout.Width = 70;
        readout.TextAlignment = TextAlignment.Right;
        readout.Text = value + unit;
        DockPanel.SetDock(readout, Dock.Right);
        row.Children.Add(readout);
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value, TickFrequency = step, SmallChange = step,
            LargeChange = step * 5, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (sender, args) =>
        {
            int updated = (int)args.NewValue;
            readout.Text = updated + unit;
            set(updated);
            PreferencesChanged();
        };
        row.Children.Add(slider);
        return value;
    }

    CheckBox AddOption(string label, bool value, Action<bool> set)
    {
        var check = new CheckBox
        {
            Content = label, IsChecked = value, Foreground = Color("#FFF0F5FA"),
            Margin = new Thickness(0, 0, 15, 0), VerticalAlignment = VerticalAlignment.Center
        };
        RoutedEventHandler changed = (sender, args) =>
        {
            set(check.IsChecked == true);
            PreferencesChanged();
        };
        check.Checked += changed;
        check.Unchecked += changed;
        return check;
    }

    void PreferencesChanged()
    {
        ApplyPreferences();
        RenderLyrics();
        ScheduleSave();
    }

    void ScheduleSave()
    {
        save.Stop();
        save.Start();
    }

    void SavePreferences()
    {
        save.Stop();
        try { preferences.Save(); }
        catch (IOException) { settingsNotice.Text = "设置暂时无法保存"; }
        catch (UnauthorizedAccessException) { settingsNotice.Text = "设置暂时无法保存"; }
    }

    void ApplyPreferences()
    {
        var background = ((SolidColorBrush)Color(preferences.BackgroundColor)).Color;
        card.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            (byte)Math.Round(preferences.Background * 255 / 100.0), background.R, background.G, background.B));
        card.BorderBrush = Color(preferences.BorderColor);
        card.BorderThickness = new Thickness(preferences.BorderWidth);
        card.CornerRadius = new CornerRadius(preferences.CornerRadius);
        var textBrush = (SolidColorBrush)Color(preferences.TextColor);
        var accent = ((SolidColorBrush)Color(preferences.HighlightColor)).Color;
        current.Foreground = textBrush;
        next.Foreground = new SolidColorBrush(textBrush.Color) { Opacity = 0.72 };
        highlight.Color = accent;
        title.Foreground = highlight;
        lyricColor = textBrush.Color;
        if (glyphPaint != null)
        {
            foreach (var brush in glyphPaint)
            {
                brush.GradientStops[0].Color = accent;
                brush.GradientStops[1].Color = lyricColor;
            }
        }
        var font = new FontFamily(preferences.FontName + ", Microsoft YaHei UI, Segoe UI");
        current.FontFamily = font;
        next.FontFamily = font;
        title.FontFamily = font;
        title.FontSize = preferences.TitleFontSize;
        current.FontSize = preferences.FontSize;
        current.LineHeight = Math.Ceiling(preferences.FontSize * 1.18);
        current.MaxHeight = current.LineHeight * 2;
        next.FontSize = Math.Max(11, Math.Round(preferences.FontSize * 0.56));
        next.Visibility = preferences.Preview ? Visibility.Visible : Visibility.Collapsed;
        var layout = (Grid)card.Child;
        bool settingsOpen = settingsFrame.Visibility == Visibility.Visible;
        resizeGrip.Visibility = settingsOpen ? Visibility.Collapsed : Visibility.Visible;
        double headerHeight = settingsOpen ? 42 : Math.Max(29, preferences.TitleFontSize + 10);
        double previewHeight = preferences.Preview ? (settingsOpen ? 42 : next.FontSize + 13) : 0;
        layout.RowDefinitions[0].Height = new GridLength(headerHeight);
        layout.RowDefinitions[2].Height = new GridLength(previewHeight);
        double lyricHeight = settingsOpen ? Math.Ceiling(52 * 1.18) * 2 : current.MaxHeight;
        double newHeight = headerHeight + lyricHeight + previewHeight + 37 + preferences.BorderWidth * 2;
        if (!settingsOpen && preferences.WindowHeight > 0)
            newHeight = Math.Max(MinimumHudHeight(), Math.Min(SystemParameters.WorkArea.Height - 16, preferences.WindowHeight));
        double settingsHeight = 0;
        if (settingsOpen)
        {
            var page = appearanceSettings.Visibility == Visibility.Visible ? appearanceSettings : playbackSettings;
            page.Measure(new Size(Math.Max(0, Width - 52 - preferences.BorderWidth * 2), double.PositiveInfinity));
            settingsHeight = page.DesiredSize.Height + 42;
            settings.InvalidateMeasure();
            settingsFrame.InvalidateMeasure();
        }
        layout.RowDefinitions[3].Height = new GridLength(settingsHeight);
        if (settingsHeight > 0) newHeight += settingsHeight + 7;
        double bottom = Top + Height;
        Height = newHeight;
        var area = SystemParameters.WorkArea;
        if (area.Contains(new Point(Left, Top)))
            Top = Math.Max(area.Top, Math.Min(bottom - newHeight, area.Bottom - newHeight));
    }

    void ToggleSettings()
    {
        settingsFrame.Visibility = settingsFrame.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ApplyPreferences();
    }

    void SelectTab(bool appearance)
    {
        playbackSettings.Visibility = appearance ? Visibility.Collapsed : Visibility.Visible;
        appearanceSettings.Visibility = appearance ? Visibility.Visible : Visibility.Collapsed;
        playbackTab.Background = Color(appearance ? "#FF2B3540" : "#FF38634F");
        appearanceTab.Background = Color(appearance ? "#FF38634F" : "#FF2B3540");
        ApplyPreferences();
    }

    void ImportLyrics()
    {
        if (track == null) { settingsNotice.Text = "请先在 Spotifast 播放一首歌"; return; }
        var key = track.Key;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "为当前曲目选择歌词", Filter = "LRC 歌词 (*.lrc)|*.lrc", CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (track == null || track.Key != key) { settingsNotice.Text = "曲目已切换，请重新选择"; return; }
        try
        {
            if (new FileInfo(dialog.FileName).Length > 1024 * 1024)
            {
                settingsNotice.Text = "歌词文件超过 1 MB";
                return;
            }
            var result = Lookup.ParseLrc(File.ReadAllText(dialog.FileName, Encoding.UTF8), "导入 LRC");
            if (result == null) { settingsNotice.Text = "文件中没有可读的时间戳"; return; }
            imported = result;
            importedKey = key;
            settingsNotice.Text = result.lines.Any(line => line.Segments != null)
                ? "已导入字词时间戳" : "已导入逐行时间戳";
            RenderLyrics();
        }
        catch (IOException) { settingsNotice.Text = "无法读取歌词文件"; }
        catch (UnauthorizedAccessException) { settingsNotice.Text = "无法读取歌词文件"; }
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
        timeline.Update(latest, clock.Elapsed.TotalMilliseconds, changed);
        if (changed)
        {
            if (lookup != null) lookup.Cancel();
            lyrics = null;
            plainIndex = 0;
            status = "";
            retryAt = DateTime.MaxValue;
            cacheCheckAt = DateTime.MaxValue;
        }
        if (latest != null && (changed || DateTime.UtcNow >= retryAt))
            await RefreshLyrics(latest);
        else if (latest != null && lookup == null && DateTime.UtcNow >= cacheCheckAt)
            await RefreshCachedTiming(latest);
        RenderLyrics();
    }

    async Task RefreshCachedTiming(Track latest)
    {
        // Spotifast can write timed lyrics after its panel is opened mid-song.
        var plain = lyrics;
        cacheCheckAt = DateTime.UtcNow.AddSeconds(3);
        var cached = await Task.Run(() => Lookup.ReadLocal(latest));
        if (closed || track == null || track.Key != latest.Key || lyrics != plain) return;
        if (cached == null || !cached.synced) return;
        lyrics = cached;
        cacheCheckAt = DateTime.MaxValue;
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
            var preview = new Progress<Lyrics>(available =>
            {
                if (closed || lookup != request) return;
                lyrics = available;
                RenderLyrics();
            });
            var result = await Task.Run(() => Lookup.Find(latest, request.Token, preview));
            if (closed || request.IsCancellationRequested) return;
            lyrics = result;
            status = "没有找到歌词";
            if (result == null) retryAt = DateTime.UtcNow.AddSeconds(20);
            else if (!result.synced && !result.instrumental) cacheCheckAt = DateTime.UtcNow.AddSeconds(3);
        }
        catch (Exception)
        {
            if (closed || request.IsCancellationRequested) return;
            status = "暂时无法获取歌词";
            retryAt = DateTime.UtcNow.AddSeconds(20);
            if (lyrics != null && !lyrics.synced && !lyrics.instrumental) cacheCheckAt = DateTime.UtcNow.AddSeconds(3);
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
        var selected = CurrentLyrics();
        string trackText = track == null ? "SPOTIFAST · 桌面歌词" : track.DisplayLabel(preferences.SimplifiedLyrics);
        string sourceText = track == null ? "" : (!track.Playing ? "已暂停" : (selected == null ? "" : selected.Source));
        string currentText = track == null ? "等待 Spotifast 播放" : status;
        string nextText = "";
        int prefix = -1;
        int fillEnd = -1;
        double fraction = 0;
        bool plain = selected != null && !selected.instrumental && !selected.synced && selected.lines.Count > 0;
        plainNavigation.Visibility = plain ? Visibility.Visible : Visibility.Collapsed;
        if (track != null && selected != null)
        {
            if (selected.instrumental) currentText = "纯音乐";
            else if (selected.synced && selected.lines.Count > 0)
            {
                long position = (long)timeline.Position(clock.Elapsed.TotalMilliseconds);
                if (track.Duration > 0) position = Math.Min(track.Duration, position);
                position += preferences.OffsetMs;
                var lines = selected.lines;
                int active = ActiveLine(lines, position);
                bool simplify = preferences.SimplifiedLyrics;
                currentText = active < 0 || string.IsNullOrWhiteSpace(lines[active].text)
                    ? "♪" : lines[active].DisplayText(simplify);
                if (preferences.Karaoke && active >= 0 && currentText != "♪")
                {
                    var line = lines[active];
                    if (line.Segments != null)
                    {
                        prefix = line.DisplayOffset(TimedPrefix(line, position), simplify);
                        if (track.Playing) sourceText = "字词同步";
                    }
                    else
                    {
                        long start = line.at_ms.Value;
                        long end = active + 1 < lines.Count ? lines[active + 1].at_ms.Value : track.Duration;
                        PrepareTextElements(currentText);
                        long span = Math.Max(1, Math.Min(end - start, Math.Max(1400, textElements.Length * 420L)));
                        double progress = Math.Min(textElements.Length, (position - start) * (double)textElements.Length / span);
                        int count = (int)progress;
                        prefix = count == textElements.Length ? currentText.Length : textElements[count];
                        if (count < textElements.Length)
                        {
                            fillEnd = count + 1 == textElements.Length ? currentText.Length : textElements[count + 1];
                            fraction = progress - count;
                        }
                        if (track.Playing) sourceText = "逐字估算";
                    }
                }
                for (int i = active + 1; i < lines.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i].text)) continue;
                    nextText = lines[i].DisplayText(simplify);
                    break;
                }
            }
            else if (plain)
            {
                plainIndex = Math.Min(plainIndex, selected.lines.Count - 1);
                currentText = selected.lines[plainIndex].DisplayText(preferences.SimplifiedLyrics);
                nextText = plainIndex + 1 < selected.lines.Count
                    ? selected.lines[plainIndex + 1].DisplayText(preferences.SimplifiedLyrics) : "";
                sourceText = "纯文本 " + (plainIndex + 1) + "/" + selected.lines.Count;
            }
        }

        if (title.Text != trackText) title.Text = trackText;
        if (source.Text != sourceText) source.Text = sourceText;
        DrawCurrentLine(currentText, prefix, fillEnd, fraction);
        if (next.Text != nextText) next.Text = nextText;
    }

    void PrepareTextElements(string text)
    {
        if (segmentedText == text) return;
        segmentedText = text;
        textElements = StringInfo.ParseCombiningCharacters(text);
    }

    void DrawCurrentLine(string text, int prefix, int fillEnd, double fraction)
    {
        bool highlighting = prefix >= 0;
        if (displayedLine != text || displayedHighlight != highlighting)
        {
            displayedLine = text;
            displayedHighlight = highlighting;
            if (!highlighting)
            {
                current.Text = text;
                glyphPaint = null;
                return;
            }

            PrepareTextElements(text);
            current.Inlines.Clear();
            glyphPaint = new LinearGradientBrush[textElements.Length];
            for (int i = 0; i < glyphPaint.Length; i++)
            {
                int start = textElements[i];
                int end = i + 1 < textElements.Length ? textElements[i + 1] : text.Length;
                var stops = new GradientStopCollection
                {
                    new GradientStop(highlight.Color, -0.16), new GradientStop(lyricColor, 0)
                };
                var paint = new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5));
                glyphPaint[i] = paint;
                current.Inlines.Add(new Run(text.Substring(start, end - start)) { Foreground = paint });
            }
        }
        if (!highlighting) return;

        for (int i = 0; i < glyphPaint.Length; i++)
        {
            int start = textElements[i];
            double edge = (start < prefix ? 1 : start < fillEnd ? fraction : 0) * 1.16;
            var stops = glyphPaint[i].GradientStops;
            if (stops[1].Offset == edge) continue;
            stops[0].Offset = edge - 0.16;
            stops[1].Offset = edge;
        }
    }

    Lyrics CurrentLyrics()
    {
        if (track == null) return null;
        return track.Key == importedKey ? imported : lyrics;
    }

    bool MovePlain(int step)
    {
        var selected = CurrentLyrics();
        if (selected == null || selected.synced || selected.instrumental || selected.lines.Count == 0) return false;
        plainIndex = Math.Max(0, Math.Min(selected.lines.Count - 1, plainIndex + step));
        RenderLyrics();
        return true;
    }

    public static int TimedPrefix(Line line, long position)
    {
        int prefix = line.Segments[0].Start;
        for (int i = 0; i < line.Segments.Count && line.Segments[i].At <= position; i++)
            prefix = i + 1 < line.Segments.Count ? line.Segments[i + 1].Start : line.text.Length;
        return prefix;
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
                return lyrics != null ? 0 : 2;
            }
            catch (Exception error) { File.WriteAllText(args[1], error.ToString()); return 3; }
        }

        bool first;
        using (var instance = new Mutex(true, "Local\\SpotifastLyricsHud", out first))
        {
            if (!first) return 0;
            var preferences = Preferences.Load();
            var app = new Application();
            return app.Run(new Hud(preferences));
        }
    }
}
