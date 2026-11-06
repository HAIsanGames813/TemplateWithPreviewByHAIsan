using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using TemplateWithPreview.Models;

namespace TemplateWithPreview;

public partial class PreviewPresetView : UserControl
{
    private readonly MediaPlayer _audioPreviewPlayer = new();
    private FrameworkElement? _audioPreviewCard;
    private FrameworkElement? _pendingDragCard;
    private TextPresetItemViewModel? _pendingDragItem;
    private Point _mouseDownPosition;
    private bool _suppressNextCardClick;
    private readonly DispatcherTimer _dockPreviewRefreshTimer;
    private readonly DispatcherTimer _libraryPlaybackTimer;
    private readonly MediaPlayer _libraryPreviewPlayer = new();
    private bool _librarySeekDragging;
    private bool _librarySeekUpdating;
    private bool _libraryPreviewIsAudio;
    private bool _libraryPreviewPaused;
    private bool _libraryVideoMuteRetryAttempted;
    private bool _libraryVideoFallbackAttempted;
    private int _pressedCategoryIndex = -1;
    private int _draggedCategoryIndex = -1;
    private Point _categoryDragStart;
    private bool _categoryDragStarted;

    public PreviewPresetView()
    {
        InitializeComponent();
        _dockPreviewRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _dockPreviewRefreshTimer.Tick += (_, _) =>
        {
            _dockPreviewRefreshTimer.Stop();
            if (IsLoaded && DataContext is PreviewPresetViewModel viewModel && viewModel.SelectedPresetTab != "LIB")
                _ = viewModel.RefreshAllPreviewsAsync();
        };
        _libraryPlaybackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _libraryPlaybackTimer.Tick += (_, _) => UpdateLibrarySeekSliderFromPlayback();
        _libraryPreviewPlayer.MediaOpened += (_, _) =>
        {
            _libraryPreviewPlayer.Play();
            ShowLibrarySeekBar(_libraryPreviewPlayer.NaturalDuration);
        };
        _libraryPreviewPlayer.MediaEnded += (_, _) =>
        {
            _libraryPlaybackTimer.Stop();
            _libraryPreviewPlayer.Stop();
        };
        _libraryPreviewPlayer.MediaFailed += (_, e) =>
        {
            _libraryPlaybackTimer.Stop();
            _libraryPreviewPlayer.Stop();
            if (DataContext is PreviewPresetViewModel vm)
            {
                int hresult = e.ErrorException?.HResult ?? 0;
                vm.StatusMessage = hresult == unchecked((int)0xC00D11B1)
                    ? "音声を再生できませんでした (0xC00D11B1)。音声デバイスまたは音声コーデックを確認してください。"
                    : $"素材を再生できませんでした (0x{hresult:X8}): {e.ErrorException?.GetBaseException().Message}";
            }
        };
        Loaded += OnLoaded;
        SizeChanged += OnDockLayoutChanged;
        PresentationSource.AddSourceChangedHandler(this, OnPresentationSourceChanged);
        Unloaded += (_, _) =>
        {
            _dockPreviewRefreshTimer.Stop();
            StopLibraryPreview();
            StopAudioPreview();
        };
        _audioPreviewPlayer.MediaOpened += (_, _) =>
        {
            if (_audioPreviewCard?.IsKeyboardFocusWithin == true)
            {
                _audioPreviewPlayer.Position = TimeSpan.Zero;
                _audioPreviewPlayer.Play();
            }
        };
        _audioPreviewPlayer.MediaEnded += (_, _) =>
        {
            if (_audioPreviewCard?.IsKeyboardFocusWithin == true)
            {
                _audioPreviewPlayer.Position = TimeSpan.Zero;
                _audioPreviewPlayer.Play();
            }
        };
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreviewPresetViewModel viewModel)
            new PreviewSettingsWindow(viewModel).ShowDialog();
    }

    private void OpenFilter_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreviewPresetViewModel viewModel) viewModel.OpenFilterWindow();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (SearchPanel.Visibility == Visibility.Visible)
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SearchPanel.Visibility = Visibility.Visible;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void PresetTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl tabs && tabs.SelectedIndex != 3)
            LibraryPreviewPopup.IsOpen = false;
    }

    private void LibraryPreviewPopup_Closed(object? sender, EventArgs e) => StopLibraryPreview();

    private void CategoryTabs_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || scrollViewer.ScrollableWidth <= 0) return;
        scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void CategoryTabs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TabControl tabs || DataContext is not PreviewPresetViewModel vm) return;
        var tabItem = FindAncestorVisual<TabItem>(e.OriginalSource as DependencyObject);
        _pressedCategoryIndex = tabItem == null ? -1 : tabs.Items.IndexOf(tabItem.DataContext);
        _draggedCategoryIndex = _pressedCategoryIndex;
        _categoryDragStart = e.GetPosition(tabs);
        _categoryDragStarted = false;
        if (_pressedCategoryIndex <= 0 || _pressedCategoryIndex >= vm.Categories.Count)
            _pressedCategoryIndex = _draggedCategoryIndex = -1;
    }

    private void CategoryTabs_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not TabControl tabs || DataContext is not PreviewPresetViewModel vm ||
            _pressedCategoryIndex < 1 || e.LeftButton != MouseButtonState.Pressed) return;

        Point position = e.GetPosition(tabs);
        if (!_categoryDragStarted && (Math.Abs(position.X - _categoryDragStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                                      Math.Abs(position.Y - _categoryDragStart.Y) >= SystemParameters.MinimumVerticalDragDistance))
            _categoryDragStarted = true;
        if (!_categoryDragStarted) return;

        var hit = tabs.InputHitTest(position) as DependencyObject;
        var targetTab = FindAncestorVisual<TabItem>(hit);
        if (targetTab == null) return;
        int targetIndex = tabs.Items.IndexOf(targetTab.DataContext);
        if (targetIndex > 0 && targetIndex != _draggedCategoryIndex)
        {
            vm.MoveCategory(_draggedCategoryIndex, targetIndex);
            _draggedCategoryIndex = targetIndex;
            e.Handled = true;
        }
    }

    private void CategoryTabs_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pressedCategoryIndex = -1;
        _draggedCategoryIndex = -1;
        _categoryDragStarted = false;
    }

    private static T? FindAncestorVisual<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T match) return match;
            element = GetParent(element);
        }
        return null;
    }

    private static DependencyObject? GetParent(DependencyObject element)
    {
        if (element is Visual || element is Visual3D)
            return VisualTreeHelper.GetParent(element);
        if (element is FrameworkContentElement frameworkContentElement && frameworkContentElement.Parent != null)
            return frameworkContentElement.Parent;
        if (element is ContentElement contentElement)
            return ContentOperations.GetParent(contentElement) ?? LogicalTreeHelper.GetParent(element);
        return LogicalTreeHelper.GetParent(element);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScheduleDockPreviewRefresh();
    }

    private void OnDockLayoutChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded) ScheduleDockPreviewRefresh();
    }

    private void OnPresentationSourceChanged(object sender, SourceChangedEventArgs e)
    {
        if (IsLoaded) ScheduleDockPreviewRefresh();
    }

    private void ScheduleDockPreviewRefresh()
    {
        _dockPreviewRefreshTimer.Stop();
        _dockPreviewRefreshTimer.Start();
    }

    private void LibraryAsset_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement card || card.DataContext is not LibraryAsset asset) return;
        if (DataContext is PreviewPresetViewModel selectionVm && selectionVm.IsSelectionMode)
        {
            selectionVm.ToggleLibrarySelection(asset, (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
            e.Handled = true;
            return;
        }
        if (e.ClickCount >= 2)
        {
            StopLibraryPreview();
            LibraryPreviewPopup.IsOpen = false;
            if (DataContext is PreviewPresetViewModel vm) vm.AddLibraryAssetToTimeline(asset);
            e.Handled = true;
            return;
        }

        StopLibraryPreview();
        LibraryPopupImage.Source = asset.Thumbnail;
        LibraryPopupImage.Visibility = Visibility.Visible;
        LibraryPopupMedia.Visibility = Visibility.Collapsed;
        _libraryPreviewIsAudio = asset.Type is "BGM" or "SE";

        if (asset.Type == "動画")
        {
            _libraryVideoMuteRetryAttempted = false;
            _libraryVideoFallbackAttempted = false;
            LibraryPopupMedia.IsMuted = false;
            LibraryPopupMedia.Visibility = Visibility.Visible;
            LibraryPopupMedia.Source = new Uri(asset.Path);
            LibraryPopupMedia.Play();
            LibraryPauseButton.Visibility = Visibility.Visible;
            LibraryPauseButton.ContentTemplate = (DataTemplate)Resources["StopIcon"];
            LibraryPauseButton.ToolTip = "停止";
        }
        else if (_libraryPreviewIsAudio)
        {
            _libraryPreviewPlayer.Open(new Uri(asset.Path));
            _libraryPreviewPlayer.Play();
            LibraryPauseButton.Visibility = Visibility.Visible;
            LibraryPauseButton.ContentTemplate = (DataTemplate)Resources["StopIcon"];
            LibraryPauseButton.ToolTip = "停止";
        }

        PlaceLibraryPreview(e.GetPosition(RootGrid));
        LibraryPreviewPopup.IsOpen = true;
    }

    private void PlaceLibraryPreview(Point cursor)
    {
        const double previewWidth = 488;
        const double previewHeight = 358;
        const double margin = 8;
        const double gap = 14;
        double availableWidth = RootGrid.ActualWidth;
        double availableHeight = RootGrid.ActualHeight;
        double x = cursor.X + gap + previewWidth <= availableWidth - margin
            ? cursor.X + gap
            : cursor.X - gap - previewWidth;
        double y = cursor.Y + gap + previewHeight <= availableHeight - margin
            ? cursor.Y + gap
            : cursor.Y - gap - previewHeight;
        x = Math.Clamp(x, margin, Math.Max(margin, availableWidth - previewWidth - margin));
        y = Math.Clamp(y, margin, Math.Max(margin, availableHeight - previewHeight - margin));
        var chosen = new Point(x, y);

        LibraryPreviewPopup.HorizontalOffset = chosen.X;
        LibraryPreviewPopup.VerticalOffset = chosen.Y;
    }

    private void StopLibraryPreview()
    {
        _libraryPlaybackTimer.Stop();
        _libraryPreviewPlayer.Stop();
        _libraryPreviewPlayer.Close();
        LibraryPopupMedia.Stop();
        LibraryPopupMedia.Source = null;
        LibraryPopupMedia.IsMuted = false;
        LibrarySeekSlider.Visibility = Visibility.Collapsed;
        UpdateLibrarySeekTimeText(0, 0);
        LibraryPauseButton.Visibility = Visibility.Collapsed;
        LibraryPauseButton.ContentTemplate = (DataTemplate)Resources["StopIcon"];
        LibraryPauseButton.ToolTip = "停止";
        _libraryPreviewPaused = false;
        _libraryPreviewIsAudio = false;
    }

    private void ShowLibrarySeekBar(Duration duration)
    {
        if (!duration.HasTimeSpan) return;
        LibrarySeekSlider.Maximum = Math.Max(0.1, duration.TimeSpan.TotalSeconds);
        LibrarySeekSlider.Value = 0;
        UpdateLibrarySeekTimeText(0, duration.TimeSpan.TotalSeconds);
        LibrarySeekSlider.Visibility = Visibility.Visible;
        _libraryPlaybackTimer.Start();
    }

    private void UpdateLibrarySeekSliderFromPlayback()
    {
        if (_librarySeekDragging) return;
        Duration duration = _libraryPreviewIsAudio ? _libraryPreviewPlayer.NaturalDuration : LibraryPopupMedia.NaturalDuration;
        if (!duration.HasTimeSpan) return;
        TimeSpan position = _libraryPreviewIsAudio ? _libraryPreviewPlayer.Position : LibraryPopupMedia.Position;
        _librarySeekUpdating = true;
        LibrarySeekSlider.Value = position.TotalSeconds;
        _librarySeekUpdating = false;
        UpdateLibrarySeekTimeText(position.TotalSeconds, duration.TimeSpan.TotalSeconds);
    }

    private void UpdateLibrarySeekTimeText(double currentSeconds, double totalSeconds)
    {
        if (LibrarySeekTimeText == null) return;
        static string FormatTime(double seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
        }
        LibrarySeekTimeText.Text = $"{FormatTime(currentSeconds)} / {FormatTime(totalSeconds)}";
    }

    private void SeekLibraryPreview(double seconds)
    {
        var position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, LibrarySeekSlider.Maximum));
        if (_libraryPreviewIsAudio)
        {
            bool resumeAfterSeek = !_libraryPreviewPaused;
            _libraryPlaybackTimer.Stop();
            if (resumeAfterSeek) _libraryPreviewPlayer.Stop();
            _libraryPreviewPlayer.Position = position;
            UpdateLibrarySeekTimeText(position.TotalSeconds, LibrarySeekSlider.Maximum);
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                _libraryPreviewPlayer.Position = position;
                if (resumeAfterSeek && _libraryPreviewIsAudio && !_libraryPreviewPaused)
                {
                    _libraryPreviewPlayer.Play();
                    _libraryPlaybackTimer.Start();
                }
            }));
        }
        else if (LibraryPopupMedia.NaturalDuration.HasTimeSpan)
        {
            bool resumeAfterSeek = !_libraryPreviewPaused;
            _libraryPlaybackTimer.Stop();
            if (resumeAfterSeek) LibraryPopupMedia.Pause();
            LibraryPopupMedia.Position = position;
            UpdateLibrarySeekTimeText(position.TotalSeconds, LibrarySeekSlider.Maximum);
            // MediaElementによってはSliderのイベント中のPosition変更が反映されないため、UI更新後にも再適用する。
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                if (LibraryPopupMedia.NaturalDuration.HasTimeSpan)
                {
                    LibraryPopupMedia.Position = position;
                    if (resumeAfterSeek && !_libraryPreviewPaused)
                    {
                        LibraryPopupMedia.Play();
                        _libraryPlaybackTimer.Start();
                    }
                }
            }));
        }
    }

    private void LibraryPopupMedia_MediaOpened(object sender, RoutedEventArgs e)
    {
        LibraryPopupImage.Visibility = Visibility.Collapsed;
        LibraryPopupMedia.Play();
        ShowLibrarySeekBar(LibraryPopupMedia.NaturalDuration);
    }

    private void LibraryPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_libraryPreviewIsAudio && LibraryPopupMedia.Visibility != Visibility.Visible) return;
        if (_libraryPreviewPaused)
        {
            if (_libraryPreviewIsAudio) _libraryPreviewPlayer.Play();
            else LibraryPopupMedia.Play();
            _libraryPlaybackTimer.Start();
            LibraryPauseButton.ContentTemplate = (DataTemplate)Resources["StopIcon"];
            LibraryPauseButton.ToolTip = "停止";
        }
        else
        {
            if (_libraryPreviewIsAudio)
            {
                TimeSpan position = _libraryPreviewPlayer.Position;
                _libraryPreviewPlayer.Stop();
                _libraryPreviewPlayer.Position = position;
            }
            else LibraryPopupMedia.Pause();
            _libraryPlaybackTimer.Stop();
            LibraryPauseButton.ContentTemplate = (DataTemplate)Resources["PlayIcon"];
            LibraryPauseButton.ToolTip = "再生";
        }
        _libraryPreviewPaused = !_libraryPreviewPaused;
    }

    private void LibraryPopupMedia_MediaEnded(object sender, RoutedEventArgs e)
    {
        _libraryPlaybackTimer.Stop();
        LibraryPopupMedia.Stop();
    }

    private void LibraryPopupMedia_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Uri? failedSource = LibraryPopupMedia.Source;
        int hresult = e.ErrorException?.HResult ?? 0;
        if (failedSource != null && failedSource.IsFile && !_libraryVideoFallbackAttempted)
        {
            _libraryVideoFallbackAttempted = true;
            _ = RetryLibraryVideoWithFfmpegAsync(failedSource.LocalPath, hresult);
            return;
        }
        if (failedSource != null && !_libraryVideoMuteRetryAttempted)
        {
            _libraryVideoMuteRetryAttempted = true;
            LibraryPopupMedia.IsMuted = true;
            LibraryPopupMedia.Stop();
            LibraryPopupMedia.Source = null;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                LibraryPopupMedia.Source = failedSource;
                LibraryPopupMedia.Play();
            }));
            if (DataContext is PreviewPresetViewModel retryVm)
                retryVm.StatusMessage = $"音声出力をミュートして動画を再試行しています (0x{hresult:X8})";
            return;
        }
        _libraryPlaybackTimer.Stop();
        LibraryPopupMedia.Stop();
        LibraryPopupImage.Visibility = Visibility.Visible;
        if (DataContext is PreviewPresetViewModel vm)
            vm.StatusMessage = hresult == unchecked((int)0xC00D11B1)
                ? "動画を再生できませんでした (0xC00D11B1)。音声デバイスまたは動画の音声コーデックを確認してください。"
                : $"素材を再生できませんでした (0x{hresult:X8}): {e.ErrorException?.GetBaseException().Message}";
    }

    private async Task RetryLibraryVideoWithFfmpegAsync(string sourcePath, int hresult)
    {
        try
        {
            if (DataContext is PreviewPresetViewModel statusVm)
                statusVm.StatusMessage = $"動画形式を変換して再生しています (0x{hresult:X8})...";
            string cachePath = await Task.Run(() => TranscodeLibraryVideo(sourcePath));
            await Dispatcher.InvokeAsync(() =>
            {
                LibraryPopupMedia.IsMuted = false;
                LibraryPopupMedia.Source = new Uri(cachePath);
                LibraryPopupMedia.Play();
            });
        }
        catch (Exception ex)
        {
            if (DataContext is PreviewPresetViewModel vm)
                vm.StatusMessage = $"動画を再生できませんでした (0x{hresult:X8})。互換形式への変換にも失敗しました: {ex.GetBaseException().Message}";
        }
    }

    private static string TranscodeLibraryVideo(string sourcePath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("動画ファイルが見つかりません", sourcePath);
        string ffmpeg = FindLibraryFfmpegPath();
        var info = new FileInfo(sourcePath);
        string keySource = $"{Path.GetFullPath(sourcePath)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keySource))).ToLowerInvariant();
        string outputPath = Models.PresetStorage.GetLibraryVideoCachePath(key);
        if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) return outputPath;
        string temporaryPath = outputPath + ".partial";
        try
        {
            var start = new ProcessStartInfo(ffmpeg)
            {
                WorkingDirectory = Path.GetDirectoryName(ffmpeg)!, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
            };
            start.ArgumentList.Add("-y"); start.ArgumentList.Add("-i"); start.ArgumentList.Add(sourcePath);
            start.ArgumentList.Add("-map"); start.ArgumentList.Add("0:v:0");
            start.ArgumentList.Add("-map"); start.ArgumentList.Add("0:a:0?");
            start.ArgumentList.Add("-c:v"); start.ArgumentList.Add("h264_mf");
            start.ArgumentList.Add("-pix_fmt"); start.ArgumentList.Add("nv12");
            start.ArgumentList.Add("-c:a"); start.ArgumentList.Add("aac"); start.ArgumentList.Add("-b:a"); start.ArgumentList.Add("192k");
            start.ArgumentList.Add("-movflags"); start.ArgumentList.Add("+faststart"); start.ArgumentList.Add(temporaryPath);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("FFmpegを起動できませんでした");
            string errors = process.StandardError.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(errors.Trim());
            File.Move(temporaryPath, outputPath, true);
            return outputPath;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }

    private static string FindLibraryFfmpegPath()
    {
        string? directory = null;
        try
        {
            var locatorType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("YukkuriMovieMaker.Plugin.FileSource.FFmpeg.FFmpegResourceLocator", false))
                .FirstOrDefault(t => t != null);
            directory = locatorType?.GetMethod("GetUserFFmpegDirectory", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null) as string;
        }
        catch { }
        directory ??= AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => { try { return Path.GetDirectoryName(a.Location); } catch { return null; } })
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.Combine(p!, "user", "resources", "ffmpeg"))
            .FirstOrDefault(p => File.Exists(Path.Combine(p, "ffmpeg.exe")));
        string? path = string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, "ffmpeg.exe");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("YMM4付属のFFmpegが見つかりません", path ?? "YMM4\\user\\resources\\ffmpeg\\ffmpeg.exe");
        return path;
    }

    private void LibrarySeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_librarySeekUpdating) return;
        // 音声はドラッグ中に何度も再生位置を変更するとノイズが出るため、MouseUp時だけ適用する。
        if (_librarySeekDragging) return;
        SeekLibraryPreview(LibrarySeekSlider.Value);
    }

    private void LibrarySeekSlider_MouseDown(object sender, MouseButtonEventArgs e) => _librarySeekDragging = true;

    private void LibrarySeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_librarySeekDragging) SeekLibraryPreview(LibrarySeekSlider.Value);
        _librarySeekDragging = false;
    }

    private async void LibrarySetThumbnailTime_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not LibraryAsset asset || asset.Type != "動画" || DataContext is not PreviewPresetViewModel vm) return;
        await vm.SetLibraryThumbnailTimeAsync(asset);
    }

    private void LibraryRemoveAsset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is LibraryAsset asset && DataContext is PreviewPresetViewModel vm)
            vm.RemoveLibraryAsset(asset);
    }

    private void LibrarySetCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is LibraryAsset asset && DataContext is PreviewPresetViewModel vm)
            vm.SetLibraryAssetCategory(asset);
    }

    private void LibrarySetVolume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is LibraryAsset asset && DataContext is PreviewPresetViewModel vm)
            vm.SetLibraryAssetVolume(asset);
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement elem)
        {
            elem.Focus();
        }
    }

    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement card || card.DataContext is not TextPresetItemViewModel itemVM || DataContext is not PreviewPresetViewModel vm) return;
        if (vm.IsSelectionMode)
        {
            vm.TogglePresetSelection(itemVM);
            e.Handled = true;
            return;
        }

        _pendingDragCard = card;
        _pendingDragItem = itemVM;
        _mouseDownPosition = e.GetPosition(RootGrid);
        if (vm.IsReorderMode) return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            vm.BeginReorderMode();
            _suppressNextCardClick = true;
            e.Handled = true;
        }
    }

    private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not PreviewPresetViewModel viewModel || !viewModel.IsReorderMode ||
            Mouse.LeftButton != MouseButtonState.Pressed || _pendingDragCard == null || _pendingDragItem == null) return;

        Point current = e.GetPosition(RootGrid);
        if (Math.Abs(current.X - _mouseDownPosition.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _mouseDownPosition.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _suppressNextCardClick = true;
        StartReorderDrag(_pendingDragCard, _pendingDragItem);
    }

    private void StartReorderDrag(FrameworkElement card, TextPresetItemViewModel itemVM)
    {
        _pendingDragCard = null;
        _pendingDragItem = null;
        DragDrop.DoDragDrop(card, itemVM, DragDropEffects.Move);
    }

    private void Card_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool handled = _suppressNextCardClick || DataContext is PreviewPresetViewModel vm && vm.IsReorderMode;
        _suppressNextCardClick = false;
        _pendingDragCard = null;
        _pendingDragItem = null;
        if (handled) e.Handled = true;
    }

    private void Card_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (DataContext is PreviewPresetViewModel viewModel && viewModel.IsReorderMode &&
            e.Data.GetData(typeof(TextPresetItemViewModel)) is TextPresetItemViewModel)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else e.Effects = DragDropEffects.None;
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not PreviewPresetViewModel viewModel || sender is not FrameworkElement targetCard ||
            targetCard.DataContext is not TextPresetItemViewModel target ||
            e.Data.GetData(typeof(TextPresetItemViewModel)) is not TextPresetItemViewModel source) return;

        viewModel.ReorderPresets(source, target);
        e.Handled = true;
    }

    private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (LibraryPreviewPopup.IsOpen && !IsLibraryPreviewChrome(e.OriginalSource as DependencyObject))
            LibraryPreviewPopup.IsOpen = false;
        if (DataContext is not PreviewPresetViewModel viewModel || !viewModel.IsReorderMode) return;
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current != null)
        {
            if (current is ButtonBase || current is FrameworkElement child && child.DataContext is PresetPatternItemViewModel)
            {
                viewModel.EndReorderMode();
                return;
            }
            if (current is FrameworkElement element && element.DataContext is TextPresetItemViewModel) return;
            current = GetParent(current);
        }
        viewModel.EndReorderMode();
    }

    private bool IsLibraryPreviewChrome(DependencyObject? current)
    {
        while (current != null)
        {
            if (ReferenceEquals(current, LibrarySeekSlider) || ReferenceEquals(current, LibraryPauseButton)) return true;
            if (current is FrameworkElement element && element.DataContext is LibraryAsset) return true;
            current = GetParent(current);
        }
        return false;
    }

    private async void Card_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not FrameworkElement card || card.DataContext is not TextPresetItemViewModel itemVM) return;

        bool audioOnlyTemplate = itemVM.IsItemTemplate && itemVM.ActivePattern.Items.Count > 0 &&
            itemVM.ActivePattern.Items.All(item => item is YukkuriMovieMaker.Project.Items.AudioItem);
        if (audioOnlyTemplate)
        {
            if ((string.IsNullOrWhiteSpace(itemVM.PreviewAudioPath) || !System.IO.File.Exists(itemVM.PreviewAudioPath)) &&
                DataContext is PreviewPresetViewModel viewModel)
                await viewModel.EnsureAudioTemplatePreviewAsync(itemVM);

            if (string.IsNullOrWhiteSpace(itemVM.PreviewAudioPath) || !System.IO.File.Exists(itemVM.PreviewAudioPath) || !card.IsKeyboardFocusWithin) return;
            StopAudioPreview();
            var video = FindVisualChild<MediaElement>(card);
            if (video != null)
            {
                video.Stop();
                video.Source = null;
                video.Visibility = Visibility.Collapsed;
            }
            _audioPreviewCard = card;
            _audioPreviewPlayer.Open(new Uri(itemVM.PreviewAudioPath, UriKind.Absolute));
            return;
        }

        StopAudioPreview();
        if (string.IsNullOrWhiteSpace(itemVM.PreviewVideoPath) || !System.IO.File.Exists(itemVM.PreviewVideoPath) || !card.IsKeyboardFocusWithin) return;

        var media = FindVisualChild<MediaElement>(card);
        if (media == null) return;
        media.Stop();
        media.Source = new Uri(itemVM.PreviewVideoPath, UriKind.Absolute);
        media.Visibility = Visibility.Visible;
        media.MediaOpened -= CardPreviewVideo_MediaOpened;
        media.MediaOpened += CardPreviewVideo_MediaOpened;
        media.Play();
    }

    private void Card_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not FrameworkElement card) return;
        if (ReferenceEquals(_audioPreviewCard, card)) StopAudioPreview();
        var media = FindVisualChild<MediaElement>(card);
        if (media == null) return;
        media.Stop();
        media.Source = null;
        media.Visibility = Visibility.Collapsed;
    }

    private void StopAudioPreview()
    {
        _audioPreviewPlayer.Stop();
        _audioPreviewPlayer.Close();
        _audioPreviewCard = null;
    }

    private void CardPreviewVideo_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MediaElement media && FindCard(media)?.IsKeyboardFocusWithin == true)
        {
            media.Position = TimeSpan.Zero;
            media.Play();
        }
    }

    private void CardPreviewVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (sender is MediaElement media && FindCard(media)?.IsKeyboardFocusWithin == true)
        {
            media.Position = TimeSpan.Zero;
            media.Play();
        }
    }

    private void CardPreviewVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is MediaElement media)
        {
            media.Stop();
            media.Visibility = Visibility.Collapsed;
        }
    }

    private static FrameworkElement? FindCard(DependencyObject current)
    {
        var parent = VisualTreeHelper.GetParent(current);
        while (parent != null)
        {
            if (parent is FrameworkElement element && element.DataContext is TextPresetItemViewModel) return element;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null) return descendant;
        }
        return null;
    }

    private void Card_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (sender is FrameworkElement elem && elem.DataContext is TextPresetItemViewModel itemVM)
            {
                if (DataContext is PreviewPresetViewModel vm)
                {
                    vm.CyclePattern(itemVM, forward: e.Delta > 0);
                    e.Handled = true;
                }
            }
        }
    }

    private void Card_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            if (sender is FrameworkElement elem && elem.DataContext is TextPresetItemViewModel itemVM)
            {
                if (DataContext is PreviewPresetViewModel vm)
                {
                    vm.DeleteCommand.Execute(itemVM);
                    e.Handled = true;
                    return;
                }
            }
        }

        int slot = 0;
        switch (e.Key)
        {
            case Key.D1:
            case Key.NumPad1:
                slot = 1; break;
            case Key.D2:
            case Key.NumPad2:
                slot = 2; break;
            case Key.D3:
            case Key.NumPad3:
                slot = 3; break;
            case Key.D4:
            case Key.NumPad4:
                slot = 4; break;
            case Key.D5:
            case Key.NumPad5:
                slot = 5; break;
            case Key.D6:
            case Key.NumPad6:
                slot = 6; break;
            case Key.D7:
            case Key.NumPad7:
                slot = 7; break;
            case Key.D8:
            case Key.NumPad8:
                slot = 8; break;
            case Key.D9:
            case Key.NumPad9:
                slot = 9; break;
            case Key.D0:
            case Key.NumPad0:
                slot = 10; break;
        }

        if (slot > 0)
        {
            if (sender is FrameworkElement elem && elem.DataContext is TextPresetItemViewModel itemVM)
            {
                if (DataContext is PreviewPresetViewModel vm)
                {
                    vm.SwitchPattern(itemVM, slot);
                    e.Handled = true;
                }
            }
        }
    }

    private void PatternDot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is PresetPatternItemViewModel patVM)
        {
            if (FindAncestor<Border>(elem)?.DataContext is TextPresetItemViewModel itemVM)
            {
                if (DataContext is PreviewPresetViewModel vm)
                {
                    vm.SwitchPattern(itemVM, patVM.Slot);
                    e.Handled = true;
                }
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(current);
        while (parent != null)
        {
            if (parent is T typed) return typed;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
