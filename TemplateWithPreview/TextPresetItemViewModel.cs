using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TemplateWithPreview.Models;

namespace TemplateWithPreview;

public class PresetPatternItemViewModel : INotifyPropertyChanged
{
    private BitmapSource? _thumbnail;
    private bool _isActive;

    public PresetPattern Pattern { get; }

    public int Slot => Pattern.Slot;

    public string SlotText => $"P{Slot}";

    public string SampleColorHex => Pattern.SampleColor;

    public Brush SampleColorBrush
    {
        get
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(Pattern.SampleColor);
                return new SolidColorBrush(color);
            }
            catch
            {
                return Brushes.White;
            }
        }
    }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail ?? Pattern.CachedThumbnail;
        set
        {
            value = FitCardThumbnail(value);
            if (_thumbnail != value)
            {
                _thumbnail = value;
                Pattern.CachedThumbnail = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    public PresetPatternItemViewModel(PresetPattern pattern, bool isActive)
    {
        Pattern = pattern;
        _isActive = isActive;
        _thumbnail = FitCardThumbnail(pattern.CachedThumbnail);
        if (pattern.CachedThumbnail != _thumbnail) pattern.CachedThumbnail = _thumbnail;
    }

    public static BitmapSource? FitCardThumbnail(BitmapSource? image)
    {
        if (image == null || image.PixelWidth <= 320) return image;
        double scale = 320.0 / image.PixelWidth;
        var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        if (scaled.CanFreeze) scaled.Freeze();
        return scaled;
    }

    public void ReleaseThumbnail()
    {
        _thumbnail = null;
        Pattern.CachedThumbnail = null;
        OnPropertyChanged(nameof(Thumbnail));
    }

    public static BitmapSource? LoadSavedThumbnail(PresetPattern pattern)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pattern.PreviewImagePath) || !File.Exists(pattern.PreviewImagePath)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 320;
            image.UriSource = new Uri(pattern.PreviewImagePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class TextPresetItemViewModel : INotifyPropertyChanged
{
    private bool _isLoading;
    private bool _isSelected;

    public TextPreset Preset { get; }

    public string Id => Preset.Id;

    public bool IsEffectPreset => Preset.IsEffectPreset;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsItemTemplate => Preset.IsItemTemplate;

    public string PresetKindLabel => Preset.IsItemTemplate ? "TPL" : Preset.IsEffectPreset ? "FX" : "TEX";

    public string Name
    {
        get => Preset.Name;
        set
        {
            if (Preset.Name != value)
            {
                Preset.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public string Category
    {
        get => Preset.Category;
        set
        {
            if (Preset.Category != value)
            {
                Preset.Category = value;
                OnPropertyChanged();
            }
        }
    }

    public string Tags
    {
        get => Preset.Tags;
        set
        {
            if (Preset.Tags != value)
            {
                Preset.Tags = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TagList));
            }
        }
    }

    public List<string> TagList => Preset.GetTagList();

    public ObservableCollection<PresetPatternItemViewModel> Patterns { get; } = new();

    public int ActiveSlot
    {
        get => Preset.ActivePatternSlot;
        set
        {
            if (Preset.ActivePatternSlot != value)
            {
                Preset.ActivePatternSlot = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActivePattern));
                OnPropertyChanged(nameof(Thumbnail));
                OnPropertyChanged(nameof(PreviewVideoPath));
                OnPropertyChanged(nameof(ActiveSlotText));
                OnPropertyChanged(nameof(ActiveSampleColorBrush));
                OnPropertyChanged(nameof(HasEffects));
                OnPropertyChanged(nameof(ShowEffectsBadge));
                OnPropertyChanged(nameof(HasFont));
                OnPropertyChanged(nameof(HasPosition));
                OnPropertyChanged(nameof(HasSize));
                OnPropertyChanged(nameof(HasOthers));

                foreach (var p in Patterns)
                {
                    p.IsActive = (p.Slot == value);
                }
            }
        }
    }

    public PresetPattern ActivePattern => Preset.ActivePattern;

    public string ActiveSlotText => $"P{ActiveSlot}";

    public string? PreviewVideoPath => Preset.IsEffectPreset || Preset.IsItemTemplate ? ActivePattern.PreviewVideoPath : null;
    public string? PreviewAudioPath => Preset.IsItemTemplate ? ActivePattern.PreviewAudioPath : null;

    public void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(PreviewVideoPath));
        OnPropertyChanged(nameof(PreviewAudioPath));
        OnPropertyChanged(nameof(Thumbnail));
    }

    public bool HasMultiplePatterns => Preset.Patterns.Count > 1;

    public Brush ActiveSampleColorBrush
    {
        get
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(ActivePattern.SampleColor);
                return new SolidColorBrush(color);
            }
            catch
            {
                return Brushes.White;
            }
        }
    }

    public BitmapSource? Thumbnail
    {
        get => ActivePattern.CachedThumbnail;
        set
        {
            value = PresetPatternItemViewModel.FitCardThumbnail(value);
            ActivePattern.CachedThumbnail = value;
            var activeVm = Patterns.FirstOrDefault(p => p.Slot == ActiveSlot);
            if (activeVm != null)
            {
                activeVm.Thumbnail = value;
            }
            OnPropertyChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public bool HasEffects
    {
        get
        {
            var p = ActivePattern;
            return (p.Effects != null && p.Effects.Count > 0) || 
                   p.Font?.Style != YukkuriMovieMaker.Project.Items.Style.Normal ||
                   p.Font?.Bold == true ||
                   p.Font?.Italic == true ||
                   p.Font?.Underline == true ||
                   p.Font?.Strikethrough == true ||
                   p.Font?.IsDevidedPerCharacter == true;
        }
    }

    public bool ShowEffectsBadge => HasEffects && !Preset.IsEffectPreset;

    public bool HasFont => !string.IsNullOrEmpty(ActivePattern.Font?.FontName);

    public bool HasPosition => ActivePattern.Position?.X != null || 
                               ActivePattern.Position?.Y != null || 
                               ActivePattern.Position?.Z != null;

    public bool HasSize => ActivePattern.Font?.FontSize != null || 
                           ActivePattern.Position?.Zoom != null;

    public bool HasOthers => ActivePattern.Position?.Rotation != null || 
                             ActivePattern.Position?.Opacity != null ||
                             ActivePattern.Position?.Blend != YukkuriMovieMaker.Project.Blend.Normal ||
                             ActivePattern.Font?.LineHeight != null ||
                             ActivePattern.Font?.LetterSpacing != null;

    public TextPresetItemViewModel(TextPreset preset)
    {
        Preset = preset;
        preset.EnsurePatternsInitialized();
        SyncPatternsCollection();
    }

    public void SyncPatternsCollection()
    {
        Patterns.Clear();
        foreach (var pat in Preset.Patterns.OrderBy(p => p.Slot))
        {
            Patterns.Add(new PresetPatternItemViewModel(pat, pat.Slot == Preset.ActivePatternSlot));
        }
        OnPropertyChanged(nameof(HasMultiplePatterns));
        OnPropertyChanged(nameof(ActiveSlotText));
        OnPropertyChanged(nameof(ActiveSampleColorBrush));
        OnPropertyChanged(nameof(Thumbnail));
    }

    /// <summary>
    /// 次のパターンに切り替え（ループ）
    /// </summary>
    public bool NextPattern()
    {
        if (Preset.Patterns.Count <= 1) return false;
        var ordered = Preset.Patterns.OrderBy(p => p.Slot).ToList();
        int idx = ordered.FindIndex(p => p.Slot == ActiveSlot);
        if (idx < 0) idx = 0;
        int nextIdx = (idx + 1) % ordered.Count;
        ActiveSlot = ordered[nextIdx].Slot;
        return true;
    }

    /// <summary>
    /// 前のパターンに切り替え（ループ）
    /// </summary>
    public bool PrevPattern()
    {
        if (Preset.Patterns.Count <= 1) return false;
        var ordered = Preset.Patterns.OrderBy(p => p.Slot).ToList();
        int idx = ordered.FindIndex(p => p.Slot == ActiveSlot);
        if (idx < 0) idx = 0;
        int prevIdx = (idx - 1 + ordered.Count) % ordered.Count;
        ActiveSlot = ordered[prevIdx].Slot;
        return true;
    }

    /// <summary>
    /// 指定スロット番号に切り替え
    /// </summary>
    public bool SelectSlot(int slot)
    {
        if (Preset.Patterns.Any(p => p.Slot == slot))
        {
            ActiveSlot = slot;
            return true;
        }
        return false;
    }

    public async Task RenderActiveThumbnailAsync(PreviewRenderer renderer, string previewText, System.Windows.Media.Color backgroundColor)
    {
        IsLoading = true;
        try
        {
            if (Preset.IsEffectPreset || Preset.IsItemTemplate)
            {
                Thumbnail = PresetPatternItemViewModel.LoadSavedThumbnail(ActivePattern);
                return;
            }
            var bmp = await renderer.RenderPresetPatternPreviewAsync(Preset.Id, ActivePattern, previewText, backgroundColor);
            Thumbnail = bmp;
        }
        finally { IsLoading = false; }
    }

    public void ReleaseCachedThumbnails()
    {
        foreach (var pattern in Patterns) pattern.ReleaseThumbnail();
        foreach (var pattern in Preset.Patterns) pattern.CachedThumbnail = null;
        OnPropertyChanged(nameof(Thumbnail));
    }

    public async Task RenderAllPatternsAsync(PreviewRenderer renderer, string previewText, System.Windows.Media.Color backgroundColor)
    {
        // The card displays only the active pattern, so rendering inactive patterns
        // wastes GPU/CPU time and memory without producing visible output.
        await RenderActiveThumbnailAsync(renderer, previewText, backgroundColor);
        OnPropertyChanged(nameof(Thumbnail));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
