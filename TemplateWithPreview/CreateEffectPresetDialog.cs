using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TemplateWithPreview.Models;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Plugin.Effects;

namespace TemplateWithPreview;

public sealed class CreateEffectPresetDialog : Window
{
    private sealed class PresetChoice
    {
        public TextPreset? Preset { get; }
        public string Label { get; }
        public PresetChoice(TextPreset? preset, string label) { Preset = preset; Label = label; }
        public override string ToString() => Label;
    }

    private readonly ComboBox _presetCombo;
    private readonly TextBox _nameBox;
    private readonly ComboBox _categoryBox;
    private readonly TextBox _tagsBox;
    private readonly ColorPicker _colorPicker;
    private readonly CheckBox _regenerateVideoCheckBox;
    private readonly CheckBox _regenerateThumbnailCheckBox;
    private readonly Slider _thumbnailTimeSlider;
    private readonly TextBox _thumbnailTimeInput;
    private readonly TextBlock _thumbnailTimeLabel;
    private readonly Image _previewImage;
    private readonly MediaElement _previewVideo;
    private readonly Slider _videoSeekSlider = new() { Minimum = 0, Maximum = 0, IsEnabled = false, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _previewStatus;
    private readonly List<CheckBox> _effectChecks = new();
    private readonly TextPreset? _defaultTarget;
    private readonly PresetPattern? _defaultPattern;
    private readonly YukkuriMovieMaker.Project.Items.IItem? _sourceItem;
    private readonly bool _isEditMode;
    private readonly List<string> _temporaryPreviewFiles = new();
    private readonly DispatcherTimer _thumbnailRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _videoPositionTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string? _activePreviewVideoPath;
    private bool _updatingThumbnailTime;
    private bool _updatingVideoSeek;
    private bool _draggingVideoSeek;

    public TextPreset? TargetPreset => (_presetCombo.SelectedItem as PresetChoice)?.Preset;
    public string PresetName => _nameBox.Text.Trim();
    public string Category => _categoryBox.Text.Trim();
    public string Tags => string.Join(", ", _tagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase));
    public string SampleColorHex => $"#{_colorPicker.Value.R:X2}{_colorPicker.Value.G:X2}{_colorPicker.Value.B:X2}";
    public List<IVideoEffect> SelectedEffects => _effectChecks.Where(c => c.IsChecked == true).Select(c => (IVideoEffect)c.Tag).ToList();
    public bool RegenerateVideo => _regenerateVideoCheckBox.IsChecked == true;
    public bool RegenerateThumbnail => _regenerateThumbnailCheckBox.IsChecked == true;
    public double ThumbnailTimeSeconds => _thumbnailTimeSlider.Value;

