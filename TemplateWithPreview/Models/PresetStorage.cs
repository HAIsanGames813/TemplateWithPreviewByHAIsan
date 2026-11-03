using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Project.Items;

namespace TemplateWithPreview.Models;

public static class PresetStorage
{
    public sealed class PreviewGenerationCheckpoint
    {
        public string PresetId { get; set; } = "";
        public int Slot { get; set; }
        public string PresetName { get; set; } = "";
        public int CompletedFrames { get; set; }
        public int TotalFrames { get; set; }
        public string PartialVideoPath { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    private static readonly string LegacyStorageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YukkuriMovieMaker4",
        "PreviewTextPreset"
    );

    private static readonly string YmmUserDirectory = ResolveYmmUserDirectory();
    private static readonly string StorageDirectory = Path.Combine(YmmUserDirectory, "resources", "TemplateWithPreview");
    private static readonly string StorageFilePath = Path.Combine(StorageDirectory, "presets.json");
    private static readonly string PreviewDirectorySettingPath = Path.Combine(StorageDirectory, "preview-directory.txt");
    private static readonly string PreviewGenerationCheckpointPath = Path.Combine(StorageDirectory, "preview-generation-checkpoint.json");
    private static readonly string PluginSettingsPath = Path.Combine(StorageDirectory, "settings.json");
    private static readonly string LibraryAssetsPath = Path.Combine(StorageDirectory, "library-assets.json");
    public static string LibraryThumbnailDirectory => Path.Combine(StorageDirectory, "LibraryThumbnails");
    private static string? _customPreviewDirectory = LoadPreviewDirectorySetting();

    static PresetStorage() => MigrateLegacyStorage();

    public static string PreviewImageDirectory => string.IsNullOrWhiteSpace(_customPreviewDirectory)
        ? Path.Combine(StorageDirectory, "Previews")
        : _customPreviewDirectory;

    public static void SetPreviewImageDirectory(string directory)
    {
        string normalized = Path.GetFullPath(directory);
        Directory.CreateDirectory(normalized);
        Directory.CreateDirectory(StorageDirectory);
        File.WriteAllText(PreviewDirectorySettingPath, normalized);
        _customPreviewDirectory = normalized;
    }

    public static PreviewPluginSettings LoadPluginSettings()
    {
        try
        {
            if (!File.Exists(PluginSettingsPath)) return new PreviewPluginSettings();
            return Json.Load<PreviewPluginSettings>(PluginSettingsPath) ?? new PreviewPluginSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load plugin settings: {ex}");
            return new PreviewPluginSettings();
        }
    }

    public static bool SavePluginSettings(PreviewPluginSettings settings)
    {
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            Json.Save(settings, PluginSettingsPath);
            return File.Exists(PluginSettingsPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save plugin settings: {ex}");
            return false;
        }
    }

