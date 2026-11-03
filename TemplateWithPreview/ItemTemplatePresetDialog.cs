using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TemplateWithPreview.Models;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Project.Items;

namespace TemplateWithPreview;

internal sealed class ItemTemplatePresetDialog : Window
{
    private readonly TextBox _name = new();
    private readonly TextBox _category = new();
    private readonly TextBox _tags = new();
    private readonly ColorPicker _colorPicker = new() { Value = Colors.White, Width = 90, Height = 28 };
    private readonly CheckBox _generate = new() { Content = "プレビューを生成 (アイテム尺、最大15秒)", IsChecked = true };
    private readonly Slider _thumbTime = new() { Minimum = 0, Maximum = 15, TickFrequency = 0.1, IsSnapToTickEnabled = false };
    private readonly TextBox _timeInput = new() { Width = 54, Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center, Text = "0.0" };
    private readonly Slider _videoSeek = new() { Minimum = 0, Maximum = 0, IsEnabled = false, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Image _thumbnail = new() { Stretch = Stretch.Uniform };
    private readonly MediaElement _video = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop, Stretch = Stretch.Uniform, IsMuted = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) };
    private readonly Button _previewButton;
    private readonly IReadOnlyList<IItem> _items;
    private readonly List<string> _temporaryFiles = new();
    private readonly DispatcherTimer _thumbnailRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _videoPositionTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _updatingThumbnailTime;
    private bool _updatingVideoSeek;
    private bool _draggingVideoSeek;

    public string PresetName => _name.Text.Trim();
    public string Category => _category.Text.Trim();
    public string Tags => string.Join(", ", _tags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase));
    public string SampleColorHex => $"#{_colorPicker.Value.R:X2}{_colorPicker.Value.G:X2}{_colorPicker.Value.B:X2}";
    public double ThumbnailTimeSeconds => _thumbTime.Value;
    public bool GeneratePreview => _generate.IsChecked == true;
    public string? PreviewVideoPath { get; private set; }
    public double PreviewVideoDurationSeconds { get; private set; }
    public BitmapSource? PreviewImage { get; private set; }

    public ItemTemplatePresetDialog(string name, string category, string sampleColor, IReadOnlyList<IItem> items, string tags = "", IEnumerable<string>? categories = null, IEnumerable<string>? tagValues = null)
    {
        _items = items;
        Title = "テンプレートプリセットの登録 / パターン設定";
        Width = 660;
        Height = 720;
        MinWidth = 560;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Owner = Application.Current?.MainWindow;
        _name.Text = name;
        _category.Text = category;
        _tags.Text = tags;
        try { _colorPicker.Value = (Color)ColorConverter.ConvertFromString(sampleColor); }
        catch { _colorPicker.Value = Colors.White; }

        var root = new DockPanel { Margin = new Thickness(12) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        var cancel = new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        var save = new Button { Content = "保存", IsDefault = true, Padding = new Thickness(18, 4, 18, 4) };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(PresetName))
            {
                MessageBox.Show(this, "テンプレート名を入力してください。", "名前未入力", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
        };
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        root.Children.Add(footer);

        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "テンプレート情報", Margin = new Thickness(0, 0, 0, 4), FontWeight = FontWeights.SemiBold });
        content.Children.Add(Labeled("テンプレート名", _name));
        content.Children.Add(Labeled("カテゴリ", _category));
        var categoryQuickButtons = new WrapPanel { Margin = new Thickness(90, -6, 0, 3) };
        CsvInputButtons.Populate(categoryQuickButtons, categories ?? Array.Empty<string>(), value => CsvInputButtons.Append(_category, value));
        content.Children.Add(categoryQuickButtons);
        content.Children.Add(Labeled("タグ (カンマ区切り)", _tags));
        var tagQuickButtons = new WrapPanel { Margin = new Thickness(90, -6, 0, 3) };
        CsvInputButtons.Populate(tagQuickButtons, tagValues ?? Array.Empty<string>(), value => CsvInputButtons.Append(_tags, value));
        content.Children.Add(tagQuickButtons);

        var itemGroup = new GroupBox { Header = "登録するアイテム", Margin = new Thickness(0, 5, 0, 8), Padding = new Thickness(10) };
        itemGroup.Content = new TextBlock
        {
            Text = $"選択中の {_items.Count} 個のアイテムを、位置関係を保ったテンプレートとして登録します。音声・映像アイテムも含まれます。",
            TextWrapping = TextWrapping.Wrap
        };
        content.Children.Add(itemGroup);

        var colorGroup = new GroupBox { Header = "パターン色", Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10, 7, 10, 7) };
        colorGroup.Content = _colorPicker;
        content.Children.Add(colorGroup);

        var previewGroup = new GroupBox { Header = "プレビュー設定", Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10) };
        var previewOptions = new StackPanel();
        _generate.Margin = new Thickness(0, 0, 0, 5);
        previewOptions.Children.Add(_generate);
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 6) };
        timeRow.Children.Add(new TextBlock { Text = "サムネイルにする動画の秒数:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _thumbTime.Width = 250;
        _thumbnailRefreshTimer.Tick += async (_, _) =>
        {
            _thumbnailRefreshTimer.Stop();
            if (!string.IsNullOrWhiteSpace(PreviewVideoPath) && File.Exists(PreviewVideoPath))
            {
                try
                {
                    PreviewImage = await EffectPreviewVideoGenerator.ExtractThumbnailAsync(PreviewVideoPath, _thumbTime.Value);
                    _thumbnail.Source = PreviewImage;
                }
                catch { }
            }
        };
        _thumbTime.ValueChanged += (_, _) =>
        {
            if (!_timeInput.IsKeyboardFocusWithin)
            {
                _updatingThumbnailTime = true;
                _timeInput.Text = _thumbTime.Value.ToString("0.0", CultureInfo.CurrentCulture);
                _updatingThumbnailTime = false;
            }
            if (!string.IsNullOrWhiteSpace(PreviewVideoPath) && File.Exists(PreviewVideoPath))
            {
                _thumbnailRefreshTimer.Stop();
                _thumbnailRefreshTimer.Start();
            }
        };
        _timeInput.LostFocus += (_, _) => ApplyThumbnailTimeInput();
        _timeInput.TextChanged += (_, _) =>
        {
            if (_updatingThumbnailTime || !double.TryParse(_timeInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)) return;
            _thumbTime.Value = Math.Clamp(value, _thumbTime.Minimum, _thumbTime.Maximum);
        };
        _timeInput.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { ApplyThumbnailTimeInput(); e.Handled = true; }
        };
        timeRow.Children.Add(_thumbTime);
        timeRow.Children.Add(_timeInput);
        timeRow.Children.Add(new TextBlock { Text = "秒", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) });
        previewOptions.Children.Add(timeRow);

        var previewHeader = new DockPanel { Margin = new Thickness(0, 3, 0, 4) };
        previewHeader.Children.Add(new TextBlock { Text = "生成プレビュー", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _previewButton = new Button { Content = "動画プレビュー生成", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(_previewButton, Dock.Right);
        _previewButton.Click += async (_, _) => await GeneratePreviewAsync();
        previewHeader.Children.Add(_previewButton);
        previewOptions.Children.Add(previewHeader);

        var previewGrid = new Grid { Height = 200, Margin = new Thickness(0, 0, 0, 3) };
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition());
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var thumbPanel = CreatePreviewPanel("サムネイル", _thumbnail);
        previewGrid.Children.Add(thumbPanel);
        var videoPanel = CreatePreviewPanel("動画", _video);
        _videoSeek.PreviewMouseLeftButtonDown += (_, _) => _draggingVideoSeek = true;
        _videoSeek.PreviewMouseLeftButtonUp += (_, _) => _draggingVideoSeek = false;
        _videoSeek.ValueChanged += (_, _) =>
        {
            if (!_updatingVideoSeek && _draggingVideoSeek && _video.NaturalDuration.HasTimeSpan)
                _video.Position = TimeSpan.FromSeconds(_videoSeek.Value);
        };
        DockPanel.SetDock(_videoSeek, Dock.Bottom);
        videoPanel.Children.Insert(Math.Max(0, videoPanel.Children.Count - 1), _videoSeek);
        Grid.SetColumn(videoPanel, 1);
        previewGrid.Children.Add(videoPanel);
        previewOptions.Children.Add(previewGrid);
        _status.Text = "「動画プレビュー生成」で保存前にテンプレートの見た目を確認できます。";
        previewOptions.Children.Add(_status);
        previewGroup.Content = previewOptions;
        content.Children.Add(previewGroup);

        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;
        _videoPositionTimer.Tick += (_, _) => UpdateVideoSeekPosition();
        _video.MediaOpened += (_, _) =>
        {
            _videoSeek.Maximum = _video.NaturalDuration.HasTimeSpan ? _video.NaturalDuration.TimeSpan.TotalSeconds : 0;
            _videoSeek.IsEnabled = _videoSeek.Maximum > 0;
            _videoPositionTimer.Start();
            _video.Play();
        };
        _video.MediaEnded += (_, _) => { _video.Position = TimeSpan.Zero; _video.Play(); };
        _video.MediaFailed += (_, _) => _status.Text = "動画を再生できませんでした。サムネイルは表示できます。";
        Closed += (_, _) =>
        {
            _thumbnailRefreshTimer.Stop();
            _videoPositionTimer.Stop();
            _video.Stop();
            _video.Source = null;
            foreach (var path in _temporaryFiles) try { if (File.Exists(path)) File.Delete(path); } catch { }
        };
    }

    private void ApplyThumbnailTimeInput()
    {
        if (!double.TryParse(_timeInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value))
        {
            _timeInput.Text = _thumbTime.Value.ToString("0.0", CultureInfo.CurrentCulture);
            return;
        }
        _thumbTime.Value = Math.Clamp(value, _thumbTime.Minimum, _thumbTime.Maximum);
        _updatingThumbnailTime = true;
        _timeInput.Text = _thumbTime.Value.ToString("0.0", CultureInfo.CurrentCulture);
        _updatingThumbnailTime = false;
    }

    private void UpdateVideoSeekPosition()
    {
        if (_draggingVideoSeek || !_video.NaturalDuration.HasTimeSpan) return;
        _updatingVideoSeek = true;
        _videoSeek.Value = Math.Clamp(_video.Position.TotalSeconds, _videoSeek.Minimum, _videoSeek.Maximum);
        _updatingVideoSeek = false;
    }

    private static FrameworkElement Labeled(string label, TextBox input)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var caption = new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(caption, Dock.Left);
        input.Padding = new Thickness(5, 3, 5, 3);
        row.Children.Add(caption);
        row.Children.Add(input);
        return row;
    }

    private static DockPanel CreatePreviewPanel(string label, FrameworkElement preview)
    {
        var panel = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) };
        DockPanel.SetDock(caption, Dock.Top);
        panel.Children.Add(caption);
        preview.HorizontalAlignment = HorizontalAlignment.Stretch;
        preview.VerticalAlignment = VerticalAlignment.Stretch;
        panel.Children.Add(new Border { BorderBrush = SystemColors.ControlDarkBrush, BorderThickness = new Thickness(1), Background = Brushes.Black, Child = preview });
        return panel;
    }

    private async Task GeneratePreviewAsync()
    {
        _previewButton.IsEnabled = false;
        try
        {
            _status.Text = "選択アイテムだけの仮想シーンでプレビューを生成しています...";
            if (_items.Count > 0 && _items.All(item => item is YukkuriMovieMaker.Project.Items.AudioItem))
            {
                var waveform = await EffectPreviewVideoGenerator.GenerateAudioWaveformAsync(_items);
                PreviewVideoPath = null;
                PreviewVideoDurationSeconds = waveform.DurationSeconds;
                PreviewImage = waveform.Waveform;
                _thumbnail.Source = PreviewImage;
                _thumbTime.IsEnabled = false;
                _video.Stop();
                _video.Source = null;
                _video.Visibility = Visibility.Collapsed;
                _videoSeek.Visibility = Visibility.Collapsed;
                _status.Text = $"音声アイテムのみのため波形を表示しています ({waveform.DurationSeconds:0.0} 秒)。";
                return;
            }
            string previewPath = Path.Combine(Path.GetTempPath(), $"YMM4-TemplatePreview-{Guid.NewGuid():N}.mp4");
            _temporaryFiles.Add(previewPath);
            var result = await EffectPreviewVideoGenerator.GenerateItemTemplateAsync(_items, "template-preview", 1, _thumbTime.Value, previewPath);
            PreviewVideoPath = result.VideoPath;
            PreviewVideoDurationSeconds = result.DurationSeconds;
            PreviewImage = result.Thumbnail;
            _thumbnail.Source = PreviewImage;
            _thumbTime.Maximum = Math.Max(0, result.DurationSeconds - 1.0 / Math.Max(1, result.FPS));
            _thumbTime.IsEnabled = _thumbTime.Maximum > 0;
            _video.Visibility = Visibility.Visible;
            _videoSeek.Visibility = Visibility.Visible;
            _video.Stop();
            _video.Source = new Uri(result.VideoPath, UriKind.Absolute);
            _status.Text = $"プレビュー更新済み ({result.DurationSeconds:0.0} 秒 / サムネイル {_thumbTime.Value:0.0} 秒)";
        }
        catch (Exception ex)
        {
            _status.Text = $"プレビュー生成に失敗しました: {ex.GetBaseException().Message}";
        }
        finally { _previewButton.IsEnabled = true; }
    }
}
