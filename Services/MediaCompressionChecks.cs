using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.UI;

namespace Apex.Services;

public static class MediaCompressionChecks
{
    public static async Task<List<string>> RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var folder = await StorageFolder.GetFolderFromPathAsync(root);
        var checks = new List<string>();
        void Assert(bool success, string name)
        {
            if (!success)
                throw new InvalidOperationException(name);
            checks.Add(name);
        }
        var pixels = new byte[512 * 512 * 4];
        new Random(42).NextBytes(pixels);
        for (int i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
        var bitmap = await folder.CreateFileAsync("noise.bmp", CreationCollisionOption.FailIfExists);
        using (var stream = await bitmap.OpenAsync(FileAccessMode.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 512, 512, 96, 96, pixels);
            await encoder.FlushAsync();
        }
        var original = FileToolkit.Inspect(bitmap.Path);
        string originalHash = await FileToolkit.HashAsync(original, default);
        var settings = new CompressionOptions(false, 256, 80, 0, false, false);
        var jpeg = await MediaCompressor.CompressAsync(bitmap.Path, root, settings, null, default);
        using (var file = await (await StorageFile.GetFileFromPathAsync(jpeg.Output)).OpenReadAsync())
        {
            var decoded = await BitmapDecoder.CreateAsync(file);
            Assert(decoded.PixelWidth == 256 && decoded.PixelHeight == 256 && jpeg.OutputBytes < jpeg.OriginalBytes, "JPEG compression and dimension limit");
        }
        Assert(await FileToolkit.HashAsync(original, default) == originalHash, "Compression preserves original bytes");
        var collision = await MediaCompressor.CompressAsync(bitmap.Path, root, settings, null, default);
        Assert(collision.Output != jpeg.Output && File.Exists(jpeg.Output), "Compressed output never overwrites a previous file");
        var limited = await MediaCompressor.CompressAsync(bitmap.Path, root, settings with
        {
            TargetBytes = 10000
        }, null, default);
        Assert(limited.OutputBytes <= 10000, "Image size limit enforced");
        var alpha = await folder.CreateFileAsync("alpha.png", CreationCollisionOption.FailIfExists);
        using (var stream = await alpha.OpenAsync(FileAccessMode.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 2, 2, 96, 96, [0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0]);
            await encoder.FlushAsync();
        }
        var png = await MediaCompressor.CompressAsync(alpha.Path, root, settings with
        {
            Png = true
        }, null, default);
        using (var stream = await (await StorageFile.GetFileFromPathAsync(png.Output)).OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var data = (await decoder.GetPixelDataAsync()).DetachPixelData();
            Assert(data[3] == 0, "PNG transparency preserved");
        }
        var white = await MediaCompressor.CompressAsync(alpha.Path, root, settings, null, default);
        using (var stream = await (await StorageFile.GetFileFromPathAsync(white.Output)).OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var data = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb)).DetachPixelData();
            Assert(data[0] > 245 && data[1] > 245 && data[2] > 245, "JPEG transparent areas flatten to white");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            bool canceled = false;
            try
            {
                await MediaCompressor.CompressAsync(bitmap.Path, root, settings, new InlineProgress(_ => cancellation.Cancel()), cancellation.Token);
            }
            catch (OperationCanceledException) { canceled = true; }
            Assert(canceled && !Directory.EnumerateFiles(root, ".apex-compress-*").Any(), "Image cancellation removes partial output");
        }
        var video = await folder.CreateFileAsync("video-fixture.mp4", CreationCollisionOption.FailIfExists);
        var composition = new MediaComposition();
        foreach (var color in new[] { Color.FromArgb(255, 210, 80, 40), Color.FromArgb(255, 40, 180, 70), Color.FromArgb(255, 30, 70, 210) })
            composition.Clips.Add(MediaClip.CreateFromColor(color, TimeSpan.FromSeconds(1)));
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width = 640;
        profile.Video.Height = 360;
        profile.Video.FrameRate.Numerator = 60;
        profile.Video.FrameRate.Denominator = 1;
        var rendering = await composition.RenderToFileAsync(video, MediaTrimmingPreference.Fast, profile);
        if (rendering != Windows.Media.Transcoding.TranscodeFailureReason.None)
            throw new IOException("Cannot render video fixture: " + rendering);
        var originalVideo = FileToolkit.Inspect(video.Path);
        var videoHash = await FileToolkit.HashAsync(originalVideo, default);
        var compressed = await MediaCompressor.CompressAsync(video.Path, root, new(true, 320, 70, 250000, false, true), null, default);
        var properties = await (await StorageFile.GetFileFromPathAsync(compressed.Output)).Properties.GetVideoPropertiesAsync();
        Assert(properties.Width == 320 && properties.Height == 180 && properties.Duration.TotalSeconds > 2 && compressed.OutputBytes <= 250000, "Playable MP4 dimensions, duration, and size limit");
        Assert(await FileToolkit.HashAsync(originalVideo, default) == videoHash, "Video original preserved");
        var software = await MediaCompressor.CompressAsync(video.Path, root, new(true, 320, 70, 0, false, false), null, default);
        Assert(new FileInfo(software.Output).Length > 0, "Video software encoding path");
        using (var cancellation = new CancellationTokenSource())
        {
            bool canceled = false;
            try
            {
                await MediaCompressor.CompressAsync(video.Path, root, new(true, 320, 70, 0, false, true), new InlineProgress(_ => cancellation.Cancel()), cancellation.Token);
            }
            catch (OperationCanceledException) { canceled = true; }
            Assert(canceled && !Directory.EnumerateFiles(root, ".apex-compress-*").Any(), "Video cancellation removes partial output");
        }
        var qualityOptions = settings with
        {
            Method = CompressionMethod.Quality,
            PreserveDimensions = true,
            Quality = 80
        };
        var preserved = await MediaCompressor.CompressAsync(bitmap.Path, root, qualityOptions, null, default);
        using (var stream = await (await StorageFile.GetFileFromPathAsync(preserved.Output)).OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            Assert(decoder.PixelWidth == 512 && decoder.PixelHeight == 512, "Quality defaults preserve image dimensions");
        }
        var lowQuality = await MediaCompressor.CompressAsync(bitmap.Path, root, qualityOptions with
        {
            Quality = 20
        }, null, default);
        Assert(lowQuality.OutputBytes < preserved.OutputBytes, "JPEG quality controls encoder output rather than always using maximum compression");
        var percentSettings = qualityOptions with
        {
            Method = CompressionMethod.TargetPercent,
            TargetPercent = 60
        };
        var percentOutput = await MediaCompressor.CompressAsync(bitmap.Path, root, percentSettings, null, default);
        Assert(percentOutput.OutputBytes <= percentSettings.ResolveTarget(original.Size), "Per-file percentage cap is verified after image encoding");
        var mbOutput = await MediaCompressor.CompressAsync(bitmap.Path, root, qualityOptions with
        {
            Method = CompressionMethod.TargetMB,
            TargetBytes = 100000
        }, null, default);
        Assert(mbOutput.OutputBytes <= 100000, "Explicit MB mode enforces the maximum output size");
        bool unattainable = false;
        try
        {
            await MediaCompressor.CompressAsync(bitmap.Path, root, percentSettings with
            {
                TargetPercent = 1,
                Png = true
            }, null, default);
        }
        catch (IOException) { unattainable = true; }
        Assert(unattainable && !Directory.EnumerateFiles(root, ".apex-compress-*").Any(), "Unattainable lossless target fails without silent resizing or partial output");
        var resized = await MediaCompressor.CompressAsync(bitmap.Path, root, percentSettings with
        {
            TargetPercent = 1,
            Png = true,
            AllowResizeToTarget = true
        }, null, default);
        using (var stream = await (await StorageFile.GetFileFromPathAsync(resized.Output)).OpenReadAsync())
        {
            var decoder = await BitmapDecoder.CreateAsync(stream);
            Assert(decoder.PixelWidth < 512 && resized.OutputBytes <= percentSettings.ResolveTarget(original.Size) / 60, "Explicit resizing can meet a small lossless target");
        }
        var sourceProps = (await MediaClip.CreateFromFileAsync(video)).GetVideoEncodingProperties();
        var videoQuality = new CompressionOptions(true, 1280, 80, 0, false, false, CompressionMethod.Quality, 60, true, true);
        var preservedVideo = await MediaCompressor.CompressAsync(video.Path, root, videoQuality, null, default);
        var resultProps = (await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(preservedVideo.Output))).GetVideoEncodingProperties();
        Assert(resultProps.Width == sourceProps.Width && resultProps.Height == sourceProps.Height && Math.Abs(resultProps.FrameRate.Numerator / (double)resultProps.FrameRate.Denominator - sourceProps.FrameRate.Numerator / (double)sourceProps.FrameRate.Denominator) < .01, "Video quality preserves original dimensions and 60 fps");
        var videoPercent = videoQuality with
        {
            Method = CompressionMethod.TargetPercent,
            TargetPercent = 60
        };
        var percentVideo = await MediaCompressor.CompressAsync(video.Path, root, videoPercent, null, default);
        Assert(percentVideo.OutputBytes <= videoPercent.ResolveTarget(originalVideo.Size), "Video percentage mode uses its own original byte size");
        var mbVideo = await MediaCompressor.CompressAsync(video.Path, root, videoQuality with
        {
            Method = CompressionMethod.TargetMB,
            TargetBytes = 100000
        }, null, default);
        Assert(mbVideo.OutputBytes <= 100000, "Video MB mode verifies the per-file cap");
        var cappedVideo = await MediaCompressor.CompressAsync(video.Path, root, videoQuality with
        {
            PreserveFrameRate = false,
            MaximumFrameRate = 24
        }, null, default);
        var capProps = (await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(cappedVideo.Output))).GetVideoEncodingProperties();
        Assert(capProps.FrameRate.Numerator / (double)capProps.FrameRate.Denominator <= 24.01, "Explicit video frame-rate cap is respected");
        var mixed = new[] { new CompressionQueueItem(bitmap.Path, percentSettings), new CompressionQueueItem(video.Path, videoQuality) };
        var batch = await CompressionBatch.RunAsync(mixed, (item, token) => MediaCompressor.CompressAsync(item.Source, root, item.Options, null, token), _ => { }, default);
        Assert(batch.Kind == TaskOutcomeKind.Success && batch.Outputs?.Count == 2 && mixed.All(i => i.State == QueueState.Completed), "Real mixed-media queue retains independent methods and completed results");
        return checks;
    }
    private sealed class InlineProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }
}
