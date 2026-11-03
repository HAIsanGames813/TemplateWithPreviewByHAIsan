using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using TemplateWithPreview.Models;
using YukkuriMovieMaker.ItemEditor;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace TemplateWithPreview;

internal sealed record EffectPreviewVideoResult(string VideoPath, BitmapSource? Thumbnail, int FrameCount, int FPS)
{
    public double DurationSeconds => (double)FrameCount / FPS;
}

internal sealed record AudioWaveformResult(BitmapSource Waveform, double DurationSeconds, string? AudioPath);

internal static class EffectPreviewVideoGenerator
{
    private const int PreviewWidth = 640;
    private const int PreviewHeight = 360;

    public static Task<EffectPreviewVideoResult> GenerateAsync(
        IItem sourceItem,
        IReadOnlyCollection<IVideoEffect> selectedEffects,
        string presetId,
        int slot,
        double thumbnailSeconds,
        string? outputPath = null)
    {
        string ffmpegPath = FindFfmpegPath();
        var timeline = PresetApplier.GetActiveTimeline();
        var sourceVideoInfo = timeline?.VideoInfo;
        int fps = Math.Clamp(ReadInt(sourceVideoInfo, "FPS", 30), 1, 60);
        int width = Math.Clamp(ReadInt(sourceVideoInfo, "Width", PreviewWidth), 64, 3840);
        int height = Math.Clamp(ReadInt(sourceVideoInfo, "Height", PreviewHeight), 64, 2160);
        int hz = ReadInt(sourceVideoInfo, "Hz", 48000);
        Color backgroundColor = ReadColor(sourceVideoInfo, "BackgroundColor", Colors.Black);
        int frameCount = Math.Clamp(sourceItem.Length, 1, fps * 15);
        var cloned = Json.GetClone(sourceItem) as IItem ?? throw new InvalidOperationException("選択アイテムを複製できませんでした");
        var clonedEffects = selectedEffects
            .Select(effect => Json.GetClone(effect))
            .Where(effect => effect != null)
            .Select(effect => effect!)
            .ToList();
        return Task.Run(() => Generate(cloned, clonedEffects, outputPath ?? PresetStorage.GetPreviewVideoPath(presetId, slot), ffmpegPath, thumbnailSeconds, fps, width, height, hz, backgroundColor, frameCount));
    }

    public static Task<EffectPreviewVideoResult> GenerateItemTemplateAsync(
        IReadOnlyCollection<IItem> sourceItems,
        string presetId,
        int slot,
        double thumbnailSeconds,
        string? outputPath = null,
        Action<int>? onFrameProgress = null)
    {
        if (sourceItems.Count == 0) throw new InvalidOperationException("テンプレートにアイテムがありません");
        string ffmpegPath = FindFfmpegPath();
        var videoInfo = PresetApplier.GetActiveTimeline()?.VideoInfo;
        int fps = Math.Clamp(ReadInt(videoInfo, "FPS", 30), 1, 60);
        int width = Math.Clamp(ReadInt(videoInfo, "Width", PreviewWidth), 64, 3840);
        int height = Math.Clamp(ReadInt(videoInfo, "Height", PreviewHeight), 64, 2160);
        int hz = ReadInt(videoInfo, "Hz", 48000);
        Color backgroundColor = ReadColor(videoInfo, "BackgroundColor", Colors.Black);
        var clones = sourceItems.Select(item => Json.GetClone(item) as IItem ?? throw new InvalidOperationException("テンプレートアイテムを複製できませんでした")).ToList();
        int firstFrame = clones.Min(item => item.Frame);
        int firstLayer = clones.Min(item => item.Layer);
        int lastFrame = clones.Max(item => item.Frame + Math.Max(1, item.Length));
        int frameCount = Math.Clamp(lastFrame - firstFrame, 1, fps * 15);
        foreach (var item in clones)
        {
            item.Frame -= firstFrame;
            item.Layer = item.Layer - firstLayer + 1;
        }
        return Task.Run(() => GenerateItems(clones, Array.Empty<IVideoEffect>(), false,
            outputPath ?? PresetStorage.GetPreviewVideoPath(presetId, slot), ffmpegPath, thumbnailSeconds,
            fps, width, height, hz, backgroundColor, frameCount, onFrameProgress));
    }

