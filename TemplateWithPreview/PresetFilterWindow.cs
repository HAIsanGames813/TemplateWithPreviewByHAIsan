using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TemplateWithPreview.Models;
using YukkuriMovieMaker.Project.Items;

namespace TemplateWithPreview;

internal sealed class PresetFilterWindow : Window
{
    private readonly ComboBox _sort = new();
    private readonly ComboBox _direction = new();
    private readonly CheckBox _onlySelectedTypes = new() { Content = "選択した種類のみを含む", Margin = new Thickness(0, 6, 0, 4) };
    private readonly List<CheckBox> _typeChecks = new();

    public string SortMode => _sort.SelectedItem as string ?? "追加順";
    public bool SortDescending => string.Equals(_direction.SelectedItem as string, "降順", System.StringComparison.Ordinal);
    public bool OnlySelectedTypes => _onlySelectedTypes.IsChecked == true;
    public List<string> SelectedTypes => _typeChecks.Where(check => check.IsChecked == true).Select(check => (string)check.Tag).ToList();

    public PresetFilterWindow(bool templates, string sortMode, bool sortDescending, IEnumerable<string> selectedTypes, bool onlySelectedTypes)
    {
        Title = "検索フィルター";
        Width = 380;
        Height = templates ? 490 : 250;
        MinWidth = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Owner = Application.Current?.MainWindow;

        var root = new DockPanel { Margin = new Thickness(14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cancel = new Button { Content = "キャンセル", IsCancel = true, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 4, 10, 4) };
        var apply = new Button { Content = "適用", IsDefault = true, Padding = new Thickness(14, 4, 14, 4) };
        apply.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        root.Children.Add(buttons);

        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "並び順", Margin = new Thickness(0, 0, 0, 4), FontWeight = FontWeights.SemiBold });
        _sort.Items.Add("追加順"); _sort.Items.Add("名前順");
        _sort.SelectedItem = sortMode;
        content.Children.Add(_sort);
        content.Children.Add(new TextBlock { Text = "順序", Margin = new Thickness(0, 10, 0, 4), FontWeight = FontWeights.SemiBold });
        _direction.Items.Add("昇順"); _direction.Items.Add("降順");
        _direction.SelectedIndex = sortDescending ? 1 : 0;
        content.Children.Add(_direction);

        if (templates)
        {
            var typeGroup = new GroupBox { Header = "含めるアイテムの種類", Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8) };
            var typePanel = new StackPanel();
            string[] labels = { "テキスト", "ボイス", "音声", "動画", "画像", "図形", "グループ" };
            string[] types = { nameof(TextItem), nameof(VoiceItem), nameof(AudioItem), "VideoItem", "ImageItem", "ShapeItem", "GroupItem" };
            var selected = selectedTypes.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < labels.Length; i++)
            {
                var check = new CheckBox { Content = labels[i], Tag = types[i], IsChecked = selected.Count == 0 || selected.Contains(types[i]), Margin = new Thickness(2) };
                _typeChecks.Add(check);
                typePanel.Children.Add(check);
            }
            _onlySelectedTypes.IsChecked = onlySelectedTypes;
            typePanel.Children.Add(_onlySelectedTypes);
            typeGroup.Content = typePanel;
            content.Children.Add(typeGroup);
        }
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }
}