    public CreateEffectPresetDialog(IEnumerable<TextPreset> existingPresets, IEnumerable<string> categories, IEnumerable<IVideoEffect> availableEffects, TextPreset? defaultTarget = null, PresetPattern? defaultPattern = null, bool hasSourceItem = true, YukkuriMovieMaker.Project.Items.IItem? sourceItem = null)
    {
        _defaultTarget = defaultTarget;
        _defaultPattern = defaultPattern;
        _sourceItem = sourceItem;
        var presetList = existingPresets.ToList();
        var categoryList = categories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _isEditMode = defaultPattern != null;
        Title = _isEditMode ? "エフェクトプリセットの編集" : "エフェクトプリセットの登録 / パターン追加";
        Width = 660;
        Height = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Owner = Application.Current?.MainWindow;

        var root = new DockPanel { Margin = new Thickness(12) };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var cancel = new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        var register = new Button { Content = _isEditMode ? "保存" : "登録", IsDefault = true, Padding = new Thickness(18, 4, 18, 4) };
        register.Click += (_, _) =>
        {
            if (RegenerateVideo && SelectedEffects.Count == 0)
            {
                MessageBox.Show(this, "動画を再生成する場合は、登録するエフェクトを1つ以上選択してください。", "エフェクト未選択", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (TargetPreset == null && string.IsNullOrWhiteSpace(PresetName))
            {
                MessageBox.Show(this, "プリセット名を入力してください。", "名前未入力", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        };
        bottom.Children.Add(cancel);
        bottom.Children.Add(register);
        root.Children.Add(bottom);

        var content = new StackPanel();
        DockPanel.SetDock(content, Dock.Top);
        var targetGroup = new GroupBox { Header = _isEditMode ? "親プリセット" : "登録先", Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10) };
        var targetContent = new StackPanel();
        targetContent.Children.Add(new TextBlock { Text = _isEditMode ? "このパターンを所属させる親プリセット" : "新規プリセットまたは既存プリセットへのパターン追加", Margin = new Thickness(0, 0, 0, 4), Opacity = 0.8 });
        _presetCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 10), MinHeight = 26 };
        if (!_isEditMode) _presetCombo.Items.Add(new PresetChoice(null, "新しいエフェクトプリセット"));
        foreach (var preset in presetList.OrderBy(p => p.Name)) _presetCombo.Items.Add(new PresetChoice(preset, $"[FX] {preset.Name}"));
        _presetCombo.SelectedIndex = defaultTarget == null
            ? 0
            : Enumerable.Range(0, _presetCombo.Items.Count)
                .FirstOrDefault(i => ReferenceEquals((_presetCombo.Items[i] as PresetChoice)?.Preset, defaultTarget));
        targetContent.Children.Add(_presetCombo);

        var metadataPanel = new StackPanel();
        metadataPanel.Children.Add(new TextBlock { Text = "プリセット名", Margin = new Thickness(0, 0, 0, 3) });
        _nameBox = new TextBox { Text = "新しいエフェクト", Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4, 3, 4, 3) };
        metadataPanel.Children.Add(_nameBox);
        metadataPanel.Children.Add(new TextBlock { Text = "カテゴリ", Margin = new Thickness(0, 0, 0, 3) });
        _categoryBox = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 0, 8), MinHeight = 26 };
        foreach (var category in categoryList) _categoryBox.Items.Add(category);
        _categoryBox.Text = "";
        metadataPanel.Children.Add(_categoryBox);
        var categoryQuickButtons = new WrapPanel { Margin = new Thickness(0, -5, 0, 7) };
        CsvInputButtons.Populate(categoryQuickButtons, categoryList, value => CsvInputButtons.Append(_categoryBox, value));
        metadataPanel.Children.Add(categoryQuickButtons);
        targetContent.Children.Add(metadataPanel);
        metadataPanel.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;
        targetContent.Children.Add(new TextBlock { Text = "タグ (カンマ区切り)", Margin = new Thickness(0, 2, 0, 3) });
        _tagsBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4, 3, 4, 3), ToolTip = "例: 感情, 赤系" };
        targetContent.Children.Add(_tagsBox);
        var tagQuickButtons = new WrapPanel { Margin = new Thickness(0, -5, 0, 7) };
        CsvInputButtons.Populate(tagQuickButtons, presetList.SelectMany(preset => preset.GetTagList()), value => CsvInputButtons.Append(_tagsBox, value));
        targetContent.Children.Add(tagQuickButtons);
        targetGroup.Content = targetContent;
        content.Children.Add(targetGroup);

        var colorGroup = new GroupBox { Header = "パターン色", Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10, 7, 10, 7) };
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        colorRow.Children.Add(new TextBlock { Text = "登録パターンの色を指定", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _colorPicker = new ColorPicker { Value = Colors.White, Width = 72, Height = 26 };
        colorRow.Children.Add(_colorPicker);
        colorGroup.Content = colorRow;
        content.Children.Add(colorGroup);

        var previewGroup = new GroupBox { Header = "プレビュー設定", Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10) };
        var previewContent = new StackPanel();
        _regenerateVideoCheckBox = new CheckBox { Content = _isEditMode ? "プレビュー動画を再生成 (最大15秒)" : "プレビューを生成 (アイテム尺、最大15秒)", IsChecked = hasSourceItem, IsEnabled = hasSourceItem, Margin = new Thickness(0, 2, 0, 4) };
        _regenerateThumbnailCheckBox = new CheckBox { Content = "サムネイルを再生成", IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };
        var thumbnailTimeRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 8) };
        thumbnailTimeRow.Children.Add(new TextBlock { Text = "サムネイルにする動画の秒数:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        double maxPreviewSeconds = GetMaximumPreviewSeconds(sourceItem, defaultPattern);
        _thumbnailTimeSlider = new Slider { Minimum = 0, Maximum = maxPreviewSeconds, Value = 0, Width = 250, IsEnabled = maxPreviewSeconds > 0, TickFrequency = 0.1, IsSnapToTickEnabled = false };
        _thumbnailTimeInput = new TextBox { Text = "0.0", Width = 54, Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        _thumbnailTimeLabel = new TextBlock { Text = "秒", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        _thumbnailTimeSlider.ValueChanged += (_, _) =>
        {
            if (!_thumbnailTimeInput.IsKeyboardFocusWithin)
            {
                _updatingThumbnailTime = true;
                _thumbnailTimeInput.Text = _thumbnailTimeSlider.Value.ToString("0.0", CultureInfo.CurrentCulture);
                _updatingThumbnailTime = false;
            }
            if (!_updatingThumbnailTime)
            {
                _thumbnailRefreshTimer.Stop();
                _thumbnailRefreshTimer.Start();
            }
        };
        _thumbnailTimeInput.LostFocus += (_, _) => ApplyThumbnailTimeInput();
        _thumbnailTimeInput.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { ApplyThumbnailTimeInput(); e.Handled = true; }
        };
        _thumbnailTimeInput.TextChanged += (_, _) =>
        {
            if (_updatingThumbnailTime || !double.TryParse(_thumbnailTimeInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)) return;
            _thumbnailTimeSlider.Value = Math.Clamp(value, _thumbnailTimeSlider.Minimum, _thumbnailTimeSlider.Maximum);
        };
        thumbnailTimeRow.Children.Add(_thumbnailTimeSlider);
        thumbnailTimeRow.Children.Add(_thumbnailTimeInput);
        thumbnailTimeRow.Children.Add(_thumbnailTimeLabel);
        previewContent.Children.Add(_regenerateVideoCheckBox);
        previewContent.Children.Add(_regenerateThumbnailCheckBox);
        previewContent.Children.Add(thumbnailTimeRow);

        var previewHeader = new DockPanel { Margin = new Thickness(0, 6, 0, 4) };
        previewHeader.Children.Add(new TextBlock { Text = "生成プレビュー", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _previewRefreshButton = new Button { Content = _isEditMode ? "プレビュー更新" : "動画プレビュー生成", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(_previewRefreshButton, Dock.Right);
        _previewRefreshButton.Click += async (_, _) => await RefreshPreviewAsync();
        previewHeader.Children.Add(_previewRefreshButton);
        previewContent.Children.Add(previewHeader);

        var previewGrid = new Grid { Height = 200, Margin = new Thickness(0, 0, 0, 3) };
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition());
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var thumbnailPanel = new DockPanel { Margin = new Thickness(0, 0, 4, 0) };
        var thumbnailLabel = new TextBlock { Text = "サムネイル", Margin = new Thickness(0, 0, 0, 3) };
        DockPanel.SetDock(thumbnailLabel, Dock.Top);
        thumbnailPanel.Children.Add(thumbnailLabel);
        _previewImage = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        thumbnailPanel.Children.Add(new Border { BorderBrush = SystemColors.ControlDarkBrush, BorderThickness = new Thickness(1), Background = Brushes.Black, Child = _previewImage });
        previewGrid.Children.Add(thumbnailPanel);
        var videoPanel = new DockPanel { Margin = new Thickness(4, 0, 0, 0) };
        var videoLabel = new TextBlock { Text = "動画", Margin = new Thickness(0, 0, 0, 3) };
        DockPanel.SetDock(videoLabel, Dock.Top);
        videoPanel.Children.Add(videoLabel);
        _previewVideo = new MediaElement { Stretch = Stretch.Uniform, IsMuted = true, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop };
        videoPanel.Children.Add(new Border { BorderBrush = SystemColors.ControlDarkBrush, BorderThickness = new Thickness(1), Background = Brushes.Black, Child = _previewVideo });
        _videoSeekSlider.PreviewMouseLeftButtonDown += (_, _) => _draggingVideoSeek = true;
        _videoSeekSlider.PreviewMouseLeftButtonUp += (_, _) => _draggingVideoSeek = false;
        _videoSeekSlider.ValueChanged += (_, _) =>
        {
            if (!_updatingVideoSeek && _draggingVideoSeek && _previewVideo.NaturalDuration.HasTimeSpan)
                _previewVideo.Position = TimeSpan.FromSeconds(_videoSeekSlider.Value);
        };
        DockPanel.SetDock(_videoSeekSlider, Dock.Bottom);
        videoPanel.Children.Insert(Math.Max(0, videoPanel.Children.Count - 1), _videoSeekSlider);
        Grid.SetColumn(videoPanel, 1);
        previewGrid.Children.Add(videoPanel);
        previewContent.Children.Add(previewGrid);
        _previewStatus = new TextBlock { Text = "「プレビュー更新」で現在の設定を確認できます。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        previewContent.Children.Add(_previewStatus);
        previewGroup.Content = previewContent;
        content.Children.Add(previewGroup);
        _thumbnailRefreshTimer.Tick += async (_, _) =>
        {
            _thumbnailRefreshTimer.Stop();
            await RefreshThumbnailPreviewAsync();
        };
        _videoPositionTimer.Tick += (_, _) => UpdateVideoSeekPosition();
        _previewVideo.MediaOpened += (_, _) =>
        {
            _videoSeekSlider.Maximum = _previewVideo.NaturalDuration.HasTimeSpan ? _previewVideo.NaturalDuration.TimeSpan.TotalSeconds : 0;
            _videoSeekSlider.IsEnabled = _videoSeekSlider.Maximum > 0;
            _videoPositionTimer.Start();
            _previewVideo.Play();
        };
        _previewVideo.MediaEnded += (_, _) => { _previewVideo.Position = TimeSpan.Zero; _previewVideo.Play(); };
        _previewVideo.MediaFailed += (_, _) => _previewStatus.Text = "動画を再生できませんでした。サムネイルは表示できます。";
        Closed += (_, _) =>
        {
            _thumbnailRefreshTimer.Stop();
            _videoPositionTimer.Stop();
            CleanupTemporaryPreviewFiles();
        };

        var effectsGroup = new GroupBox { Header = "登録するエフェクト", Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10) };
        var effectsContent = new StackPanel();
        effectsContent.Children.Add(new TextBlock { Text = "選択中アイテムに登録されているエフェクトから選択", Margin = new Thickness(0, 0, 0, 4), Opacity = 0.8 });
        var list = new StackPanel();
        int index = 0;
        foreach (var effect in availableEffects)
        {
            var label = GetEffectName(effect.GetType());
            var check = new CheckBox
            {
                Content = $"{++index}. {label}",
                Tag = effect,
                IsChecked = true,
                Margin = new Thickness(2, 3, 2, 3),
                ToolTip = effect.GetType().FullName
            };
            _effectChecks.Add(check);
            list.Children.Add(check);
        }
        var effectScroller = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(1), BorderBrush = SystemColors.ControlDarkBrush, MinHeight = 120 };
        effectsContent.Children.Add(effectScroller);
        effectsGroup.Content = effectsContent;
        effectsGroup.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;
        content.Children.Add(effectsGroup);

        _effectChecks.ForEach(check => check.IsEnabled = hasSourceItem);
        root.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        Content = root;

        _presetCombo.SelectionChanged += (_, _) => { UpdatePresetTargetFields(); LoadExistingPreview(); };
        UpdatePresetTargetFields();
        LoadExistingPreview();
        ContentRendered += async (_, _) =>
        {
            if (_sourceItem != null && GetSelectedPattern()?.PreviewImagePath is not { Length: > 0 })
                await RefreshThumbnailPreviewAsync();
        };
    }

    private void UpdatePresetTargetFields()
    {
        var preset = TargetPreset;
        bool isNew = preset == null;
        _nameBox.IsEnabled = isNew;
        _categoryBox.IsEnabled = isNew;
        if (preset != null)
        {
            _nameBox.Text = preset.Name;
            _categoryBox.Text = preset.Category;
            _tagsBox.Text = preset.Tags;
            var palette = new[] { Colors.White, Colors.Red, Colors.Yellow, Colors.Cyan, Colors.Lime, Colors.Magenta, Colors.Orange, Colors.DodgerBlue };
            var nextColor = palette.FirstOrDefault(color => !preset.Patterns.Any(pattern => string.Equals(
                pattern.SampleColor,
                $"#{color.R:X2}{color.G:X2}{color.B:X2}",
                StringComparison.OrdinalIgnoreCase)));
            if (nextColor != default) _colorPicker.Value = nextColor;
        }
        else
        {
            _nameBox.Text = "新しいエフェクト";
            _categoryBox.Text = "";
            _tagsBox.Text = "";
        }
        if (preset != null)
        {
            var pattern = _isEditMode ? _defaultPattern : ReferenceEquals(preset, _defaultTarget) ? _defaultPattern : preset.ActivePattern;
            _thumbnailTimeSlider.Maximum = GetMaximumPreviewSeconds(_sourceItem, pattern);
            _thumbnailTimeSlider.IsEnabled = _thumbnailTimeSlider.Maximum > 0;
            _thumbnailTimeSlider.Value = Math.Clamp(pattern?.ThumbnailTimeSeconds ?? 0, _thumbnailTimeSlider.Minimum, _thumbnailTimeSlider.Maximum);
            if (pattern != null && (_isEditMode || ReferenceEquals(preset, _defaultTarget)))
            {
                try { _colorPicker.Value = (Color)ColorConverter.ConvertFromString(pattern.SampleColor); }
                catch { }
            }
        }
        else
        {
            _thumbnailTimeSlider.Maximum = GetMaximumPreviewSeconds(_sourceItem, _defaultPattern);
            _thumbnailTimeSlider.IsEnabled = _thumbnailTimeSlider.Maximum > 0;
            _thumbnailTimeSlider.Value = Math.Clamp(_defaultPattern?.ThumbnailTimeSeconds ?? 0, _thumbnailTimeSlider.Minimum, _thumbnailTimeSlider.Maximum);
        }
    }

    private readonly Button _previewRefreshButton;

    private static string GetEffectName(Type type)
    {
        var attribute = type.GetCustomAttribute<System.ComponentModel.DisplayNameAttribute>();
        foreach (var effectAttribute in type.GetCustomAttributes(false))
        {
            var resourceType = effectAttribute.GetType().GetProperty("ResourceType")?.GetValue(effectAttribute) as Type;
            var resourceName = effectAttribute.GetType().GetProperty("Name")?.GetValue(effectAttribute) as string;
            if (resourceType == null || string.IsNullOrWhiteSpace(resourceName)) continue;
            try
            {
                if (resourceType.GetProperty(resourceName, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is string localized && !string.IsNullOrWhiteSpace(localized))
                    return localized;
            }
            catch { }
        }
        return attribute?.DisplayName is { Length: > 0 } name ? name : type.Name;
    }

    private static double GetMaximumPreviewSeconds(YukkuriMovieMaker.Project.Items.IItem? item, PresetPattern? pattern)
    {
        if (item != null)
        {
            var info = PresetApplier.GetActiveTimeline()?.VideoInfo;
            int fps = 30;
            try { fps = (int?)info?.GetType().GetProperty("FPS")?.GetValue(info) ?? fps; } catch { }
            return Math.Clamp((double)Math.Max(1, item.Length - 1) / Math.Max(1, fps), 0, 15);
        }
        return Math.Clamp(pattern?.PreviewVideoDurationSeconds ?? 15, 0, 15);
    }

    private PresetPattern? GetSelectedPattern()
    {
        if (_isEditMode) return _defaultPattern;
        if (TargetPreset == null) return _defaultPattern;
        return ReferenceEquals(TargetPreset, _defaultTarget) && _defaultPattern != null ? _defaultPattern : TargetPreset.ActivePattern;
    }

    private void LoadExistingPreview()
    {
        var pattern = GetSelectedPattern();
        if (pattern == null) return;
        _previewImage.Source = pattern.CachedThumbnail ?? PresetPatternItemViewModel.LoadSavedThumbnail(pattern);
        var path = pattern.PreviewVideoPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            _activePreviewVideoPath = path;
            _previewVideo.Stop();
            _previewVideo.Source = new Uri(path, UriKind.Absolute);
            _previewStatus.Text = "保存済みプレビューです。設定を変更した場合は「プレビュー更新」を押してください。";
        }
        else
        {
            _activePreviewVideoPath = null;
            _previewVideo.Stop();
            _previewVideo.Source = null;
            _previewStatus.Text = "「プレビュー更新」で現在の設定を確認できます。";
        }
    }

    private async Task RefreshPreviewAsync()
    {
        _previewRefreshButton.IsEnabled = false;
        try
        {
            if (_sourceItem != null)
            {
                _previewStatus.Text = "選択アイテムだけの仮想シーンで動画を生成しています...";
                string previewPath = Path.Combine(Path.GetTempPath(), $"YMM4-FXPreview-{Guid.NewGuid():N}.mp4");
                _temporaryPreviewFiles.Add(previewPath);
                var generated = await EffectPreviewVideoGenerator.GenerateAsync(_sourceItem, SelectedEffects, "dialog-preview", 1, ThumbnailTimeSeconds, previewPath);
                _activePreviewVideoPath = generated.VideoPath;
                _previewImage.Source = generated.Thumbnail;
                _previewVideo.Stop();
                _previewVideo.Source = new Uri(generated.VideoPath, UriKind.Absolute);
                _previewStatus.Text = $"プレビュー更新済み ({generated.DurationSeconds:0.0} 秒 / サムネイル {ThumbnailTimeSeconds:0.0} 秒)";
            }
            else
            {
                var pattern = GetSelectedPattern();
                if (pattern == null || string.IsNullOrWhiteSpace(pattern.PreviewVideoPath) || !File.Exists(pattern.PreviewVideoPath))
                {
                    _previewStatus.Text = "編集中の保存済み動画がありません。アイテムを選択して動画を再生成してください。";
                    return;
                }
                _previewStatus.Text = "保存済み動画から指定秒数のサムネイルを切り出しています...";
                _activePreviewVideoPath = pattern.PreviewVideoPath;
                _previewImage.Source = await EffectPreviewVideoGenerator.ExtractThumbnailAsync(pattern.PreviewVideoPath, ThumbnailTimeSeconds);
                _previewVideo.Stop();
                _previewVideo.Source = new Uri(pattern.PreviewVideoPath, UriKind.Absolute);
                _previewStatus.Text = $"保存済み動画を表示中 (サムネイル {ThumbnailTimeSeconds:0.0} 秒)";
            }
        }
        catch (Exception ex)
        {
            _previewStatus.Text = $"プレビュー生成に失敗しました: {ex.Message}";
        }
        finally
        {
            _previewRefreshButton.IsEnabled = true;
        }
    }

    private async Task RefreshThumbnailPreviewAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_activePreviewVideoPath) && File.Exists(_activePreviewVideoPath))
            {
                _previewImage.Source = await EffectPreviewVideoGenerator.ExtractThumbnailAsync(_activePreviewVideoPath, ThumbnailTimeSeconds);
                _previewStatus.Text = $"サムネイル位置: {ThumbnailTimeSeconds:0.0} 秒";
            }
            else if (_sourceItem != null)
            {
                _previewStatus.Text = "選択アイテムだけの静止画プレビューを生成しています...";
                _previewImage.Source = await EffectPreviewVideoGenerator.GenerateThumbnailAsync(_sourceItem, SelectedEffects, ThumbnailTimeSeconds);
                _previewStatus.Text = $"静止画プレビューです ({ThumbnailTimeSeconds:0.0} 秒)。動画は「動画プレビュー生成」で確認できます。";
            }
        }
        catch (Exception ex)
        {
            _previewStatus.Text = $"静止画プレビューを生成できませんでした: {ex.Message}";
        }
    }

    private void ApplyThumbnailTimeInput()
    {
        if (!double.TryParse(_thumbnailTimeInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value))
        {
            _updatingThumbnailTime = true;
            _thumbnailTimeInput.Text = _thumbnailTimeSlider.Value.ToString("0.0", CultureInfo.CurrentCulture);
            _updatingThumbnailTime = false;
            return;
        }
        _thumbnailTimeSlider.Value = Math.Clamp(value, _thumbnailTimeSlider.Minimum, _thumbnailTimeSlider.Maximum);
        _updatingThumbnailTime = true;
        _thumbnailTimeInput.Text = _thumbnailTimeSlider.Value.ToString("0.0", CultureInfo.CurrentCulture);
        _updatingThumbnailTime = false;
    }

    private void UpdateVideoSeekPosition()
    {
        if (_draggingVideoSeek || !_previewVideo.NaturalDuration.HasTimeSpan) return;
        _updatingVideoSeek = true;
        _videoSeekSlider.Value = Math.Clamp(_previewVideo.Position.TotalSeconds, _videoSeekSlider.Minimum, _videoSeekSlider.Maximum);
        _updatingVideoSeek = false;
    }

    private void CleanupTemporaryPreviewFiles()
    {
        _previewVideo.Stop();
        _previewVideo.Source = null;
        foreach (string path in _temporaryPreviewFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
