using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using TemplateWithPreview.Models;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace TemplateWithPreview;

public static class PresetApplier
{
    public sealed record YmmTemplateInfo(object Instance, string Name, string Group, List<IItem> Items);

    public static string GetLocalizedTemplateName(YmmTemplateInfo template)
    {
        string name = template.Name?.Trim() ?? "";
        var nameParts = SplitTemplatePath(name);
        if (nameParts.Length > 1) name = nameParts[^1];
        if (!string.IsNullOrWhiteSpace(name) && !Guid.TryParse(name, out _) && !template.Items.Any(item => string.Equals(name, item.GetType().Name, StringComparison.OrdinalIgnoreCase)))
            return name;
        var itemNames = template.Items.Select(GetLocalizedItemDisplayName).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        return itemNames.Count > 0 ? string.Join("＋", itemNames) : "YMM4テンプレート";
    }

    public static string GetLocalizedTemplateCategory(YmmTemplateInfo template)
    {
        var nameParts = SplitTemplatePath(template.Name);
        if (nameParts.Length > 1) return nameParts[0];

        var groupParts = GetTemplateGroupParts(template);
        if (groupParts.Length > 1) return groupParts[0];

        if (template.Items.Select(item => item.GetType()).Distinct().Skip(1).Any()) return "";

        try
        {
            var group = template.Instance.GetType().GetProperty("Group")?.GetValue(template.Instance);
            if (group != null && group.GetType().IsEnum)
            {
                var field = group.GetType().GetField(group.ToString() ?? "");
                foreach (var attribute in field?.GetCustomAttributes(false) ?? Array.Empty<object>())
                {
                    if (attribute is System.ComponentModel.DisplayNameAttribute display && !string.IsNullOrWhiteSpace(display.DisplayName)) return display.DisplayName;
                    var resourceType = attribute.GetType().GetProperty("ResourceType")?.GetValue(attribute) as Type;
                    var resourceName = attribute.GetType().GetProperty("Name")?.GetValue(attribute) as string;
                    if (resourceType?.GetProperty(resourceName ?? "", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is string localized && !string.IsNullOrWhiteSpace(localized)) return localized;
                }
            }
        }
        catch { }

        return template.Group switch
        {
            "None" => "その他",
            "Text" => "テキスト",
            "Voice" => "ボイス",
            "Audio" => "音声",
            "Video" => "動画",
            "Image" => "画像",
            "Shape" => "図形",
            _ => string.IsNullOrWhiteSpace(template.Group) ? "テンプレート" : template.Group
        };
    }

    public static string GetLocalizedTemplateTags(YmmTemplateInfo template)
    {
        var nameParts = SplitTemplatePath(template.Name);
        if (nameParts.Length > 1)
            return string.Join(", ", nameParts.Skip(1).Take(nameParts.Length - 2).Distinct(StringComparer.CurrentCultureIgnoreCase));

        var groupParts = GetTemplateGroupParts(template);
        return groupParts.Length > 1 ? string.Join(", ", groupParts.Skip(1).Distinct(StringComparer.CurrentCultureIgnoreCase)) : "";
    }

    private static string[] SplitTemplatePath(string? path) =>
        (path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] GetTemplateGroupParts(YmmTemplateInfo template)
    {
        string groupPath = template.Instance.GetType().GetProperty("Group")?.GetValue(template.Instance)?.ToString() ?? template.Group;
        return SplitTemplatePath(groupPath);
    }

    public static string GetLocalizedItemDisplayName(IItem item)
    {
        var type = item.GetType();
        foreach (var attribute in type.GetCustomAttributes(false))
        {
            if (attribute is System.ComponentModel.DisplayNameAttribute display && !string.IsNullOrWhiteSpace(display.DisplayName)) return display.DisplayName;
            var resourceType = attribute.GetType().GetProperty("ResourceType")?.GetValue(attribute) as Type;
            var resourceName = attribute.GetType().GetProperty("Name")?.GetValue(attribute) as string;
            if (resourceType?.GetProperty(resourceName ?? "", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is string localized && !string.IsNullOrWhiteSpace(localized)) return localized;
        }
        return type.Name switch
        {
            "TextItem" => "テキスト",
            "VoiceItem" => "ボイス",
            "AudioItem" => "音声",
            "VideoItem" => "動画",
            "ImageItem" => "画像",
            "ShapeItem" => "図形",
            "GroupItem" => "グループ",
            _ => type.Name
        };
    }

    public static List<YmmTemplateInfo> GetYmmItemTemplates()
    {
        try
        {
            var settingsType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.Name == "ItemSettings");
            if (settingsType == null) return new();
            var settingsBase = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.IsGenericTypeDefinition && t.Name.StartsWith("SettingsBase`", StringComparison.Ordinal));
            var defaultSettings = settingsBase?.MakeGenericType(settingsType).GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var templatesValue = defaultSettings?.GetType().GetProperty("Templates", BindingFlags.Public | BindingFlags.Instance)?.GetValue(defaultSettings) as System.Collections.IEnumerable;
            if (templatesValue == null) return new();
            var result = new List<YmmTemplateInfo>();
            foreach (var instance in templatesValue)
            {
                if (instance == null) continue;
                var type = instance.GetType();
                var items = type.GetProperty("Items")?.GetValue(instance) as System.Collections.IEnumerable;
                if (items == null) continue;
                var itemList = items.OfType<IItem>().ToList();
                if (itemList.Count == 0) continue;
                result.Add(new YmmTemplateInfo(instance,
                    type.GetProperty("Name")?.GetValue(instance)?.ToString() ?? "YMM4テンプレート",
                    type.GetProperty("Group")?.GetValue(instance)?.ToString() ?? "テンプレート", itemList));
            }
            return result;
        }
        catch (Exception ex) { Debug.WriteLine($"[PresetApplier] GetYmmItemTemplates failed: {ex}"); return new(); }
    }

