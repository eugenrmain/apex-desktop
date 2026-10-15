using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Apex.Services;

public sealed record AudioConversionResult(string Output, long Bytes, TimeSpan Duration);

public static class MediaConverter
{
    public static async Task<AudioConversionResult> ExtractMp3Async(string source, IProgress<string>? progress, CancellationToken token)
    {
        try
        {
            return await ConvertMp3Async(source, progress, token);
        }
        catch (System.Runtime.InteropServices.COMException e) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException("Conversion canceled.", e, token);
        }
        catch (System.Runtime.InteropServices.COMException e)
        {
            throw new IOException("Windows could not convert this MP4 to MP3. The audio may be unsupported or protected.", e);
        }
    }

    private static async Task<AudioConversionResult> ConvertMp3Async(string source, IProgress<string>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var entry = FileToolkit.Inspect(source);
        if (!Path.GetExtension(entry.Path).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose an MP4 file to convert to MP3.");
        string directory = Path.GetDirectoryName(entry.Path)!;
        FileToolkit.ValidateMutation(directory);
        var input = await StorageFile.GetFileFromPathAsync(entry.Path).AsTask(token);
        var sourceProfile = await MediaEncodingProfile.CreateFromFileAsync(input).AsTask(token);
        if (sourceProfile.Audio is null || sourceProfile.Audio.ChannelCount == 0)
            throw new IOException("This MP4 has no audio track to convert.");
        string stem = Path.GetFileNameWithoutExtension(entry.Path);
        string output = Path.Combine(directory, stem + ".mp3");
        for (int i = 2; File.Exists(output) || Directory.Exists(output); i++)
            output = Path.Combine(directory, stem + " (" + i + ").mp3");
        string temporary = Path.Combine(directory, ".apex-convert-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(directory).AsTask(token);
            var target = await folder.CreateFileAsync(Path.GetFileName(temporary), CreationCollisionOption.FailIfExists).AsTask(token);
            var profile = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);
            profile.Audio.Bitrate = 192000;
            profile.Audio.SampleRate = 44100;
            profile.Audio.ChannelCount = Math.Min(2u, sourceProfile.Audio.ChannelCount);
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = false, AlwaysReencode = true };
            var prepared = await transcoder.PrepareFileTranscodeAsync(input, target, profile).AsTask(token);
            if (!prepared.CanTranscode)
                throw new IOException("Windows cannot convert this audio (" + prepared.FailureReason + "). The original is unchanged.");
            progress?.Report("Converting to MP3…");
            await prepared.TranscodeAsync().AsTask(token, new Progress<double>(p => progress?.Report($"Converting · {p:0}%")));
            token.ThrowIfCancellationRequested();
            var encoded = await MediaEncodingProfile.CreateFromFileAsync(target).AsTask(token);
            var properties = await target.Properties.GetMusicPropertiesAsync().AsTask(token);
            long bytes = new FileInfo(temporary).Length;
            if (encoded.Audio is null || !encoded.Audio.Subtype.Equals("MP3", StringComparison.OrdinalIgnoreCase) || properties.Duration <= TimeSpan.Zero || bytes == 0)
                throw new IOException("The MP3 output could not be verified.", new InvalidDataException($"Subtype={encoded.Audio?.Subtype}; duration={properties.Duration}; bytes={bytes}"));
            FileToolkit.Recheck(entry);
            FileToolkit.ValidateMutation(output);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, output, false);
            return new(output, bytes, properties.Duration);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