    public static Task<AudioWaveformResult> GenerateAudioWaveformAsync(IReadOnlyCollection<IItem> sourceItems, string? presetId = null, int slot = 1)
    {
        if (sourceItems.Count == 0 || sourceItems.Any(item => item is not IAudioItem))
            throw new InvalidOperationException("音声アイテムだけのテンプレートを指定してください");
        var videoInfo = PresetApplier.GetActiveTimeline()?.VideoInfo;
        int fps = Math.Clamp(ReadInt(videoInfo, "FPS", 30), 1, 60);
        int hz = ReadInt(videoInfo, "Hz", 48000);
        var clones = sourceItems.Select(item => Json.GetClone(item) as IItem ?? throw new InvalidOperationException("音声アイテムを複製できませんでした")).ToList();
        int firstFrame = clones.Min(item => item.Frame);
        int firstLayer = clones.Min(item => item.Layer);
        int lastFrame = clones.Max(item => item.Frame + Math.Max(1, item.Length));
        int frameCount = Math.Clamp(lastFrame - firstFrame, 1, fps * 15);
        foreach (var item in clones)
        {
            item.Frame -= firstFrame;
            item.Layer = item.Layer - firstLayer + 1;
        }
        return Task.Run(() =>
        {
            var info = new VideoInfo { Width = PreviewWidth, Height = PreviewHeight, FPS = fps, Hz = hz, BackgroundColor = Colors.Transparent };
            var scenes = new Scenes(isUndoEnabled: false);
            scenes.CreateScene("AudioTemplateWaveform", 0, info, new TimelineVerticalLine());
            var scene = scenes.AllScenes.FirstOrDefault() ?? throw new InvalidOperationException("波形生成用シーンを作成できませんでした");
            scene.Timeline.TryAddItems(clones.ToArray(), 0, 0);
            string audioPath = RenderAudio(scene, frameCount, fps, hz);
            string? previewAudioPath = string.IsNullOrWhiteSpace(presetId) ? null : PresetStorage.GetPreviewAudioPath(presetId, slot);
            try
            {
                var bitmap = CreateWaveformBitmap(audioPath, PreviewWidth, PreviewHeight);
                if (previewAudioPath != null) WritePcmWaveFile(audioPath, previewAudioPath, hz);
                return new AudioWaveformResult(bitmap, (double)frameCount / fps, previewAudioPath);
            }
            catch
            {
                try { if (previewAudioPath != null && File.Exists(previewAudioPath)) File.Delete(previewAudioPath); } catch { }
                throw;
            }
            finally { try { File.Delete(audioPath); } catch { } }
        });
    }

