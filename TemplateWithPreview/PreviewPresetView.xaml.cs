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
    private bool _librarySeekDragging;
    private bool _librarySeekUpdating;
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
        _libraryPlaybackTimer.Tick += (_, _) =>
        {
            if (_librarySeekDragging || !LibraryPopupMedia.NaturalDuration.HasTimeSpan) return;
            _librarySeekUpdating = true;
            LibrarySeekSlider.Value = LibraryPopupMedia.Position.TotalSeconds;
            _librarySeekUpdating = false;
        };
        Loaded += OnLoaded;
        SizeChanged += OnDockLayoutChanged;
        PresentationSource.AddSourceChangedHandler(this, OnPresentationSourceChanged);
        Unloaded += (_, _) =>
        {
            _dockPreviewRefreshTimer.Stop();
            _libraryPlaybackTimer.Stop();
            LibraryPopupMedia.Stop();
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

    private void LibraryPreviewPopup_Closed(object? sender, EventArgs e)
    {
        _libraryPlaybackTimer.Stop();
        LibraryPopupMedia.Stop();
        LibraryPopupMedia.Source = null;
        LibrarySeekSlider.Visibility = Visibility.Collapsed;
    }

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
        if (e.ClickCount >= 2)
        {
            if (DataContext is PreviewPresetViewModel vm) vm.AddLibraryAssetToTimeline(asset);
            e.Handled = true;
            return;
        }

        LibraryPopupMedia.Stop();
        LibraryPopupMedia.Source = null;
        LibraryPopupMedia.Visibility = Visibility.Collapsed;
        LibraryPopupImage.Source = asset.Thumbnail;
        LibraryPopupImage.Visibility = Visibility.Visible;
        LibrarySeekSlider.Visibility = Visibility.Collapsed;
        _libraryPlaybackTimer.Stop();

        if (asset.Type is "BGM" or "SE" or "動画")
        {
            LibraryPopupMedia.Visibility = Visibility.Visible;
            LibraryPopupMedia.Opacity = asset.Type == "動画" ? 1 : 0;
            if (asset.Type == "動画") LibraryPopupImage.Visibility = Visibility.Collapsed;
            LibraryPopupMedia.Source = new System.Uri(asset.Path);
            LibraryPopupMedia.Play();
        }

        Point point = e.GetPosition(RootGrid);
        const double previewWidth = 328;
        const double previewHeight = 198;
        double x = point.X + 18;
        double y = point.Y + 18;
        if (x + previewWidth > RootGrid.ActualWidth) x = Math.Max(0, point.X - previewWidth - 12);
        if (y + previewHeight > RootGrid.ActualHeight) y = Math.Max(0, point.Y - previewHeight - 12);
        LibraryPreviewPopup.HorizontalOffset = x;
        LibraryPreviewPopup.VerticalOffset = y;
        LibraryPreviewPopup.IsOpen = true;
    }

    private void LibraryPopupMedia_MediaOpened(object sender, RoutedEventArgs e)
    {
        LibraryPopupMedia.Play();
        if (LibraryPopupMedia.NaturalDuration.HasTimeSpan)
        {
            LibrarySeekSlider.Maximum = Math.Max(0.1, LibraryPopupMedia.NaturalDuration.TimeSpan.TotalSeconds);
            LibrarySeekSlider.Value = 0;
            LibrarySeekSlider.Visibility = Visibility.Visible;
            _libraryPlaybackTimer.Start();
        }
    }

    private void LibraryPopupMedia_MediaEnded(object sender, RoutedEventArgs e)
    {
        _libraryPlaybackTimer.Stop();
        LibraryPopupMedia.Stop();
    }

    private void LibraryPopupMedia_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _libraryPlaybackTimer.Stop();
        LibraryPopupMedia.Stop();
        if (DataContext is PreviewPresetViewModel vm)
            vm.StatusMessage = $"素材を再生できませんでした: {e.ErrorException?.GetBaseException().Message}";
    }

    private void LibrarySeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_librarySeekUpdating || !_librarySeekDragging || !LibraryPopupMedia.NaturalDuration.HasTimeSpan) return;
        LibraryPopupMedia.Position = TimeSpan.FromSeconds(Math.Clamp(LibrarySeekSlider.Value, 0, LibrarySeekSlider.Maximum));
    }

    private void LibrarySeekSlider_MouseDown(object sender, MouseButtonEventArgs e) => _librarySeekDragging = true;

    private void LibrarySeekSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_librarySeekDragging && LibraryPopupMedia.NaturalDuration.HasTimeSpan)
            LibraryPopupMedia.Position = TimeSpan.FromSeconds(Math.Clamp(LibrarySeekSlider.Value, 0, LibrarySeekSlider.Maximum));
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
        if (LibraryPreviewPopup.IsOpen && !IsLibraryAssetSource(e.OriginalSource as DependencyObject))
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

    private static bool IsLibraryAssetSource(DependencyObject? current)
    {
        while (current != null)
        {
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
