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
using System.Windows.Documents;
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
    public List<Segment> Segments { get; set; }
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
        return ParseLrc(record.syncedLyrics, "LRCLIB", record.instrumental);
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
    static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotifastLyricsHud");
    static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public int Background { get; set; }
    public int FontSize { get; set; }
    public int OffsetMs { get; set; }
    public bool Karaoke { get; set; }
    public bool Preview { get; set; }

    public Preferences()
    {
        Background = 93;
        FontSize = 27;
        Karaoke = true;
        Preview = true;
    }

    public static Preferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new Preferences();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        return new Preferences();
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this));
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
        else File.Move(temp, FilePath);
    }
}

sealed class Hud : Window
{
    readonly TextBlock title, source, current, next;
    readonly Border card;
    readonly Border settingsFrame;
    readonly StackPanel settings = new StackPanel { Visibility = Visibility.Collapsed };
    readonly TextBlock settingsNotice;
    readonly Preferences preferences = Preferences.Load();
    readonly Brush highlight = Color("#FF9AE5BD");
    readonly DispatcherTimer save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
    readonly DispatcherTimer poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer render = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
    readonly Stopwatch clock = new Stopwatch();
    Track track;
    Lyrics lyrics;
    Lyrics imported;
    string importedKey;
    string displayedLine;
    int displayedPrefix = -1;
    string segmentedText;
    int[] textElements;
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
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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
        current.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        current.ClipToBounds = true;
        current.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 5, ShadowDepth = 1, Opacity = 0.9
        };
        Grid.SetRow(current, 1);
        layout.Children.Add(current);

        next = Text(15, "#FFA6B2BF");
        next.TextAlignment = TextAlignment.Center;
        Grid.SetRow(next, 2);
        layout.Children.Add(next);

        settings.Margin = new Thickness(10, 5, 10, 5);
        settingsFrame = new Border
        {
            Child = settings, Background = Color("#F026303A"), CornerRadius = new CornerRadius(8),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(settingsFrame, 3);
        layout.Children.Add(settingsFrame);
        preferences.Background = AddSlider("背景不透明度", 0, 100, 1, preferences.Background, "%",
            value => preferences.Background = value);
        preferences.FontSize = AddSlider("歌词字号", 16, 52, 1, preferences.FontSize, "",
            value => preferences.FontSize = value);
        preferences.OffsetMs = AddSlider("歌词提前量", -2000, 2000, 50, preferences.OffsetMs, " ms",
            value => preferences.OffsetMs = value);

        var options = new WrapPanel { Margin = new Thickness(0, 6, 0, 3) };
        settings.Children.Add(options);
        options.Children.Add(AddOption("卡拉 OK 高亮", preferences.Karaoke, value => preferences.Karaoke = value));
        options.Children.Add(AddOption("显示下一句", preferences.Preview, value => preferences.Preview = value));
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
        settingsNotice.Text = "正值让歌词提前。带字词时间戳的 LRC 可精确高亮。";
        settings.Children.Add(settingsNotice);

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

    int AddSlider(string label, int min, int max, int step, int initial, string unit, Action<int> set)
    {
        int value = Math.Max(min, Math.Min(max, initial));
        var row = new DockPanel { Height = 29 };
        settings.Children.Add(row);
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
            LargeChange = step * 5, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center
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
        card.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            (byte)Math.Round(preferences.Background * 255 / 100.0), 21, 27, 34));
        current.FontSize = preferences.FontSize;
        current.LineHeight = Math.Ceiling(preferences.FontSize * 1.18);
        current.MaxHeight = current.LineHeight * 2;
        next.FontSize = Math.Max(11, Math.Round(preferences.FontSize * 0.56));
        next.Visibility = preferences.Preview ? Visibility.Visible : Visibility.Collapsed;
        var layout = (Grid)card.Child;
        layout.RowDefinitions[2].Height = new GridLength(preferences.Preview ? next.FontSize + 13 : 0);
        double newHeight = 29 + current.MaxHeight + layout.RowDefinitions[2].Height.Value + 39;
        if (settings.Visibility == Visibility.Visible) newHeight += 156;
        double bottom = Top + Height;
        Height = newHeight;
        var area = SystemParameters.WorkArea;
        if (area.Contains(new Point(Left, Top)))
            Top = Math.Max(area.Top, Math.Min(bottom - newHeight, area.Bottom - newHeight));
    }

    void ToggleSettings()
    {
        settings.Visibility = settings.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        settingsFrame.Visibility = settings.Visibility;
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
        var selected = track != null && track.Key == importedKey ? imported : lyrics;
        string trackText = track == null ? "SPOTIFAST · 桌面歌词" : track.Title + "  ·  " + track.Artist;
        string sourceText = track == null ? "" : (!track.Playing ? "已暂停" : (selected == null ? "" : selected.Source));
        string currentText = track == null ? "等待 Spotifast 播放" : status;
        string nextText = "";
        int prefix = -1;
        if (track != null && selected != null)
        {
            if (selected.instrumental) currentText = "纯音乐";
            else if (selected.synced && selected.lines.Count > 0)
            {
                long position = track.Position + (track.Playing ? Math.Min(850, clock.ElapsedMilliseconds) : 0);
                if (track.Duration > 0) position = Math.Min(track.Duration, position);
                position += preferences.OffsetMs;
                var lines = selected.lines;
                int active = ActiveLine(lines, position);
                currentText = active < 0 || string.IsNullOrWhiteSpace(lines[active].text) ? "♪" : lines[active].text;
                if (preferences.Karaoke && active >= 0 && currentText != "♪")
                {
                    var line = lines[active];
                    if (line.Segments != null)
                    {
                        prefix = TimedPrefix(line, position);
                        if (track.Playing) sourceText = "字词同步";
                    }
                    else
                    {
                        long start = line.at_ms.Value;
                        long end = active + 1 < lines.Count ? lines[active + 1].at_ms.Value : track.Duration;
                        if (segmentedText != currentText)
                        {
                            segmentedText = currentText;
                            textElements = StringInfo.ParseCombiningCharacters(currentText);
                        }
                        long span = Math.Max(1, Math.Min(end - start, Math.Max(1400, textElements.Length * 420L)));
                        int count = (int)Math.Min(textElements.Length, (position - start) * textElements.Length / span + 1);
                        prefix = count == textElements.Length ? currentText.Length : textElements[count];
                        if (track.Playing) sourceText = "逐字估算";
                    }
                }
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
        if (displayedLine != currentText || displayedPrefix != prefix)
        {
            displayedLine = currentText;
            displayedPrefix = prefix;
            if (prefix < 0) current.Text = currentText;
            else
            {
                current.Inlines.Clear();
                current.Inlines.Add(new Run(currentText.Substring(0, prefix)) { Foreground = highlight });
                current.Inlines.Add(new Run(currentText.Substring(prefix)));
            }
        }
        if (next.Text != nextText) next.Text = nextText;
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