    public static List<LibraryAsset> LoadLibraryAssets()
    {
        try { return File.Exists(LibraryAssetsPath) ? Json.Load<List<LibraryAsset>>(LibraryAssetsPath) ?? new() : new(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to load media library: {ex}"); return new(); }
    }

    public static bool SaveLibraryAssets(IEnumerable<LibraryAsset> assets)
    {
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            Json.Save(assets.ToList(), LibraryAssetsPath);
            return File.Exists(LibraryAssetsPath);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save media library: {ex}"); return false; }
    }

    public static string SaveLibraryThumbnail(string id, BitmapSource image)
    {
        Directory.CreateDirectory(LibraryThumbnailDirectory);
        string path = Path.Combine(LibraryThumbnailDirectory, $"{id}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    public static void SavePreviewGenerationCheckpoint(PreviewGenerationCheckpoint checkpoint)
    {
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            checkpoint.UpdatedAt = DateTime.Now;
            Json.Save(checkpoint, PreviewGenerationCheckpointPath);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save preview generation checkpoint: {ex}"); }
    }

    public static PreviewGenerationCheckpoint? LoadPreviewGenerationCheckpoint()
    {
        try { return File.Exists(PreviewGenerationCheckpointPath) ? Json.Load<PreviewGenerationCheckpoint>(PreviewGenerationCheckpointPath) : null; }
        catch { return null; }
    }

    public static void ClearPreviewGenerationCheckpoint()
    {
        try { if (File.Exists(PreviewGenerationCheckpointPath)) File.Delete(PreviewGenerationCheckpointPath); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to clear preview generation checkpoint: {ex}"); }
    }

    public static void RelocatePreviewGenerationCheckpoint(string oldDirectory, string newDirectory)
    {
        var checkpoint = LoadPreviewGenerationCheckpoint();
        if (checkpoint == null || string.IsNullOrWhiteSpace(checkpoint.PartialVideoPath)) return;
        try
        {
            string oldRoot = Path.GetFullPath(oldDirectory);
            string partialPath = Path.GetFullPath(checkpoint.PartialVideoPath);
            string relative = Path.GetRelativePath(oldRoot, partialPath);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)) return;
            string newPath = Path.Combine(Path.GetFullPath(newDirectory), relative);
            if (!File.Exists(newPath)) return;
            checkpoint.PartialVideoPath = newPath;
            SavePreviewGenerationCheckpoint(checkpoint);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not relocate preview checkpoint: {ex}"); }
    }

    private static string? LoadPreviewDirectorySetting()
    {
        try
        {
            if (!File.Exists(PreviewDirectorySettingPath)) return null;
            string path = File.ReadAllText(PreviewDirectorySettingPath).Trim();
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch { return null; }
    }

    private static string ResolveYmmUserDirectory()
    {
        try
        {
            var locatorType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("YukkuriMovieMaker.Plugin.FileSource.FFmpeg.FFmpegResourceLocator", false))
                .FirstOrDefault(type => type != null);
            locatorType ??= Assembly.Load("YukkuriMovieMaker.Plugin.FileSource.FFmpeg")
                .GetType("YukkuriMovieMaker.Plugin.FileSource.FFmpeg.FFmpegResourceLocator", false);
            string? ffmpegDirectory = locatorType?.GetMethod("GetUserFFmpegDirectory", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null) as string;
            if (!string.IsNullOrWhiteSpace(ffmpegDirectory))
            {
                var ffmpeg = new DirectoryInfo(Path.GetFullPath(ffmpegDirectory));
                var userDirectory = string.Equals(ffmpeg.Name, "ffmpeg", StringComparison.OrdinalIgnoreCase)
                    ? ffmpeg.Parent?.Parent
                    : null;
                if (userDirectory != null && string.Equals(userDirectory.Name, "user", StringComparison.OrdinalIgnoreCase))
                    return userDirectory.FullName;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not locate the active YMM4 user folder through FFmpeg settings: {ex}"); }

        try
        {
            var ymmAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "YukkuriMovieMaker", StringComparison.OrdinalIgnoreCase));
            string? installDirectory = ymmAssembly == null ? null : Path.GetDirectoryName(ymmAssembly.Location);
            if (!string.IsNullOrWhiteSpace(installDirectory)) return Path.Combine(installDirectory, "user");
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not locate the YMM4 installation folder: {ex}"); }

        return Path.Combine(AppContext.BaseDirectory, "user");
    }

    private static void MigrateLegacyStorage()
    {
        try
        {
            if (!Directory.Exists(LegacyStorageDirectory)) return;
            Directory.CreateDirectory(StorageDirectory);

            string[] files = { "presets.json", "text-categories.json", "fx-categories.json", "template-categories.json", "preview-generation-checkpoint.json" };
            foreach (string file in files)
            {
                string oldPath = Path.Combine(LegacyStorageDirectory, file);
                string newPath = Path.Combine(StorageDirectory, file);
                if (!File.Exists(newPath) && File.Exists(oldPath)) File.Copy(oldPath, newPath);
            }

            if (File.Exists(StorageFilePath))
            {
                var presets = Json.Load<List<TextPreset>>(StorageFilePath);
                if (presets == null) return;

                bool changed = false;
                Directory.CreateDirectory(PreviewImageDirectory);
                foreach (var preset in presets)
                foreach (var pattern in preset.Patterns ?? new List<PresetPattern>())
                {
                    string? image = MigratePreviewReference(pattern.PreviewImagePath, preset.Id, pattern.Slot);
                    string? video = MigratePreviewReference(pattern.PreviewVideoPath, preset.Id, pattern.Slot);
                    string? audio = MigratePreviewReference(pattern.PreviewAudioPath, preset.Id, pattern.Slot);
                    if (image != pattern.PreviewImagePath) { pattern.PreviewImagePath = image; changed = true; }
                    if (video != pattern.PreviewVideoPath) { pattern.PreviewVideoPath = video; changed = true; }
                    if (audio != pattern.PreviewAudioPath) { pattern.PreviewAudioPath = audio; changed = true; }
                }

                if (changed) Json.Save(presets, StorageFilePath);
            }

            MigrateCheckpoint();
            Directory.Delete(LegacyStorageDirectory, recursive: true);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not migrate legacy TemplateWithPreview data: {ex}"); }
    }

    private static string? MigratePreviewReference(string? oldPath, string presetId, int slot)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || !File.Exists(oldPath)) return oldPath;
        string extension = Path.GetExtension(oldPath);
        string newPath = Path.Combine(PreviewImageDirectory, $"{presetId}_P{slot}{extension}");
        if (!string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            File.Copy(oldPath, newPath, overwrite: true);
        return newPath;
    }

    private static void MigrateCheckpoint()
    {
        var checkpoint = LoadPreviewGenerationCheckpoint();
        if (checkpoint == null || string.IsNullOrWhiteSpace(checkpoint.PartialVideoPath) || !File.Exists(checkpoint.PartialVideoPath)) return;
        string newPath = Path.Combine(PreviewImageDirectory, $"{checkpoint.PresetId}_P{checkpoint.Slot}.partial.mp4");
        if (!string.Equals(Path.GetFullPath(checkpoint.PartialVideoPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(checkpoint.PartialVideoPath, newPath, overwrite: true);
            checkpoint.PartialVideoPath = newPath;
            SavePreviewGenerationCheckpoint(checkpoint);
        }
    }

    public static List<string> LoadCategories(bool forEffects, bool forTemplates = false)
    {
        try
        {
            string path = Path.Combine(StorageDirectory, forTemplates ? "template-categories.json" : forEffects ? "fx-categories.json" : "text-categories.json");
            return File.Exists(path) ? Json.Load<List<string>>(path)?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new() : new();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static void SaveCategories(bool forEffects, List<string> categories, bool forTemplates = false)
    {
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            string path = Path.Combine(StorageDirectory, forTemplates ? "template-categories.json" : forEffects ? "fx-categories.json" : "text-categories.json");
            Json.Save(categories, path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save categories: {ex}");
        }
    }

    public static string SavePreviewImage(string presetId, int slot, BitmapSource image)
    {
        string directory = PreviewImageDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{presetId}_P{slot}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    public static string GetPreviewVideoPath(string presetId, int slot)
    {
        Directory.CreateDirectory(PreviewImageDirectory);
        return Path.Combine(PreviewImageDirectory, $"{presetId}_P{slot}.mp4");
    }

    public static string GetPreviewAudioPath(string presetId, int slot)
    {
        Directory.CreateDirectory(PreviewImageDirectory);
        return Path.Combine(PreviewImageDirectory, $"{presetId}_P{slot}.wav");
    }

    public static int DeleteUnusedPreviewData(IEnumerable<TextPreset> presets)
    {
        string directory = Path.GetFullPath(PreviewImageDirectory);
        if (!Directory.Exists(directory)) return 0;
        var referenced = presets.SelectMany(preset => preset.Patterns)
            .SelectMany(pattern => new[] { pattern.PreviewImagePath, pattern.PreviewVideoPath, pattern.PreviewAudioPath })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checkpoint = LoadPreviewGenerationCheckpoint();
        if (!string.IsNullOrWhiteSpace(checkpoint?.PartialVideoPath))
            referenced.Add(Path.GetFullPath(checkpoint.PartialVideoPath));

        int removed = 0;
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            string fullPath = Path.GetFullPath(file);
            if (referenced.Contains(fullPath)) continue;
            try { File.Delete(fullPath); removed++; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not remove unused preview file '{Path.GetFileName(file)}': {ex.Message}"); }
        }
        return removed;
    }

    public static List<TextPreset> LoadPresets()
    {
        try
        {
            if (!File.Exists(StorageFilePath))
            {
                var defaults = CreateDefaultPresets();
                SavePresets(defaults);
                return defaults;
            }

            var presets = Json.Load<List<TextPreset>>(StorageFilePath);
            if (presets != null)
            {
                foreach (var p in presets)
                {
                    p.EnsurePatternsInitialized();
                }
                return presets;
            }
            return CreateDefaultPresets();
        }
        catch (Exception)
        {
            return CreateDefaultPresets();
        }
    }

    public static bool SavePresets(List<TextPreset> presets)
    {
        try
        {
            if (!Directory.Exists(StorageDirectory))
            {
                Directory.CreateDirectory(StorageDirectory);
            }
            Json.Save(presets, StorageFilePath);
            return File.Exists(StorageFilePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save presets: {ex}");
            return false;
        }
    }

    public static string ExportPresetJson(TextPreset preset)
    {
        return Json.GetJsonText(preset);
    }

    public static string ExportPresetsJson(IEnumerable<TextPreset> presets)
    {
        return Json.GetJsonText(presets.ToList());
    }

    public static TextPreset? ImportPresetJson(string json)
    {
        try
        {
            return Json.LoadFromText<TextPreset>(json);
        }
        catch
        {
            return null;
        }
    }

    public static List<TextPreset> ImportPresetsJson(string json)
    {
        try
        {
            var presets = Json.LoadFromText<List<TextPreset>>(json);
            if (presets != null) return presets;
        }
        catch { }

        var preset = ImportPresetJson(json);
        return preset == null ? new List<TextPreset>() : new List<TextPreset> { preset };
    }

    private static List<TextPreset> CreateDefaultPresets()
    {
        return new List<TextPreset>
        {
            new TextPreset
            {
                Id = "preset_default_1",
                Name = "白文字＋黒縁取り",
                Category = "基本",
                Apply = new ApplyFlags { Effects = true, Font = true, Position = false },
                Font = new FontPresetData
                {
                    FontName = "メイリオ",
                    FontColor = System.Windows.Media.Colors.White,
                    Style = Style.Border,
                    StyleColor = System.Windows.Media.Colors.Black,
                    Bold = true,
                }
            },
            new TextPreset
            {
                Id = "preset_default_2",
                Name = "黄色太字＋下部配置",
                Category = "テロップ",
                Apply = new ApplyFlags { Effects = false, Font = true, Position = true },
                Font = new FontPresetData
                {
                    FontName = "メイリオ",
                    FontColor = System.Windows.Media.Color.FromRgb(255, 220, 0),
                    Style = Style.Border,
                    StyleColor = System.Windows.Media.Colors.Black,
                    Bold = true,
                },
                Position = new PositionPresetData
                {
                    Y = new YukkuriMovieMaker.Commons.Animation(400.0)
                }
            }
        };
    }
}
