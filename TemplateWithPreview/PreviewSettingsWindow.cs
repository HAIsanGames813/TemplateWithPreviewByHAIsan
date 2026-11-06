using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace TemplateWithPreview;

internal sealed class PreviewSettingsWindow : Window
{
    public PreviewSettingsWindow(PreviewPresetViewModel viewModel)
    {
        Title = "プリセット表示設定";
        Width = 440;
        Height = 560;
        MinWidth = 400;
        MinHeight = 420;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;
        Style = Application.Current?.TryFindResource(typeof(Window)) as Style;
        DataContext = viewModel;

        var root = new DockPanel { Margin = new Thickness(16) };
        var close = new Button { Content = "閉じる", IsDefault = true, IsCancel = true, Width = 84, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var content = new StackPanel();
        var simpleView = new YukkuriMovieMaker.Controls.ToggleSlider { Width = 72, Height = 26, ToolTip = "プレビュー画像と右上のパターン色だけを表示します" };
        simpleView.SetBinding(YukkuriMovieMaker.Controls.ToggleSlider.ValueProperty, new Binding(nameof(PreviewPresetViewModel.IsSimpleView)) { Mode = BindingMode.TwoWay });
        AddSettingRow(content, "簡易表示", simpleView);
        var slider = CreateYmmTextBoxSlider(80, 100, 400, "px");
        slider.SetBinding(YukkuriMovieMaker.Controls.TextBoxSlider.ValueProperty, new Binding(nameof(PreviewPresetViewModel.PresetCardWidth)) { Mode = BindingMode.TwoWay });
        AddSettingRow(content, "表示サイズ", slider);

        var colorPicker = new YukkuriMovieMaker.Controls.ColorPicker { Width = 56, Height = 26, HorizontalAlignment = HorizontalAlignment.Left };
        colorPicker.SetBinding(YukkuriMovieMaker.Controls.ColorPicker.ValueProperty, new Binding(nameof(PreviewPresetViewModel.PreviewBackgroundColor)) { Mode = BindingMode.TwoWay });
        AddSettingRow(content, "プレビュー背景色", colorPicker);

        var load = new Button { Content = "読み込む", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10, 4, 10, 4) };
        load.Click += (_, _) => viewModel.LoadYmmTemplatesCommand.Execute(null);
        AddSettingRow(content, "YMM4テンプレート", load);
        var importPresets = new Button { Content = "読み込む", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10, 4, 10, 4) };
        importPresets.SetBinding(Button.CommandProperty, new Binding(nameof(PreviewPresetViewModel.ImportCommand)));
        AddSettingRow(content, "プリセットファイル", importPresets);
        content.Children.Add(new TextBlock { Text = "プレビュー画像・動画の保存先", Margin = new Thickness(0, 12, 0, 4), FontWeight = FontWeights.SemiBold });
        var previewDirectory = new YukkuriMovieMaker.Controls.DirectorySelector { Width = 300, Height = 28, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "YMM4標準のフォルダー選択コントロール" };
        previewDirectory.SetBinding(YukkuriMovieMaker.Controls.DirectorySelector.ValueProperty, new Binding(nameof(PreviewPresetViewModel.PreviewStorageDirectory)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AddSettingRow(content, "保存先フォルダー", previewDirectory);
        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        var openFolder = new Button { Content = "保存先を開く", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        openFolder.SetBinding(Button.CommandProperty, new Binding(nameof(PreviewPresetViewModel.OpenPreviewStorageDirectoryCommand)));
        folderButtons.Children.Add(openFolder);
        content.Children.Add(folderButtons);
        var cleanup = new Button { Content = "未使用のプレビューデータを削除", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(10, 5, 10, 5) };
        cleanup.SetBinding(Button.CommandProperty, new Binding(nameof(PreviewPresetViewModel.DeleteUnusedPreviewDataCommand)));
        content.Children.Add(cleanup);

        content.Children.Add(new TextBlock { Text = "素材ライブラリの監視フォルダ（種別ごと）", Margin = new Thickness(0, 18, 0, 5), FontWeight = FontWeights.SemiBold });
        var watchEnabled = new YukkuriMovieMaker.Controls.ToggleSlider { Width = 72, Height = 26, ToolTip = "監視フォルダの自動更新を切り替えます" };
        watchEnabled.SetBinding(YukkuriMovieMaker.Controls.ToggleSlider.ValueProperty, new Binding(nameof(PreviewPresetViewModel.LibraryMonitorEnabled)) { Mode = BindingMode.TwoWay });
        AddSettingRow(content, "監視フォルダ", watchEnabled);
        content.Children.Add(new TextBlock { Text = "素材追加時の既定音量（%）", Margin = new Thickness(0, 8, 0, 3), FontWeight = FontWeights.SemiBold });
        AddVolumeEditor(content, "BGM", nameof(PreviewPresetViewModel.BgmDefaultVolumePercent));
        AddVolumeEditor(content, "SE", nameof(PreviewPresetViewModel.SeDefaultVolumePercent));
        AddVolumeEditor(content, "動画", nameof(PreviewPresetViewModel.VideoDefaultVolumePercent));
        var normalize = new YukkuriMovieMaker.Controls.ToggleSlider { Width = 72, Height = 26, ToolTip = "追加時に最大音量を自動解析して補正します" };
        normalize.SetBinding(YukkuriMovieMaker.Controls.ToggleSlider.ValueProperty, new Binding(nameof(PreviewPresetViewModel.LibraryAutoNormalizeVolume)) { Mode = BindingMode.TwoWay });
        AddSettingRow(content, "最大音量を自動解析", normalize);
        var normalizeSlider = CreateYmmTextBoxSlider(1, 100, 400, "%");
        normalizeSlider.SetBinding(YukkuriMovieMaker.Controls.TextBoxSlider.ValueProperty, new Binding(nameof(PreviewPresetViewModel.LibraryAutoNormalizeTargetPercent)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AddSettingRow(content, "解析後の最大音量", normalizeSlider);
        var folderSources = new (string Type, System.Collections.IEnumerable Items)[]
        {
            ("BGM", viewModel.BgmWatchFolders), ("SE", viewModel.SeWatchFolders),
            ("動画", viewModel.VideoWatchFolders), ("画像", viewModel.ImageWatchFolders)
        };
        foreach (var source in folderSources)
        {
            content.Children.Add(new TextBlock { Text = source.Type, Margin = new Thickness(0, 5, 0, 2), FontWeight = FontWeights.SemiBold });
            var watchFolders = new ListBox { Height = 86, MinHeight = 52, ItemsSource = source.Items, SelectionMode = SelectionMode.Single };
            ScrollViewer.SetVerticalScrollBarVisibility(watchFolders, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(watchFolders, ScrollBarVisibility.Disabled);
            ScrollViewer.SetCanContentScroll(watchFolders, false);
            watchFolders.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, e) =>
            {
                var internalScroll = FindVisualChild<ScrollViewer>(watchFolders);
                if (internalScroll == null || internalScroll.ScrollableHeight <= 0) return;
                internalScroll.ScrollToVerticalOffset(internalScroll.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            }), true);
            content.Children.Add(watchFolders);
            var watchButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 2) };
            var addWatchFolder = new Button { Content = "追加...", Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 0) };
            addWatchFolder.Click += (_, _) => viewModel.AddLibraryWatchFolder(source.Type);
            var removeWatchFolder = new Button { Content = "削除", Padding = new Thickness(9, 3, 9, 3) };
            removeWatchFolder.Click += (_, _) => viewModel.RemoveLibraryWatchFolder(watchFolders.SelectedItem as string, source.Type);
            watchButtons.Children.Add(addWatchFolder);
            watchButtons.Children.Add(removeWatchFolder);
            content.Children.Add(watchButtons);
        }
        root.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        });
        Content = root;

        void AddSettingRow(Panel parent, string label, FrameworkElement control)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3), MinHeight = 32 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            control.VerticalAlignment = VerticalAlignment.Center;
            control.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            parent.Children.Add(row);
        }

        static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        void AddVolumeEditor(Panel parent, string label, string propertyName)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Width = 230 };
            var slider = CreateYmmTextBoxSlider(0, 100, 400, "%");
            slider.SetBinding(YukkuriMovieMaker.Controls.TextBoxSlider.ValueProperty, new Binding(propertyName) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            panel.Children.Add(slider);
            AddSettingRow(parent, label, panel);
        }

        static YukkuriMovieMaker.Controls.TextBoxSlider CreateYmmTextBoxSlider(double minimum, double initialMaximum, double maximum, string unit)
        {
            return new YukkuriMovieMaker.Controls.TextBoxSlider
            {
                Width = 230,
                Height = 28,
                Min = minimum,
                Max = maximum,
                DefaultMin = minimum,
                DefaultMax = initialMaximum,
                Unit = unit,
                IsUnitVisible = true,
                StringFormat = "F1",
                Delta = 0.1,
                ToolTip = $"初期範囲 {minimum:0}～{initialMaximum:0}{unit}。上限到達時は倍々に拡張します。"
            };
        }

    }
}
