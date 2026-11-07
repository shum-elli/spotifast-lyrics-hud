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
    static void Require(bool condition)
    {
        if (!condition) throw new Exception("HUD check failed");
    }

    [STAThread]
    static void Main()
    {
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
        setLocked.Invoke(hud, new object[] { true });
        Require(!hud.ShowInTaskbar && !grip.IsEnabled && grip.Visibility == Visibility.Collapsed);
        var sizeBefore = new Size(hud.Width, hud.Height);
        grip.RaiseEvent(new DragDeltaEventArgs(80, 80) { RoutedEvent = Thumb.DragDeltaEvent });
        Require(hud.Width == sizeBefore.Width && hud.Height == sizeBefore.Height);
        var prefs = (Preferences)typeof(Hud).GetField("preferences", access).GetValue(hud);
        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
        Require(serializer.Deserialize<Preferences>(serializer.Serialize(prefs)).Locked);
        var dragTitle = (TextBlock)typeof(Hud).GetField("title", access).GetValue(hud);
        Require(dragTitle.Cursor == Cursors.Arrow);
        setLocked.Invoke(hud, new object[] { false });
        Require(grip.IsEnabled && grip.Visibility == Visibility.Visible && dragTitle.Cursor == Cursors.SizeAll);
        ((DispatcherTimer)typeof(Hud).GetField("save", access).GetValue(hud)).Stop();
        using (var iconData = typeof(Hud).Assembly.GetManifestResourceStream("HUD.Icon"))
        using (var icon = new System.Drawing.Icon(iconData))
            Require(icon.Width == 32 && icon.Height == 32);
        Console.WriteLine("Lock blocks resize events and survives settings serialization. Tray icon loads.");
        var track = new Track { Position = 1250, Duration = 5000, Title = "Synthetic", Artist = "Test", Album = "", Cover = "" };
        typeof(Hud).GetField("track", access).SetValue(hud, track);
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
        Require(Equals(play.Content, "▶") && play.IsEnabled);
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
        Require(Equals(play.Content, "⏸"));
        Console.WriteLine("Playback button reflects paused and playing states.");
    }
}
