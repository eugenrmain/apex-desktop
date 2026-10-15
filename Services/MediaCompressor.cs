using Windows.Graphics.Imaging;
using Windows.Media.MediaProperties;
using Windows.Media.Editing;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Foundation;

namespace Apex.Services;

public enum CompressionMethod
{
    Quality, TargetPercent, TargetMB
}
public sealed record CompressionOptions(bool Video, int MaximumEdge, int Quality, long TargetBytes, bool Png, bool Hardware,
    CompressionMethod? Method = null, double TargetPercent = 60, bool PreserveDimensions = false, bool PreserveFrameRate = true, double MaximumFrameRate = 30, bool AllowResizeToTarget = false)
{
    public CompressionMethod EffectiveMethod => Method ?? (TargetBytes > 0 ? CompressionMethod.TargetMB : CompressionMethod.Quality);
    public long ResolveTarget(long originalBytes) => EffectiveMethod switch
    {
        CompressionMethod.Quality => 0,
        CompressionMethod.TargetPercent when double.IsFinite(TargetPercent) && TargetPercent is >= 1 and <= 100 => Math.Max(1, (long)Math.Floor(originalBytes * TargetPercent / 100)),
        CompressionMethod.TargetMB when TargetBytes is >= 100000 and <= 10240000000 => TargetBytes,
        // Existing callers can specify small fixture limits; explicit UI MB mode has the stricter range.
        CompressionMethod.TargetMB when Method is null && TargetBytes > 0 => TargetBytes,
        _ => throw new IOException("Choose a valid compression target.")
    };
}
public sealed record CompressionResult(string Output, long OriginalBytes, long OutputBytes, string Details);

