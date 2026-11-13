using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
static class HudChecks
{
    static void Require(bool condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (!condition) throw new Exception("HUD check failed at line " + line);
    }

    [STAThread]
    static int Main()
    {
        try
        {
            RunChecks();
            return 0;
        }
        catch (Exception error)
        {
            for (var current = error; current != null; current = current.InnerException)
                Console.Error.WriteLine(current.GetType().FullName + " — " + current.Message);
            return 1;
        }
    }

    static void RunChecks()
    {
        CheckPlaylistBeforeHistory();
        Require(Hud.LicenseText("HUD.LICENSE").Contains("Copyright (c) 2026 shum-elli"));
        Require(Hud.LicenseText("OpenCC.LICENSE").Contains("TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION"));
        var licenseWindow = Hud.CreateLicenseWindow();
        var licenseTabs = (TabControl)licenseWindow.Content;
        Require(licenseTabs.Items.Count == 2);
        foreach (TabItem tab in licenseTabs.Items)
            Require(((TextBox)tab.Content).IsReadOnly && ((TextBox)tab.Content).Text.Length > 1000);
        licenseWindow.Close();
        var lrc = "[offset:100]\n[00:01.00]<00:01.00>你<00:01.50>好<00:02.20>\n[00:03.00]下一句";
        var timed = Lookup.ParseLrc(lrc, "test");
        var line = timed.lines[0];
        Require(line.text == "你好" && line.at_ms == 900 && line.Segments.Count == 3);
        Require(Hud.TimedPrefix(line, 899) == 0 && Hud.TimedPrefix(line, 900) == 1);
        Require(Hud.TimedPrefix(line, 1399) == 1 && Hud.TimedPrefix(line, 1400) == 2);
        Require(Hud.ActiveLine(timed.lines, 2900) == 1 && timed.lines[1].Segments == null);
        var plain = Lookup.ParseLrc("[00:01.0][00:03.0]重复\n[00:04.0]字 [00:00.0]", "test");
        Require(plain.lines.Count == 3 && plain.lines[2].text == "字 [00:00.0]");
        Require(plain.lines.All(l => l.Segments == null));
        var traditional = new Line { text = "聽見風聲與回憶 · English" };
        Require(traditional.DisplayText(true) == "听见风声与回忆 · English");
        Require(traditional.DisplayText(false) == traditional.text);
        Require(ChineseText.Simplify("我终於看到所有夢想都開花") == "我终于看到所有梦想都开花");
        Require(ChineseText.Simplify("乾隆說天氣乾燥") == "乾隆说天气干燥");
        var translatedTitle = new Track { Title = "隱形的翅膀", Artist = "Angela Chang" };
        Require(translatedTitle.DisplayLabel(true) == "隐形的翅膀  ·  Angela Chang");
        Require(translatedTitle.DisplayLabel(false) == "隱形的翅膀  ·  Angela Chang");
        var expansion = new Line { text = "㑮你", Segments = new List<Segment>() };
        Require(expansion.DisplayText(true) == "𫝈你" && expansion.DisplayOffset(1, true) == 2);
        Console.WriteLine("Timestamp, offset, line fallback, and text preservation checks passed.");
        var playback = new PlaybackTimeline();
        var sample = new Track { Playing = true, Position = 1000 };
        playback.Update(sample, 0, true);
        Require(playback.Position(600) == 1600);
        sample.Position = 1500;
        playback.Update(sample, 600, false);
        Require(playback.Position(600) == 1600 && playback.Position(700) > 1600 && playback.Position(700) < 1700);
        sample.Position = 2140;
        double beforeResync = playback.Position(1200);
        playback.Update(sample, 1200, false);
        Require(playback.Position(1200) == beforeResync);
        sample.Position = 700;
        playback.Update(sample, 1800, false);
        Require(playback.Position(1800) == 700);
        sample.Playing = false;
        sample.Position = 800;
        playback.Update(sample, 2000, false);
        Require(playback.Position(2600) == 800);
        Console.WriteLine("Small timing corrections never rewind playback. Seeks and pause remain responsive.");
        var hud = new Hud(new Preferences());
        const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
        var setLocked = typeof(Hud).GetMethod("SetLocked", access);
        var grip = (Thumb)typeof(Hud).GetField("resizeGrip", access).GetValue(hud);
        var lockControl = (Button)typeof(Hud).GetField("lockButton", access).GetValue(hud);
        var closeControl = (Button)typeof(Hud).GetField("closeButton", access).GetValue(hud);
        var settingsControl = (Button)typeof(Hud).GetField("settingsButton", access).GetValue(hud);
        var transport = (StackPanel)typeof(Hud).GetField("transport", access).GetValue(hud);
        foreach (var name in new[] { "lockButton", "closeButton", "settingsButton", "previousButton", "playButton", "nextButton" })
        {
            var button = (Button)typeof(Hud).GetField(name, access).GetValue(hud);
            Require(((SolidColorBrush)button.Background).Color.A == 0 && button.Template != null);
        }
        setLocked.Invoke(hud, new object[] { true });
        Require(!hud.ShowInTaskbar && !grip.IsEnabled && grip.Visibility == Visibility.Collapsed);
        Require(lockControl.Visibility == Visibility.Visible && closeControl.Visibility == Visibility.Collapsed
            && settingsControl.Visibility == Visibility.Collapsed && transport.Visibility == Visibility.Collapsed);
        var sizeBefore = new Size(hud.Width, hud.Height);
        grip.RaiseEvent(new DragDeltaEventArgs(80, 80) { RoutedEvent = Thumb.DragDeltaEvent });
        Require(hud.Width == sizeBefore.Width && hud.Height == sizeBefore.Height);
        var prefs = (Preferences)typeof(Hud).GetField("preferences", access).GetValue(hud);
        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
        Require(serializer.Deserialize<Preferences>(serializer.Serialize(prefs)).Locked);
        var dragTitle = (TextBlock)typeof(Hud).GetField("title", access).GetValue(hud);
        var header = (Border)typeof(Hud).GetField("headerDragArea", access).GetValue(hud);
        var headerStatus = (TextBlock)typeof(Hud).GetField("source", access).GetValue(hud);
        Require(Hud.CanDragHeader(header) && Hud.CanDragHeader(dragTitle) && Hud.CanDragHeader(headerStatus));
        Require(!Hud.CanDragHeader(lockControl) && !Hud.CanDragHeader(settingsControl));
        Require(dragTitle.Cursor == Cursors.Arrow);
        setLocked.Invoke(hud, new object[] { false });
        Require(grip.IsEnabled && grip.Visibility == Visibility.Visible && dragTitle.Cursor == Cursors.SizeAll);
        Require(closeControl.Visibility == Visibility.Visible && settingsControl.Visibility == Visibility.Visible
            && transport.Visibility == Visibility.Visible);
        ((DispatcherTimer)typeof(Hud).GetField("save", access).GetValue(hud)).Stop();
        using (var iconData = typeof(Hud).Assembly.GetManifestResourceStream("HUD.Icon"))
        using (var icon = new System.Drawing.Icon(iconData))
            Require(icon.Width == 32 && icon.Height == 32);
        Console.WriteLine("Lock blocks resize events and survives settings serialization. Tray icon loads.");
        var track = new Track { Position = 1250, Duration = 5000, Title = "Synthetic", Artist = "Test", Album = "", Cover = "" };
        typeof(Hud).GetField("track", access).SetValue(hud, track);
        var lyricField = typeof(Hud).GetField("lyrics", access);
        var delayed = new Lyrics { synced = true, Source = "delayed local cache",
            lines = new List<Line> { new Line { at_ms = 0, text = "迟到的本地歌词" } } };
        lyricField.SetValue(hud, null);
        Require(Hud.NeedsLocalRefresh(null));
        // Discovery must work while the original network request is still pending.
        using (var pending = new System.Threading.CancellationTokenSource())
        {
            typeof(Hud).GetField("lookup", access).SetValue(hud, pending);
            Require(hud.ApplyCachedLyrics(track, null, delayed));
            Require(ReferenceEquals(lyricField.GetValue(hud), delayed) && !Hud.NeedsLocalRefresh(delayed));
            var cachedDisplay = (TextBlock)typeof(Hud).GetField("current", access).GetValue(hud);
            Require(new TextRange(cachedDisplay.ContentStart, cachedDisplay.ContentEnd).Text == "迟到的本地歌词");
            typeof(Hud).GetField("lookup", access).SetValue(hud, null);
        }
        lyricField.SetValue(hud, null);
        var latePlain = new Lyrics { lines = new List<Line> { new Line { text = "后来出现的纯文本" } } };
        Require(hud.ApplyCachedLyrics(track, null, latePlain) && Hud.NeedsLocalRefresh(latePlain));
        Require(!hud.ApplyCachedLyrics(new Track { Title = "previous song" }, latePlain, delayed));
        Require(!hud.ApplyCachedLyrics(track, null, delayed));
        Require(hud.ApplyCachedLyrics(track, latePlain, delayed));
        var authoritativePlain = new Lyrics { CacheSignature = "local-revision-1", Source = "Spotifast cache",
            lines = new List<Line> { new Line { text = "Spotifast 的原始文字" } } };
        Require(hud.ApplyCachedLyrics(track, delayed, authoritativePlain));
        var unchanged = new Lyrics { CacheSignature = "local-revision-1", lines = authoritativePlain.lines };
        Require(!hud.ApplyCachedLyrics(track, authoritativePlain, unchanged));
        var revised = new Lyrics { CacheSignature = "local-revision-2", synced = true,
            lines = new List<Line> { new Line { at_ms = 0, text = "Spotifast 更新后的文字" } } };
        Require(hud.ApplyCachedLyrics(track, authoritativePlain, revised));
        using (var canceled = new System.Threading.CancellationTokenSource())
        {
            canceled.Cancel();
            Require(ReferenceEquals(Lookup.Find(track, canceled.Token, null, t => authoritativePlain).GetAwaiter().GetResult(), authoritativePlain));
        }
        // Corrupt data reaching the renderer must not escape the timer callback.
        lyricField.SetValue(hud, new Lyrics { synced = true, lines = new List<Line> { null } });
        typeof(Hud).GetMethod("RenderSafely", access).Invoke(hud, null);
        Require(lyricField.GetValue(hud) == null);
        Require(!Diagnostics.Recoverable(new OutOfMemoryException()));
        Console.WriteLine("Spotifast plain lyrics replace timed fallback; revisions refresh without redraw churn, and malformed render state is contained.");
        Require(!Hud.NeedsLocalRefresh(new Lyrics { instrumental = true }));
        Console.WriteLine("Delayed local lyrics load after a miss or during a pending request; stale results are rejected.");
        typeof(Hud).GetField("lyrics", access).SetValue(hud, new Lyrics
        {
            synced = true, Source = "test",
            lines = new List<Line> { new Line { at_ms = 1000, text = "你好世界" }, new Line { at_ms = 3000, text = "聽見風聲" } }
        });
        var render = typeof(Hud).GetMethod("RenderLyrics", access);
        var display = (TextBlock)typeof(Hud).GetField("current", access).GetValue(hud);
        var timeline = (PlaybackTimeline)typeof(Hud).GetField("timeline", access).GetValue(hud);
        timeline.Update(track, 0, true);
        render.Invoke(hud, null);
        var play = (Button)typeof(Hud).GetField("playButton", access).GetValue(hud);
        Require(Equals(play.ToolTip, "播放") && play.IsEnabled && play.Content is System.Windows.Shapes.Path);
        var runs = display.Inlines.OfType<Run>().ToArray();
        var active = runs[0];
        var gradient = (LinearGradientBrush)active.Foreground;
        double before = gradient.GradientStops[1].Offset;
        Require(active.Text == "你" && before > 0 && before < 1);
        var next = (TextBlock)typeof(Hud).GetField("next", access).GetValue(hud);
        var title = (TextBlock)typeof(Hud).GetField("title", access).GetValue(hud);
        var accent = (SolidColorBrush)typeof(Hud).GetField("highlight", access).GetValue(hud);
        Require(next.Text == "听见风声" && title.Foreground == accent);
        track.Position = 1330;
        timeline.Update(track, 0, false);
        render.Invoke(hud, null);
        Require(display.Inlines.FirstInline == active && gradient.GradientStops[1].Offset > before);
        track.Position = 1480;
        timeline.Update(track, 0, false);
        render.Invoke(hud, null);
        Require(display.Inlines.OfType<Run>().SequenceEqual(runs));
        Require(gradient.GradientStops[1].Offset > 1
            && ((LinearGradientBrush)runs[1].Foreground).GradientStops[1].Offset > 0);
        Console.WriteLine("Highlight crosses a character boundary without rebuilding the line.");
        track.Playing = true;
        timeline.Update(track, 0, false);
        render.Invoke(hud, null);
        Require(Equals(play.ToolTip, "暂停"));
        Console.WriteLine("Playback button reflects paused and playing states.");
        typeof(Hud).GetField("lyrics", access).SetValue(hud, new Lyrics
        {
            lines = new List<Line> { new Line { text = "纯文本测试" } }
        });
        setLocked.Invoke(hud, new object[] { true });
        render.Invoke(hud, null);
        var navigation = (StackPanel)typeof(Hud).GetField("plainNavigation", access).GetValue(hud);
        Require(navigation.Visibility == Visibility.Collapsed && transport.Visibility == Visibility.Collapsed);
        typeof(Hud).GetMethod("ToggleSettings", access).Invoke(hud, null);
        var panel = (Border)typeof(Hud).GetField("settingsFrame", access).GetValue(hud);
        Require(panel.Visibility == Visibility.Collapsed);
        ((DispatcherTimer)typeof(Hud).GetField("save", access).GetValue(hud)).Stop();
        Console.WriteLine("Transparent controls keep only unlock visible while locked, including plain lyrics.");
        var handle = new System.Windows.Interop.WindowInteropHelper(hud).EnsureHandle();
        var getStyle = typeof(Hud).GetMethod("GetWindowStyle", BindingFlags.NonPublic | BindingFlags.Static);
        int inputFlags = 0x20 | 0x08000000;
        int style = (int)getStyle.Invoke(null, new object[] { handle, -20 });
        Require((style & inputFlags) == inputFlags);
        setLocked.Invoke(hud, new object[] { false });
        style = (int)getStyle.Invoke(null, new object[] { handle, -20 });
        Require((style & inputFlags) == 0);
        var message = typeof(Hud).GetMethod("WindowMessage", access);
        var hotkey = new object[] { handle, 0x0312, new IntPtr(0x4844), IntPtr.Zero, false };
        message.Invoke(hud, hotkey);
        Require((bool)hotkey[4] && prefs.Locked);
        var activate = new object[] { handle, 0x0021, IntPtr.Zero, IntPtr.Zero, false };
        Require((IntPtr)message.Invoke(hud, activate) == new IntPtr(3) && (bool)activate[4]);
        hotkey[4] = false;
        message.Invoke(hud, hotkey);
        Require((bool)hotkey[4] && !prefs.Locked);
        style = (int)getStyle.Invoke(null, new object[] { handle, -20 });
        Require((style & inputFlags) == 0);
        hud.Left = -10000;
        hud.Top = -10000;
        setLocked.Invoke(hud, new object[] { true });
        ((DispatcherTimer)typeof(Hud).GetField("save", access).GetValue(hud)).Stop();
        hud.Show();
        hud.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        var surfaceField = typeof(Hud).GetField("unlockSurface", access);
        var surface = (Window)surfaceField.GetValue(hud);
        Require(surface != null && surface.IsVisible && !surface.ShowInTaskbar);
        var unlockHandle = new System.Windows.Interop.WindowInteropHelper(surface).Handle;
        int unlockStyle = (int)getStyle.Invoke(null, new object[] { unlockHandle, -20 });
        Require((unlockStyle & 0x08000000) != 0 && (unlockStyle & 0x20) == 0);
        var expectedPoint = lockControl.PointToScreen(new Point());
        var actualPoint = surface.PointToScreen(new Point());
        Require(Math.Abs(expectedPoint.X - actualPoint.X) <= 1 && Math.Abs(expectedPoint.Y - actualPoint.Y) <= 1);
        var unlockMessage = typeof(Hud).GetMethod("UnlockWindowMessage", access);
        var buttonActivate = new object[] { unlockHandle, 0x0021, IntPtr.Zero, IntPtr.Zero, false };
        Require((IntPtr)unlockMessage.Invoke(hud, buttonActivate) == new IntPtr(3) && (bool)buttonActivate[4]);
        ((Button)surface.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((DispatcherTimer)typeof(Hud).GetField("save", access).GetValue(hud)).Stop();
        Require(!prefs.Locked && !surface.IsVisible);
        hud.UpdateLayout();
        Require(!Hud.CanDragHeader((DependencyObject)play.Content));
        style = (int)getStyle.Invoke(null, new object[] { handle, -20 });
        Require((style & inputFlags) == 0);
        setLocked.Invoke(hud, new object[] { true });
        ((DispatcherTimer)typeof(Hud).GetField("save", access).GetValue(hud)).Stop();
        hud.Hide();
        Require(!surface.IsVisible);
        hud.Show();
        hud.UpdateLayout();
        Require(surface.IsVisible);
        hud.Close();
        Require(surfaceField.GetValue(hud) == null);
        Console.WriteLine("Native click-through and no-activation flags toggle correctly. Hotkey messages unlock without activation.");
        Console.WriteLine("The separate unlock button is clickable, aligned, non-activating, and follows owner visibility.");
    }

    static void CheckPlaylistBeforeHistory()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HudChecks-" + Guid.NewGuid().ToString("N"));
        string data = System.IO.Path.Combine(root, "data");
        string account = System.IO.Path.Combine(root, @"cache\playlists\test-account");
        string cache = System.IO.Path.Combine(root, @"cache\lyrics");
        try
        {
            System.IO.Directory.CreateDirectory(data);
            System.IO.Directory.CreateDirectory(account);
            System.IO.Directory.CreateDirectory(cache);
            const string id = "7rx2ZwinWnNWJNPxZU2oOP";
            const string playlist = "37i9dQZF1DX5vMTfJy5XKE";
            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            System.IO.File.WriteAllText(System.IO.Path.Combine(data, "history.json"), "[]");
            System.IO.File.WriteAllText(System.IO.Path.Combine(data, "session.json"),
                json.Serialize(new SavedSession { last_context = "spotify:playlist:" + playlist }));
            string manifest = System.IO.Path.Combine(account, playlist + ".manifest.json");
            System.IO.File.WriteAllText(manifest, json.Serialize(new PlaylistManifest { version = 2, data_file = 1 }));
            var metadata = new HistoryTrack { id = id, uri = "spotify:track:" + id,
                name = "Fixture song", duration_ms = 200000, artists = new[] { new Artist { name = "Fixture artist" } },
                album = new Album { name = "Fixture album", images = new[] { new Cover { url = "cover-a" } } } };
            string rows = System.IO.Path.Combine(account, playlist + ".rows.0000000000000001");
            System.IO.File.WriteAllText(rows, json.Serialize(new[] { new PlaylistRow { item = metadata } }));
            System.IO.File.WriteAllText(System.IO.Path.Combine(cache, "spotify-v2-" + id + ".json"),
                json.Serialize(new Cache { found = new Lyrics { synced = true,
                    lines = new List<Line> { new Line { at_ms = 1000, text = "first-play lyrics" } } } }));
            var track = new Track { Title = metadata.name, Artist = "Fixture artist", Album = "Fixture album",
                Duration = 200000, Cover = "cover-a" };
            var result = Lookup.ReadLocal(track, root);
            Require(result != null && result.synced && result.lines[0].text == "first-play lyrics");
            string currentCache = System.IO.Path.Combine(cache, "spotify-v2-" + id + ".json");
            string legacyCache = System.IO.Path.Combine(cache, "spotify-" + id + ".json");
            string timedJson = System.IO.File.ReadAllText(currentCache);
            System.IO.File.WriteAllText(legacyCache, timedJson);
            System.IO.File.WriteAllText(currentCache, json.Serialize(new Cache { found = new Lyrics {
                lines = new List<Line> { null, new Line { text = "authoritative plain lyrics" } } } }));
            result = Lookup.ReadLocal(track, root);
            Require(result != null && !result.synced && result.lines.Count == 1 && result.lines[0].text == "authoritative plain lyrics");
            System.IO.File.WriteAllText(currentCache, "{\"found\":null}");
            Require(Lookup.ReadLocal(track, root) == null);
            System.IO.File.SetLastWriteTimeUtc(currentCache, DateTime.UtcNow.AddDays(-31));
            Require(Lookup.ReadLocal(track, root) != null);
            System.IO.File.WriteAllText(currentCache, timedJson);
            System.IO.File.SetLastWriteTimeUtc(currentCache, DateTime.UtcNow);
            track.Cover = "different recording";
            Require(Lookup.ReadLocal(track, root) == null);
            track.Cover = "cover-a";
            // Radio can leave the saved context pointing at a different playlist.
            // The same artist can also have different localized display names.
            metadata.artists[0].name = "歌手中文名";
            System.IO.File.WriteAllText(rows, json.Serialize(new[] { new PlaylistRow { item = metadata } }));
            string session = System.IO.Path.Combine(data, "session.json");
            System.IO.File.WriteAllText(session, json.Serialize(new SavedSession {
                last_context = "spotify:playlist:0000000000000000000000", last_track = "spotify:track:" + id }));
            Require(Lookup.ReadLocal(track, root) != null);
            // A stale saved ID alone never authorizes a localized-name match.
            System.IO.File.WriteAllText(session, json.Serialize(new SavedSession {
                last_context = "spotify:playlist:0000000000000000000000", last_track = "spotify:track:0000000000000000000000" }));
            Require(Lookup.ReadLocal(track, root) == null);
            System.IO.File.WriteAllText(session, json.Serialize(new SavedSession { last_track = "spotify:track:" + id }));
            track.Cover = "";
            Require(Lookup.ReadLocal(track, root) == null);
            track.Cover = "cover-a";
            track.Duration++;
            Require(Lookup.ReadLocal(track, root) == null);
            track.Duration--;
            Require(Lookup.ReadLocal(track, root) != null);
            metadata.uri = "spotify:track:another-version";
            System.IO.File.WriteAllText(rows, json.Serialize(new[] { new PlaylistRow { item = metadata } }));
            Require(Lookup.ReadLocal(track, root) == null);
            System.IO.File.WriteAllText(manifest, "{incomplete");
            Require(Lookup.ReadLocal(track, root) == null);
            Console.WriteLine("Cached playlist lyrics load before play history exists; mismatched recordings and incomplete cache are rejected.");
            Console.WriteLine("Autoplay finds other cached playlists; localized artist names require matching saved ID and full recording metadata.");
        }
        finally
        {
            // This absolute, uniquely named directory was created by this check.
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    }
}