    public static Task<BitmapSource> GenerateAudioFileWaveformAsync(string audioPath)
    {
        string ffmpegPath = FindFfmpegPath();
        return Task.Run(() =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                WorkingDirectory = Path.GetDirectoryName(ffmpegPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            AddArgument(startInfo, "-v"); AddArgument(startInfo, "error");
            AddArgument(startInfo, "-i"); AddArgument(startInfo, audioPath);
            AddArgument(startInfo, "-t"); AddArgument(startInfo, "10");
            AddArgument(startInfo, "-vn");
            AddArgument(startInfo, "-ac"); AddArgument(startInfo, "2");
            AddArgument(startInfo, "-ar"); AddArgument(startInfo, "24000");
            AddArgument(startInfo, "-f"); AddArgument(startInfo, "f32le");
            AddArgument(startInfo, "pipe:1");

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("FFmpegを起動できませんでした");
            using var buffer = new MemoryStream();
            Task copyTask = process.StandardOutput.BaseStream.CopyToAsync(buffer);
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            Task.WaitAll(copyTask, process.WaitForExitAsync());
            string errors = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || buffer.Length == 0)
                throw new InvalidOperationException($"音声波形を読み込めませんでした (exit {process.ExitCode}): {errors}");
            return CreateWaveformBitmap(buffer.ToArray(), PreviewWidth, PreviewHeight);
        });
    }

    private static void WritePcmWaveFile(string rawAudioPath, string wavePath, int hz)
    {
        byte[] rawAudio = File.ReadAllBytes(rawAudioPath);
        int sampleCount = rawAudio.Length / sizeof(float);
        int dataLength = checked(sampleCount * sizeof(short));
        Directory.CreateDirectory(Path.GetDirectoryName(wavePath)!);
        using var stream = File.Create(wavePath);
        using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(hz);
        writer.Write(checked(hz * 2 * sizeof(short)));
        writer.Write((short)(2 * sizeof(short)));
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        for (int index = 0; index < sampleCount; index++)
        {
            float sample = BitConverter.ToSingle(rawAudio, index * sizeof(float));
            int scaled = (int)Math.Round(Math.Clamp(sample, -1f, 1f) * (sample < 0 ? 32768 : 32767));
            writer.Write((short)Math.Clamp(scaled, short.MinValue, short.MaxValue));
        }
    }

    private static BitmapSource CreateWaveformBitmap(string audioPath, int width, int height)
    {
        byte[] audio = File.ReadAllBytes(audioPath);
        return CreateWaveformBitmap(audio, width, height);
    }

    private static BitmapSource CreateWaveformBitmap(byte[] audio, int width, int height)
    {
        int floatCount = audio.Length / sizeof(float);
        if (floatCount < 2) throw new InvalidOperationException("音声データが空のため波形を生成できません");
        int frameCount = floatCount / 2;
        var pixels = new byte[width * height * 4];
        int center = height / 2;
        for (int x = 0; x < width; x++)
        {
            int start = x * frameCount / width;
            int end = Math.Max(start + 1, (x + 1) * frameCount / width);
            end = Math.Min(end, frameCount);
            float peak = 0;
            for (int frame = start; frame < end; frame++)
            {
                int byteOffset = frame * 2 * sizeof(float);
                float left = BitConverter.ToSingle(audio, byteOffset);
                float right = BitConverter.ToSingle(audio, byteOffset + sizeof(float));
                peak = Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)));
            }
            int amplitude = Math.Clamp((int)(Math.Min(1f, peak) * (height * 0.44f)), 1, center - 1);
            for (int y = center - amplitude; y <= center + amplitude; y++)
            {
                int offset = (y * width + x) * 4;
                pixels[offset] = 255;
                pixels[offset + 1] = 198;
                pixels[offset + 2] = 107;
                pixels[offset + 3] = 255;
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static Task<BitmapSource?> ExtractThumbnailAsync(string videoPath, double seconds)
    {
        string ffmpegPath = FindFfmpegPath();
        return Task.Run(() => ExtractThumbnail(videoPath, seconds, ffmpegPath));
    }

    public static Task<BitmapSource?> GenerateThumbnailAsync(
        IItem sourceItem,
        IReadOnlyCollection<IVideoEffect> selectedEffects,
        double seconds)
    {
        var timeline = PresetApplier.GetActiveTimeline();
        var sourceVideoInfo = timeline?.VideoInfo;
        int fps = Math.Clamp(ReadInt(sourceVideoInfo, "FPS", 30), 1, 60);
        int width = Math.Clamp(ReadInt(sourceVideoInfo, "Width", PreviewWidth), 64, 3840);
        int height = Math.Clamp(ReadInt(sourceVideoInfo, "Height", PreviewHeight), 64, 2160);
        int hz = ReadInt(sourceVideoInfo, "Hz", 48000);
        Color backgroundColor = ReadColor(sourceVideoInfo, "BackgroundColor", Colors.Black);
        int itemLength = Math.Max(1, sourceItem.Length);
        var cloned = Json.GetClone(sourceItem) as IItem ?? throw new InvalidOperationException("選択アイテムを複製できませんでした");
        var clonedEffects = selectedEffects
            .Select(effect => Json.GetClone(effect))
            .Where(effect => effect != null)
            .Select(effect => effect!)
            .ToList();
        double renderSeconds = Math.Clamp(seconds, 0, Math.Max(0, (double)(itemLength - 1) / fps));
        return Task.Run<BitmapSource?>(() => RenderThumbnail(cloned, clonedEffects, renderSeconds, fps, width, height, hz, backgroundColor, itemLength));
    }

    private static BitmapSource RenderThumbnail(IItem cloned, IReadOnlyCollection<IVideoEffect> clonedEffects, double seconds, int fps, int width, int height, int hz, Color backgroundColor, int itemLength)
    {
        var videoInfo = new VideoInfo { Width = width, Height = height, FPS = fps, Hz = hz, BackgroundColor = backgroundColor };
        var scenes = new Scenes(isUndoEnabled: false);
        scenes.CreateScene("EffectPresetThumbnail", 0, videoInfo, new TimelineVerticalLine());
        var scene = scenes.AllScenes.FirstOrDefault() ?? throw new InvalidOperationException("プレビューシーンを作成できませんでした");
        cloned.Frame = 0;
        cloned.Layer = 1;
        cloned.Length = itemLength;
        if (cloned is VoiceItem voiceItem) voiceItem.JimakuVisibility = JimakuVisibility.Custom;
        var effectsProperty = cloned.GetType().GetProperty(cloned is VoiceItem ? "JimakuVideoEffects" : "VideoEffects", BindingFlags.Public | BindingFlags.Instance);
        if (effectsProperty?.CanWrite != true) throw new InvalidOperationException("選択アイテムの映像エフェクトを設定できません");
        foreach (var effect in clonedEffects) effect.IsEnabled = true;
        effectsProperty.SetValue(cloned, ImmutableList.CreateRange(clonedEffects));
        var backgroundItem = PreviewBackgroundItem.Create(backgroundColor, itemLength);
        scene.Timeline.TryAddItems(new IItem[] { backgroundItem, cloned }, 0, 0);
        using var renderer = new TimelineSourceAndDevices(scene);
        renderer.Update(TimeSpan.FromSeconds(seconds), TimelineSourceUsage.Paused);
        var source = renderer.RenderBitmapSource();
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        var bitmap = BitmapSource.Create(width, height, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static EffectPreviewVideoResult Generate(IItem cloned, IReadOnlyCollection<IVideoEffect> clonedEffects, string videoPath, string ffmpegPath, double thumbnailSeconds, int fps, int width, int height, int hz, Color backgroundColor, int frameCount)
    {
        return GenerateItems(new List<IItem> { cloned }, clonedEffects, true, videoPath, ffmpegPath, thumbnailSeconds, fps, width, height, hz, backgroundColor, frameCount, null);
    }

    private static EffectPreviewVideoResult GenerateItems(IReadOnlyList<IItem> clonedItems, IReadOnlyCollection<IVideoEffect> clonedEffects, bool applySelectedEffects, string videoPath, string ffmpegPath, double thumbnailSeconds, int fps, int width, int height, int hz, Color backgroundColor, int frameCount, Action<int>? onFrameProgress)
    {
        int thumbnailFrame = Math.Clamp((int)Math.Round(thumbnailSeconds * fps), 0, frameCount - 1);

        var videoInfo = new VideoInfo
        {
            Width = width,
            Height = height,
            FPS = fps,
            Hz = hz,
            BackgroundColor = backgroundColor
        };

        var scenes = new Scenes(isUndoEnabled: false);
        scenes.CreateScene("EffectPresetPreview", 0, videoInfo, new TimelineVerticalLine());
        var scene = scenes.AllScenes.FirstOrDefault() ?? throw new InvalidOperationException("プレビューシーンを作成できませんでした");
        if (applySelectedEffects)
        {
            var cloned = clonedItems[0];
            cloned.Frame = 0;
            cloned.Layer = 1;
            cloned.Length = frameCount;
            if (cloned is VoiceItem voiceItem) voiceItem.JimakuVisibility = JimakuVisibility.Custom;
            var effectsProperty = cloned.GetType().GetProperty(cloned is VoiceItem ? "JimakuVideoEffects" : "VideoEffects", BindingFlags.Public | BindingFlags.Instance);
            if (effectsProperty?.CanWrite != true) throw new InvalidOperationException("選択アイテムの映像エフェクトを設定できません");
            foreach (var effect in clonedEffects) effect.IsEnabled = true;
            effectsProperty.SetValue(cloned, ImmutableList.CreateRange(clonedEffects));
        }
        var backgroundItem = PreviewBackgroundItem.Create(backgroundColor, frameCount);
        scene.Timeline.TryAddItems(new IItem[] { backgroundItem }.Concat(clonedItems).ToArray(), 0, 0);
        Directory.CreateDirectory(Path.GetDirectoryName(videoPath)!);
        string? audioPath = null;
        if (clonedItems.Any(item => item is IAudioItem))
            audioPath = RenderAudio(scene, frameCount, fps, hz);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };
        AddArgument(startInfo, "-y");
        AddArgument(startInfo, "-f"); AddArgument(startInfo, "rawvideo");
        AddArgument(startInfo, "-pixel_format"); AddArgument(startInfo, "bgra");
        AddArgument(startInfo, "-video_size"); AddArgument(startInfo, $"{width}x{height}");
        AddArgument(startInfo, "-framerate"); AddArgument(startInfo, fps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(startInfo, "-i"); AddArgument(startInfo, "pipe:0");
        if (audioPath != null)
        {
            AddArgument(startInfo, "-f"); AddArgument(startInfo, "f32le");
            AddArgument(startInfo, "-ar"); AddArgument(startInfo, hz.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddArgument(startInfo, "-ac"); AddArgument(startInfo, "2");
            AddArgument(startInfo, "-i"); AddArgument(startInfo, audioPath);
            AddArgument(startInfo, "-map"); AddArgument(startInfo, "0:v:0");
            AddArgument(startInfo, "-map"); AddArgument(startInfo, "1:a:0");
            AddArgument(startInfo, "-c:a"); AddArgument(startInfo, "aac");
            AddArgument(startInfo, "-b:a"); AddArgument(startInfo, "128k");
        }
        else AddArgument(startInfo, "-an");
        AddArgument(startInfo, "-vf"); AddArgument(startInfo, $"scale={PreviewWidth}:{PreviewHeight}:force_original_aspect_ratio=decrease,pad={PreviewWidth}:{PreviewHeight}:(ow-iw)/2:(oh-ih)/2");
        startInfo.WorkingDirectory = Path.GetDirectoryName(ffmpegPath)!;
        // YMM4's MP4 output is H.264 via Media Foundation; the bundled FFmpeg
        // exposes h264_mf, not libx264.
        AddArgument(startInfo, "-c:v"); AddArgument(startInfo, "h264_mf");
        AddArgument(startInfo, "-b:v"); AddArgument(startInfo, "1500k");
        AddArgument(startInfo, "-pix_fmt"); AddArgument(startInfo, "nv12");
        AddArgument(startInfo, "-threads"); AddArgument(startInfo, "1");
        AddArgument(startInfo, "-movflags"); AddArgument(startInfo, "+faststart");
        AddArgument(startInfo, videoPath);

        BitmapSource? thumbnail = null;
        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("FFmpegを起動できませんでした");
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            using var renderer = new TimelineSourceAndDevices(scene);
            var input = process.StandardInput.BaseStream;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            try
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    renderer.Update(TimeSpan.FromSeconds((double)frame / fps), TimelineSourceUsage.Paused);
                    var bitmap = renderer.RenderBitmapSource();
                    var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                    converted.CopyPixels(pixels, stride, 0);
                    input.Write(pixels, 0, pixels.Length);

                    if (frame == thumbnailFrame)
                    {
                        thumbnail = BitmapSource.Create(width, height, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null, (byte[])pixels.Clone(), stride);
                        thumbnail.Freeze();
                    }
                    if (onFrameProgress != null && (frame % 5 == 0 || frame == frameCount - 1))
                        onFrameProgress(frame + 1);
                }
                process.StandardInput.Close();
                process.WaitForExit();
                string errors = errorTask.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException($"FFmpegが動画を出力できませんでした (exit {process.ExitCode}): {errors}");
            }
            catch (Exception ex) when (ex is not InvalidOperationException || !ex.Message.StartsWith("FFmpegが動画を出力できませんでした", StringComparison.Ordinal))
            {
                try { process.StandardInput.Close(); } catch { }
                try { if (!process.WaitForExit(5000)) process.Kill(true); } catch { }
                string errors = errorTask.IsCompletedSuccessfully ? errorTask.Result : "FFmpegの詳細出力を取得できませんでした";
                throw new InvalidOperationException($"プレビュー動画生成に失敗しました (exit {(process.HasExited ? process.ExitCode : -1)}): {errors}", ex);
            }
        }
        catch
        {
            try { if (File.Exists(videoPath)) File.Delete(videoPath); } catch { }
            throw;
        }
        finally
        {
            if (audioPath != null) try { File.Delete(audioPath); } catch { }
        }

        return new EffectPreviewVideoResult(videoPath, thumbnail, frameCount, fps);
    }

    private static string RenderAudio(Scene scene, int frameCount, int fps, int hz)
    {
        var audioTimelineType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("YukkuriMovieMaker.Player.Audio.TimelineSource", false))
            .FirstOrDefault(type => type != null);
        if (audioTimelineType == null)
            audioTimelineType = Assembly.Load("YukkuriMovieMaker.Plugin").GetType("YukkuriMovieMaker.Player.Audio.TimelineSource", true);
        var source = Activator.CreateInstance(audioTimelineType!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { scene }, null)
            ?? throw new InvalidOperationException("YMM4の仮想タイムライン音声ソースを作成できませんでした");
        var sourceType = source.GetType();
        sourceType.GetMethod("Seek", new[] { typeof(TimeSpan) })?.Invoke(source, new object[] { TimeSpan.Zero });
        var read = sourceType.GetMethod("Read", new[] { typeof(float[]), typeof(int), typeof(int) })
            ?? throw new InvalidOperationException("YMM4の音声読み込みAPIが見つかりません");
        long targetSamples = (long)Math.Ceiling((double)frameCount / fps * hz) * 2;
        const int chunkSize = 8192;
        var samples = new float[chunkSize];
        string audioPath = Path.Combine(Path.GetTempPath(), $"ymm4-preview-audio-{Guid.NewGuid():N}.f32le");
        try
        {
            using (source as IDisposable)
            using (var stream = new FileStream(audioPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
            using (var writer = new BinaryWriter(stream))
            {
                long written = 0;
                while (written < targetSamples)
                {
                    int count = (int)Math.Min(chunkSize, targetSamples - written);
                    int readCount = (int)(read.Invoke(source, new object[] { samples, 0, count }) ?? 0);
                    if (readCount > 0)
                    {
                        for (int i = 0; i < readCount; i++) writer.Write(samples[i]);
                        written += readCount;
                    }
                    if (readCount < count)
                    {
                        for (int i = readCount; i < count; i++) writer.Write(0f);
                        written += count - readCount;
                    }
                }
            }
            return audioPath;
        }
        catch
        {
            try { if (File.Exists(audioPath)) File.Delete(audioPath); } catch { }
            throw;
        }
    }

    private static BitmapSource? ExtractThumbnail(string videoPath, double seconds, string ffmpegPath)
    {
        if (!File.Exists(videoPath)) return null;
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            WorkingDirectory = Path.GetDirectoryName(ffmpegPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        AddArgument(startInfo, "-v"); AddArgument(startInfo, "error");
        AddArgument(startInfo, "-ss"); AddArgument(startInfo, Math.Max(0, seconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(startInfo, "-i"); AddArgument(startInfo, videoPath);
        AddArgument(startInfo, "-frames:v"); AddArgument(startInfo, "1");
        AddArgument(startInfo, "-f"); AddArgument(startInfo, "image2pipe");
        AddArgument(startInfo, "-vcodec"); AddArgument(startInfo, "png");
        AddArgument(startInfo, "pipe:1");

        using var process = Process.Start(startInfo);
        if (process == null) return null;
        using var buffer = new MemoryStream();
        Task copyTask = process.StandardOutput.BaseStream.CopyToAsync(buffer);
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        Task.WaitAll(copyTask, process.WaitForExitAsync());
        if (process.ExitCode != 0 || buffer.Length == 0)
        {
            System.Diagnostics.Debug.WriteLine($"Thumbnail extraction failed: {errorTask.GetAwaiter().GetResult()}");
            return null;
        }
        buffer.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = buffer;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static int ReadInt(object? source, string propertyName, int fallback)
    {
        try
        {
            var value = source?.GetType().GetProperty(propertyName)?.GetValue(source);
            return value is int result ? result : fallback;
        }
        catch { return fallback; }
    }

    private static Color ReadColor(object? source, string propertyName, Color fallback)
    {
        try
        {
            var value = source?.GetType().GetProperty(propertyName)?.GetValue(source);
            return value is Color result ? result : fallback;
        }
        catch { return fallback; }
    }

    private static string FindFfmpegPath()
    {
        string? userDirectory = null;
        try
        {
            var locatorType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("YukkuriMovieMaker.Plugin.FileSource.FFmpeg.FFmpegResourceLocator", false))
                .FirstOrDefault(type => type != null);
            locatorType ??= Assembly.Load("YukkuriMovieMaker.Plugin.FileSource.FFmpeg")
                .GetType("YukkuriMovieMaker.Plugin.FileSource.FFmpeg.FFmpegResourceLocator", false);
            userDirectory = locatorType?.GetMethod("GetUserFFmpegDirectory", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null) as string;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"YMM4 FFmpeg resource locator failed: {ex}");
        }

        string? ymmRoot = Path.GetDirectoryName(typeof(Scenes).Assembly.Location);
        userDirectory ??= string.IsNullOrWhiteSpace(ymmRoot) ? null : Path.Combine(ymmRoot, "user", "resources", "ffmpeg");
        string? userPath = string.IsNullOrWhiteSpace(userDirectory) ? null : Path.Combine(userDirectory, "ffmpeg.exe");
        if (!string.IsNullOrWhiteSpace(userPath) && File.Exists(userPath)) return userPath;

        string expected = userPath ?? "YMM4フォルダ\\user\\resources\\ffmpeg\\ffmpeg.exe";
        string message = $"YMM4用のFFmpegが見つかりませんでした。\n\nYMM4で一度動画を書き出してから、もう一度お試しください。\n\nFFmpegの確認場所:\n{expected}";
        var application = Application.Current;
        if (application?.Dispatcher != null && !application.Dispatcher.CheckAccess())
            application.Dispatcher.Invoke(() => MessageBox.Show(application.MainWindow, message, "FFmpegが見つかりません", MessageBoxButton.OK, MessageBoxImage.Information));
        else
            MessageBox.Show(application?.MainWindow, message, "FFmpegが見つかりません", MessageBoxButton.OK, MessageBoxImage.Information);
        throw new FileNotFoundException("YMM4付属のFFmpegが見つかりません", expected);
    }

    private static void AddArgument(ProcessStartInfo startInfo, string argument) => startInfo.ArgumentList.Add(argument);
}