public static class MediaCompressor
{
    public static async Task<CompressionResult> CompressAsync(string source, string directory, CompressionOptions options, IProgress<string>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(source))
            throw new IOException("Choose a file first.");
        if (string.IsNullOrWhiteSpace(directory))
            throw new IOException("Choose an output directory.");
        var entry = FileToolkit.Inspect(source);
        directory = Path.GetFullPath(directory);
        FileToolkit.ValidateMutation(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("The output directory does not exist.");
        if ((!options.PreserveDimensions && (options.MaximumEdge < 2 || options.MaximumEdge > 8192)) || options.Quality < 10 || options.Quality > 100 || options.TargetBytes < 0)
            throw new IOException("Choose valid compression settings.");
        options = options with
        {
            TargetBytes = options.ResolveTarget(entry.Size)
        };
        var extension = options.Video ? ".mp4" : options.Png ? ".png" : ".jpg";
        var stem = Path.GetFileNameWithoutExtension(entry.Path) + "-compressed";
        string output = Path.Combine(directory, stem + extension);
        for (int i = 2; File.Exists(output) || Directory.Exists(output) || string.Equals(output, entry.Path, StringComparison.OrdinalIgnoreCase); i++)
            output = Path.Combine(directory, stem + " (" + i + ")" + extension);
        string temporary = Path.Combine(directory, ".apex-compress-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            var input = await StorageFile.GetFileFromPathAsync(entry.Path).AsTask(token);
            var folder = await StorageFolder.GetFolderFromPathAsync(directory).AsTask(token);
            var target = await folder.CreateFileAsync(Path.GetFileName(temporary), CreationCollisionOption.FailIfExists).AsTask(token);
            string detail = options.Video ? await VideoAsync(input, target, options, progress, token) : await ImageAsync(input, target, options, progress, token);
            token.ThrowIfCancellationRequested();
            FileToolkit.Recheck(entry);
            FileToolkit.ValidateMutation(output);
            long bytes = new FileInfo(temporary).Length;
            if (bytes == 0)
                throw new IOException("The encoder produced an empty file.");
            if (options.TargetBytes > 0 && bytes > options.TargetBytes)
                throw new IOException("The chosen size limit could not be reached. Try a larger limit or a lower resolution.");
            File.Move(temporary, output, false);
            return new(output, entry.Size, bytes, detail);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static (uint Width, uint Height) Fit(uint width, uint height, int maximum, bool even)
    {
        if (width == 0 || height == 0)
            throw new IOException("Could not read the media dimensions.");
        double scale = Math.Min(1d, maximum / (double)Math.Max(width, height));
        uint w = (uint)Math.Max(even ? 2 : 1, Math.Floor(width * scale));
        uint h = (uint)Math.Max(even ? 2 : 1, Math.Floor(height * scale));
        if (even)
        {
            w -= w % 2;
            h -= h % 2;
        }
        return (w, h);
    }
    private static async Task<string> VideoAsync(StorageFile input, StorageFile output, CompressionOptions options, IProgress<string>? progress, CancellationToken token)
    {
        var properties = await input.Properties.GetVideoPropertiesAsync().AsTask(token);
        double seconds = properties.Duration.TotalSeconds;
        if (seconds <= 0)
            throw new IOException("This file is not a supported video or its duration cannot be read.");
        var (width, height) = Fit(properties.Width, properties.Height, options.PreserveDimensions ? (int)Math.Max(properties.Width, properties.Height) : options.MaximumEdge, true);
        var clip = await MediaClip.CreateFromFileAsync(input).AsTask(token);
        var sourceVideo = clip.GetVideoEncodingProperties();
        uint numerator = sourceVideo.FrameRate.Numerator, denominator = sourceVideo.FrameRate.Denominator;
        if (numerator == 0 || denominator == 0)
        {
            if (options.PreserveFrameRate)
                throw new IOException("Cannot read the original frame rate; choose an explicit frame-rate limit.");
            if (!double.IsFinite(options.MaximumFrameRate) || options.MaximumFrameRate is < 1 or > 240)
                throw new IOException("Choose a frame-rate limit from 1 to 240.");
            numerator = (uint)Math.Round(options.MaximumFrameRate * 1000);
            denominator = 1000;
        }
        double fps = numerator / (double)denominator;
        if (!options.PreserveFrameRate)
        {
            if (!double.IsFinite(options.MaximumFrameRate) || options.MaximumFrameRate is < 1 or > 240)
                throw new IOException("Choose a frame-rate limit from 1 to 240.");
            if (fps > options.MaximumFrameRate)
            {
                numerator = (uint)Math.Round(options.MaximumFrameRate * 1000);
                denominator = 1000;
                fps = numerator / (double)denominator;
            }
        }
        uint audio = 96000;
        double bitrate = options.TargetBytes > 0 ? options.TargetBytes * 8d * .94 / seconds - audio : (sourceVideo.Bitrate > 0 ? sourceVideo.Bitrate : width * (double)height * fps * 4 / 30) * (options.Quality / 100d);
        if (options.TargetBytes == 0)
            bitrate = Math.Max(80000, bitrate);
        if (bitrate < 80000)
            throw new IOException("This size limit is too small for the video duration. Choose a larger limit.");
        bitrate = Math.Min(bitrate, 40_000_000);
        bool hardware = options.Hardware;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            using (var reset = await output.OpenAsync(FileAccessMode.ReadWrite).AsTask(token))
                reset.Size = 0;
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
            profile.Video.Width = width;
            profile.Video.Height = height;
            profile.Video.Bitrate = (uint)bitrate;
            profile.Audio.Bitrate = audio;
            profile.Video.FrameRate.Numerator = numerator;
            profile.Video.FrameRate.Denominator = denominator;
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = hardware, AlwaysReencode = true };
            var prepared = await transcoder.PrepareFileTranscodeAsync(input, output, profile).AsTask(token);
            if (!prepared.CanTranscode && hardware)
            {
                hardware = false;
                transcoder = new MediaTranscoder { HardwareAccelerationEnabled = false, AlwaysReencode = true };
                prepared = await transcoder.PrepareFileTranscodeAsync(input, output, profile).AsTask(token);
            }
            if (!prepared.CanTranscode)
                throw new IOException("Windows cannot encode this video (" + prepared.FailureReason + "). Try a supported MP4/MOV file. No codecs are downloaded automatically.");
            progress?.Report(attempt == 0 ? "Encoding video…" : "Adjusting bitrate to fit the size limit…");
            try
            {
                await prepared.TranscodeAsync().AsTask(token, new Progress<double>(p => progress?.Report($"Encoding video · {p:0}%")));
            }
            catch (System.Runtime.InteropServices.COMException) when (hardware && !token.IsCancellationRequested)
            {
                hardware = false;
                progress?.Report("Hardware encoding unavailable · using software…");
                using (var reset = await output.OpenAsync(FileAccessMode.ReadWrite).AsTask(token))
                    reset.Size = 0;
                prepared = await new MediaTranscoder { HardwareAccelerationEnabled = false, AlwaysReencode = true }.PrepareFileTranscodeAsync(input, output, profile).AsTask(token);
                if (!prepared.CanTranscode)
                    throw new IOException("Windows cannot encode this video: " + prepared.FailureReason);
                await prepared.TranscodeAsync().AsTask(token, new Progress<double>(p => progress?.Report($"Encoding video · {p:0}%")));
            }
            long size = (long)(await output.GetBasicPropertiesAsync().AsTask(token)).Size;
            if (options.TargetBytes == 0 || size <= options.TargetBytes)
                return $"MP4 · H.264 / AAC · {width} × {height} · {fps:0.###} fps";
            bitrate *= options.TargetBytes / (double)size * .90;
            if (bitrate < 80000)
            {
                if (!options.AllowResizeToTarget)
                    break;
                width = Math.Max(2, (uint)(width * .75) / 2 * 2);
                height = Math.Max(2, (uint)(height * .75) / 2 * 2);
                bitrate = 80000;
            }
        }
        throw new IOException("Could not reach the requested size limit. Try a larger limit or lower resolution.");
    }
    private static async Task<string> ImageAsync(StorageFile input, StorageFile output, CompressionOptions options, IProgress<string>? progress, CancellationToken token)
    {
        using var stream = await input.OpenReadAsync().AsTask(token);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        if (decoder.FrameCount != 1)
            throw new IOException("Animated and multi-page images are not supported; frames will not be silently discarded.");
        var (width, height) = Fit(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, options.PreserveDimensions ? (int)Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight) : options.MaximumEdge, false);
        if ((long)width * height > 40_000_000)
            throw new IOException("Choose a smaller maximum image dimension.");
        int quality = options.TargetBytes > 0 ? 95 : options.Quality;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"Encoding image · {width} × {height}" + (options.Png ? "" : $" · quality {quality}%"));
            bool rotated = decoder.OrientedPixelWidth != decoder.PixelWidth;
            var transform = new BitmapTransform { ScaledWidth = rotated ? height : width, ScaledHeight = rotated ? width : height, InterpolationMode = BitmapInterpolationMode.Fant };
            var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(token)).DetachPixelData();
            if (!options.Png)
                await Task.Run(() =>
                {
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        if (i % 262144 == 0)
                            token.ThrowIfCancellationRequested();
                        int alpha = pixels[i + 3];
                        for (int channel = 0; channel < 3; channel++)
                            pixels[i + channel] = (byte)((pixels[i + channel] * alpha + 255 * (255 - alpha) + 127) / 255);
                        pixels[i + 3] = 255;
                    }
                }, token);
            using (var destination = await output.OpenAsync(FileAccessMode.ReadWrite).AsTask(token))
            {
                destination.Size = 0;
                BitmapEncoder encoder;
                if (options.Png)
                    encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, destination).AsTask(token);
                else
                    encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, destination, new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(quality / 100f, PropertyType.Single) }).AsTask(token);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, options.Png ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
                await encoder.FlushAsync().AsTask(token);
            }
            long bytes = (long)(await output.GetBasicPropertiesAsync().AsTask(token)).Size;
            if (options.TargetBytes == 0 || bytes <= options.TargetBytes)
                return $"{(options.Png ? "PNG · transparency preserved" : "JPEG · transparent areas become white")} · {width} × {height}";
            if (!options.Png && quality > 10)
                quality = Math.Max(10, quality - 15);
            else if (options.AllowResizeToTarget)
            {
                width = Math.Max(1, (uint)(width * .75));
                height = Math.Max(1, (uint)(height * .75));
            }
            else
                break;
        }
        throw new IOException("Could not reach this image size limit. Choose a larger limit or explicitly allow resizing.");
    }
}
