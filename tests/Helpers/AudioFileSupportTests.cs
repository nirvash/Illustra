using System.IO;
using Illustra.Helpers;
using Illustra.Models;
using NUnit.Framework;

namespace Illustra.Tests.Helpers;

[TestFixture]
public class AudioFileSupportTests
{
    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void AudioNameUpdatesWhenSwitchingBetweenAudioFiles()
    {
        var control = new Illustra.Controls.VideoPlayerControl();
        try
        {
            control.FilePath = "first.wav";
            control.FilePath = "second.mp3";
            var name = (System.Windows.Controls.TextBlock)control.FindName("AudioFileName");
            Assert.That(name.Text, Is.EqualTo("second.mp3"));
            var media = (System.Windows.Controls.MediaElement)control.FindName("VideoPlayer");
            Assert.That(media.Visibility, Is.EqualTo(System.Windows.Visibility.Visible),
                "音声のMediaElementも表示ツリーでメディアを開ける状態を保持する");
        }
        finally { control.DisposeForFinalClose(); }
    }

    [Test]
    [Apartment(System.Threading.ApartmentState.STA)]
    public async System.Threading.Tasks.Task OptimizedAudioThumbnailUsesTheAudioArtworkAsync()
    {
        var expected = ThumbnailHelper.GenerateAudioThumbnail(64, 64);
        var actual = await ThumbnailHelper.CreateThumbnailOptimizedAsync("not-an-image.wav", 64, 64);
        var expectedPixels = new byte[64 * 64 * 4];
        var actualPixels = new byte[64 * 64 * 4];
        expected.CopyPixels(expectedPixels, 256, 0);
        actual.CopyPixels(actualPixels, 256, 0);
        Assert.That(actualPixels, Is.EqualTo(expectedPixels));
        Assert.That(actual.IsFrozen, Is.True);
    }

    [Test]
    public async System.Threading.Tasks.Task AudioPropertiesReadDurationAndClearAudioFlagAsync()
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"audio-{System.Guid.NewGuid():N}.wav");
        try
        {
            using (var stream = System.IO.File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(16036);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(16000); writer.Write(new byte[16000]);
            }
            var properties = await ImagePropertiesModel.LoadFromFileAsync(path);
            Assert.That(properties.IsAudio, Is.True);
            Assert.That(properties.IsVideo, Is.False);
            Assert.That(properties.Duration.TotalSeconds, Is.EqualTo(1).Within(0.01));
            Assert.That(properties.HasDuration, Is.True);
            Assert.That(properties.Resolution, Is.Empty);
            properties.Clear();
            Assert.That(properties.IsAudio, Is.False);
        }
        finally { System.IO.File.Delete(path); }
    }

    [TestCase("wav")]
    [TestCase("mp3")]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void RealAudioMediaOpensWithoutWaitingForPlayButton(string extension)
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"playback-{System.Guid.NewGuid():N}.{extension}");
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(16036);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(16000); writer.Write(new byte[16000]);
        }
        if (extension == "mp3")
        {
            using var input = typeof(AudioFileSupportTests).Assembly.GetManifestResourceStream("Illustra.Tests.Fixtures.silence.mp3")!;
            using var output = File.Create(path);
            input.CopyTo(output);
        }
        var app = System.Windows.Application.Current ?? new System.Windows.Application();
        var oldMode = app.ShutdownMode;
        var oldMain = app.MainWindow;
        var oldContext = System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
        var oldLoadError = app.Resources["String_Video_LoadError"];
        app.Resources["String_Video_LoadError"] = "Media load failed: {0}";
        app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        var control = new Illustra.Controls.VideoPlayerControl();
        var host = new System.Windows.Window { Content = control, Width = 300, Height = 300, ShowInTaskbar = false,
            WindowState = System.Windows.WindowState.Minimized };
        var opened = false;
        var media = (System.Windows.Controls.MediaElement)control.FindName("VideoPlayer");
        media.MediaOpened += (_, _) => opened = true;
        try
        {
            control.FilePath = path;
            // ビューアではホスト表示前にファイルが指定されることがある。
            control.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            host.Show();
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            Assert.That(opened, Is.True, "実WAVのMediaOpenedイベントが再生UIを有効にする");
            Assert.That(media.NaturalDuration.HasTimeSpan, Is.True);
            Assert.That(media.Source, Is.Not.Null);
            control.StopVideo();
            Assert.That(media.Source, Is.Not.Null, "停止ボタンでは同じ動画を再開できる");
            control.ReleaseVideo();
            Assert.Multiple(() =>
            {
                Assert.That(media.Source, Is.Null, "別の画像を表示するときは動画デコーダを解放する");
                Assert.That(control.FilePath, Is.Empty);
            });
            control.FilePath = path;
            control.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            Assert.That(media.Source, Is.Not.Null, "同じ動画に戻っても再読み込みできる");
        }
        finally
        {
            control.DisposeForFinalClose(); host.Close();
            app.MainWindow = oldMain; app.ShutdownMode = oldMode;
            if (oldLoadError == null) app.Resources.Remove("String_Video_LoadError");
            else app.Resources["String_Video_LoadError"] = oldLoadError;
            System.Threading.SynchronizationContext.SetSynchronizationContext(oldContext);
            File.Delete(path);
        }
    }

    [TestCase("song.wav")]
    [TestCase("song.MP3")]
    public void AudioFilesAreMediaButNotVideo(string path)
    {
        Assert.That(FileHelper.IsAudioFile(path), Is.True);
        Assert.That(FileHelper.IsMediaFile(path), Is.True);
        Assert.That(FileHelper.IsVideoFile(path), Is.False);
        Assert.That(FileHelper.SupportedExtensions, Does.Contain(Path.GetExtension(path).ToLowerInvariant()));
        Assert.That(new FileNodeModel(path).IsImage, Is.False);
    }
}
