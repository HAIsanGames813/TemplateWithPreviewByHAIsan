using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace TemplateWithPreview;

internal sealed class PreviewSettingsWindow : Window
{
    public PreviewSettingsWindow(PreviewPresetViewModel viewModel)
    {
        Title = "プリセット表示設定";
        Width = 440;
        Height = 390;
        MinWidth = 400;
        MinHeight = 350;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;
        DataContext = viewModel;

        var root = new DockPanel { Margin = new Thickness(16) };
        var close = new Button { Content = "閉じる", IsDefault = true, IsCancel = true, Width = 84, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var content = new StackPanel();
        var simpleView = new ToggleButton { Content = "簡易表示", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(10, 5, 10, 5), ToolTip = "プレビュー画像と右上のパターン色だけを表示します" };
        simpleView.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(PreviewPresetViewModel.IsSimpleView)) { Mode = BindingMode.TwoWay });
        content.Children.Add(simpleView);
        content.Children.Add(new TextBlock { Text = "表示サイズ", Margin = new Thickness(0, 0, 0, 5), FontWeight = FontWeights.SemiBold });
        var slider = new Slider { Minimum = 80, Maximum = 360, TickFrequency = 20, HorizontalAlignment = HorizontalAlignment.Stretch };
        slider.SetBinding(Slider.ValueProperty, new Binding(nameof(PreviewPresetViewModel.PresetCardWidth)) { Mode = BindingMode.TwoWay });
        content.Children.Add(slider);

        content.Children.Add(new TextBlock { Text = "プレビュー背景色", Margin = new Thickness(0, 16, 0, 5), FontWeight = FontWeights.SemiBold });
        var colorPicker = new YukkuriMovieMaker.Controls.ColorPicker { Width = 56, Height = 26, HorizontalAlignment = HorizontalAlignment.Left };
        colorPicker.SetBinding(YukkuriMovieMaker.Controls.ColorPicker.ValueProperty, new Binding(nameof(PreviewPresetViewModel.PreviewBackgroundColor)) { Mode = BindingMode.TwoWay });
        content.Children.Add(colorPicker);

        var load = new Button { Content = "YMM4テンプレートを読み込む", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(10, 5, 10, 5) };
        load.Click += (_, _) => viewModel.LoadYmmTemplatesCommand.Execute(null);
        content.Children.Add(load);
        var importPresets = new Button { Content = "プリセットファイルをインポート...", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 5, 10, 5) };
        importPresets.SetBinding(Button.CommandProperty, new Binding(nameof(PreviewPresetViewModel.ImportCommand)));
        content.Children.Add(importPresets);
        content.Children.Add(new TextBlock { Text = "プレビュー画像・動画の保存先", Margin = new Thickness(0, 18, 0, 4), FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "保存先フォルダーを開くか、別のフォルダーへ変更できます。パスは画面に表示しません。", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        var folderButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var openFolder = new Button { Content = "保存先を開く", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        openFolder.SetBinding(Button.CommandProperty, new Binding(nameof(PreviewPresetViewModel.OpenPreviewStorageDirectoryCommand)));
        var changeFolder = new Button { Content = "保存先を変更...", Padding = new Thickness(10, 4, 10, 4) };
        changeFolder.SetBinding(Button.CommandProperty, new Binding(nameof(PreviewPresetViewModel.ChangePreviewStorageDirectoryCommand)));
        folderButtons.Children.Add(openFolder);
        folderButtons.Children.Add(changeFolder);
        content.Children.Add(folderButtons);
        var cleanup = new Button { Content = "未使用のプレビューデータを削除", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(10, 5, 10, 5) };
        cleanup.SetBinding(Button.CommandProperty, new Binding(nameof(PreviewPresetViewModel.DeleteUnusedPreviewDataCommand)));
        content.Children.Add(cleanup);

        content.Children.Add(new TextBlock { Text = "素材ライブラリの監視フォルダ", Margin = new Thickness(0, 18, 0, 5), FontWeight = FontWeights.SemiBold });
        var watchEnabled = new CheckBox { Content = "監視フォルダを有効にする", Margin = new Thickness(0, 0, 0, 5) };
        watchEnabled.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(PreviewPresetViewModel.LibraryMonitorEnabled)) { Mode = BindingMode.TwoWay });
        content.Children.Add(watchEnabled);
        var watchFolders = new ListBox { Height = 88, MinHeight = 56, ItemsSource = viewModel.LibraryWatchFolders, SelectionMode = SelectionMode.Single };
        content.Children.Add(watchFolders);
        var watchButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        var addWatchFolder = new Button { Content = "フォルダを追加...", Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 6, 0) };
        addWatchFolder.Click += (_, _) => viewModel.AddLibraryWatchFolder();
        var removeWatchFolder = new Button { Content = "選択を削除", Padding = new Thickness(9, 4, 9, 4) };
        removeWatchFolder.Click += (_, _) => viewModel.RemoveLibraryWatchFolder(watchFolders.SelectedItem as string);
        watchButtons.Children.Add(addWatchFolder);
        watchButtons.Children.Add(removeWatchFolder);
        content.Children.Add(watchButtons);
        root.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        });
        Content = root;
    }
}