    public static string ApplyItemTemplateToSelected(string name, IReadOnlyCollection<IItem> items)
    {
        try
        {
            if (items.Count == 0) return "テンプレートにアイテムがありません";
            var timeline = GetActiveTimeline();
            if (timeline == null) return "アクティブなタイムラインがありません";
            var mainVM = GetActiveMainViewModel();
            var model = mainVM?.GetType().GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mainVM);
            if (model == null) return "YMM4のプロジェクトモデルを取得できません";
            var templateType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("YukkuriMovieMaker.Settings.ItemTemplate", false)).FirstOrDefault(t => t != null);
            if (templateType == null) return "YMM4のItemTemplate型が見つかりません";
            var ctor = templateType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault(c => c.GetParameters().Any(p => p.ParameterType == typeof(IEnumerable<IItem>) || typeof(IEnumerable<IItem>).IsAssignableFrom(p.ParameterType)));
            if (ctor == null) return "YMM4テンプレートを作成できません";
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.ParameterType == typeof(string)) args[i] = name;
                else if (p.ParameterType == typeof(IEnumerable<IItem>) || typeof(IEnumerable<IItem>).IsAssignableFrom(p.ParameterType)) args[i] = items;
                else if (p.ParameterType.IsInstanceOfType(timeline.VideoInfo)) args[i] = timeline.VideoInfo;
                else if (p.ParameterType == typeof(Guid)) args[i] = Guid.Empty;
                else if (p.ParameterType.IsEnum) args[i] = Enum.GetValues(p.ParameterType).GetValue(0);
                else args[i] = p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);
            }
            var template = ctor.Invoke(args);
            var add = model.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == "AddTemplateItemAsync");
            if (add == null) return "YMM4のテンプレート適用APIが見つかりません";
            int currentFrame = timeline.CurrentFrame;
            int selectedLayer = timeline.SelectedItem?.Layer ?? 0;
            bool assignedFrame = false;
            var callArgs = add.GetParameters().Select(p => p.ParameterType == typeof(int)
                ? (object)GetTimelineIntArgument(p, currentFrame, selectedLayer, ref assignedFrame)
                : p.ParameterType.IsInstanceOfType(template) ? template
                : p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)).ToArray();
            var task = add.Invoke(model, callArgs) as Task;
            task?.GetAwaiter().GetResult();
            return $"「{name}」をタイムラインに適用しました";
        }
        catch (Exception ex) { Debug.WriteLine($"[PresetApplier] ApplyItemTemplateToSelected failed: {ex}"); return $"テンプレート適用に失敗しました: {ex.GetBaseException().Message}"; }
    }

    public static string AddMediaFileToTimeline(string path, string assetType, double volumePercent = 100)
    {
        try
        {
            string typeName = assetType is "BGM" or "SE" ? "AudioItem" : assetType == "動画" ? "VideoItem" : "ImageItem";
            var itemType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType($"YukkuriMovieMaker.Project.Items.{typeName}", false))
                .FirstOrDefault(type => type != null && typeof(IItem).IsAssignableFrom(type))
                ?? throw new InvalidOperationException($"YMM4の{typeName}型が見つかりません");

            IItem? item = CreateDefaultMediaItem(typeName, path);
            foreach (var constructor in item == null ? itemType.GetConstructors()
                .OrderByDescending(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(string) &&
                    (parameter.Name?.Contains("path", StringComparison.OrdinalIgnoreCase) == true || parameter.Name?.Contains("file", StringComparison.OrdinalIgnoreCase) == true || parameter.Name?.Contains("source", StringComparison.OrdinalIgnoreCase) == true)))
                .ThenBy(constructor => constructor.GetParameters().Length) : Enumerable.Empty<ConstructorInfo>())
            {
                try
                {
                    var parameters = constructor.GetParameters();
                    var args = new object?[parameters.Length];
                    var pathParameter = parameters.FirstOrDefault(parameter => parameter.ParameterType == typeof(string) &&
                        (parameter.Name?.Contains("path", StringComparison.OrdinalIgnoreCase) == true ||
                         parameter.Name?.Contains("file", StringComparison.OrdinalIgnoreCase) == true ||
                         parameter.Name?.Contains("source", StringComparison.OrdinalIgnoreCase) == true));
                    if (pathParameter == null && parameters.Count(parameter => parameter.ParameterType == typeof(string)) == 1)
                        pathParameter = parameters.First(parameter => parameter.ParameterType == typeof(string));
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var parameter = parameters[i];
                        if (ReferenceEquals(parameter, pathParameter))
                        {
                            args[i] = path;
                        }
                        else if (parameter.HasDefaultValue) args[i] = parameter.DefaultValue;
                        else if (parameter.ParameterType.IsEnum) args[i] = Enum.GetValues(parameter.ParameterType).GetValue(0);
                        else if (parameter.ParameterType.IsValueType) args[i] = Activator.CreateInstance(parameter.ParameterType);
                        else args[i] = null;
                    }
                    if (constructor.Invoke(args) is IItem created && (pathParameter != null || TrySetMediaPath(created, path)))
                    {
                        item = created;
                        break;
                    }
                }
                catch { }
            }

            if (item == null && Activator.CreateInstance(itemType) is IItem defaultItem && TrySetMediaPath(defaultItem, path))
                item = defaultItem;
            if (item == null) throw new InvalidOperationException($"{typeName}にファイルパスを設定できませんでした");
            if (assetType == "画像")
            {
                int defaultLength = GetDefaultImageItemLength();
                if (defaultLength > 0) SetItemLengthFrames(item, defaultLength);
            }
            else if (assetType is "BGM" or "SE" or "動画")
            {
                double durationSeconds = ProbeMediaDurationSeconds(path);
                if (durationSeconds > 0) SetItemLength(item, durationSeconds, GetTimelineFps());
                TrySetVolumePercent(item, volumePercent);
            }
            return ApplyItemTemplateToSelected(Path.GetFileNameWithoutExtension(path), new[] { item });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PresetApplier] AddMediaFileToTimeline failed: {ex}");
            return $"素材をタイムラインに追加できませんでした: {ex.GetBaseException().Message}";
        }
    }

    private static void SetItemLength(IItem item, double durationSeconds, double fps)
    {
        var property = item.GetType().GetProperty("Length", BindingFlags.Public | BindingFlags.Instance);
        if (property?.CanWrite != true) return;
        int frames = Math.Max(1, (int)Math.Ceiling(durationSeconds * fps));
        SetItemLengthFrames(item, frames, property);
    }

    private static IItem? CreateDefaultMediaItem(string typeName, string path)
    {
        try
        {
            var settingsType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => { try { return assembly.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(type => type.FullName == "YukkuriMovieMaker.Settings.ItemSettings");
            var settingsBase = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => { try { return assembly.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(type => type.IsGenericTypeDefinition && type.Name.StartsWith("SettingsBase`", StringComparison.Ordinal));
            if (settingsType == null || settingsBase == null) return null;
            var settings = settingsBase.MakeGenericType(settingsType)
                .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var defaults = settings?.GetType().GetProperty("DefaultItems", BindingFlags.Public | BindingFlags.Instance)?.GetValue(settings);
            var defaultItem = defaults?.GetType().GetProperty(typeName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(defaults) as IItem;
            if (defaultItem == null) return null;
            var clone = defaultItem.GetClone();
            return TrySetMediaPath(clone, path) ? clone : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PresetApplier] Could not clone YMM4 default {typeName}: {ex}");
            return null;
        }
    }

    private static void SetItemLengthFrames(IItem item, int frames, PropertyInfo? knownProperty = null)
    {
        var property = knownProperty ?? item.GetType().GetProperty("Length", BindingFlags.Public | BindingFlags.Instance);
        if (property?.CanWrite != true) return;
        frames = Math.Max(1, frames);
        if (property.PropertyType == typeof(int)) property.SetValue(item, frames);
        else if (TryConvertNumber(frames, property.PropertyType, out var converted)) property.SetValue(item, converted);
    }

    private static int GetDefaultImageItemLength()
    {
        try
        {
            var settingsType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => { try { return assembly.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(type => type.FullName == "YukkuriMovieMaker.Settings.ItemSettings");
            var settingsBase = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => { try { return assembly.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(type => type.IsGenericTypeDefinition && type.Name.StartsWith("SettingsBase`", StringComparison.Ordinal));
            if (settingsType == null || settingsBase == null) return 0;
            var defaultSettings = settingsBase.MakeGenericType(settingsType)
                .GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var defaultItems = defaultSettings?.GetType().GetProperty("DefaultItems", BindingFlags.Public | BindingFlags.Instance)?.GetValue(defaultSettings);
            var imageItem = defaultItems?.GetType().GetProperty("ImageItem", BindingFlags.Public | BindingFlags.Instance)?.GetValue(defaultItems);
            var length = imageItem?.GetType().GetProperty("Length", BindingFlags.Public | BindingFlags.Instance)?.GetValue(imageItem);
            return length is IConvertible convertible ? convertible.ToInt32(System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PresetApplier] Could not read YMM4 default ImageItem length: {ex}");
            return 0;
        }
    }

    private static double GetTimelineFps()
    {
        try
        {
            var info = GetActiveTimeline()?.VideoInfo;
            if (info == null) return 30;
            foreach (string name in new[] { "FPS", "FrameRate", "Framerate" })
            {
                object? value = info.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(info);
                if (value is double d && d > 0) return d;
                if (value is float f && f > 0) return f;
                if (value is int i && i > 0) return i;
            }
            var numerator = info.GetType().GetProperty("FrameRateNumerator")?.GetValue(info) as IConvertible;
            var denominator = info.GetType().GetProperty("FrameRateDenominator")?.GetValue(info) as IConvertible;
            if (numerator != null && denominator != null && denominator.ToDouble(null) > 0) return numerator.ToDouble(null) / denominator.ToDouble(null);
        }
        catch { }
        return 30;
    }

    private static double ProbeMediaDurationSeconds(string path)
    {
        string ffprobe = Path.Combine(Environment.GetEnvironmentVariable("YMM4DirPath") ?? @"C:\Users\ADMIN\YukkuriMovieMaker_v4_Lite", "Resources", "bin", "x64", "ffmpeg", "ffprobe.exe");
        if (!File.Exists(ffprobe)) return 0;
        try
        {
            var start = new ProcessStartInfo(ffprobe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-v"); start.ArgumentList.Add("error"); start.ArgumentList.Add("-show_entries"); start.ArgumentList.Add("format=duration"); start.ArgumentList.Add("-of"); start.ArgumentList.Add("default=noprint_wrappers=1:nokey=1"); start.ArgumentList.Add(path);
            using var process = Process.Start(start);
            if (process == null) return 0;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double duration) ? duration : 0;
        }
        catch { return 0; }
    }

    private static int GetTimelineIntArgument(ParameterInfo parameter, int currentFrame, int selectedLayer, ref bool assignedFrame)
    {
        string name = parameter.Name ?? "";
        if (name.Contains("layer", StringComparison.OrdinalIgnoreCase)) return selectedLayer;
        if (name.Contains("frame", StringComparison.OrdinalIgnoreCase) || name.Contains("start", StringComparison.OrdinalIgnoreCase) || name.Contains("position", StringComparison.OrdinalIgnoreCase))
        {
            assignedFrame = true;
            return currentFrame;
        }
        if (!assignedFrame)
        {
            assignedFrame = true;
            return currentFrame;
        }
        return selectedLayer;
    }

    private static bool TrySetVolumePercent(IItem item, double volumePercent)
    {
        foreach (var property in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.Name.Contains("Volume", StringComparison.OrdinalIgnoreCase) && !property.Name.Contains("Gain", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (TrySetNumericProperty(property, item, volumePercent)) return true;
                object? holder = property.GetValue(item);
                volumePercent = ClampToParameterRange(holder, volumePercent);
                var valueProperty = holder?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                if (valueProperty?.CanWrite == true && TryConvertNumber(volumePercent, valueProperty.PropertyType, out var converted))
                {
                    valueProperty.SetValue(holder, converted);
                    return true;
                }
            }
            catch { }
        }
        return false;
    }

    private static double ClampToParameterRange(object? holder, double value)
    {
        if (holder == null) return value;
        double minimum = GetNumericProperty(holder, "Min", "Minimum") ?? 0;
        double maximum = GetNumericProperty(holder, "Max", "Maximum") ?? 400;
        return Math.Clamp(value, Math.Min(minimum, maximum), Math.Max(minimum, maximum));
    }

    private static double? GetNumericProperty(object target, params string[] names)
    {
        foreach (string name in names)
        {
            try
            {
                object? value = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
                if (value != null && double.TryParse(value.ToString(), out double result)) return result;
            }
            catch { }
        }
        return null;
    }

    private static bool TrySetNumericProperty(PropertyInfo property, object target, double value)
    {
        if (!property.CanWrite || !TryConvertNumber(value, property.PropertyType, out var converted)) return false;
        property.SetValue(target, converted);
        return true;
    }

    private static bool TryConvertNumber(double value, Type type, out object? converted)
    {
        try
        {
            Type actual = Nullable.GetUnderlyingType(type) ?? type;
            if (actual == typeof(double)) converted = value;
            else if (actual == typeof(float)) converted = (float)value;
            else if (actual == typeof(decimal)) converted = (decimal)value;
            else if (actual == typeof(int)) converted = (int)Math.Round(value);
            else if (actual == typeof(long)) converted = (long)Math.Round(value);
            else { converted = null; return false; }
            return true;
        }
        catch { converted = null; return false; }
    }

    private static bool TrySetMediaPath(IItem item, string path)
    {
        bool assigned = false;
        foreach (var property in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property => property.Name.Contains("File", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(property.Name, "Path", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(property.Name, "Source", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (property.PropertyType == typeof(string) && property.CanWrite)
                {
                    property.SetValue(item, path);
                    assigned = true;
                }
                else
                {
                    var holder = property.GetValue(item);
                    var valueProperty = holder?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                    if (valueProperty?.CanWrite == true && valueProperty.PropertyType == typeof(string))
                    {
                        valueProperty.SetValue(holder, path);
                        assigned = true;
                    }
                }
            }
            catch { }
        }
        return assigned;
    }

    public static object? GetActiveMainViewModel()
    {
        try
        {
            if (Application.Current == null) return null;

            foreach (Window window in Application.Current.Windows)
            {
                if (window.GetType().Name == "MainView")
                {
                    return window.DataContext;
                }
            }

            if (Application.Current.MainWindow != null &&
                Application.Current.MainWindow.GetType().Name == "MainView")
            {
                return Application.Current.MainWindow.DataContext;
            }

            foreach (Window window in Application.Current.Windows)
            {
                if (window.DataContext?.GetType().Name == "MainViewModel")
                {
                    return window.DataContext;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PresetApplier] GetActiveMainViewModel error: {ex}");
        }

        return null;
    }

    public static Timeline? GetActiveTimeline()
    {
        var mainVM = GetActiveMainViewModel();
        if (mainVM == null) return null;

        var mainVMType = mainVM.GetType();

        try
        {
            var modelField = mainVMType.GetField("model", BindingFlags.NonPublic | BindingFlags.Instance);
            var model = modelField?.GetValue(mainVM);
            if (model != null)
            {
                var timelineProp = model.GetType().GetProperty("Timeline", BindingFlags.Public | BindingFlags.Instance);
                if (timelineProp?.GetValue(model) is Timeline timeline)
                {
                    return timeline;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PresetApplier] Route A failed: {ex}");
        }

        try
        {
            var activeTimelineVMProp = mainVMType.GetProperty("ActiveTimelineViewModel", BindingFlags.Public | BindingFlags.Instance);
            var timelineVM = activeTimelineVMProp?.GetValue(mainVM);
            if (timelineVM != null)
            {
                var timelineField = timelineVM.GetType().GetField("timeline", BindingFlags.NonPublic | BindingFlags.Instance);
                if (timelineField?.GetValue(timelineVM) is Timeline timeline)
                {
                    return timeline;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PresetApplier] Route B failed: {ex}");
        }

        return null;
    }

    public static List<IItem> GetSelectedItems()
    {
        return GetSelectedTimelineItems().Where(x => x is TextItem || x is VoiceItem).ToList();
    }

    public static List<IItem> GetSelectedEffectTargets()
    {
        return GetSelectedTimelineItems().Where(HasVideoEffectsProperty).ToList();
    }

    public static List<IVideoEffect> GetVideoEffects(IItem item)
    {
        var property = GetVideoEffectsProperty(item);
        if (property?.GetValue(item) is IEnumerable<IVideoEffect> effects)
        {
            return effects.ToList();
        }

        return new List<IVideoEffect>();
    }

    public static List<IItem> GetSelectedTimelineItems()
    {
        var list = new List<IItem>();
        var timeline = GetActiveTimeline();
        if (timeline != null)
        {
            if (!timeline.SelectedItems.IsEmpty)
            {
                list.AddRange(timeline.SelectedItems);
            }
            else if (timeline.SelectedItem != null)
            {
                list.Add(timeline.SelectedItem);
            }
        }

        if (list.Count == 0)
        {
            try
            {
                var mainVM = GetActiveMainViewModel();
                if (mainVM != null)
                {
                    var itemEditorAreaField = mainVM.GetType().GetField("itemEditorAreaViewModel", BindingFlags.NonPublic | BindingFlags.Instance);
                    var itemEditorArea = itemEditorAreaField?.GetValue(mainVM);
                    if (itemEditorArea != null)
                    {
                        var vmProp = itemEditorArea.GetType().GetProperty("ViewModel", BindingFlags.Public | BindingFlags.Instance);
                        var itemEditorVM = vmProp?.GetValue(itemEditorArea);
                        if (itemEditorVM != null)
                        {
                            var itemsProp = itemEditorVM.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
                            var itemsReactive = itemsProp?.GetValue(itemEditorVM);
                            var valueProp = itemsReactive?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                            if (valueProp?.GetValue(itemsReactive) is ImmutableList<IItem> editorItems && !editorItems.IsEmpty)
                            {
                                list.AddRange(editorItems);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PresetApplier] Route ItemEditor failed: {ex}");
            }
        }

        return list.Distinct().ToList();
    }

    private static bool HasVideoEffectsProperty(IItem item) => GetVideoEffectsProperty(item) != null;

    private static PropertyInfo? GetVideoEffectsProperty(IItem item)
    {
        // VoiceItem exposes subtitle effects separately; other visual items use VideoEffects.
        var propertyName = item is VoiceItem ? "JimakuVideoEffects" : "VideoEffects";
        return item.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
    }

    public static IItem? GetSelectedItem()
    {
        var items = GetSelectedItems();
        return items.Count > 0 ? items[0] : null;
    }

    public static string GetSelectedItemStatusDescription()
    {
        var items = GetSelectedItems();
        if (items.Count == 0) return "アイテムが選択されていません";
        if (items.Count > 1) return $"{items.Count}個のアイテムを選択中 (複数パターン一括登録可能)";

        return items[0] switch
        {
            TextItem => "テキストアイテム選択中",
            VoiceItem => "字幕(ボイス)アイテム選択中",
            _ => $"非対応アイテム選択中 ({items[0].GetType().Name})"
        };
    }

    /// <summary>
    /// アイテムから最適な代表色を自動検出する
    /// ルール:
    /// 1. 文字色が #FFFFFF か #000000 でなければ採用
    /// 2. 文字色が白/黒なら、装飾色をチェック（白/黒以外なら採用）
    /// 3. 装飾色も白/黒なら、エフェクトを1つ目から順に色をチェック
    /// 4. 最後まで白/黒またはデータなしなら、最初の文字色を採用
    /// </summary>
    public static string DetectRepresentativeColor(IItem item)
    {
        System.Windows.Media.Color fontColor = System.Windows.Media.Colors.White;
        System.Windows.Media.Color styleColor = System.Windows.Media.Colors.Black;
        bool hasStyle = false;
        IEnumerable<IVideoEffect>? effects = null;

        if (item is TextItem t)
        {
            fontColor = t.FontColor;
            styleColor = t.StyleColor;
            hasStyle = t.Style != YukkuriMovieMaker.Project.Items.Style.Normal;
            effects = t.VideoEffects;
        }
        else if (item is VoiceItem v)
        {
            fontColor = v.FontColor;
            styleColor = v.StyleColor;
            hasStyle = v.Style != YukkuriMovieMaker.Project.Items.Style.Normal;
            effects = v.JimakuVideoEffects;
        }

        static bool IsWhiteOrBlack(System.Windows.Media.Color c)
        {
            if (c.A < 10) return true; // 透明に近い場合は色なし扱い
            bool isWhite = (c.R >= 250 && c.G >= 250 && c.B >= 250);
            bool isBlack = (c.R <= 5 && c.G <= 5 && c.B <= 5);
            return isWhite || isBlack;
        }

        static string ToHex(System.Windows.Media.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        // 1. 文字色が白・黒でなければ採用
        if (!IsWhiteOrBlack(fontColor))
        {
            return ToHex(fontColor);
        }

        // 2. 装飾色が白・黒でなければ採用
        if (hasStyle && !IsWhiteOrBlack(styleColor))
        {
            return ToHex(styleColor);
        }

        // 3. エフェクト1つ目から順に色をチェック
        if (effects != null)
        {
            foreach (var effect in effects)
            {
                var colors = ExtractColorsFromEffect(effect);
                foreach (var col in colors)
                {
                    if (!IsWhiteOrBlack(col))
                    {
                        return ToHex(col);
                    }
                }
            }
        }

        // 4. 最後までなければ文字色
        return ToHex(fontColor);
    }

    /// <summary>
    /// テキストアイテムから、文字色の自動判定結果とエフェクト内の色パラメーターを順番に取得する。
    /// パターン色として使う色は重複を除き、最大10色まで返す。
    /// </summary>
    public static List<string> DetectPatternColors(IItem item, int startEffectIndex = 0, int maxColors = PresetPattern.MaxPatternCount)
    {
        if (item is not (TextItem or VoiceItem)) return new List<string> { DetectRepresentativeColor(item) };

        static string ToHex(System.Windows.Media.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        startEffectIndex = Math.Clamp(startEffectIndex, 0, 10);
        var colors = new List<string>();
        if (startEffectIndex == 0) colors.Add(DetectRepresentativeColor(item));
        IEnumerable<IVideoEffect> effects = item switch
        {
            TextItem text => text.VideoEffects,
            VoiceItem voice => voice.JimakuVideoEffects,
            _ => Enumerable.Empty<IVideoEffect>()
        };

        foreach (var effect in effects.Skip(Math.Max(0, startEffectIndex - 1)))
        {
            foreach (var color in ExtractColorsFromEffect(effect))
            {
                if (color.A < 10) continue;
                string hex = ToHex(color);
                if (!colors.Contains(hex, StringComparer.OrdinalIgnoreCase)) colors.Add(hex);
                if (colors.Count >= Math.Clamp(maxColors, 1, PresetPattern.MaxPatternCount)) return colors;
            }
        }
        if (colors.Count == 0) colors.Add(DetectRepresentativeColor(item));
        return colors;
    }

    /// <summary>
    /// エフェクトオブジェクトから色を再帰的・網羅的に抽出
    /// </summary>
    private static List<System.Windows.Media.Color> ExtractColorsFromEffect(IVideoEffect effect)
    {
        var result = new List<System.Windows.Media.Color>();
        if (effect == null) return result;

        try
        {
            var effectType = effect.GetType();
            foreach (var prop in effectType.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(property => property.MetadataToken))
            {
                // Direct Color property
                if (prop.PropertyType == typeof(System.Windows.Media.Color))
                {
                    if (prop.GetValue(effect) is System.Windows.Media.Color col)
                    {
                        result.Add(col);
                    }
                }
                // Stops collection (Gradient etc.)
                else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType != typeof(string))
                {
                    if (prop.GetValue(effect) is System.Collections.IEnumerable list)
                    {
                        foreach (var item in list)
                        {
                            if (item == null) continue;
                            var colProp = item.GetType().GetProperty("Color", BindingFlags.Public | BindingFlags.Instance);
                            if (colProp?.PropertyType == typeof(System.Windows.Media.Color))
                            {
                                if (colProp.GetValue(item) is System.Windows.Media.Color c)
                                {
                                    result.Add(c);
                                }
                            }
                        }
                    }
                }
                // Brush parameter
                else if (prop.Name.Contains("Brush", StringComparison.OrdinalIgnoreCase))
                {
                    var brushObj = prop.GetValue(effect);
                    if (brushObj != null)
                    {
                        var paramProp = brushObj.GetType().GetProperty("Parameter", BindingFlags.Public | BindingFlags.Instance);
                        var target = paramProp?.GetValue(brushObj) ?? brushObj;
                        var colProp = target.GetType().GetProperty("Color", BindingFlags.Public | BindingFlags.Instance);
                        if (colProp?.PropertyType == typeof(System.Windows.Media.Color))
                        {
                            if (colProp.GetValue(target) is System.Windows.Media.Color c)
                            {
                                result.Add(c);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return result;
    }

    public static (string text, string colorHex) GetSelectedItemTextAndColor()
    {
        var item = GetSelectedItem();
        if (item is TextItem t)
        {
            string color = DetectRepresentativeColor(t);
            string txt = string.IsNullOrWhiteSpace(t.Text) ? "カスタムテロップ" : t.Text.Trim();
            if (txt.Length > 20) txt = txt.Substring(0, 20);
            return (txt, color);
        }
        if (item is VoiceItem v)
        {
            string color = DetectRepresentativeColor(v);
            return ("字幕プリセット", color);
        }
        return ("新規プリセット", "#FFFFFF");
    }

    /// <summary>
    /// 指定されたアイテムからパターンデータを作成
    /// </summary>
    public static PresetPattern? CreatePatternFromItem(
        IItem selected,
        int slot,
        string sampleColor,
        DetailedApplyFlags flags)
    {
        if (selected == null) return null;

        var pattern = new PresetPattern
        {
            Slot = Math.Clamp(slot, 1, PresetPattern.MaxPatternCount),
            SampleColor = string.IsNullOrWhiteSpace(sampleColor) ? "#FFFFFF" : sampleColor,
            ItemFlags = flags,
            Apply = new ApplyFlags
            {
                Effects = flags.VideoEffects || flags.FontColor || flags.Style || flags.FontStyles || flags.IsDevidedPerCharacter,
                Font = flags.FontName,
                Position = flags.Position,
                Size = flags.FontSize || flags.Zoom,
                Others = flags.Spacing || flags.BasePoint || flags.Rotation || flags.Opacity || flags.Blend || flags.Fade
            }
        };

        if (selected is TextItem textItem)
        {
            pattern.Font = new FontPresetData
            {
                FontName = textItem.Font,
                FontColor = textItem.FontColor,
                Style = textItem.Style,
                StyleColor = textItem.StyleColor,
                Bold = textItem.Bold,
                Italic = textItem.Italic,
                Underline = textItem.Underline,
                Strikethrough = textItem.Strikethrough,
                IsDevidedPerCharacter = textItem.IsDevidedPerCharacter,
                BasePoint = textItem.BasePoint
            };

            var fontSize = new Animation();
            fontSize.CopyFrom(textItem.FontSize);
            pattern.Font.FontSize = fontSize;

            var lineHeight = new Animation();
            lineHeight.CopyFrom(textItem.LineHeight2);
            pattern.Font.LineHeight = lineHeight;

            var letterSpacing = new Animation();
            letterSpacing.CopyFrom(textItem.LetterSpacing2);
            pattern.Font.LetterSpacing = letterSpacing;

            var posX = new Animation();
            posX.CopyFrom(textItem.X);
            var posY = new Animation();
            posY.CopyFrom(textItem.Y);
            var posZ = new Animation();
            posZ.CopyFrom(textItem.Z);
            var zoom = new Animation();
            zoom.CopyFrom(textItem.Zoom);
            var rot = new Animation();
            rot.CopyFrom(textItem.Rotation);
            var opacity = new Animation();
            opacity.CopyFrom(textItem.Opacity);

            pattern.Position = new PositionPresetData
            {
                X = posX,
                Y = posY,
                Z = posZ,
                Zoom = zoom,
                Rotation = rot,
                Opacity = opacity,
                Blend = textItem.Blend,
                FadeIn = textItem.FadeIn,
                FadeOut = textItem.FadeOut
            };

            if (textItem.VideoEffects != null)
            {
                pattern.Effects = textItem.VideoEffects
                    .Select(e => Json.GetClone(e))
                    .Where(e => e != null)
                    .Select(e => e!)
                    .ToList();
            }

            return pattern;
        }

        if (selected is VoiceItem voiceItem)
        {
            pattern.Font = new FontPresetData
            {
                FontName = voiceItem.Font,
                FontColor = voiceItem.FontColor,
                Style = voiceItem.Style,
                StyleColor = voiceItem.StyleColor,
                Bold = false,
                Italic = false,
                IsDevidedPerCharacter = false
            };

            var fontSize = new Animation();
            fontSize.CopyFrom(voiceItem.FontSize);
            pattern.Font.FontSize = fontSize;

            var posX = new Animation();
            posX.CopyFrom(voiceItem.JimakuX);
            var posY = new Animation();
            posY.CopyFrom(voiceItem.JimakuY);
            var zoom = new Animation();
            zoom.CopyFrom(voiceItem.JimakuZoom);
            var opacity = new Animation();
            opacity.CopyFrom(voiceItem.JimakuOpacity);

            pattern.Position = new PositionPresetData
            {
                X = posX,
                Y = posY,
                Zoom = zoom,
                Opacity = opacity
            };

            if (voiceItem.JimakuVideoEffects != null)
            {
                pattern.Effects = voiceItem.JimakuVideoEffects
                    .Select(e => Json.GetClone(e))
                    .Where(e => e != null)
                    .Select(e => e!)
                    .ToList();
            }

            return pattern;
        }

        return null;
    }

    public static PositionPresetData? CreatePositionPresetFromItem(IItem item)
    {
        if (item == null) return null;
        bool voice = item is VoiceItem;
        string prefix = voice ? "Jimaku" : string.Empty;
        var position = new PositionPresetData
        {
            X = CopyAnimationFromProperty(item, prefix + "X"),
            Y = CopyAnimationFromProperty(item, prefix + "Y"),
            Z = CopyAnimationFromProperty(item, prefix + "Z"),
            Zoom = CopyAnimationFromProperty(item, prefix + "Zoom"),
            Rotation = CopyAnimationFromProperty(item, prefix + "Rotation"),
            Opacity = CopyAnimationFromProperty(item, prefix + "Opacity")
        };
        if (position.X == null && position.Y == null && position.Z == null && position.Zoom == null && position.Rotation == null && position.Opacity == null)
            return null;
        string blendName = prefix + "Blend";
        if (item.GetType().GetProperty(blendName)?.GetValue(item) is Blend blend) position.Blend = blend;
        string fadeInName = prefix + "FadeIn";
        string fadeOutName = prefix + "FadeOut";
        if (item.GetType().GetProperty(fadeInName)?.GetValue(item) is double fadeIn) position.FadeIn = fadeIn;
        if (item.GetType().GetProperty(fadeOutName)?.GetValue(item) is double fadeOut) position.FadeOut = fadeOut;
        return position;
    }

    private static Animation? CopyAnimationFromProperty(IItem item, string propertyName)
    {
        try
        {
            if (item.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) is not Animation source)
                return null;
            var copy = new Animation();
            copy.CopyFrom(source);
            return copy;
        }
        catch { return null; }
    }

    /// <summary>
    /// 選択中の単一アイテムからパターンデータを作成 (互換用)
    /// </summary>
    public static PresetPattern? CreatePatternFromSelectedItem(
        int slot,
        string sampleColor,
        DetailedApplyFlags flags)
    {
        var selected = GetSelectedItem();
        if (selected == null) return null;
        return CreatePatternFromItem(selected, slot, sampleColor, flags);
    }

    /// <summary>
    /// 選択中のアイテムから新規プリセットを作成
    /// </summary>
    public static TextPreset? CreatePresetFromSelectedItem(
        string name, string category,
        bool saveEffects, bool saveFont, bool savePosition, bool saveSize, bool saveOthers)
    {
        var flags = new DetailedApplyFlags
        {
            VideoEffects = saveEffects,
            FontColor = saveEffects,
            Style = saveEffects,
            FontStyles = saveEffects,
            IsDevidedPerCharacter = saveEffects,
            FontName = saveFont,
            Position = savePosition,
            FontSize = saveSize,
            Zoom = saveSize,
            Spacing = saveOthers,
            BasePoint = saveOthers,
            Rotation = saveOthers,
            Opacity = saveOthers,
            Blend = saveOthers,
            Fade = saveOthers
        };

        var (_, color) = GetSelectedItemTextAndColor();
        var pattern = CreatePatternFromSelectedItem(1, color, flags);
        if (pattern == null) return null;

        var preset = new TextPreset
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? "新規プリセット" : name,
            Category = string.IsNullOrWhiteSpace(category) ? "T" : category,
            ActivePatternSlot = 1,
            Patterns = new List<PresetPattern> { pattern }
        };

        return preset;
    }

    /// <summary>
    /// プリセットを選択中のアイテムに適用（現在アクティブなパターンを使用）
    /// </summary>
    public static (bool success, string message) ApplyPresetToSelected(
        TextPreset preset,
        bool applyEffects,
        bool applyFont,
        bool applyPosition,
        bool applySize,
        bool applyOthers)
    {
        var pattern = preset.ActivePattern;
        return ApplyPresetPatternToSelected(preset.Name, pattern, applyEffects, applyFont, applyPosition, applySize, applyOthers);
    }

    /// <summary>
    /// パターンを選択中のアイテムに適用
    /// </summary>
    public static (bool success, string message) ApplyPresetPatternToSelected(
        string presetName,
        PresetPattern pattern,
        bool applyEffects,
        bool applyFont,
        bool applyPosition,
        bool applySize,
        bool applyOthers)
    {
        var selected = GetSelectedItem();
        if (selected == null)
        {
            return (false, "タイムライン上でテキストまたは字幕アイテムを選択してください");
        }

        var flags = pattern.ItemFlags ?? new DetailedApplyFlags();

        if (selected is TextItem textItem)
        {
            // 1. エフェクト (映像エフェクト + 文字色 + 装飾 + 装飾色 + 太字 + イタリック + 下線 + 打ち消し線 + 文字ごとに分割)
            if (applyEffects && pattern.Apply.Effects)
            {
                if (flags.VideoEffects && pattern.Effects != null)
                {
                    var cloned = pattern.Effects
                        .Select(e => Json.GetClone(e))
                        .Where(e => e != null)
                        .Select(e => e!)
                        .ToList();

                    textItem.VideoEffects = ImmutableList.CreateRange(cloned);
                }

                if (pattern.Font != null)
                {
                    if (flags.FontColor) textItem.FontColor = pattern.Font.FontColor;
                    if (flags.Style)
                    {
                        textItem.Style = pattern.Font.Style;
                        textItem.StyleColor = pattern.Font.StyleColor;
                    }
                    if (flags.FontStyles)
                    {
                        textItem.Bold = pattern.Font.Bold;
                        textItem.Italic = pattern.Font.Italic;
                        textItem.Underline = pattern.Font.Underline;
                        textItem.Strikethrough = pattern.Font.Strikethrough;
                    }
                    if (flags.IsDevidedPerCharacter)
                    {
                        textItem.IsDevidedPerCharacter = pattern.Font.IsDevidedPerCharacter;
                    }
                }
            }

            // 2. フォント (フォント名のみ)
            if (applyFont && pattern.Apply.Font && flags.FontName && pattern.Font != null)
            {
                textItem.Font = pattern.Font.FontName;
            }

            // 3. 位置 (X, Y, Z)
            if (applyPosition && pattern.Apply.Position && flags.Position && pattern.Position != null)
            {
                if (pattern.Position.X != null) textItem.X.CopyFrom(pattern.Position.X);
                if (pattern.Position.Y != null) textItem.Y.CopyFrom(pattern.Position.Y);
                if (pattern.Position.Z != null) textItem.Z.CopyFrom(pattern.Position.Z);
            }

            // 4. サイズ (FontSize, Zoom)
            if (applySize && pattern.Apply.Size)
            {
                if (flags.FontSize && pattern.Font?.FontSize != null) textItem.FontSize.CopyFrom(pattern.Font.FontSize);
                if (flags.Zoom && pattern.Position?.Zoom != null) textItem.Zoom.CopyFrom(pattern.Position.Zoom);
            }

            // 5. その他 (行間, 文字間隔, 配置基準点, 回転, 不透明度, ブレンド, フェード)
            if (applyOthers && pattern.Apply.Others)
            {
                if (pattern.Font != null)
                {
                    if (flags.BasePoint) textItem.BasePoint = pattern.Font.BasePoint;
                    if (flags.Spacing)
                    {
                        if (pattern.Font.LineHeight != null) textItem.LineHeight2.CopyFrom(pattern.Font.LineHeight);
                        if (pattern.Font.LetterSpacing != null) textItem.LetterSpacing2.CopyFrom(pattern.Font.LetterSpacing);
                    }
                }

                if (pattern.Position != null)
                {
                    if (flags.Rotation && pattern.Position.Rotation != null) textItem.Rotation.CopyFrom(pattern.Position.Rotation);
                    if (flags.Opacity && pattern.Position.Opacity != null) textItem.Opacity.CopyFrom(pattern.Position.Opacity);
                    if (flags.Blend) textItem.Blend = pattern.Position.Blend;
                    if (flags.Fade)
                    {
                        textItem.FadeIn = pattern.Position.FadeIn;
                        textItem.FadeOut = pattern.Position.FadeOut;
                    }
                }
            }

            RecordUndoRedo();
            return (true, $"テキストアイテムに「{presetName} (P{pattern.Slot})」を適用しました");
        }

        if (selected is VoiceItem voiceItem)
        {
            // 個別設定に切り替えてから字幕のプリセット内容を適用する。
            // キャラクター設定に従う状態のままでは、字幕の個別プロパティが
            // プレビューや適用結果に反映されないことがある。
            voiceItem.JimakuVisibility = JimakuVisibility.Custom;

            // 1. エフェクト (字幕エフェクト + 文字色 + 装飾 + 装飾色)
            if (applyEffects && pattern.Apply.Effects)
            {
                if (flags.VideoEffects && pattern.Effects != null)
                {
                    var cloned = pattern.Effects
                        .Select(e => Json.GetClone(e))
                        .Where(e => e != null)
                        .Select(e => e!)
                        .ToList();

                    voiceItem.JimakuVideoEffects = ImmutableList.CreateRange(cloned);
                }

                if (pattern.Font != null)
                {
                    if (flags.FontColor) voiceItem.FontColor = pattern.Font.FontColor;
                    if (flags.Style)
                    {
                        voiceItem.Style = pattern.Font.Style;
                        voiceItem.StyleColor = pattern.Font.StyleColor;
                    }
                }
            }

            // 2. フォント (フォント名のみ)
            if (applyFont && pattern.Apply.Font && flags.FontName && pattern.Font != null)
            {
                voiceItem.Font = pattern.Font.FontName;
            }

            // 3. 位置 (JimakuX, JimakuY)
            if (applyPosition && pattern.Apply.Position && flags.Position && pattern.Position != null)
            {
                if (pattern.Position.X != null) voiceItem.JimakuX.CopyFrom(pattern.Position.X);
                if (pattern.Position.Y != null) voiceItem.JimakuY.CopyFrom(pattern.Position.Y);
            }

            // 4. サイズ (FontSize, JimakuZoom)
            if (applySize && pattern.Apply.Size)
            {
                if (flags.FontSize && pattern.Font?.FontSize != null) voiceItem.FontSize.CopyFrom(pattern.Font.FontSize);
                if (flags.Zoom && pattern.Position?.Zoom != null) voiceItem.JimakuZoom.CopyFrom(pattern.Position.Zoom);
            }

            // 5. その他 (不透明度)
            if (applyOthers && pattern.Apply.Others && flags.Opacity && pattern.Position?.Opacity != null)
            {
                voiceItem.JimakuOpacity.CopyFrom(pattern.Position.Opacity);
            }

            RecordUndoRedo();
            return (true, $"字幕(ボイス)アイテムに「{presetName} (P{pattern.Slot})」を適用しました");
        }

        return (false, $"非対応アイテムです: {selected.GetType().Name}");
    }

    public static (bool success, string message) ApplyEffectPresetToSelected(string presetName, PresetPattern pattern, bool applyPosition = false, bool applyEffects = true)
    {
        var targets = GetSelectedEffectTargets();
        if (targets.Count == 0)
        {
            return (false, "映像エフェクトを適用できるアイテムを選択してください");
        }

        var additions = ((applyEffects ? pattern.Effects : null) ?? new List<IVideoEffect>())
            .Select(e => Json.GetClone(e))
            .Where(e => e != null)
            .Select(e => e!)
            .ToList();
        if (additions.Count == 0 && !(applyPosition && pattern.Apply.Position && pattern.Position != null))
        {
            return (false, "このエフェクトプリセットには適用できるエフェクトがありません");
        }

        int appliedCount = 0;
        foreach (var item in targets)
        {
            try
            {
                if (item is VoiceItem voiceItem)
                {
                    voiceItem.JimakuVisibility = JimakuVisibility.Custom;
                }

                var property = GetVideoEffectsProperty(item);
                if (property?.GetValue(item) is not IEnumerable<IVideoEffect> currentEffects || !property.CanWrite)
                {
                    continue;
                }

                var existing = currentEffects.ToList();
                var itemAdditions = additions
                    .Select(e => Json.GetClone(e))
                    .Where(e => e != null)
                    .Select(e => e!)
                    .ToList();
                if (itemAdditions.Count > 0)
                    property.SetValue(item, ImmutableList.CreateRange(existing.Concat(itemAdditions)));
                bool changed = itemAdditions.Count > 0;
                if (applyPosition && pattern.Apply.Position && pattern.Position != null)
                {
                    var position = pattern.Position;
                    bool voice = item is VoiceItem;
                    changed |= CopyAnimation(item, voice ? "JimakuX" : "X", position.X);
                    changed |= CopyAnimation(item, voice ? "JimakuY" : "Y", position.Y);
                    changed |= CopyAnimation(item, voice ? "JimakuZ" : "Z", position.Z);
                    changed |= CopyAnimation(item, voice ? "JimakuZoom" : "Zoom", position.Zoom);
                    changed |= CopyAnimation(item, voice ? "JimakuRotation" : "Rotation", position.Rotation);
                    changed |= CopyAnimation(item, voice ? "JimakuOpacity" : "Opacity", position.Opacity);
                    changed |= SetPropertyIfAvailable(item, voice ? "JimakuBlend" : "Blend", position.Blend);
                    changed |= SetPropertyIfAvailable(item, voice ? "JimakuFadeIn" : "FadeIn", position.FadeIn);
                    changed |= SetPropertyIfAvailable(item, voice ? "JimakuFadeOut" : "FadeOut", position.FadeOut);
                }
                if (changed) appliedCount++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PresetApplier] Effect preset failed for {item.GetType().Name}: {ex}");
            }
        }

        if (appliedCount == 0)
        {
            return (false, "選択アイテムにエフェクトを適用できませんでした");
        }

        RecordUndoRedo();
        return (true, $"「{presetName}」のエフェクト / 位置を {appliedCount} 個のアイテムに適用しました");
    }

    public static (bool success, string message) ApplyTextPresetToEffectTargets(
        string presetName,
        PresetPattern pattern,
        bool applyEffects,
        bool applyPosition)
    {
        if (!applyEffects && !applyPosition)
        {
            return (false, "エフェクトと位置の適用対象が両方OFFです");
        }

        var targets = GetSelectedEffectTargets();
        if (targets.Count == 0)
        {
            return (false, "映像エフェクトを適用できるアイテムを選択してください");
        }

        int appliedCount = 0;
        foreach (var item in targets)
        {
            try
            {
                if (item is VoiceItem voiceItem)
                {
                    voiceItem.JimakuVisibility = JimakuVisibility.Custom;
                }

                bool changed = false;
                if (applyEffects && pattern.Apply.Effects && pattern.Effects is { Count: > 0 })
                {
                    var property = GetVideoEffectsProperty(item);
                    if (property?.GetValue(item) is IEnumerable<IVideoEffect> current && property.CanWrite)
                    {
                        var additions = pattern.Effects.Select(e => Json.GetClone(e)).Where(e => e != null).Select(e => e!).ToList();
                        property.SetValue(item, ImmutableList.CreateRange(current.Concat(additions)));
                        changed = true;
                    }
                }

                if (applyPosition && pattern.Apply.Position && pattern.Position != null)
                {
                    var position = pattern.Position;
                    bool voice = item is VoiceItem;
                    changed |= CopyAnimation(item, voice ? "JimakuX" : "X", position.X);
                    changed |= CopyAnimation(item, voice ? "JimakuY" : "Y", position.Y);
                    changed |= CopyAnimation(item, voice ? "JimakuZ" : "Z", position.Z);
                    changed |= CopyAnimation(item, voice ? "JimakuZoom" : "Zoom", position.Zoom);
                    changed |= CopyAnimation(item, voice ? "JimakuRotation" : "Rotation", position.Rotation);
                    changed |= CopyAnimation(item, voice ? "JimakuOpacity" : "Opacity", position.Opacity);
                    changed |= SetPropertyIfAvailable(item, voice ? "JimakuBlend" : "Blend", position.Blend);
                    changed |= SetPropertyIfAvailable(item, voice ? "JimakuFadeIn" : "FadeIn", position.FadeIn);
                    changed |= SetPropertyIfAvailable(item, voice ? "JimakuFadeOut" : "FadeOut", position.FadeOut);
                }

                if (changed) appliedCount++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PresetApplier] Text preset FX application failed for {item.GetType().Name}: {ex}");
            }
        }

        if (appliedCount == 0)
        {
            return (false, "適用可能なエフェクトまたは位置情報がありませんでした");
        }

        RecordUndoRedo();
        return (true, $"テキストプリセット「{presetName}」のエフェクト / 位置を {appliedCount} 個の対応アイテムに適用しました");
    }

    private static bool CopyAnimation(IItem item, string propertyName, Animation? source)
    {
        if (source == null) return false;
        var property = item.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        var target = property?.GetValue(item);
        var copyMethod = target?.GetType().GetMethod("CopyFrom", new[] { typeof(Animation) });
        if (copyMethod == null) return false;
        copyMethod.Invoke(target, new object[] { source });
        return true;
    }

    private static bool SetPropertyIfAvailable(IItem item, string propertyName, object value)
    {
        var property = item.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.CanWrite != true || !property.PropertyType.IsInstanceOfType(value)) return false;
        property.SetValue(item, value);
        return true;
    }

    private static void RecordUndoRedo()
    {
        try
        {
            var mainVM = GetActiveMainViewModel();
            if (mainVM == null) return;

            var modelField = mainVM.GetType().GetField("model", BindingFlags.NonPublic | BindingFlags.Instance);
            var model = modelField?.GetValue(mainVM);
            if (model != null)
            {
                var undoRedoProp = model.GetType().GetProperty("UndoRedoManager", BindingFlags.Public | BindingFlags.Instance);
                var undoRedo = undoRedoProp?.GetValue(model);
                var recordMethod = undoRedo?.GetType().GetMethod("Record", Type.EmptyTypes);
                recordMethod?.Invoke(undoRedo, null);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PresetApplier] Failed to record Undo/Redo: {ex}");
        }
    }
}
