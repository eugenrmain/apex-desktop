using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.UI;

namespace Apex.Services;

public static class MediaConversionChecks
{
    public static async Task<List<string>> RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var folder = await StorageFolder.GetFolderFromPathAsync(root);
        var checks = new List<string>();
        void Assert(bool ok, string name)
        {
            if (!ok)
                throw new IOException(name);
            checks.Add(name);
        }
        string wave = Path.Combine(root, "tone.wav");
        const int sampleRate = 44100, samples = sampleRate * 3;
        using (var writer = new BinaryWriter(File.Create(wave)))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + samples * 2);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(samples * 2);
            for (int i = 0; i < samples; i++)
                writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 12000));
        }
        var composition = new MediaComposition();
        composition.Clips.Add(MediaClip.CreateFromColor(Color.FromArgb(255, 100, 149, 237), TimeSpan.FromSeconds(3)));
        composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(wave)));
        var video = await folder.CreateFileAsync("audio-fixture.mp4", CreationCollisionOption.FailIfExists);
        var videoProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Wvga);
        videoProfile.Video.Width = 320;
        videoProfile.Video.Height = 180;
        var render = await composition.RenderToFileAsync(video, MediaTrimmingPreference.Fast, videoProfile);
        Assert(render == Windows.Media.Transcoding.TranscodeFailureReason.None, "MP4 fixture contains generated audio");
        var original = FileToolkit.Inspect(video.Path);
        string hash = await FileToolkit.HashAsync(original, default);
        var result = await MediaConverter.ExtractMp3Async(video.Path, null, default);
        var encoded = await MediaEncodingProfile.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(result.Output));
        Assert(string.Equals(encoded.Audio?.Subtype, "MP3", StringComparison.OrdinalIgnoreCase) && encoded.Video is null && result.Bytes > 0 && Math.Abs(result.Duration.TotalSeconds - 3) < .25, "MP4 conversion produces verified audio-only MP3 with the source duration");
        Assert(Path.GetDirectoryName(result.Output) == root && Path.GetFileName(result.Output) == "audio-fixture.mp3", "Converted audio saves beside the original with its filename");
        Assert(await FileToolkit.HashAsync(original, default) == hash, "MP3 conversion preserves the original MP4");
        var first = FileToolkit.Inspect(result.Output);
        string firstHash = await FileToolkit.HashAsync(first, default);
        var second = await MediaConverter.ExtractMp3Async(video.Path, null, default);
        Assert(second.Output != result.Output && await FileToolkit.HashAsync(first, default) == firstHash, "MP3 filename collisions keep existing outputs intact");
        using (var cancel = new CancellationTokenSource())
        {
            try
            {
                await MediaConverter.ExtractMp3Async(video.Path, new InlineProgress(_ => cancel.Cancel()), cancel.Token);
                throw new Exception("Cancellation ignored");
            }
            catch (OperationCanceledException) { checks.Add("In-flight MP3 cancellation leaves no completed output"); }
            Assert(!Directory.EnumerateFiles(root, ".apex-convert-*").Any(), "Canceled conversion removes partial MP3 files");
        }
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try
            {
                await MediaConverter.ExtractMp3Async(video.Path, null, cancel.Token);
                throw new Exception("Pre-cancellation ignored");
            }
            catch (OperationCanceledException) { checks.Add("Pre-canceled MP3 conversion performs no work"); }
        }
        try
        {
            await MediaConverter.ExtractMp3Async(wave, null, default);
            throw new Exception("Wrong format accepted");
        }
        catch (IOException) { checks.Add("Converter rejects unsupported input formats"); }
        string corrupt = Path.Combine(root, "corrupt.mp4");
        File.WriteAllText(corrupt, "This is not an MP4 container.");
        try
        {
            await MediaConverter.ExtractMp3Async(corrupt, null, default);
            throw new Exception("Malformed MP4 accepted");
        }
        catch (IOException e) { Assert(e.Message.Contains("Windows could not convert"), "Malformed MP4 fails with a readable conversion error"); }
        var silent = new MediaComposition();
        silent.Clips.Add(MediaClip.CreateFromColor(Color.FromArgb(255, 0, 0, 0), TimeSpan.FromSeconds(1)));
        var noAudio = await folder.CreateFileAsync("no-audio.mp4", CreationCollisionOption.FailIfExists);
        videoProfile.Audio = null;
        await silent.RenderToFileAsync(noAudio, MediaTrimmingPreference.Fast, videoProfile);
        try
        {
            await MediaConverter.ExtractMp3Async(noAudio.Path, null, default);
            throw new Exception("Audio-free video accepted");
        }
        catch (IOException e) { Assert(e.Message.Contains("no audio track"), "MP4 with no audio gives an explicit explanation"); }
        string racing = Path.Combine(root, "racing.mp4"), occupied = Path.Combine(root, "racing.mp3");
        File.Copy(video.Path, racing);
        try
        {
            await MediaConverter.ExtractMp3Async(racing, new InlineProgress(_ => { if (!File.Exists(occupied)) File.WriteAllText(occupied, "keep existing output"); }), default);
            throw new Exception("Concurrent output overwritten");
        }
        catch (IOException) { Assert(File.ReadAllText(occupied) == "keep existing output", "Output created during conversion is never overwritten"); }
        bool changed = false;
        try
        {
            await MediaConverter.ExtractMp3Async(video.Path, new InlineProgress(_ => { if (!changed) { changed = true; File.SetLastWriteTimeUtc(video.Path, original.ModifiedUtc.AddMinutes(1)); } }), default);
            throw new Exception("Changed source accepted");
        }
        catch (IOException e) { Assert(e.Message.Contains("File changed"), "Source changed during conversion is rejected before publishing"); }
        finally { File.SetLastWriteTimeUtc(video.Path, original.ModifiedUtc); }
        Assert(!Directory.EnumerateFiles(root, ".apex-convert-*").Any(), "Failed conversions leave no temporary MP3 files");
        return checks;
    }

    private sealed class InlineProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }
}
