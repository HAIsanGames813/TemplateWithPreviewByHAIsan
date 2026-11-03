using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using System.Windows.Media.Imaging;
using System.Threading;
using TemplateWithPreview.Models;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Plugin;

namespace TemplateWithPreview;

public class CategoryItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; set; } = "";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public CategoryItemViewModel(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class PreviewPresetViewModel : IToolViewModel, INotifyPropertyChanged
{
    private readonly PreviewRenderer _renderer = new();
    private List<TextPreset> _rawPresets = new();
    private readonly Dictionary<string, TextPresetItemViewModel> _itemViewModelCache = new();
    private readonly Dictionary<string, Task> _audioPreviewGenerationTasks = new(StringComparer.Ordinal);
    private readonly System.Windows.Threading.DispatcherTimer _settingsSaveTimer;
    private readonly System.Windows.Threading.DispatcherTimer _libraryWatchRefreshTimer;
    private readonly List<FileSystemWatcher> _libraryWatchers = new();
    private readonly HashSet<string> _libraryRefreshInProgress = new(StringComparer.OrdinalIgnoreCase);
    private bool _libraryMonitorEnabled;

    private string _previewText = "あA";
    private CancellationTokenSource? _previewTextRefreshCts;
    private int _previewRefreshVersion;
    private string _statusMessage = "";
    private TextPresetItemViewModel? _selectedPreset;
    private string _selectedCategory = "全て";
    private string _selectedPresetTab = "T";
    private string _searchQuery = "";
    private string _sortMode = "追加順";
    private bool _sortDescending = true;
    private bool _isReorderMode;
    private List<string> _templateItemTypeFilter = new();
    private bool _templateOnlySelectedTypes;
    private int _presetTabIndex;
    private bool _isSelectionMode;
    private double _presetCardWidth = 150;
    private List<string> _textCategories = new();
    private List<string> _effectCategories = new();
    private List<string> _templateCategories = new();

    // 5つの適用対象切り替えトグル (エフェクト / フォント / 位置 / サイズ / その他)
    private bool _applyEffectsTarget = true;
    private bool _applyFontTarget = true;
    private bool _applyPositionTarget = true;
    private bool _applySizeTarget = true;
    private bool _applyOthersTarget = true;
    private bool _isSimpleView;
    private int _autoColorStartEffect;

    public string Title => "テキストプリセット";

    public bool CanSuspend => true;

    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning disable CS0067
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested;
#pragma warning restore CS0067

    public ObservableCollection<TextPresetItemViewModel> Presets { get; } = new();

    public ObservableCollection<LibraryAsset> LibraryAssets { get; } = new();
    public ObservableCollection<LibraryAsset> VisibleLibraryAssets { get; } = new();
    public ObservableCollection<string> LibraryWatchFolders { get; } = new();

    public bool LibraryMonitorEnabled
    {
        get => _libraryMonitorEnabled;
        set
        {
            if (_libraryMonitorEnabled == value) return;
            _libraryMonitorEnabled = value;
            OnPropertyChanged();
            UpdateLibraryWatchers();
            ScheduleSettingsSave();
        }
    }

    public ObservableCollection<CategoryItemViewModel> Categories { get; } = new();

    public string SearchQuery
    {
        get => _searchQuery;
        set { if (_searchQuery == value) return; _searchQuery = value ?? ""; OnPropertyChanged(); ApplyCategoryFilter(); ScheduleSettingsSave(); }
    }

    public string SortMode => _sortMode;
    public bool SortDescending => _sortDescending;

    public bool IsReorderMode
    {
        get => _isReorderMode;
        private set
        {
            if (_isReorderMode == value) return;
            _isReorderMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusBarMessage));
        }
    }

    public void SetFilterSettings(string sortMode, bool descending, IEnumerable<string> itemTypes, bool onlySelectedTypes)
    {
        _sortMode = sortMode == "名前順" ? "名前順" : "追加順";
        _sortDescending = descending;
        _templateItemTypeFilter = itemTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _templateOnlySelectedTypes = onlySelectedTypes;
        OnPropertyChanged(nameof(SortMode));
        OnPropertyChanged(nameof(SortDescending));
        ApplyCategoryFilter();
        ScheduleSettingsSave();
    }

    public void OpenFilterWindow()
    {
        var dialog = new PresetFilterWindow(SelectedPresetTab == "TPL", _sortMode, _sortDescending, _templateItemTypeFilter, _templateOnlySelectedTypes);
        if (dialog.ShowDialog() == true)
            SetFilterSettings(dialog.SortMode, dialog.SortDescending, dialog.SelectedTypes, dialog.OnlySelectedTypes);
    }

    public double PresetCardWidth
    {
        get => _presetCardWidth;
        set
        {
            double adjusted = Math.Clamp(value, 80, 360);
            if (Math.Abs(_presetCardWidth - adjusted) < 0.1) return;
            _presetCardWidth = adjusted;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PresetPreviewHeight));
            ScheduleSettingsSave();
        }
    }

    public bool IsSimpleView
    {
        get => _isSimpleView;
        set
        {
            if (_isSimpleView == value) return;
            _isSimpleView = value;
            OnPropertyChanged();
            ScheduleSettingsSave();
        }
    }

    public int AutoColorStartEffect
    {
        get => _autoColorStartEffect;
        set
        {
            int adjusted = Math.Clamp(value, 0, 10);
            if (_autoColorStartEffect == adjusted) return;
            _autoColorStartEffect = adjusted;
            ScheduleSettingsSave();
        }
    }

    public GridLength PresetPreviewHeight => new(_presetCardWidth * 0.56);

    public bool IsSelectionMode
    {
        get => _isSelectionMode;
        set
        {
            if (_isSelectionMode == value) return;
            _isSelectionMode = value;
            if (!value)
                foreach (var item in _itemViewModelCache.Values) item.IsSelected = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPresetCount));
            OnPropertyChanged(nameof(StatusBarMessage));
        }
    }

    public int SelectedPresetCount => _itemViewModelCache.Values.Count(item => item.IsSelected);

    public void TogglePresetSelection(TextPresetItemViewModel item)
    {
        if (!IsSelectionMode) return;
        item.IsSelected = !item.IsSelected;
        OnPropertyChanged(nameof(SelectedPresetCount));
        OnPropertyChanged(nameof(StatusBarMessage));
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory != value)
            {
                _selectedCategory = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedCategoryItem));
                ApplyCategoryFilter();
                if (SelectedPresetTab != "LIB") _ = RefreshAllPreviewsAsync();
                ScheduleSettingsSave();
            }
        }
    }

    public CategoryItemViewModel? SelectedCategoryItem
    {
        get => Categories.FirstOrDefault(c => string.Equals(c.Name, SelectedCategory, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value != null && SelectedCategory != value.Name) SelectedCategory = value.Name;
        }
    }

    public int PresetTabIndex
    {
        get => _presetTabIndex;
        set
        {
            int index = value switch { 1 => 1, 2 => 2, 3 => 3, _ => 0 };
            if (_presetTabIndex != index)
            {
                _presetTabIndex = index;
                OnPropertyChanged();
                SelectedPresetTab = index switch { 1 => "FX", 2 => "TPL", 3 => "LIB", _ => "T" };
            }
        }
    }

    public string SelectedPresetTab
    {
        get => _selectedPresetTab;
        set
        {
            if (_selectedPresetTab != value)
            {
                _selectedPresetTab = value switch { "FX" => "FX", "TPL" => "TPL", "LIB" => "LIB", _ => "T" };
                OnPropertyChanged();
                int index = _selectedPresetTab switch { "FX" => 1, "TPL" => 2, "LIB" => 3, _ => 0 };
                if (_presetTabIndex != index)
                {
                    _presetTabIndex = index;
                    OnPropertyChanged(nameof(PresetTabIndex));
                }
                _selectedCategory = "全て";
                OnPropertyChanged(nameof(SelectedCategory));
                UpdateCategories();
                ApplyCategoryFilter();
                if (_selectedPresetTab != "LIB") _ = RefreshAllPreviewsAsync();
                ScheduleSettingsSave();
            }
        }
    }

    public string PreviewText
    {
        get => _previewText;
        set
        {
            string limitedValue = new string((value ?? string.Empty).Take(3).ToArray());
            if (_previewText != limitedValue)
            {
                _previewText = limitedValue;
                OnPropertyChanged();
                ScheduleSettingsSave();
                _previewTextRefreshCts?.Cancel();
                _previewTextRefreshCts?.Dispose();
                _previewTextRefreshCts = new CancellationTokenSource();
                int version = Interlocked.Increment(ref _previewRefreshVersion);
                _ = RefreshPreviewsAfterTypingAsync(_previewTextRefreshCts.Token, version);
            }
        }
    }

    private async Task RefreshPreviewsAfterTypingAsync(CancellationToken cancellationToken, int version)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
                await RefreshAllPreviewsAsync(version);
        }
        catch (OperationCanceledException) { }
    }

    private System.Windows.Media.Color _previewBackgroundColor = System.Windows.Media.Color.FromRgb(0x0C, 0x0C, 0x0E);

    public System.Windows.Media.Color PreviewBackgroundColor
    {
        get => _previewBackgroundColor;
        set
        {
            if (_previewBackgroundColor != value)
            {
                _previewBackgroundColor = value;
                // Update the brush to reflect the new color
                PreviewBackgroundBrush = new System.Windows.Media.SolidColorBrush(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewBackgroundBrush));
                ScheduleSettingsSave();
                _ = RefreshAllPreviewsAsync(reuseRenderedFrames: true);
            }
        }
    }

    private System.Windows.Media.Brush _previewBackgroundBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0C, 0x0C, 0x0E));

    public System.Windows.Media.Brush PreviewBackgroundBrush
    {
        get => _previewBackgroundBrush;
        private set
        {
            if (_previewBackgroundBrush != value)
            {
                _previewBackgroundBrush = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusBarMessage));
        }
    }

    public string StatusBarMessage => IsSelectionMode
        ? $"{StatusMessage}　(選択中: {SelectedPresetCount}件)"
        : IsReorderMode
            ? $"{StatusMessage}　(並び替え中: カードをドラッグ、カード外をクリックで終了)"
            : StatusMessage;

    public void BeginReorderMode()
    {
        if (IsSelectionMode) return;
        IsReorderMode = true;
        StatusMessage = "並び替えモードです。カードをドラッグして順序を変更できます";
    }

    public void EndReorderMode()
    {
        if (!IsReorderMode) return;
        IsReorderMode = false;
        StatusMessage = "並び替えを終了しました";
    }

    public void ReorderPresets(TextPresetItemViewModel source, TextPresetItemViewModel target)
    {
        if (!IsReorderMode || ReferenceEquals(source, target)) return;
        var visibleOrder = Presets.ToList();
        int sourceIndex = visibleOrder.IndexOf(source);
        int targetIndex = visibleOrder.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0) return;

        (visibleOrder[sourceIndex], visibleOrder[targetIndex]) = (visibleOrder[targetIndex], visibleOrder[sourceIndex]);

        var visibleIds = visibleOrder.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var globalOrder = _rawPresets
            .OrderBy(preset => preset.SortOrder ?? int.MinValue)
            .ThenByDescending(preset => preset.CreatedAt)
            .ToList();
        int anchor = globalOrder.FindIndex(preset => visibleIds.Contains(preset.Id));
        if (anchor < 0) return;
        globalOrder.RemoveAll(preset => visibleIds.Contains(preset.Id));
        anchor = Math.Clamp(anchor, 0, globalOrder.Count);
        globalOrder.InsertRange(anchor, visibleOrder.Select(item => item.Preset));
        for (int index = 0; index < globalOrder.Count; index++) globalOrder[index].SortOrder = index;

        _sortMode = "追加順";
        _sortDescending = true;
        OnPropertyChanged(nameof(SortMode));
        OnPropertyChanged(nameof(SortDescending));
        PresetStorage.SavePresets(_rawPresets);
        ApplyCategoryFilter();
        StatusMessage = "プリセットの並び順を保存しました";
    }

    public TextPresetItemViewModel? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            _selectedPreset = value;
            OnPropertyChanged();
        }
    }

    public bool ApplyEffectsTarget
    {
        get => _applyEffectsTarget;
        set
        {
            if (_applyEffectsTarget != value)
            {
                _applyEffectsTarget = value;
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public bool ApplyFontTarget
    {
        get => _applyFontTarget;
        set
        {
            if (_applyFontTarget != value)
            {
                _applyFontTarget = value;
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public bool ApplyPositionTarget
    {
        get => _applyPositionTarget;
        set
        {
            if (_applyPositionTarget != value)
            {
                _applyPositionTarget = value;
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public bool ApplySizeTarget
    {
        get => _applySizeTarget;
        set
        {
            if (_applySizeTarget != value)
            {
                _applySizeTarget = value;
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public bool ApplyOthersTarget
    {
        get => _applyOthersTarget;
        set
        {
            if (_applyOthersTarget != value)
            {
                _applyOthersTarget = value;
                OnPropertyChanged();
                ScheduleSettingsSave();
            }
        }
    }

    public ICommand ApplyCommand { get; }
    public ICommand ApplyEffectOnlyCommand { get; }
    public ICommand ApplyFontOnlyCommand { get; }
    public ICommand ApplyPositionOnlyCommand { get; }
    public ICommand ApplySizeOnlyCommand { get; }
    public ICommand ApplyOthersOnlyCommand { get; }
    public ICommand CreatePresetCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand EditPatternCommand { get; }
    public ICommand OpenPatternOrderCommand { get; }
    public ICommand UpdatePatternFromSelectedCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DeletePatternCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ToggleSelectionModeCommand { get; }
    public ICommand DeleteSelectedPresetsCommand { get; }
    public ICommand ExportSelectedPresetsCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AddLibraryFilesCommand { get; }
    public ICommand SelectPatternSlotCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand SelectPresetTabCommand { get; }
    public ICommand ManageCategoriesCommand { get; }
    public ICommand AddCategoryCommand { get; }
    public ICommand RenameCategoryCommand { get; }
    public ICommand DeleteCategoryCommand { get; }
    public ICommand LoadYmmTemplatesCommand { get; }
    public ICommand OpenPreviewStorageDirectoryCommand { get; }
    public ICommand ChangePreviewStorageDirectoryCommand { get; }
    public ICommand DeleteUnusedPreviewDataCommand { get; }
    public string PreviewStorageDirectory => PresetStorage.PreviewImageDirectory;

    public PreviewPresetViewModel()
    {
        _settingsSaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _settingsSaveTimer.Tick += (_, _) =>
        {
            _settingsSaveTimer.Stop();
            SavePluginSettings();
        };
        _libraryWatchRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _libraryWatchRefreshTimer.Tick += async (_, _) =>
        {
            _libraryWatchRefreshTimer.Stop();
            await RefreshLibraryWatchAssetsAsync();
        };
        RestorePluginSettings();

        ApplyCommand = new RelayCommand(p => ApplyPresetByCurrentToggles(p as TextPresetItemViewModel));

        ApplyEffectOnlyCommand = new RelayCommand(p => ApplyPresetExplicit(p as TextPresetItemViewModel, true, false, false, false, false));
        ApplyFontOnlyCommand = new RelayCommand(p => ApplyPresetExplicit(p as TextPresetItemViewModel, false, true, false, false, false));
        ApplyPositionOnlyCommand = new RelayCommand(p => ApplyPresetExplicit(p as TextPresetItemViewModel, false, false, true, false, false));
        ApplySizeOnlyCommand = new RelayCommand(p => ApplyPresetExplicit(p as TextPresetItemViewModel, false, false, false, true, false));
        ApplyOthersOnlyCommand = new RelayCommand(p => ApplyPresetExplicit(p as TextPresetItemViewModel, false, false, false, false, true));

        CreatePresetCommand = new RelayCommand(_ =>
        {
            if (SelectedPresetTab == "LIB") AddLibraryFiles();
            else if (SelectedPresetTab == "FX") CreateEffectPreset();
            else if (SelectedPresetTab == "TPL") CreateItemTemplate();
            else CreatePreset();
        });
        DuplicateCommand = new RelayCommand(p => DuplicatePreset(p as TextPresetItemViewModel));
        RenameCommand = new RelayCommand(p => RenamePreset(p as TextPresetItemViewModel));
        EditPatternCommand = new RelayCommand(p => EditPattern(p as TextPresetItemViewModel));
        OpenPatternOrderCommand = new RelayCommand(p => OpenPatternOrder(p as TextPresetItemViewModel));
        UpdatePatternFromSelectedCommand = new RelayCommand(p => UpdatePatternFromSelected(p as TextPresetItemViewModel));
        DeleteCommand = new RelayCommand(p => DeletePreset(p as TextPresetItemViewModel));
        DeletePatternCommand = new RelayCommand(p => DeleteCurrentPattern(p as TextPresetItemViewModel));
        ExportCommand = new RelayCommand(p => ExportPreset(p as TextPresetItemViewModel));
        ImportCommand = new RelayCommand(_ => ImportPreset());
        ToggleSelectionModeCommand = new RelayCommand(_ => IsSelectionMode = !IsSelectionMode);
        DeleteSelectedPresetsCommand = new RelayCommand(_ => DeleteSelectedPresets());
        ExportSelectedPresetsCommand = new RelayCommand(_ => ExportSelectedPresets());
        RefreshCommand = new RelayCommand(_ =>
        {
            if (SelectedPresetTab == "LIB")
            {
                ApplyCategoryFilter();
                _ = RefreshLibraryWatchAssetsAsync();
            }
            else _ = RefreshAllPreviewsAsync();
        });
        AddLibraryFilesCommand = new RelayCommand(_ => AddLibraryFiles());

        SelectPatternSlotCommand = new RelayCommand(param =>
        {
            if (param is ValueTuple<TextPresetItemViewModel, int> tuple)
            {
                SwitchPattern(tuple.Item1, tuple.Item2);
            }
        });

        SelectCategoryCommand = new RelayCommand(cat =>
        {
            if (cat is string categoryName)
            {
                SelectedCategory = categoryName;
            }
        });

        SelectPresetTabCommand = new RelayCommand(tab => SelectedPresetTab = tab as string ?? "T");
        ManageCategoriesCommand = new RelayCommand(_ => ManageCategories());
        AddCategoryCommand = new RelayCommand(_ => AddCategory());
        RenameCategoryCommand = new RelayCommand(category => RenameCategory(category as string));
        DeleteCategoryCommand = new RelayCommand(category => DeleteCategory(category as string));
        LoadYmmTemplatesCommand = new RelayCommand(_ => LoadYmmTemplates());
        OpenPreviewStorageDirectoryCommand = new RelayCommand(_ => OpenPreviewStorageDirectory());
        ChangePreviewStorageDirectoryCommand = new RelayCommand(_ => ChangePreviewStorageDirectory());
        DeleteUnusedPreviewDataCommand = new RelayCommand(_ => DeleteUnusedPreviewData());

        LoadPresets();
        Application.Current?.Dispatcher.BeginInvoke(new Action(OfferInterruptedPreviewRegeneration));
    }

    public ToolState SaveState()
    {
        SavePluginSettings();
        return new ToolState();
    }
    public void LoadState(ToolState stateData) { }

    private void RestorePluginSettings()
    {
        var settings = PresetStorage.LoadPluginSettings();
        _presetCardWidth = Math.Clamp(settings.PresetCardWidth, 80, 360);
        _isSimpleView = settings.IsSimpleView;
        _autoColorStartEffect = Math.Clamp(settings.AutoColorStartEffect, 0, 10);
        _previewText = new string((settings.PreviewText ?? "あA").Take(3).ToArray());
        _selectedPresetTab = settings.SelectedPresetTab switch { "FX" => "FX", "TPL" => "TPL", "LIB" => "LIB", _ => "T" };
        _presetTabIndex = _selectedPresetTab switch { "FX" => 1, "TPL" => 2, "LIB" => 3, _ => 0 };
        _libraryMonitorEnabled = settings.LibraryMonitorEnabled;
        LibraryWatchFolders.Clear();
        foreach (string folder in settings.LibraryWatchFolders ?? new List<string>())
            if (!string.IsNullOrWhiteSpace(folder) && !LibraryWatchFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) LibraryWatchFolders.Add(folder);
        _selectedCategory = string.IsNullOrWhiteSpace(settings.SelectedCategory) ? "全て" : settings.SelectedCategory;
        _searchQuery = settings.SearchQuery ?? "";
        _sortMode = settings.SortMode == "名前順" ? "名前順" : "追加順";
        _sortDescending = settings.SortDescending;
        _templateItemTypeFilter = settings.TemplateItemTypeFilter ?? new List<string>();
        _templateOnlySelectedTypes = settings.TemplateOnlySelectedTypes;
        _applyEffectsTarget = settings.ApplyEffectsTarget;
        _applyFontTarget = settings.ApplyFontTarget;
        _applyPositionTarget = settings.ApplyPositionTarget;
        _applySizeTarget = settings.ApplySizeTarget;
        _applyOthersTarget = settings.ApplyOthersTarget;
        try
        {
            _previewBackgroundColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.PreviewBackgroundColor);
            _previewBackgroundBrush = new System.Windows.Media.SolidColorBrush(_previewBackgroundColor);
        }
        catch { }
    }

    private void ScheduleSettingsSave()
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void SavePluginSettings()
    {
        PresetStorage.SavePluginSettings(new PreviewPluginSettings
        {
            PresetCardWidth = _presetCardWidth,
            PreviewBackgroundColor = _previewBackgroundColor.ToString(),
            IsSimpleView = _isSimpleView,
            AutoColorStartEffect = _autoColorStartEffect,
            PreviewText = _previewText,
            SelectedPresetTab = _selectedPresetTab,
            SelectedCategory = _selectedCategory,
            SearchQuery = _searchQuery,
            SortMode = _sortMode,
            SortDescending = _sortDescending,
            TemplateItemTypeFilter = _templateItemTypeFilter.ToList(),
            TemplateOnlySelectedTypes = _templateOnlySelectedTypes,
            LibraryMonitorEnabled = _libraryMonitorEnabled,
            LibraryWatchFolders = LibraryWatchFolders.ToList(),
            ApplyEffectsTarget = _applyEffectsTarget,
            ApplyFontTarget = _applyFontTarget,
            ApplyPositionTarget = _applyPositionTarget,
            ApplySizeTarget = _applySizeTarget,
            ApplyOthersTarget = _applyOthersTarget
        });
    }

    private void LoadPresets()
    {
        _rawPresets = PresetStorage.LoadPresets();
        LibraryAssets.Clear();
        foreach (var asset in PresetStorage.LoadLibraryAssets())
        {
            if (!File.Exists(asset.Path)) continue;
            LibraryAssets.Add(asset);
        }
        _ = LoadSavedLibraryThumbnailsAsync();
        _textCategories = PresetStorage.LoadCategories(forEffects: false);
        _effectCategories = PresetStorage.LoadCategories(forEffects: true);
        _templateCategories = PresetStorage.LoadCategories(forEffects: false, forTemplates: true);
        _itemViewModelCache.Clear();
        foreach (var p in _rawPresets)
        {
            var itemVM = new TextPresetItemViewModel(p);
            _itemViewModelCache[p.Id] = itemVM;
        }

        UpdateCategories();
        ApplyCategoryFilter();
        UpdateLibraryWatchers();
    }

    public void UpdateCategories()
    {
        if (SelectedPresetTab == "LIB")
        {
            var libraryCategories = new[] { "全て", "BGM", "SE", "動画", "画像" };
            if (!libraryCategories.Contains(SelectedCategory, StringComparer.OrdinalIgnoreCase))
            {
                _selectedCategory = "全て";
                OnPropertyChanged(nameof(SelectedCategory));
            }
            Categories.Clear();
            foreach (string category in libraryCategories)
                Categories.Add(new CategoryItemViewModel(category, string.Equals(category, SelectedCategory, StringComparison.OrdinalIgnoreCase)));
            OnPropertyChanged(nameof(SelectedCategoryItem));
            return;
        }
        var categorySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (SelectedPresetTab == "FX")
        {
            categorySet.UnionWith(_effectCategories);
            categorySet.UnionWith(_textCategories);
        }
        else if (SelectedPresetTab == "TPL") categorySet.UnionWith(_templateCategories);
        else
        {
            categorySet.UnionWith(_textCategories);
        }
        foreach (var p in _rawPresets.Where(PresetMatchesCurrentTab))
        {
            if (SelectedPresetTab == "TPL" && IsUncategorizedMixedItemTypeTemplate(p)) continue;
            foreach (var c in p.GetCategoryList())
            {
                categorySet.Add(c);
            }
        }

        var savedOrder = SelectedPresetTab switch
        {
            "FX" => _effectCategories.Concat(_textCategories),
            "TPL" => _templateCategories,
            _ => _textCategories
        };
        var ordered = savedOrder.Where(categorySet.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var category in categorySet.OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase))
            if (!ordered.Contains(category, StringComparer.OrdinalIgnoreCase)) ordered.Add(category);

        if (SelectedPresetTab == "FX")
        {
            _effectCategories = ordered.ToList();
        }
        else if (SelectedPresetTab == "TPL") _templateCategories = ordered.ToList();
        else _textCategories = ordered.ToList();

        var sorted = new List<string> { "全て" };
        sorted.AddRange(ordered);

        // 既存の選択がなければ "全て"
        if (!sorted.Contains(SelectedCategory, StringComparer.OrdinalIgnoreCase))
        {
            _selectedCategory = "全て";
            OnPropertyChanged(nameof(SelectedCategory));
        }

        Categories.Clear();
        foreach (var cat in sorted)
        {
            Categories.Add(new CategoryItemViewModel(cat, string.Equals(cat, SelectedCategory, StringComparison.OrdinalIgnoreCase)));
        }
        OnPropertyChanged(nameof(SelectedCategoryItem));
    }

    public void MoveCategory(int oldIndex, int newIndex)
    {
        if (SelectedPresetTab == "LIB") return;
        if (oldIndex <= 0 || newIndex <= 0 || oldIndex >= Categories.Count || newIndex >= Categories.Count || oldIndex == newIndex)
            return;

        Categories.Move(oldIndex, newIndex);
        var reordered = Categories.Skip(1).Select(category => category.Name).ToList();
        if (SelectedPresetTab == "FX")
        {
            _effectCategories = reordered.ToList();
            PresetStorage.SaveCategories(true, _effectCategories);
        }
        else if (SelectedPresetTab == "TPL")
        {
            _templateCategories = reordered;
            PresetStorage.SaveCategories(false, _templateCategories, forTemplates: true);
        }
        else
        {
            _textCategories = reordered;
            PresetStorage.SaveCategories(false, _textCategories);
        }
    }

    public void ApplyCategoryFilter()
    {
        foreach (var cat in Categories)
        {
            cat.IsSelected = string.Equals(cat.Name, SelectedCategory, StringComparison.OrdinalIgnoreCase);
        }

        if (SelectedPresetTab == "LIB")
        {
            IEnumerable<LibraryAsset> assets = LibraryAssets.Where(asset =>
                (SelectedCategory == "全て" || string.Equals(asset.Type, SelectedCategory, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(SearchQuery) || asset.Name.Contains(SearchQuery.Trim(), StringComparison.CurrentCultureIgnoreCase)));
            assets = (_sortMode, _sortDescending) switch
            {
                ("名前順", false) => assets.OrderBy(asset => asset.Name, StringComparer.CurrentCultureIgnoreCase),
                ("名前順", true) => assets.OrderByDescending(asset => asset.Name, StringComparer.CurrentCultureIgnoreCase),
                ("追加順", false) => assets.OrderByDescending(asset => asset.AddedAt),
                _ => assets.OrderByDescending(asset => asset.AddedAt)
            };
            VisibleLibraryAssets.Clear();
            foreach (var asset in assets) VisibleLibraryAssets.Add(asset);
            Presets.Clear();
            return;
        }

        VisibleLibraryAssets.Clear();
        IEnumerable<TextPreset> filtered = _rawPresets.Where(p => PresetMatchesCurrentTab(p) && MatchesSelectedCategory(p) &&
            MatchesSearch(p) && MatchesItemTypeFilter(p));
        filtered = (_sortMode, _sortDescending) switch
        {
            ("名前順", false) => filtered.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            ("名前順", true) => filtered.OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
            ("追加順", false) => filtered.OrderByDescending(p => p.SortOrder ?? int.MinValue).ThenBy(p => p.CreatedAt),
            _ => filtered.OrderBy(p => p.SortOrder ?? int.MinValue).ThenByDescending(p => p.CreatedAt)
        };
        var orderedPresets = filtered.ToList();
        var visiblePresetIds = orderedPresets.Select(preset => preset.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var cached in _itemViewModelCache)
        {
            if (!visiblePresetIds.Contains(cached.Key)) cached.Value.ReleaseCachedThumbnails();
        }

        Presets.Clear();
        foreach (var p in orderedPresets)
        {
            if (!_itemViewModelCache.TryGetValue(p.Id, out var vm))
            {
                vm = new TextPresetItemViewModel(p);
                _itemViewModelCache[p.Id] = vm;
            }
            Presets.Add(vm);
        }
    }

    private bool MatchesSearch(TextPreset preset)
    {
        string query = SearchQuery.Trim();
        if (query.Length == 0) return true;
        return preset.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               preset.GetCategoryList().Any(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
               preset.GetTagList().Any(value => value.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private bool MatchesItemTypeFilter(TextPreset preset)
    {
        if (SelectedPresetTab != "TPL" || _templateItemTypeFilter.Count == 0) return true;
        var types = preset.ActivePattern.Items.Select(item => item.GetType().Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (_templateOnlySelectedTypes)
            return types.Count > 0 && types.All(type => _templateItemTypeFilter.Contains(type, StringComparer.OrdinalIgnoreCase));
        return types.Any(type => _templateItemTypeFilter.Contains(type, StringComparer.OrdinalIgnoreCase));
    }

    private bool MatchesSelectedCategory(TextPreset preset)
    {
        if (SelectedPresetTab == "TPL" && IsUncategorizedMixedItemTypeTemplate(preset))
            return string.IsNullOrEmpty(SelectedCategory) || string.Equals(SelectedCategory, "全て", StringComparison.OrdinalIgnoreCase);
        return preset.MatchesCategory(SelectedCategory);
    }

    private static bool IsMixedItemTypeTemplate(TextPreset preset) => preset.IsItemTemplate &&
        preset.Patterns.SelectMany(pattern => pattern.Items)
            .Select(item => item.GetType())
            .Distinct()
            .Skip(1)
            .Any();

    private static bool IsUncategorizedMixedItemTypeTemplate(TextPreset preset) =>
        IsMixedItemTypeTemplate(preset) && preset.GetCategoryList().Count == 0;

    private bool PresetMatchesCurrentTab(TextPreset preset) => SelectedPresetTab switch
    {
        "FX" => !preset.IsItemTemplate,
        "TPL" => preset.IsItemTemplate,
        _ => !preset.IsEffectPreset && !preset.IsItemTemplate
    };

    private bool PresetCategoryIsManagedInCurrentTab(TextPreset preset) => SelectedPresetTab switch
    {
        "FX" => preset.IsEffectPreset,
        "TPL" => preset.IsItemTemplate,
        _ => !preset.IsEffectPreset && !preset.IsItemTemplate
    };

    public async Task RefreshAllPreviewsAsync(bool reuseRenderedFrames = false) =>
        await RefreshAllPreviewsAsync(Interlocked.Increment(ref _previewRefreshVersion), reuseRenderedFrames);

    private async Task RefreshAllPreviewsAsync(int version, bool reuseRenderedFrames = false)
    {
        if (reuseRenderedFrames)
        {
            _renderer.ClearColorizedCache();
        }
        else
        {
            _renderer.ClearCache();
        }
        foreach (var itemVM in Presets.ToList())
        {
            if (version != Volatile.Read(ref _previewRefreshVersion)) return;
            await itemVM.RenderAllPatternsAsync(_renderer, PreviewText, PreviewBackgroundColor);
        }
    }

    public async Task RenderSinglePreviewAsync(TextPresetItemViewModel itemVM)
    {
        _renderer.ClearCache();
        await itemVM.RenderAllPatternsAsync(_renderer, PreviewText, PreviewBackgroundColor);
    }

    public async Task EnsureAudioTemplatePreviewAsync(TextPresetItemViewModel itemVM)
    {
        var pattern = itemVM.ActivePattern;
        if (!itemVM.IsItemTemplate || pattern.Items.Count == 0 ||
            !pattern.Items.All(item => item is YukkuriMovieMaker.Project.Items.AudioItem) ||
            (!string.IsNullOrWhiteSpace(pattern.PreviewAudioPath) && File.Exists(pattern.PreviewAudioPath))) return;

        string key = $"{itemVM.Id}:{pattern.Slot}";
        if (!_audioPreviewGenerationTasks.TryGetValue(key, out var task))
        {
            task = GenerateAndSaveTemplatePreviewAsync(itemVM.Preset, pattern, pattern.ThumbnailTimeSeconds);
            _audioPreviewGenerationTasks[key] = task;
        }

        try
        {
            await task;
            itemVM.NotifyPreviewChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = $"音声プレビューを生成できませんでした: {ex.GetBaseException().Message}";
        }
        finally
        {
            _audioPreviewGenerationTasks.Remove(key);
        }
    }

    public void SwitchPattern(TextPresetItemViewModel itemVM, int slot)
    {
        if (itemVM.SelectSlot(slot))
        {
            if (itemVM.Thumbnail == null)
            {
                _ = itemVM.RenderActiveThumbnailAsync(_renderer, PreviewText, PreviewBackgroundColor);
            }
            StatusMessage = $"「{itemVM.Name}」をパターン {slot} に切り替えました";
            PresetStorage.SavePresets(_rawPresets);
        }
    }

    private void OpenPatternOrder(TextPresetItemViewModel? itemVM)
    {
        if (itemVM == null || itemVM.Preset.Patterns.Count < 2) return;

        var activePattern = itemVM.Preset.ActivePattern;
        var window = new PatternOrderWindow(itemVM.Preset);
        if (window.ShowDialog() != true) return;

        var orderedPatterns = window.OrderedPatterns;
        var colors = window.PatternColors;
        for (int index = 0; index < orderedPatterns.Count; index++)
        {
            orderedPatterns[index].Slot = index + 1;
            orderedPatterns[index].SampleColor = colors[orderedPatterns[index]];
        }

        itemVM.Preset.Patterns = orderedPatterns;
        itemVM.Preset.ActivePatternSlot = Math.Max(1, orderedPatterns.IndexOf(activePattern) + 1);
        PresetStorage.SavePresets(_rawPresets);
        itemVM.SyncPatternsCollection();
        itemVM.NotifyPreviewChanged();
        StatusMessage = $"「{itemVM.Name}」のパターン順序と色を保存しました";
    }

    public void CyclePattern(TextPresetItemViewModel itemVM, bool forward)
    {
        bool changed = forward ? itemVM.NextPattern() : itemVM.PrevPattern();
        if (changed)
        {
            if (itemVM.Thumbnail == null)
            {
                _ = itemVM.RenderActiveThumbnailAsync(_renderer, PreviewText, PreviewBackgroundColor);
            }
            StatusMessage = $"「{itemVM.Name}」をパターン {itemVM.ActiveSlot} に切り替えました";
            PresetStorage.SavePresets(_rawPresets);
        }
    }

    private void ApplyPresetByCurrentToggles(TextPresetItemViewModel? itemVM)
    {
        if (itemVM == null) return;

        if (itemVM.IsItemTemplate)
        {
            StatusMessage = PresetApplier.ApplyItemTemplateToSelected(itemVM.Name, itemVM.ActivePattern.Items);
            return;
        }

        if (SelectedPresetTab == "FX")
        {
            var pattern = itemVM.ActivePattern;
            (bool success, string message) result;
            if (itemVM.Preset.IsEffectPreset)
            {
                result = PresetApplier.ApplyEffectPresetToSelected(itemVM.Name, pattern, ApplyPositionTarget, ApplyEffectsTarget);
            }
            else
            {
                result = PresetApplier.ApplyTextPresetToEffectTargets(
                    itemVM.Name,
                    pattern,
                    ApplyEffectsTarget && pattern.Apply.Effects,
                    ApplyPositionTarget && pattern.Apply.Position);
            }
            StatusMessage = result.message;
            return;
        }

        bool ef = ApplyEffectsTarget && itemVM.ActivePattern.Apply.Effects;
        bool fn = ApplyFontTarget && itemVM.ActivePattern.Apply.Font;
        bool pos = ApplyPositionTarget && itemVM.ActivePattern.Apply.Position;
        bool sz = ApplySizeTarget && itemVM.ActivePattern.Apply.Size;
        bool oth = ApplyOthersTarget && itemVM.ActivePattern.Apply.Others;

        if (!ef && !fn && !pos && !sz && !oth)
        {
            StatusMessage = "適用対象がすべてOFFになっています";
            return;
        }

        var (success, message) = PresetApplier.ApplyPresetToSelected(itemVM.Preset, ef, fn, pos, sz, oth);
        StatusMessage = message;
    }

    private void ApplyPresetExplicit(
        TextPresetItemViewModel? itemVM,
        bool applyEffects,
        bool applyFont,
        bool applyPosition,
        bool applySize,
        bool applyOthers)
    {
        if (itemVM == null) return;

        if (itemVM.IsItemTemplate)
        {
            StatusMessage = PresetApplier.ApplyItemTemplateToSelected(itemVM.Name, itemVM.ActivePattern.Items);
            return;
        }

        if (SelectedPresetTab == "FX")
        {
            var pattern = itemVM.ActivePattern;
            (bool success, string message) result;
            if (itemVM.Preset.IsEffectPreset)
            {
                result = PresetApplier.ApplyEffectPresetToSelected(itemVM.Name, pattern, applyPosition, applyEffects);
            }
            else
            {
                result = PresetApplier.ApplyTextPresetToEffectTargets(
                    itemVM.Name,
                    pattern,
                    applyEffects && pattern.Apply.Effects,
                    applyPosition && pattern.Apply.Position);
            }
            StatusMessage = result.message;
            return;
        }

        if (itemVM.Preset.IsEffectPreset)
        {
            var result = PresetApplier.ApplyEffectPresetToSelected(itemVM.Name, itemVM.ActivePattern, true);
            StatusMessage = result.message;
            return;
        }

        var (success, message) = PresetApplier.ApplyPresetToSelected(
            itemVM.Preset,
            applyEffects,
            applyFont,
            applyPosition,
            applySize,
            applyOthers
        );
        StatusMessage = message;
    }

    private void EditPattern(TextPresetItemViewModel? itemVM)
    {
        if (itemVM == null) return;
        if (itemVM.IsItemTemplate)
        {
            CreateItemTemplate(itemVM.Preset, itemVM.ActivePattern);
            return;
        }
        if (itemVM.Preset.IsEffectPreset)
        {
            CreateEffectPreset(itemVM.Preset, itemVM.ActivePattern);
            return;
        }

        var pattern = itemVM.ActivePattern;
        var textParentPresets = _rawPresets.Where(p => !p.IsEffectPreset && !p.IsItemTemplate).ToList();
        var dialog = new CreatePresetDialog(textParentPresets, itemVM.Preset, pattern, 1, _autoColorStartEffect)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            AutoColorStartEffect = dialog.AutoColorStartEffect;
            var originalPreset = itemVM.Preset;
            var targetPreset = dialog.TargetPreset;
            if (targetPreset == null)
            {
                StatusMessage = "親テンプレートを選択してください";
                return;
            }
            if (!ReferenceEquals(originalPreset, targetPreset))
            {
                var matchingPattern = targetPreset.Patterns.FirstOrDefault(p => string.Equals(p.SampleColor, dialog.SampleColorHex, StringComparison.OrdinalIgnoreCase));
                int destinationSlot = matchingPattern?.Slot ?? targetPreset.GetNextFreeSlot();
                if (destinationSlot < 0)
                {
                    StatusMessage = $"移動先テンプレートのパターン数が上限({PresetPattern.MaxPatternCount})に達しています";
                    return;
                }

                originalPreset.Patterns.Remove(pattern);
                if (matchingPattern != null) targetPreset.Patterns.Remove(matchingPattern);
                pattern.Slot = destinationSlot;
                targetPreset.Patterns.Add(pattern);
                targetPreset.Patterns = targetPreset.Patterns.OrderBy(p => p.Slot).ToList();
                targetPreset.ActivePatternSlot = destinationSlot;

                if (originalPreset.Patterns.Count == 0)
                {
                    _rawPresets.Remove(originalPreset);
                    _itemViewModelCache.Remove(originalPreset.Id);
                }
                else if (_itemViewModelCache.TryGetValue(originalPreset.Id, out var originalVM))
                    originalVM.SyncPatternsCollection();

                if (!_itemViewModelCache.TryGetValue(targetPreset.Id, out var targetVM))
                {
                    targetVM = new TextPresetItemViewModel(targetPreset);
                    _itemViewModelCache[targetPreset.Id] = targetVM;
                }
                else targetVM.SyncPatternsCollection();
                itemVM = targetVM;
            }
            pattern.SampleColor = dialog.SampleColorHex;
            pattern.ItemFlags = dialog.ItemFlags;
            pattern.Apply = new ApplyFlags
            {
                Effects = dialog.ItemFlags.VideoEffects || dialog.ItemFlags.FontColor || dialog.ItemFlags.Style || dialog.ItemFlags.FontStyles || dialog.ItemFlags.IsDevidedPerCharacter,
                Font = dialog.ItemFlags.FontName,
                Position = dialog.ItemFlags.Position,
                Size = dialog.ItemFlags.FontSize || dialog.ItemFlags.Zoom,
                Others = dialog.ItemFlags.Spacing || dialog.ItemFlags.BasePoint || dialog.ItemFlags.Rotation || dialog.ItemFlags.Opacity || dialog.ItemFlags.Blend || dialog.ItemFlags.Fade
            };

            // カテゴリの更新
            if (ReferenceEquals(originalPreset, targetPreset)) targetPreset.Category = dialog.Category;
            targetPreset.Tags = dialog.Tags;

            PresetStorage.SavePresets(_rawPresets);
            itemVM.SyncPatternsCollection();
            _renderer.InvalidatePreset(itemVM.Id);
            _ = itemVM.RenderActiveThumbnailAsync(_renderer, PreviewText, PreviewBackgroundColor);

            UpdateCategories();
            ApplyCategoryFilter();

            StatusMessage = $"「{itemVM.Name}」のパターン・カテゴリ設定を変更しました";
        }
    }

    private void UpdatePatternFromSelected(TextPresetItemViewModel? itemVM)
    {
        if (itemVM == null) return;

        if (itemVM.IsItemTemplate)
        {
            CreateItemTemplate(itemVM.Preset, itemVM.ActivePattern);
            return;
        }

        if (itemVM.Preset.IsEffectPreset)
        {
            CreateEffectPreset(itemVM.Preset, itemVM.ActivePattern);
            return;
        }

        var selected = PresetApplier.GetSelectedItem();
        if (selected == null)
        {
            StatusMessage = "更新失敗: " + PresetApplier.GetSelectedItemStatusDescription();
            return;
        }

        var pattern = itemVM.ActivePattern;
        var newPatternData = PresetApplier.CreatePatternFromItem(
            selected,
            pattern.Slot,
            pattern.SampleColor,
            pattern.ItemFlags
        );

        if (newPatternData == null)
        {
            StatusMessage = "選択中アイテムからのパターン生成に失敗しました";
            return;
        }

        pattern.Font = newPatternData.Font;
        pattern.Position = newPatternData.Position;
        pattern.Effects = newPatternData.Effects;
        pattern.CachedThumbnail = null;

        PresetStorage.SavePresets(_rawPresets);
        _renderer.InvalidatePreset(itemVM.Id);
        _ = itemVM.RenderActiveThumbnailAsync(_renderer, PreviewText, PreviewBackgroundColor);

        StatusMessage = $"「{itemVM.Name}」のパターンを選択中アイテムで上書き更新しました";
    }

    private void CreatePreset()
    {
        var selectedItems = PresetApplier.GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            StatusMessage = "登録失敗: " + PresetApplier.GetSelectedItemStatusDescription();
            return;
        }

        var textPresets = _rawPresets.Where(preset => !preset.IsEffectPreset && !preset.IsItemTemplate).ToList();
        var dialog = new CreatePresetDialog(textPresets, selectedItems.Count, _autoColorStartEffect)
        {
            Owner = Application.Current?.MainWindow
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }
        AutoColorStartEffect = dialog.AutoColorStartEffect;

        if (dialog.IsNewPreset)
        {
            // 新規テンプレート作成
            var preset = new TextPreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = dialog.PresetName,
                Category = dialog.Category,
                Tags = dialog.Tags,
                Patterns = new List<PresetPattern>()
            };

            if (selectedItems.Count > 1)
            {
                // 複数選択されたアイテムを1つのテンプレートにまとめて複数パターン登録
                int slot = 1;
                foreach (var item in selectedItems)
                {
                    if (slot > PresetPattern.MaxPatternCount) break;
                    string color = PresetApplier.DetectPatternColors(item, dialog.AutoColorStartEffect).FirstOrDefault() ?? PresetApplier.DetectRepresentativeColor(item);
                    var pat = PresetApplier.CreatePatternFromItem(item, slot, color, dialog.ItemFlags);
                    if (pat != null)
                    {
                        preset.Patterns.Add(pat);
                        slot++;
                    }
                }
            }
            else
            {
                // 単一テキストアイテムからも、文字色とエフェクト内の色を順に自動取得する。
                var detectedColors = PresetApplier.DetectPatternColors(selectedItems[0], dialog.AutoColorStartEffect);
                if (dialog.AutoColorStartEffect == 0 && detectedColors.Count > 0 && !string.Equals(dialog.SampleColorHex, detectedColors[0], StringComparison.OrdinalIgnoreCase))
                    detectedColors[0] = dialog.SampleColorHex;
                foreach (string color in detectedColors.Take(PresetPattern.MaxPatternCount))
                {
                    var pat = PresetApplier.CreatePatternFromItem(selectedItems[0], preset.Patterns.Count + 1, color, dialog.ItemFlags);
                    if (pat != null) preset.Patterns.Add(pat);
                }
            }

            if (preset.Patterns.Count == 0)
            {
                StatusMessage = "パターンの作成に失敗しました";
                return;
            }

            preset.ActivePatternSlot = preset.Patterns[0].Slot;
            _rawPresets.Insert(0, preset);
            PresetStorage.SavePresets(_rawPresets);

            var itemVM = new TextPresetItemViewModel(preset);
            _itemViewModelCache[preset.Id] = itemVM;

            UpdateCategories();
            ApplyCategoryFilter();

            _ = RenderSinglePreviewAsync(itemVM);

            StatusMessage = selectedItems.Count > 1
                ? $"新規テンプレート「{preset.Name}」を一括登録しました ({preset.Patterns.Count}パターン)"
                : $"新規テンプレート「{preset.Name}」を登録しました";
        }
        else
        {
            // 既存テンプレートへの別パターンとして登録
            var target = dialog.TargetPreset;
            if (target == null) return;

            // カテゴリも更新
            target.Category = dialog.Category;
            target.Tags = dialog.Tags;

            if (selectedItems.Count > 1)
            {
                // 複数アイテムを一括追加
                int added = 0;
                foreach (var item in selectedItems)
                {
                    int slot = target.GetNextFreeSlot();
                    if (slot < 0) break;
                    string color = PresetApplier.DetectRepresentativeColor(item);
                    var pat = PresetApplier.CreatePatternFromItem(item, slot, color, dialog.ItemFlags);
                    if (pat != null)
                    {
                        target.Patterns.Add(pat);
                        added++;
                    }
                }
                target.Patterns = target.Patterns.OrderBy(p => p.Slot).ToList();
                StatusMessage = $"「{target.Name}」に {added} パターンを一括追加しました (計{target.Patterns.Count}色)";
            }
            else
            {
                var detectedColors = PresetApplier.DetectPatternColors(selectedItems[0], dialog.AutoColorStartEffect);
                if (dialog.AutoColorStartEffect == 0 && detectedColors.Count > 0 && !string.Equals(dialog.SampleColorHex, detectedColors[0], StringComparison.OrdinalIgnoreCase))
                    detectedColors[0] = dialog.SampleColorHex;
                int added = 0;
                foreach (string color in detectedColors.Take(PresetPattern.MaxPatternCount))
                {
                    var existingPattern = target.Patterns.FirstOrDefault(pattern => string.Equals(pattern.SampleColor, color, StringComparison.OrdinalIgnoreCase));
                    int slot = existingPattern?.Slot ?? target.GetNextFreeSlot();
                    if (slot < 0) break;
                    var pattern = PresetApplier.CreatePatternFromItem(selectedItems[0], slot, color, dialog.ItemFlags);
                    if (pattern == null) continue;
                    target.Patterns.RemoveAll(existing => existing.Slot == slot);
                    target.Patterns.Add(pattern);
                    target.ActivePatternSlot = pattern.Slot;
                    added++;
                }
                target.Patterns = target.Patterns.OrderBy(p => p.Slot).ToList();
                StatusMessage = $"「{target.Name}」に自動取得した色パターンを {added} 件保存しました (計{target.Patterns.Count}色)";
            }

            PresetStorage.SavePresets(_rawPresets);

            UpdateCategories();
            ApplyCategoryFilter();

            if (_itemViewModelCache.TryGetValue(target.Id, out var existingVM))
            {
                existingVM.SyncPatternsCollection();
                _ = RenderSinglePreviewAsync(existingVM);
            }
        }
    }

    private async void CreateItemTemplate(TextPreset? targetPreset = null, PresetPattern? targetPattern = null)
    {
        var selected = PresetApplier.GetSelectedTimelineItems();
        var items = selected.Count > 0 ? selected : targetPattern?.Items ?? new List<YukkuriMovieMaker.Project.Items.IItem>();
        if (items.Count == 0)
        {
            StatusMessage = "テンプレート登録にはタイムライン上のアイテムを選択してください";
            return;
        }

        string sampleColor = targetPattern?.SampleColor ?? PresetApplier.DetectRepresentativeColor(items[0]);
        var existingTemplatePresets = _rawPresets.Where(preset => preset.IsItemTemplate).ToList();
        var dialog = new ItemTemplatePresetDialog(
            targetPreset?.Name ?? "",
            targetPreset?.Category ?? "",
            sampleColor,
            items,
            targetPreset?.Tags ?? "",
            _templateCategories.Concat(existingTemplatePresets.SelectMany(preset => preset.GetCategoryList())),
            existingTemplatePresets.SelectMany(preset => preset.GetTagList()))
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true) return;

        var target = targetPreset;
        bool isNew = target == null;
        if (target == null)
        {
            target = new TextPreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = dialog.PresetName,
                Category = dialog.Category,
                IsItemTemplate = true,
                Patterns = new List<PresetPattern>()
            };
        }

        int slot = targetPattern?.Slot ?? target.Patterns.FirstOrDefault(p => string.Equals(p.SampleColor, dialog.SampleColorHex, StringComparison.OrdinalIgnoreCase))?.Slot ?? target.GetNextFreeSlot();
        if (slot < 0)
        {
            StatusMessage = $"パターン数が上限({PresetPattern.MaxPatternCount})に達しています";
            return;
        }
        var pattern = new PresetPattern
        {
            Slot = slot,
            SampleColor = dialog.SampleColorHex,
            Items = items.Select(item => Json.GetClone(item) as YukkuriMovieMaker.Project.Items.IItem)
                .Where(item => item != null).Select(item => item!).ToList(),
            Apply = new ApplyFlags(),
            ItemFlags = new DetailedApplyFlags()
        };

        var oldPattern = target.Patterns.FirstOrDefault(p => p.Slot == slot);
        if (!dialog.GeneratePreview && oldPattern != null)
        {
            pattern.PreviewImagePath = oldPattern.PreviewImagePath;
            pattern.PreviewVideoPath = oldPattern.PreviewVideoPath;
            pattern.PreviewAudioPath = oldPattern.PreviewAudioPath;
            pattern.PreviewVideoDurationSeconds = oldPattern.PreviewVideoDurationSeconds;
            pattern.ThumbnailTimeSeconds = oldPattern.ThumbnailTimeSeconds;
            pattern.CachedThumbnail = oldPattern.CachedThumbnail;
        }
        target.Name = dialog.PresetName;
        target.Category = dialog.Category;
        target.Tags = dialog.Tags;
        target.IsEffectPreset = false;
        target.IsItemTemplate = true;
        target.Patterns.RemoveAll(p => p.Slot == slot);
        target.Patterns.Add(pattern);
        target.Patterns = target.Patterns.OrderBy(p => p.Slot).ToList();
        target.ActivePatternSlot = slot;
        if (isNew) _rawPresets.Insert(0, target);
        bool alsoAddedToTextPresets = items.Count == 1 && AddOrUpdateSingleTextPreset(target.Name, target.Category, items[0], dialog.SampleColorHex, dialog.Tags);
        PresetStorage.SavePresets(_rawPresets);

        if (dialog.GeneratePreview)
        {
            try
            {
                StatusMessage = "テンプレートの動画プレビューを生成しています (最大15秒)...";
                await GenerateAndSaveTemplatePreviewAsync(target, pattern, dialog.ThumbnailTimeSeconds);
                PresetStorage.SavePresets(_rawPresets);
            }
            catch (Exception ex)
            {
                if (!_itemViewModelCache.TryGetValue(target.Id, out var pendingVM))
                    _itemViewModelCache[target.Id] = new TextPresetItemViewModel(target);
                else pendingVM.SyncPatternsCollection();
                UpdateCategories();
                ApplyCategoryFilter();
                StatusMessage = $"プレビュー生成が停止しました。進捗を保存し、次回起動時に再生成を案内します: {ex.GetBaseException().Message}";
                return;
            }
        }
        if (!string.IsNullOrWhiteSpace(target.Category) && !_templateCategories.Contains(target.Category, StringComparer.OrdinalIgnoreCase))
            _templateCategories.AddRange(target.GetCategoryList().Where(c => !_templateCategories.Contains(c, StringComparer.OrdinalIgnoreCase)));
        PresetStorage.SaveCategories(false, _templateCategories, forTemplates: true);
        if (!_itemViewModelCache.TryGetValue(target.Id, out var itemVM))
        {
            itemVM = new TextPresetItemViewModel(target);
            _itemViewModelCache[target.Id] = itemVM;
        }
        else itemVM.SyncPatternsCollection();
        UpdateCategories();
        ApplyCategoryFilter();
        string action = isNew ? "登録" : "保存";
        StatusMessage = $"「{target.Name}」にアイテムテンプレートを{action}しました ({items.Count}アイテム)" +
            (alsoAddedToTextPresets ? "。テキストプリセット側にも登録しました" : "");
    }

    private async void LoadYmmTemplates()
    {
        try
        {
            var templates = PresetApplier.GetYmmItemTemplates();
            if (templates.Count == 0)
            {
                StatusMessage = "YMM4に読み込めるテンプレートが見つかりませんでした";
                return;
            }
            var dialog = new Window { Title = "YMM4テンプレートを読み込む", Width = 480, Height = 440, Owner = Application.Current?.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var root = new DockPanel { Margin = new Thickness(10) };
            var footer = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(footer, Dock.Bottom);
            var import = new System.Windows.Controls.Button { Content = "読み込む", IsDefault = true, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(4) };
            var cancel = new System.Windows.Controls.Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(4) };
            footer.Children.Add(import); footer.Children.Add(cancel);
            var list = new System.Windows.Controls.ListBox { SelectionMode = System.Windows.Controls.SelectionMode.Extended };
            foreach (var template in templates)
                list.Items.Add(new System.Windows.Controls.ListBoxItem { Content = $"{template.Name}　({template.Items.Count}アイテム)", Tag = template });
            root.Children.Add(footer); root.Children.Add(list); dialog.Content = root;
            import.Click += (_, _) => dialog.DialogResult = list.SelectedItems.Count > 0;
            if (dialog.ShowDialog() != true) return;
            var selectedTemplates = list.SelectedItems.OfType<System.Windows.Controls.ListBoxItem>()
                .Select(item => item.Tag).OfType<PresetApplier.YmmTemplateInfo>().ToList();
            if (selectedTemplates.Count == 0) return;

            var choice = MessageBox.Show($"選択した{selectedTemplates.Count}件のテンプレートについて、プレビュー動画とサムネイルを生成しますか？\n\n「いいえ」を選ぶとプレビューを生成せず読み込みます。", "プレビュー生成", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return;
            int importedCount = 0;
            foreach (var templateToImport in selectedTemplates)
            {
                var pattern = new PresetPattern { Slot = 1, SampleColor = PresetApplier.DetectRepresentativeColor(templateToImport.Items[0]), Items = templateToImport.Items.Select(item => Json.GetClone(item) as YukkuriMovieMaker.Project.Items.IItem).Where(item => item != null).Select(item => item!).ToList() };
                if (pattern.Items.Count == 0) continue;
                string id = Guid.NewGuid().ToString("N");
                var localizedName = PresetApplier.GetLocalizedTemplateName(templateToImport);
                var localizedCategory = PresetApplier.GetLocalizedTemplateCategory(templateToImport);
                var localizedTags = PresetApplier.GetLocalizedTemplateTags(templateToImport);
                var preset = new TextPreset { Id = id, Name = localizedName, Category = localizedCategory, Tags = localizedTags, IsItemTemplate = true, Patterns = new List<PresetPattern> { pattern } };
                if (choice == MessageBoxResult.Yes)
                {
                    StatusMessage = $"YMM4テンプレート「{templateToImport.Name}」のプレビューを生成しています... ({importedCount + 1}/{selectedTemplates.Count})";
                }
                _rawPresets.Insert(0, preset);
                AddOrUpdateSingleTextPreset(preset.Name, preset.Category, pattern.Items.Count == 1 ? pattern.Items[0] : null, pattern.SampleColor, preset.Tags);
                _templateCategories.AddRange(preset.GetCategoryList().Where(c => !_templateCategories.Contains(c, StringComparer.OrdinalIgnoreCase)));
                PresetStorage.SavePresets(_rawPresets);
                if (choice == MessageBoxResult.Yes)
                    await GenerateAndSaveTemplatePreviewAsync(preset, pattern, 0);
                var vm = new TextPresetItemViewModel(preset);
                _itemViewModelCache[preset.Id] = vm;
                importedCount++;
            }
            if (importedCount == 0) { StatusMessage = "読み込み対象テンプレートにアイテムがありません"; return; }
            PresetStorage.SaveCategories(false, _templateCategories, forTemplates: true);
            PresetStorage.SavePresets(_rawPresets);
            UpdateCategories(); ApplyCategoryFilter();
            StatusMessage = $"YMM4テンプレートを{importedCount}件読み込みました" + (choice == MessageBoxResult.Yes ? " (プレビュー生成済み)" : " (プレビューなし)");
        }
        catch (Exception ex)
        {
            UpdateCategories();
            ApplyCategoryFilter();
            StatusMessage = $"YMM4テンプレートの読み込みに失敗しました: {ex.Message}";
        }
    }

    private async Task GenerateAndSaveTemplatePreviewAsync(TextPreset preset, PresetPattern pattern, double thumbnailSeconds)
    {
        bool audioOnlyTemplate = pattern.Items.Count > 0 && pattern.Items.All(item => item is YukkuriMovieMaker.Project.Items.AudioItem);

        if (audioOnlyTemplate)
        {
            var audioPreview = await EffectPreviewVideoGenerator.GenerateAudioWaveformAsync(pattern.Items, preset.Id, pattern.Slot);
            pattern.PreviewVideoPath = null;
            pattern.PreviewAudioPath = audioPreview.AudioPath;
            pattern.PreviewVideoDurationSeconds = audioPreview.DurationSeconds;
            pattern.ThumbnailTimeSeconds = 0;
            pattern.PreviewImagePath = PresetStorage.SavePreviewImage(preset.Id, pattern.Slot, audioPreview.Waveform);
            pattern.CachedThumbnail = audioPreview.Waveform;
            PresetStorage.SavePresets(_rawPresets);
            PresetStorage.DeleteUnusedPreviewData(_rawPresets);
            PresetStorage.ClearPreviewGenerationCheckpoint();
            return;
        }

        string finalPath = PresetStorage.GetPreviewVideoPath(preset.Id, pattern.Slot);
        string partialPath = Path.Combine(PresetStorage.PreviewImageDirectory, $"{preset.Id}_P{pattern.Slot}.partial.mp4");
        try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }

        int fps = Math.Clamp(PresetApplier.GetActiveTimeline()?.VideoInfo.FPS ?? 30, 1, 60);
        int firstFrame = pattern.Items.Min(item => item.Frame);
        int lastFrame = pattern.Items.Max(item => item.Frame + Math.Max(1, item.Length));
        var checkpoint = new PresetStorage.PreviewGenerationCheckpoint
        {
            PresetId = preset.Id,
            Slot = pattern.Slot,
            PresetName = preset.Name,
            CompletedFrames = 0,
            TotalFrames = Math.Clamp(lastFrame - firstFrame, 1, fps * 15),
            PartialVideoPath = partialPath
        };
        PresetStorage.SavePreviewGenerationCheckpoint(checkpoint);

        try
        {
            var generated = await EffectPreviewVideoGenerator.GenerateItemTemplateAsync(
                pattern.Items,
                preset.Id,
                pattern.Slot,
                thumbnailSeconds,
                partialPath,
                completedFrames =>
                {
                    checkpoint.CompletedFrames = Math.Min(completedFrames, checkpoint.TotalFrames);
                    PresetStorage.SavePreviewGenerationCheckpoint(checkpoint);
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        StatusMessage = $"「{preset.Name}」のプレビュー生成中: {checkpoint.CompletedFrames}/{checkpoint.TotalFrames}フレーム";
                    }));
                });

            if (!File.Exists(partialPath)) throw new IOException("生成した途中動画ファイルが見つかりません");
            File.Move(partialPath, finalPath, overwrite: true);
            pattern.PreviewVideoPath = finalPath;
            pattern.PreviewVideoDurationSeconds = generated.DurationSeconds;
            pattern.ThumbnailTimeSeconds = Math.Min(thumbnailSeconds, Math.Max(0, generated.DurationSeconds - 1.0 / generated.FPS));
            pattern.PreviewAudioPath = null;
            if (generated.Thumbnail != null)
            {
                pattern.PreviewImagePath = PresetStorage.SavePreviewImage(preset.Id, pattern.Slot, generated.Thumbnail);
                pattern.CachedThumbnail = generated.Thumbnail;
            }
            PresetStorage.SavePresets(_rawPresets);
            PresetStorage.ClearPreviewGenerationCheckpoint();
            return;
        }
        catch
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            throw;
        }
    }

    private async void OfferInterruptedPreviewRegeneration()
    {
        var checkpoint = PresetStorage.LoadPreviewGenerationCheckpoint();
        if (checkpoint == null) return;

        try { if (File.Exists(checkpoint.PartialVideoPath)) File.Delete(checkpoint.PartialVideoPath); } catch { }
        var preset = _rawPresets.FirstOrDefault(item => item.Id == checkpoint.PresetId && item.IsItemTemplate);
        var pattern = preset?.Patterns.FirstOrDefault(item => item.Slot == checkpoint.Slot);
        if (preset == null || pattern == null || pattern.Items.Count == 0)
        {
            PresetStorage.ClearPreviewGenerationCheckpoint();
            StatusMessage = "前回停止したプレビュー生成のテンプレートが見つからなかったため、途中情報を削除しました";
            return;
        }

        int total = Math.Max(1, checkpoint.TotalFrames);
        int completed = Math.Clamp(checkpoint.CompletedFrames, 0, total);
        var answer = MessageBox.Show(
            $"前回「{preset.Name}」のプレビュー生成は {completed}/{total} フレーム ({100.0 * completed / total:0}%) まで進んだところで停止しました。\n\n途中ファイルを削除しました。保存済みのテンプレートからプレビュー動画を最初から再生成しますか？",
            "プレビュー生成の再開",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            StatusMessage = $"「{preset.Name}」のプレビュー再生成は保留中です。次回起動時にもう一度確認します";
            return;
        }

        try
        {
            StatusMessage = $"「{preset.Name}」のプレビューを再生成しています...";
            await GenerateAndSaveTemplatePreviewAsync(preset, pattern, pattern.ThumbnailTimeSeconds);
            PresetStorage.SavePresets(_rawPresets);
            if (_itemViewModelCache.TryGetValue(preset.Id, out var itemVM))
            {
                itemVM.SyncPatternsCollection();
                _renderer.InvalidatePreset(preset.Id);
                await RenderSinglePreviewAsync(itemVM);
            }
            UpdateCategories();
            ApplyCategoryFilter();
            StatusMessage = $"「{preset.Name}」のプレビュー動画を再生成しました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"プレビューの再生成が停止しました。進捗は保存されています: {ex.GetBaseException().Message}";
        }
    }

    private bool AddOrUpdateSingleTextPreset(string name, string category, YukkuriMovieMaker.Project.Items.IItem? item, string sampleColor, string tags = "")
    {
        if (item is not (YukkuriMovieMaker.Project.Items.TextItem or YukkuriMovieMaker.Project.Items.VoiceItem)) return false;

        var preset = _rawPresets.FirstOrDefault(p => !p.IsEffectPreset && !p.IsItemTemplate &&
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        bool isNew = preset == null;
        preset ??= new TextPreset
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Category = category,
            Patterns = new List<PresetPattern>()
        };

        var existing = preset.Patterns.FirstOrDefault(p => string.Equals(p.SampleColor, sampleColor, StringComparison.OrdinalIgnoreCase));
        int slot = existing?.Slot ?? preset.GetNextFreeSlot();
        if (slot < 0) return false;
        var pattern = PresetApplier.CreatePatternFromItem(item, slot, sampleColor, new DetailedApplyFlags());
        if (pattern == null) return false;
        if (existing != null) preset.Patterns.Remove(existing);
        preset.Patterns.Add(pattern);
        preset.Patterns = preset.Patterns.OrderBy(p => p.Slot).ToList();
        preset.ActivePatternSlot = slot;
        preset.Tags = tags;
        if (isNew) _rawPresets.Insert(0, preset);

        foreach (var itemCategory in preset.GetCategoryList())
            if (!_textCategories.Contains(itemCategory, StringComparer.OrdinalIgnoreCase)) _textCategories.Add(itemCategory);
        PresetStorage.SaveCategories(false, _textCategories);

        if (!_itemViewModelCache.TryGetValue(preset.Id, out var itemVM))
        {
            itemVM = new TextPresetItemViewModel(preset);
            _itemViewModelCache[preset.Id] = itemVM;
        }
        else itemVM.SyncPatternsCollection();
        _renderer.InvalidatePreset(preset.Id);
        _ = RenderSinglePreviewAsync(itemVM);
        return true;
    }

    public void AddLibraryWatchFolder()
    {
        var dialog = new OpenFolderDialog { Title = "素材ライブラリとして監視するフォルダーを選択してください" };
        if (dialog.ShowDialog(Application.Current?.MainWindow) != true || string.IsNullOrWhiteSpace(dialog.FolderName)) return;
        string path = Path.GetFullPath(dialog.FolderName);
        if (!LibraryWatchFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) LibraryWatchFolders.Add(path);
        UpdateLibraryWatchers();
        ScheduleSettingsSave();
    }

    public void RemoveLibraryWatchFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var existing = LibraryWatchFolders.FirstOrDefault(folder => string.Equals(folder, path, StringComparison.OrdinalIgnoreCase));
        if (existing == null) return;
        LibraryWatchFolders.Remove(existing);
        UpdateLibraryWatchers();
        ScheduleSettingsSave();
    }

    private void AddLibraryFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "素材を追加",
            Multiselect = true,
            Filter = "メディア素材|*.mp3;*.wav;*.m4a;*.aac;*.ogg;*.flac;*.wma;*.mp4;*.mov;*.avi;*.mkv;*.wmv;*.webm;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.psd|すべてのファイル|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        bool containsAudio = dialog.FileNames.Any(IsLibraryAudioFile);
        string audioType = "BGM";
        if (containsAudio)
        {
            var choice = MessageBox.Show("選択した音声ファイルをBGMとして登録しますか？\n\n「いいえ」を選ぶとSEとして登録します。", "音声素材の種類", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return;
            audioType = choice == MessageBoxResult.Yes ? "BGM" : "SE";
        }

        _ = AddLibraryFilesAsync(dialog.FileNames, audioType);
    }

    private async Task AddLibraryFilesAsync(IEnumerable<string> filePaths, string audioType)
    {
        int added = 0;
        foreach (string filePath in filePaths)
        {
            if (!File.Exists(filePath) || LibraryAssets.Any(asset => string.Equals(asset.Path, filePath, StringComparison.OrdinalIgnoreCase))) continue;
            string? type = GetLibraryAssetType(filePath, audioType);
            if (type == null) continue;
            var asset = new LibraryAsset { Path = Path.GetFullPath(filePath), Type = type };
            LibraryAssets.Add(asset);
            await LoadLibraryAssetThumbnailAsync(asset, force: true);
            added++;
        }
        PresetStorage.SaveLibraryAssets(LibraryAssets);
        UpdateCategories();
        ApplyCategoryFilter();
        StatusMessage = added == 0 ? "追加できる素材がありませんでした" : $"素材を{added}件ライブラリに追加しました";
    }

    private async Task LoadSavedLibraryThumbnailsAsync()
    {
        foreach (var asset in LibraryAssets.ToList())
            await LoadLibraryAssetThumbnailAsync(asset);
        PresetStorage.SaveLibraryAssets(LibraryAssets);
    }

    private async Task LoadLibraryAssetThumbnailAsync(LibraryAsset asset, bool force = false)
    {
        if (!File.Exists(asset.Path) || (!force && asset.Thumbnail != null)) return;
        if (!force && !string.IsNullOrWhiteSpace(asset.ThumbnailPath) && File.Exists(asset.ThumbnailPath))
        {
            try
            {
                var cached = new BitmapImage();
                cached.BeginInit(); cached.CacheOption = BitmapCacheOption.OnLoad; cached.UriSource = new Uri(asset.ThumbnailPath); cached.EndInit(); cached.Freeze();
                asset.Thumbnail = cached;
                return;
            }
            catch { }
        }

        try
        {
            BitmapSource? image = null;
            if (asset.Type == "BGM" || asset.Type == "SE")
                image = await EffectPreviewVideoGenerator.GenerateAudioFileWaveformAsync(asset.Path);
            else if (asset.Type == "動画")
                image = await EffectPreviewVideoGenerator.ExtractThumbnailAsync(asset.Path, asset.ThumbnailTimeSeconds);
            else
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(asset.Path); bitmap.EndInit(); bitmap.Freeze();
                    image = bitmap;
                }
                catch
                {
                    // PSDなどWPF標準デコーダーで読めない画像はYMM4付属FFmpegに試させる。
                    image = await EffectPreviewVideoGenerator.ExtractThumbnailAsync(asset.Path, 0);
                }
            }
            if (image == null) return;
            asset.Thumbnail = image;
            asset.ThumbnailPath = PresetStorage.SaveLibraryThumbnail(asset.Id, image);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not load library asset thumbnail: {asset.Path}: {ex}");
        }
    }

    private void UpdateLibraryWatchers()
    {
        foreach (var watcher in _libraryWatchers) { try { watcher.Dispose(); } catch { } }
        _libraryWatchers.Clear();
        if (LibraryMonitorEnabled)
        {
            foreach (string path in LibraryWatchFolders.ToList())
            {
                if (!Directory.Exists(path)) continue;
                try
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    watcher.Created += (_, _) => ScheduleLibraryWatchRefresh();
                    watcher.Deleted += (_, _) => ScheduleLibraryWatchRefresh();
                    watcher.Renamed += (_, _) => ScheduleLibraryWatchRefresh();
                    watcher.Changed += (_, _) => ScheduleLibraryWatchRefresh();
                    watcher.Error += (_, _) => ScheduleLibraryWatchRefresh();
                    _libraryWatchers.Add(watcher);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not watch library folder: {ex}"); }
            }
            ScheduleLibraryWatchRefresh();
        }
        else
        {
            foreach (var asset in LibraryAssets.Where(asset => asset.IsWatchFolderAsset).ToList())
                if (!LibraryWatchFolders.Any(folder => IsPathWithin(asset.Path, folder))) LibraryAssets.Remove(asset);
            PresetStorage.SaveLibraryAssets(LibraryAssets);
            UpdateCategories();
            ApplyCategoryFilter();
        }
    }

    private void ScheduleLibraryWatchRefresh()
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            _libraryWatchRefreshTimer.Stop();
            _libraryWatchRefreshTimer.Start();
        }));
    }

    private async Task RefreshLibraryWatchAssetsAsync()
    {
        if (!LibraryMonitorEnabled || !_libraryRefreshInProgress.Add("refresh")) return;
        try
        {
            var discovered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in LibraryWatchFolders.ToList())
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                        if (GetLibraryAssetType(path, "BGM") != null && !discovered.ContainsKey(path)) discovered[path] = folder;
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not scan library watch folder: {ex}"); }
            }

            foreach (var asset in LibraryAssets.Where(asset => asset.IsWatchFolderAsset).ToList())
            {
                if (discovered.ContainsKey(asset.Path)) continue;
                LibraryAssets.Remove(asset);
                try { if (!string.IsNullOrWhiteSpace(asset.ThumbnailPath) && File.Exists(asset.ThumbnailPath)) File.Delete(asset.ThumbnailPath); } catch { }
            }

            foreach (var entry in discovered)
            {
                var asset = LibraryAssets.FirstOrDefault(candidate => string.Equals(candidate.Path, entry.Key, StringComparison.OrdinalIgnoreCase));
                string type = GetLibraryAssetType(entry.Key, InferWatchAudioType(entry.Key, entry.Value))!;
                if (asset == null)
                {
                    asset = new LibraryAsset { Path = Path.GetFullPath(entry.Key), Type = type, IsWatchFolderAsset = true, WatchFolderPath = entry.Value };
                    LibraryAssets.Add(asset);
                    await LoadLibraryAssetThumbnailAsync(asset, force: true);
                }
                else
                {
                    asset.IsWatchFolderAsset = true;
                    asset.WatchFolderPath = entry.Value;
                    if (asset.Type != type) asset.Type = type;
                }
            }
            PresetStorage.SaveLibraryAssets(LibraryAssets);
            UpdateCategories();
            ApplyCategoryFilter();
        }
        finally { _libraryRefreshInProgress.Remove("refresh"); }
    }

    private static string InferWatchAudioType(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Contains("SE", StringComparison.OrdinalIgnoreCase) || part.Contains("効果音", StringComparison.OrdinalIgnoreCase)) ? "SE" : "BGM";
    }

    private static bool IsPathWithin(string file, string folder)
    {
        try
        {
            string fullFile = Path.GetFullPath(file), fullFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullFile.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsLibraryAudioFile(string path) =>
        new[] { ".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac", ".wma" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string? GetLibraryAssetType(string path, string audioType)
    {
        string extension = Path.GetExtension(path);
        if (IsLibraryAudioFile(path)) return audioType;
        if (new[] { ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".webm", ".m4v" }.Contains(extension, StringComparer.OrdinalIgnoreCase)) return "動画";
        if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".psd" }.Contains(extension, StringComparer.OrdinalIgnoreCase)) return "画像";
        return null;
    }

    public void RemoveLibraryAsset(LibraryAsset asset)
    {
        if (asset.IsWatchFolderAsset)
        {
            StatusMessage = "監視フォルダ内の素材は、監視フォルダから移動または削除するとライブラリから消えます";
            return;
        }
        LibraryAssets.Remove(asset);
        try { if (!string.IsNullOrWhiteSpace(asset.ThumbnailPath) && File.Exists(asset.ThumbnailPath)) File.Delete(asset.ThumbnailPath); } catch { }
        PresetStorage.SaveLibraryAssets(LibraryAssets);
        UpdateCategories();
        ApplyCategoryFilter();
        StatusMessage = $"「{asset.Name}」をライブラリから削除しました";
    }

    public void AddLibraryAssetToTimeline(LibraryAsset asset)
    {
        if (!File.Exists(asset.Path))
        {
            StatusMessage = "素材ファイルが見つかりません";
            return;
        }
        StatusMessage = PresetApplier.AddMediaFileToTimeline(asset.Path, asset.Type);
    }

    public async Task SetLibraryThumbnailTimeAsync(LibraryAsset asset)
    {
        var window = new Window
        {
            Title = "動画サムネイル位置",
            Width = 440,
            Height = 390,
            MinWidth = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Owner = Application.Current?.MainWindow
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        var footer = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(footer, Dock.Bottom);
        var cancel = new System.Windows.Controls.Button { Content = "キャンセル", IsCancel = true, Margin = new Thickness(0, 8, 6, 0), Padding = new Thickness(10, 4, 10, 4) };
        var save = new System.Windows.Controls.Button { Content = "保存", IsDefault = true, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(14, 4, 14, 4) };
        save.Click += (_, _) => window.DialogResult = true;
        footer.Children.Add(cancel); footer.Children.Add(save); root.Children.Add(footer);

        var content = new StackPanel();
        var preview = new System.Windows.Controls.Image { Height = 230, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 0, 0, 8) };
        content.Children.Add(preview);
        var row = new System.Windows.Controls.DockPanel();
        var value = new System.Windows.Controls.TextBox { Width = 70, Text = asset.ThumbnailTimeSeconds.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture), Margin = new Thickness(6, 0, 0, 0) };
        System.Windows.Controls.DockPanel.SetDock(value, System.Windows.Controls.Dock.Right);
        row.Children.Add(value);
        var slider = new System.Windows.Controls.Slider { Minimum = 0, Maximum = 3600, Value = Math.Clamp(asset.ThumbnailTimeSeconds, 0, 3600), TickFrequency = 1 };
        row.Children.Add(slider);
        content.Children.Add(row);
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = "秒", Margin = new Thickness(0, 4, 0, 0), Opacity = 0.75 });
        root.Children.Add(content); window.Content = root;

        var refresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        refresh.Tick += async (_, _) =>
        {
            refresh.Stop();
            try { preview.Source = await EffectPreviewVideoGenerator.ExtractThumbnailAsync(asset.Path, slider.Value); }
            catch { }
        };
        slider.ValueChanged += (_, _) =>
        {
            value.Text = slider.Value.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
            refresh.Stop(); refresh.Start();
        };
        value.LostFocus += (_, _) =>
        {
            if (double.TryParse(value.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double seconds))
                slider.Value = Math.Clamp(seconds, slider.Minimum, slider.Maximum);
        };
        try { preview.Source = await EffectPreviewVideoGenerator.ExtractThumbnailAsync(asset.Path, asset.ThumbnailTimeSeconds); } catch { }
        if (window.ShowDialog() == true)
        {
            asset.ThumbnailTimeSeconds = slider.Value;
            await LoadLibraryAssetThumbnailAsync(asset, force: true);
            PresetStorage.SaveLibraryAssets(LibraryAssets);
            ApplyCategoryFilter();
        }
    }

    private void OpenPreviewStorageDirectory()
    {
        try
        {
            Directory.CreateDirectory(PresetStorage.PreviewImageDirectory);
            Process.Start(new ProcessStartInfo { FileName = PresetStorage.PreviewImageDirectory, UseShellExecute = true });
        }
        catch { StatusMessage = "保存先フォルダーを開けませんでした。フォルダーへのアクセス権を確認してください"; }
    }

    private void DeleteUnusedPreviewData()
    {
        int deleted = PresetStorage.DeleteUnusedPreviewData(_rawPresets);
        StatusMessage = deleted == 0 ? "未使用のプレビューデータはありませんでした" : $"未使用のプレビューデータを {deleted} ファイル削除しました";
    }

    private void ChangePreviewStorageDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "プレビュー画像・動画の保存先を選択",
            InitialDirectory = PresetStorage.PreviewImageDirectory,
            Multiselect = false
        };
        if (dialog.ShowDialog(Application.Current?.MainWindow) != true) return;

        string oldDirectory = Path.GetFullPath(PresetStorage.PreviewImageDirectory);
        string newDirectory = Path.GetFullPath(dialog.FolderName);
        if (string.Equals(oldDirectory, newDirectory, StringComparison.OrdinalIgnoreCase)) return;
        if (IsSameOrNestedDirectory(oldDirectory, newDirectory) || IsSameOrNestedDirectory(newDirectory, oldDirectory))
        {
            StatusMessage = "新しい保存先には、現在のプレビューフォルダーの内側や親フォルダーを指定できません";
            return;
        }

        var migrations = new List<(PresetPattern Pattern, int Kind, string OldPath, string NewPath)>();
        try
        {
            Directory.CreateDirectory(newDirectory);
            CopyPreviewDirectoryContents(oldDirectory, newDirectory);
            foreach (var preset in _rawPresets)
            foreach (var pattern in preset.Patterns)
            {
                AddMigration(pattern, 0, pattern.PreviewImagePath);
                AddMigration(pattern, 1, pattern.PreviewVideoPath);
                AddMigration(pattern, 2, pattern.PreviewAudioPath);
            }

            foreach (var migration in migrations)
                if (!File.Exists(migration.NewPath)) File.Copy(migration.OldPath, migration.NewPath, overwrite: true);

            foreach (var migration in migrations)
            {
                if (migration.Kind == 0) migration.Pattern.PreviewImagePath = migration.NewPath;
                else if (migration.Kind == 1) migration.Pattern.PreviewVideoPath = migration.NewPath;
                else migration.Pattern.PreviewAudioPath = migration.NewPath;
            }
            if (!PresetStorage.SavePresets(_rawPresets))
            {
                RestoreOldPreviewPaths();
                throw new IOException("プリセット情報を保存できませんでした");
            }
            try { PresetStorage.SetPreviewImageDirectory(newDirectory); }
            catch
            {
                RestoreOldPreviewPaths();
                PresetStorage.SavePresets(_rawPresets);
                throw;
            }
            PresetStorage.RelocatePreviewGenerationCheckpoint(oldDirectory, newDirectory);
            OnPropertyChanged(nameof(PreviewStorageDirectory));
            UpdateCategories();
            ApplyCategoryFilter();
            try
            {
                if (Directory.Exists(oldDirectory)) Directory.Delete(oldDirectory, recursive: true);
                StatusMessage = "プレビューを新しい保存先へコピーし、以前のフォルダーを削除しました";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存先は変更しましたが、以前のフォルダーを削除できませんでした: {ex.Message}";
            }

            void AddMigration(PresetPattern pattern, int kind, string? path)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                string fullPath = Path.GetFullPath(path);
                string relative;
                try { relative = Path.GetRelativePath(oldDirectory, fullPath); }
                catch { return; }
                if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)) return;
                string destination = Path.Combine(newDirectory, relative);
                if (string.Equals(fullPath, destination, StringComparison.OrdinalIgnoreCase)) return;
                migrations.Add((pattern, kind, fullPath, destination));
            }

            void RestoreOldPreviewPaths()
            {
                foreach (var migration in migrations)
                {
                    if (migration.Kind == 0) migration.Pattern.PreviewImagePath = migration.OldPath;
                    else if (migration.Kind == 1) migration.Pattern.PreviewVideoPath = migration.OldPath;
                    else migration.Pattern.PreviewAudioPath = migration.OldPath;
                }
            }
        }
        catch { StatusMessage = "保存先の変更に失敗しました。選択したフォルダーのアクセス権を確認してください"; }
    }

    private static bool IsSameOrNestedDirectory(string root, string candidate)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyPreviewDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory)) return;
        foreach (string sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, sourcePath);
            string destinationPath = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private async void CreateEffectPreset(TextPreset? targetPreset = null, PresetPattern? targetPattern = null)
    {
        var selectedItems = PresetApplier.GetSelectedTimelineItems();
        var targets = PresetApplier.GetSelectedEffectTargets();
        var sourceItem = selectedItems.Count == 1 && targets.Count == 1 ? targets[0] : null;
        if (targetPreset == null && sourceItem == null)
        {
            StatusMessage = "FXパターンの登録は、映像エフェクト対応アイテムを1つだけ選択して行ってください";
            return;
        }

        var effects = sourceItem != null ? PresetApplier.GetVideoEffects(sourceItem) : new List<YukkuriMovieMaker.Plugin.Effects.IVideoEffect>();
        if (effects.Count == 0 && targetPattern != null)
            effects = targetPattern.Effects.ToList();
        else if (effects.Count == 0 && targetPreset != null)
            effects = targetPreset.ActivePattern.Effects.ToList();
        if (effects.Count == 0 && targetPreset == null)
        {
            StatusMessage = "FX登録失敗: 選択アイテムに映像エフェクトがありません";
            return;
        }

        var dialog = new CreateEffectPresetDialog(
            _rawPresets.Where(p => p.IsEffectPreset),
            _effectCategories,
            effects,
            targetPreset,
            targetPattern,
            sourceItem != null,
            sourceItem)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true) return;

        var target = dialog.TargetPreset;
        var sourcePattern = targetPattern ?? (ReferenceEquals(target, targetPreset) ? targetPattern : target?.ActivePattern);
        var storedEffects = dialog.RegenerateVideo ? dialog.SelectedEffects : sourcePattern?.Effects ?? dialog.SelectedEffects;
        var sourcePosition = sourceItem == null
            ? null
            : PresetApplier.CreatePositionPresetFromItem(sourceItem);
        var pattern = new PresetPattern
        {
            Slot = 1,
            SampleColor = dialog.SampleColorHex,
            Apply = new ApplyFlags { Effects = true, Font = false, Position = sourcePosition != null || (sourcePattern?.Apply.Position ?? false), Size = false, Others = false },
            ItemFlags = new DetailedApplyFlags { VideoEffects = true, FontColor = false, Style = false, FontStyles = false, IsDevidedPerCharacter = false, FontName = false, Position = sourcePosition != null || (sourcePattern?.ItemFlags.Position ?? false), FontSize = false, Zoom = false, Spacing = false, BasePoint = false, Rotation = false, Opacity = false, Blend = false, Fade = false },
            Position = sourcePosition ?? sourcePattern?.Position,
            Effects = storedEffects.Select(e => Json.GetClone(e)).Where(e => e != null).Select(e => e!).ToList()
        };

        PresetPattern? sameColor = null;
        if (target != null)
        {
            sameColor = target.Patterns.FirstOrDefault(p => string.Equals(p.SampleColor, pattern.SampleColor, StringComparison.OrdinalIgnoreCase));
            if (sameColor != null)
            {
                pattern.Slot = sameColor.Slot;
            }
            else
            {
                pattern.Slot = target.GetNextFreeSlot();
                if (pattern.Slot < 0)
                {
                    StatusMessage = $"パターン数が上限({PresetPattern.MaxPatternCount})に達しています";
                    return;
                }
            }
        }

        string presetId = target?.Id ?? Guid.NewGuid().ToString("N");
        BitmapSource? previewImage;
        try
        {
            previewImage = null;
            pattern.ThumbnailTimeSeconds = Math.Clamp(dialog.ThumbnailTimeSeconds, 0, 15);
            string? previewVideoPath = sameColor?.PreviewVideoPath ?? sourcePattern?.PreviewVideoPath ?? target?.ActivePattern.PreviewVideoPath;
            double previewDuration = sameColor?.PreviewVideoDurationSeconds ?? sourcePattern?.PreviewVideoDurationSeconds ?? target?.ActivePattern.PreviewVideoDurationSeconds ?? 0;

            if (dialog.RegenerateVideo)
            {
                if (sourceItem == null)
                {
                    StatusMessage = "動画を再生成するには、対象アイテムを1つだけ選択してください";
                    return;
                }
                StatusMessage = "選択アイテムのプレビュー動画を生成しています (最大15秒)...";
                var generated = await EffectPreviewVideoGenerator.GenerateAsync(sourceItem, dialog.SelectedEffects, presetId, pattern.Slot, pattern.ThumbnailTimeSeconds);
                previewVideoPath = generated.VideoPath;
                previewDuration = generated.DurationSeconds;
                pattern.PreviewVideoPath = previewVideoPath;
                pattern.PreviewVideoDurationSeconds = previewDuration;
                pattern.ThumbnailTimeSeconds = Math.Min(pattern.ThumbnailTimeSeconds, Math.Max(0, previewDuration - 1.0 / generated.FPS));
                if (dialog.RegenerateThumbnail) previewImage = generated.Thumbnail;
            }
            else
            {
                pattern.PreviewVideoPath = previewVideoPath;
                pattern.PreviewVideoDurationSeconds = previewDuration;
                if (dialog.RegenerateThumbnail && !string.IsNullOrWhiteSpace(previewVideoPath))
                {
                    pattern.ThumbnailTimeSeconds = Math.Min(pattern.ThumbnailTimeSeconds, previewDuration > 0 ? Math.Max(0, previewDuration - 1.0 / 30.0) : 15);
                    StatusMessage = "保存済みプレビュー動画からサムネイルを切り出しています...";
                    previewImage = await EffectPreviewVideoGenerator.ExtractThumbnailAsync(previewVideoPath, pattern.ThumbnailTimeSeconds);
                    if (previewImage == null && sourceItem != null)
                        previewImage = await EffectPreviewVideoGenerator.GenerateThumbnailAsync(sourceItem, dialog.SelectedEffects, pattern.ThumbnailTimeSeconds);
                }
                else if (dialog.RegenerateThumbnail && sourceItem != null)
                {
                    StatusMessage = "選択アイテムだけの仮想プレビューからサムネイルを生成しています...";
                    previewImage = await EffectPreviewVideoGenerator.GenerateThumbnailAsync(sourceItem, dialog.SelectedEffects, pattern.ThumbnailTimeSeconds);
                }
            }

            if (!dialog.RegenerateThumbnail)
            {
                var preserved = sameColor ?? sourcePattern ?? target?.ActivePattern;
                if (preserved != null)
                {
                    pattern.PreviewImagePath = preserved.PreviewImagePath;
                    pattern.CachedThumbnail = preserved.CachedThumbnail ?? PresetPatternItemViewModel.LoadSavedThumbnail(preserved);
                    pattern.ThumbnailTimeSeconds = preserved.ThumbnailTimeSeconds;
                }
            }

            if (dialog.RegenerateThumbnail && previewImage == null)
            {
                StatusMessage = "サムネイルを生成できませんでした。既存の動画または表示中のプレビューを確認してください";
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FX preview capture failed: {ex}");
            StatusMessage = $"FXプレビューの生成に失敗しました: {ex.Message}";
            return;
        }
        if (previewImage != null)
        {
            try
            {
                pattern.PreviewImagePath = PresetStorage.SavePreviewImage(presetId, pattern.Slot, previewImage);
                pattern.CachedThumbnail = previewImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FX preview image save failed: {ex}");
                StatusMessage = "FXプレビュー画像を保存できませんでした";
                return;
            }
        }

        if (target == null)
        {
            target = new TextPreset
            {
                Id = presetId,
                Name = dialog.PresetName,
                Category = dialog.Category,
                Tags = dialog.Tags,
                IsEffectPreset = true,
                Patterns = new List<PresetPattern> { pattern }
            };
            _rawPresets.Insert(0, target);
        }
        else
        {
            if (targetPattern != null && targetPreset != null && !ReferenceEquals(target, targetPreset))
            {
                targetPreset.Patterns.Remove(targetPattern);
                if (targetPreset.Patterns.Count == 0)
                {
                    _rawPresets.Remove(targetPreset);
                    _itemViewModelCache.Remove(targetPreset.Id);
                }
                else if (_itemViewModelCache.TryGetValue(targetPreset.Id, out var oldParentVM))
                    oldParentVM.SyncPatternsCollection();
            }
            if (sameColor != null) target.Patterns.Remove(sameColor);
            target.Patterns.Add(pattern);
            target.Patterns = target.Patterns.OrderBy(p => p.Slot).ToList();
            target.ActivePatternSlot = pattern.Slot;
        }
        target.Tags = dialog.Tags;

        if (!_itemViewModelCache.TryGetValue(target.Id, out var itemVM))
        {
            itemVM = new TextPresetItemViewModel(target);
            _itemViewModelCache[target.Id] = itemVM;
        }
        else itemVM.SyncPatternsCollection();

        if (!string.IsNullOrWhiteSpace(dialog.Category))
        {
            foreach (string category in dialog.Category.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!_effectCategories.Contains(category, StringComparer.OrdinalIgnoreCase)) _effectCategories.Add(category);
            }
        }
        PresetStorage.SaveCategories(true, _effectCategories);
        PresetStorage.SavePresets(_rawPresets);
        UpdateCategories();
        ApplyCategoryFilter();
        _renderer.InvalidatePreset(target.Id);
        _ = RenderSinglePreviewAsync(itemVM);
        StatusMessage = $"「{target.Name}」にFXパターンとプレビュー画像を保存しました ({pattern.Effects.Count}個のエフェクト / P{pattern.Slot})";
    }

    private void ManageCategories()
    {
        if (SelectedPresetTab == "LIB") return;
        bool forTemplates = SelectedPresetTab == "TPL";
        bool forEffects = SelectedPresetTab == "FX";
        var categories = new List<string>(forTemplates ? _templateCategories : forEffects ? _effectCategories : _textCategories);
        var dialog = new Window
        {
            Title = forTemplates ? "テンプレートカテゴリの編集" : forEffects ? "FXカテゴリの編集" : "テキストカテゴリの編集",
            Width = 380,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow
        };
        var panel = new System.Windows.Controls.DockPanel { Margin = new Thickness(10) };
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom);
        var addButton = new System.Windows.Controls.Button { Content = "追加", Margin = new Thickness(3), Padding = new Thickness(10, 3, 10, 3) };
        var renameButton = new System.Windows.Controls.Button { Content = "名前変更", Margin = new Thickness(3), Padding = new Thickness(10, 3, 10, 3) };
        var deleteButton = new System.Windows.Controls.Button { Content = "削除", Margin = new Thickness(3), Padding = new Thickness(10, 3, 10, 3) };
        var closeButton = new System.Windows.Controls.Button { Content = "閉じる", Margin = new Thickness(3), Padding = new Thickness(10, 3, 10, 3) };
        buttons.Children.Add(addButton); buttons.Children.Add(renameButton); buttons.Children.Add(deleteButton); buttons.Children.Add(closeButton);
        var list = new System.Windows.Controls.ListBox { ItemsSource = categories };
        panel.Children.Add(buttons); panel.Children.Add(list); dialog.Content = panel;

        void Commit()
        {
            if (forTemplates) _templateCategories = categories;
            else if (forEffects) _effectCategories = categories;
            else _textCategories = categories;
            PresetStorage.SaveCategories(forEffects, categories, forTemplates);
            UpdateCategories();
            ApplyCategoryFilter();
            PresetStorage.SavePresets(_rawPresets);
        }

        addButton.Click += (_, _) =>
        {
            string? name = PromptForCategory("追加するカテゴリ名", "新しいカテゴリ");
            if (string.IsNullOrWhiteSpace(name) || categories.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase)) return;
            categories.Add(name.Trim()); Commit(); list.Items.Refresh();
        };
        renameButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not string oldName) return;
            string? name = PromptForCategory("新しいカテゴリ名", oldName);
            if (string.IsNullOrWhiteSpace(name) || categories.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase)) return;
            string newName = name.Trim();
            for (int i = 0; i < _rawPresets.Count; i++)
            {
                var preset = _rawPresets[i];
                if (forTemplates ? !preset.IsItemTemplate : forEffects ? !preset.IsEffectPreset : preset.IsEffectPreset || preset.IsItemTemplate) continue;
                preset.Category = string.Join(", ", preset.GetCategoryList().Select(c => string.Equals(c, oldName, StringComparison.OrdinalIgnoreCase) ? newName : c));
            }
            categories[categories.IndexOf(oldName)] = newName; Commit(); list.Items.Refresh();
        };
        deleteButton.Click += (_, _) =>
        {
            if (list.SelectedItem is not string name) return;
            categories.Remove(name);
            foreach (var preset in _rawPresets.Where(p => forTemplates ? p.IsItemTemplate : forEffects ? p.IsEffectPreset : !p.IsEffectPreset && !p.IsItemTemplate))
                preset.Category = string.Join(", ", preset.GetCategoryList().Where(c => !string.Equals(c, name, StringComparison.OrdinalIgnoreCase)));
            Commit(); list.Items.Refresh();
        };
        closeButton.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();
    }

    private List<string> CurrentCategoryList => SelectedPresetTab switch { "FX" => _effectCategories, "TPL" => _templateCategories, _ => _textCategories };

    private void AddCategory()
    {
        if (SelectedPresetTab == "LIB") return;
        string? name = PromptForCategory("追加するカテゴリ名", "新しいカテゴリ");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.Contains(',') || string.Equals(name, "全て", StringComparison.OrdinalIgnoreCase) || CurrentCategoryList.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            StatusMessage = "カテゴリ名が無効か、すでに存在します";
            return;
        }

        CurrentCategoryList.Add(name);
        SaveCurrentCategories();
        UpdateCategories();
        ApplyCategoryFilter();
    }

    private void RenameCategory(string? oldName)
    {
        if (SelectedPresetTab == "LIB") return;
        if (string.IsNullOrWhiteSpace(oldName) || oldName == "全て") return;
        string? name = PromptForCategory("新しいカテゴリ名", oldName);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.Contains(',') || string.Equals(name, "全て", StringComparison.OrdinalIgnoreCase) ||
            CurrentCategoryList.Any(c => !string.Equals(c, oldName, StringComparison.OrdinalIgnoreCase) && string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "カテゴリ名が無効か、すでに存在します";
            return;
        }

        foreach (var preset in _rawPresets.Where(PresetCategoryIsManagedInCurrentTab))
        {
            preset.Category = string.Join(", ", preset.GetCategoryList().Select(c => string.Equals(c, oldName, StringComparison.OrdinalIgnoreCase) ? name : c));
        }
        int index = CurrentCategoryList.FindIndex(c => string.Equals(c, oldName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) CurrentCategoryList[index] = name;
        SaveCurrentCategories();
        PresetStorage.SavePresets(_rawPresets);
        UpdateCategories();
        ApplyCategoryFilter();
    }

    private void DeleteCategory(string? name)
    {
        if (SelectedPresetTab == "LIB") return;
        if (string.IsNullOrWhiteSpace(name) || name == "全て") return;
        if (MessageBox.Show($"カテゴリ「{name}」を削除しますか？このカテゴリが付いたプリセットからもカテゴリ名を外します。", "カテゴリ削除", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        CurrentCategoryList.RemoveAll(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        foreach (var preset in _rawPresets.Where(PresetCategoryIsManagedInCurrentTab))
        {
            preset.Category = string.Join(", ", preset.GetCategoryList().Where(c => !string.Equals(c, name, StringComparison.OrdinalIgnoreCase)));
        }
        SaveCurrentCategories();
        PresetStorage.SavePresets(_rawPresets);
        UpdateCategories();
        ApplyCategoryFilter();
    }

    private void SaveCurrentCategories()
    {
        if (SelectedPresetTab == "TPL")
            PresetStorage.SaveCategories(false, _templateCategories, forTemplates: true);
        else if (SelectedPresetTab == "FX")
            PresetStorage.SaveCategories(true, _effectCategories);
        else PresetStorage.SaveCategories(false, _textCategories);
    }

    private string? PromptForCategory(string prompt, string initialValue)
    {
        var dialog = new Window { Title = prompt, Width = 320, Height = 135, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Owner = Application.Current?.MainWindow };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };
        var input = new System.Windows.Controls.TextBox { Text = initialValue, Margin = new Thickness(0, 0, 0, 10) };
        var ok = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, Width = 70, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        panel.Children.Add(input); panel.Children.Add(ok); dialog.Content = panel;
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void DuplicatePreset(TextPresetItemViewModel? itemVM)
    {
        if (itemVM == null) return;

        var cloned = Json.GetClone(itemVM.Preset);
        if (cloned != null)
        {
            cloned.Id = Guid.NewGuid().ToString("N");
            cloned.Name = itemVM.Preset.Name + " (コピー)";
            foreach (var p in cloned.Patterns)
            {
                p.CachedThumbnail = null;
            }

            _rawPresets.Add(cloned);
            PresetStorage.SavePresets(_rawPresets);

            var clonedVM = new TextPresetItemViewModel(cloned);
            _itemViewModelCache[cloned.Id] = clonedVM;
            UpdateCategories();
            ApplyCategoryFilter();

            _ = RenderSinglePreviewAsync(clonedVM);

            StatusMessage = $"「{cloned.Name}」を作成しました";
        }
    }

    private void RenamePreset(TextPresetItemViewModel? itemVM)
    {
        if (itemVM == null) return;

        var window = new Window
        {
            Title = "プリセット設定",
            Width = 380,
            Height = 440,
            MinHeight = 340,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            Owner = Application.Current?.MainWindow
        };

        var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };
        var nameBox = new System.Windows.Controls.TextBox { Text = itemVM.Name, Margin = new Thickness(0, 3, 0, 10) };
        var categoryBox = new System.Windows.Controls.TextBox { Text = itemVM.Category, Margin = new Thickness(0, 3, 0, 12) };
        var tagsBox = new System.Windows.Controls.TextBox { Text = itemVM.Tags, Margin = new Thickness(0, 3, 0, 12) };
        var relatedPresets = _rawPresets.Where(preset => preset.IsItemTemplate == itemVM.Preset.IsItemTemplate && preset.IsEffectPreset == itemVM.Preset.IsEffectPreset).ToList();
        var categoryQuickButtons = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0, -8, 0, 8) };
        CsvInputButtons.Populate(categoryQuickButtons, CurrentCategoryList, value => CsvInputButtons.Append(categoryBox, value));
        var tagQuickButtons = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0, -8, 0, 8) };
        CsvInputButtons.Populate(tagQuickButtons, relatedPresets.SelectMany(preset => preset.GetTagList()), value => CsvInputButtons.Append(tagsBox, value));
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "キャンセル", IsCancel = true, Width = 80, Margin = new Thickness(0, 0, 6, 0) };
        var save = new System.Windows.Controls.Button { Content = "保存", IsDefault = true, Width = 80 };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                MessageBox.Show(window, "プリセット名を入力してください。", "入力内容の確認", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            window.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "プリセット名" });
        stack.Children.Add(nameBox);
        stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "カテゴリ (カンマ区切りで複数設定可能)" });
        stack.Children.Add(categoryBox);
        stack.Children.Add(categoryQuickButtons);
        stack.Children.Add(new System.Windows.Controls.TextBlock { Text = "タグ (カンマ区切りで複数設定可能)" });
        stack.Children.Add(tagsBox);
        stack.Children.Add(tagQuickButtons);
        stack.Children.Add(buttons);
        window.Content = stack;

        if (window.ShowDialog() == true)
        {
            itemVM.Name = nameBox.Text.Trim();
            itemVM.Category = categoryBox.Text.Trim();
            itemVM.Tags = string.Join(", ", tagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase));
            var categoryStore = itemVM.Preset.IsItemTemplate ? _templateCategories : itemVM.Preset.IsEffectPreset ? _effectCategories : _textCategories;
            foreach (string category in itemVM.Preset.GetCategoryList())
            {
                if (!categoryStore.Contains(category, StringComparer.OrdinalIgnoreCase)) categoryStore.Add(category);
            }
            PresetStorage.SaveCategories(itemVM.Preset.IsEffectPreset, categoryStore, itemVM.Preset.IsItemTemplate);
            PresetStorage.SavePresets(_rawPresets);
            UpdateCategories();
            ApplyCategoryFilter();
            StatusMessage = "プリセット名・カテゴリ・タグを保存しました";
        }
    }

    private void DeleteCurrentPattern(TextPresetItemViewModel? itemVM)
    {
        if (itemVM == null) return;
        if (itemVM.Preset.Patterns.Count <= 1)
        {
            DeletePreset(itemVM);
            return;
        }

        var result = MessageBox.Show(
            $"テンプレート「{itemVM.Name}」のパターン {itemVM.ActiveSlot} を削除しますか？",
            "パターンの削除",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.OK)
        {
            itemVM.Preset.Patterns.RemoveAll(p => p.Slot == itemVM.ActiveSlot);
            itemVM.Preset.ActivePatternSlot = itemVM.Preset.Patterns.First().Slot;
            itemVM.SyncPatternsCollection();
            _ = RenderSinglePreviewAsync(itemVM);
            PresetStorage.SavePresets(_rawPresets);
            PresetStorage.DeleteUnusedPreviewData(_rawPresets);
            StatusMessage = $"パターンを削除しました";
        }
    }

    private void DeletePreset(TextPresetItemViewModel? itemVM)
    {
        if (itemVM == null) return;

        var result = MessageBox.Show(
            $"テンプレート「{itemVM.Name}」を完全に削除しますか？",
            "テンプレートの削除",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.OK)
        {
            _rawPresets.RemoveAll(p => p.Id == itemVM.Id);
            _itemViewModelCache.Remove(itemVM.Id);
            PresetStorage.SavePresets(_rawPresets);
            PresetStorage.DeleteUnusedPreviewData(_rawPresets);
            UpdateCategories();
            ApplyCategoryFilter();
            StatusMessage = $"「{itemVM.Name}」を削除しました";
        }
    }

    private void DeleteSelectedPresets()
    {
        var selected = _itemViewModelCache.Values.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "削除するプリセットを選択してください";
            return;
        }
        if (MessageBox.Show($"選択した {selected.Count} 件のプリセットを削除しますか？", "選択プリセットの削除", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var ids = selected.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        _rawPresets.RemoveAll(preset => ids.Contains(preset.Id));
        foreach (string id in ids) _itemViewModelCache.Remove(id);
        PresetStorage.SavePresets(_rawPresets);
        PresetStorage.DeleteUnusedPreviewData(_rawPresets);
        UpdateCategories();
        ApplyCategoryFilter();
        OnPropertyChanged(nameof(SelectedPresetCount));
        StatusMessage = $"{selected.Count} 件のプリセットを削除しました";
    }

    private void ChangeSelectedPresetsCategory()
    {
        var selected = _itemViewModelCache.Values.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "カテゴリを変更するプリセットを選択してください";
            return;
        }
        string? category = PromptForCategory("選択したプリセットのカテゴリ", selected[0].Preset.GetCategoryList().FirstOrDefault() ?? "新しいカテゴリ");
        if (category == null) return;
        category = category.Trim();
        if (category.Contains(',') || string.Equals(category, "全て", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "カテゴリ名が無効です";
            return;
        }
        foreach (var item in selected)
        {
            item.Category = category;
            var categories = item.Preset.IsItemTemplate ? _templateCategories : item.Preset.IsEffectPreset ? _effectCategories : _textCategories;
            if (!string.IsNullOrWhiteSpace(category) && !categories.Contains(category, StringComparer.OrdinalIgnoreCase)) categories.Add(category);
        }
        PresetStorage.SaveCategories(false, _textCategories);
        PresetStorage.SaveCategories(true, _effectCategories);
        PresetStorage.SaveCategories(false, _templateCategories, forTemplates: true);
        PresetStorage.SavePresets(_rawPresets);
        UpdateCategories();
        ApplyCategoryFilter();
        OnPropertyChanged(nameof(SelectedPresetCount));
        StatusMessage = $"{selected.Count} 件のカテゴリを変更しました";
    }

    private void ExportPreset(TextPresetItemViewModel? itemVM)
    {
        if (itemVM == null) return;

        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Title = "テキストプリセットのエクスポート",
            Filter = "Text Preset (*.json)|*.json",
            FileName = $"{itemVM.Name}.json"
        };

        if (sfd.ShowDialog() == true)
        {
            var json = PresetStorage.ExportPresetJson(itemVM.Preset);
            System.IO.File.WriteAllText(sfd.FileName, json);
            StatusMessage = $"エクスポート完了: {sfd.FileName}";
        }
    }

    private void ExportSelectedPresets()
    {
        var selected = _itemViewModelCache.Values.Where(item => item.IsSelected).Select(item => item.Preset).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "エクスポートするプリセットを選択してください";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "選択したプリセットをエクスポート",
            Filter = "プリセットJSON (*.json)|*.json",
            FileName = $"プリセット_{selected.Count}件.json",
            AddExtension = true,
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() != true) return;
        try
        {
            System.IO.File.WriteAllText(dialog.FileName, PresetStorage.ExportPresetsJson(selected));
            StatusMessage = $"{selected.Count} 件のプリセットをエクスポートしました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"エクスポートに失敗しました: {ex.GetBaseException().Message}";
        }
    }

    private void ImportPreset()
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = "プリセットのインポート",
            Filter = "プリセットJSON (*.json)|*.json",
            Multiselect = true
        };

        if (ofd.ShowDialog() == true)
        {
            int count = 0;
            foreach (var file in ofd.FileNames)
            {
                try
                {
                    var json = System.IO.File.ReadAllText(file);
                    foreach (var preset in PresetStorage.ImportPresetsJson(json))
                    {
                        preset.Id = Guid.NewGuid().ToString("N");
                        preset.EnsurePatternsInitialized();
                        _rawPresets.Insert(0, preset);
                        var categories = preset.IsItemTemplate ? _templateCategories : preset.IsEffectPreset ? _effectCategories : _textCategories;
                        foreach (var category in preset.GetCategoryList())
                            if (!categories.Contains(category, StringComparer.OrdinalIgnoreCase)) categories.Add(category);
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Import error: {ex}");
                }
            }

            if (count > 0)
            {
                PresetStorage.SavePresets(_rawPresets);
                PresetStorage.SaveCategories(false, _textCategories);
                PresetStorage.SaveCategories(true, _effectCategories);
                PresetStorage.SaveCategories(false, _templateCategories, forTemplates: true);
                UpdateCategories();
                ApplyCategoryFilter();
                StatusMessage = $"{count} 件のプリセットをインポートしました";
                _ = RefreshAllPreviewsAsync();
            }
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
