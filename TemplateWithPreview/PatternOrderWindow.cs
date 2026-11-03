using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TemplateWithPreview.Models;
using YukkuriMovieMaker.Controls;

namespace TemplateWithPreview;

internal sealed class PatternOrderWindow : Window
{
    private sealed class PatternRow
    {
        public PresetPattern Pattern { get; }
        public Color Color { get; set; }
        public PatternRow(PresetPattern pattern)
        {
            Pattern = pattern;
            try { Color = (Color)ColorConverter.ConvertFromString(pattern.SampleColor); }
            catch { Color = Colors.White; }
        }
    }

    private readonly List<PatternRow> _rows;
    private readonly StackPanel _rowsPanel = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap };
    private PatternRow? _movingRow;

    public List<PresetPattern> OrderedPatterns => _rows.Select(row => row.Pattern).ToList();
    public Dictionary<PresetPattern, string> PatternColors => _rows.ToDictionary(row => row.Pattern, row => ToHex(row.Color));

    public PatternOrderWindow(TextPreset preset)
    {
        _rows = preset.Patterns.OrderBy(pattern => pattern.Slot).Select(pattern => new PatternRow(pattern)).ToList();
        Title = $"パターンの並び替え - {preset.Name}";
        Width = 480;
        Height = 560;
        MinWidth = 400;
        MinHeight = 400;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;

        var root = new DockPanel { Margin = new Thickness(12) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        var cancel = new Button { Content = "キャンセル", IsCancel = true, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 0) };
        var save = new Button { Content = "保存", IsDefault = true, Padding = new Thickness(18, 4, 18, 4) };
        save.Click += Save_Click;
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        root.Children.Add(footer);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "☰で移動元を選び、色選択コントロールの間にある「ここへ移動」を押すと順番を変更できます。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        content.Children.Add(_status);

        var listGroup = new GroupBox { Header = "パターン順序・色", Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8) };
        listGroup.Content = new ScrollViewer
        {
            Content = _rowsPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        content.Children.Add(listGroup);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false });
        Content = root;
        RebuildRows();
    }

    private void RebuildRows()
    {
        _rowsPanel.Children.Clear();
        for (int index = 0; index <= _rows.Count; index++)
        {
            int insertionIndex = index;
            var insert = new Button
            {
                Content = _movingRow == null ? "　" : "ここへ移動",
                IsEnabled = _movingRow != null,
                Padding = new Thickness(5, 0, 5, 0),
                Margin = new Thickness(32, 1, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 76
            };
            insert.Click += (_, _) => InsertMovingRow(insertionIndex);
            _rowsPanel.Children.Add(insert);
            if (index == _rows.Count) break;

            var row = _rows[index];
            var panel = new DockPanel { Margin = new Thickness(0, 1, 0, 1), LastChildFill = true };
            var label = new TextBlock { Text = $"P{index + 1}", Width = 32, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(label, Dock.Left);
            panel.Children.Add(label);
            var colorPicker = new ColorPicker { Value = row.Color, Width = 82, Height = 27, Margin = new Thickness(2, 0, 6, 0) };
            DependencyPropertyDescriptor.FromProperty(ColorPicker.ValueProperty, typeof(ColorPicker))?
                .AddValueChanged(colorPicker, (_, _) => row.Color = colorPicker.Value);
            DockPanel.SetDock(colorPicker, Dock.Left);
            panel.Children.Add(colorPicker);
            var handle = new Button { Content = "☰", Width = 34, Height = 27, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(2), ToolTip = "このパターンを移動" };
            handle.Click += (_, _) =>
            {
                _movingRow = ReferenceEquals(_movingRow, row) ? null : row;
                _status.Text = _movingRow == null ? "" : $"P{_rows.IndexOf(row) + 1}を移動対象にしました。挿入位置の「ここへ移動」を押してください。";
                RebuildRows();
            };
            DockPanel.SetDock(handle, Dock.Right);
            panel.Children.Add(handle);
            var colorLabel = new TextBlock { Text = ToHex(row.Color), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
            DependencyPropertyDescriptor.FromProperty(ColorPicker.ValueProperty, typeof(ColorPicker))?
                .AddValueChanged(colorPicker, (_, _) => colorLabel.Text = ToHex(row.Color));
            panel.Children.Add(colorLabel);
            if (ReferenceEquals(_movingRow, row))
                panel.Background = SystemColors.HighlightBrush;
            _rowsPanel.Children.Add(panel);
        }
    }

    private void InsertMovingRow(int insertionIndex)
    {
        if (_movingRow == null) return;
        MoveRow(_movingRow, insertionIndex);
    }

    private void MoveRow(PatternRow row, int insertionIndex)
    {
        int oldIndex = _rows.IndexOf(row);
        if (oldIndex < 0) return;
        _rows.RemoveAt(oldIndex);
        if (oldIndex < insertionIndex) insertionIndex--;
        _rows.Insert(Math.Clamp(insertionIndex, 0, _rows.Count), row);
        _movingRow = null;
        _status.Text = "パターンの順番を変更しました。保存で確定します。";
        RebuildRows();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Select(row => ToHex(row.Color)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _rows.Count)
        {
            MessageBox.Show(this, "パターン色が重複しています。各パターンに異なる色を設定してください。", "色の重複", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

}
