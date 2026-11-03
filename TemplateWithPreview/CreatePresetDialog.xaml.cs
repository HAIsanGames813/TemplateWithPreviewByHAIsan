using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TemplateWithPreview.Models;

namespace TemplateWithPreview;

public partial class CreatePresetDialog : Window
{
    private readonly List<TextPreset> _existingPresets;
    private readonly bool _isEditMode;
    private readonly PresetPattern? _editingPattern;
    private readonly int _selectedItemCount;

    public bool IsNewPreset => !_isEditMode && ComboTargetTemplate.SelectedIndex == 0;
    public string PresetName => TextPresetName.Text.Trim();
    public string Category => TextCategory.Text.Trim();
    public string Tags => string.Join(", ", TextTags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase));
    public int AutoColorStartEffect => (int)Math.Round(AutoColorStartEffectSlider.Value);
    public TextPreset? TargetPreset { get; private set; }
    public string SampleColorHex
    {
        get
        {
            var c = ColorPickerControl.Value;
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }
    public DetailedApplyFlags ItemFlags { get; } = new();

    private class TemplateComboItem
    {
        public string DisplayText { get; set; } = "";
        public TextPreset? Preset { get; set; }
        public override string ToString() => DisplayText;
    }

    /// <summary>
    /// 新規登録または既存テンプレートへの追加用コンストラクタ
    /// </summary>
    public CreatePresetDialog(List<TextPreset> existingPresets, int selectedItemCount = 1, int autoColorStartEffect = 0) 
        : this(existingPresets, null, null, selectedItemCount, autoColorStartEffect)
    {
    }

    /// <summary>
    /// 既存パターンの設定変更（編集）用にも対応するコンストラクタ
    /// </summary>
    public CreatePresetDialog(List<TextPreset> existingPresets, TextPreset? targetPreset, PresetPattern? editPattern, int selectedItemCount = 1, int autoColorStartEffect = 0)
    {
        InitializeComponent();
        // この登録画面はTEXタブ専用。親テンプレート候補やカテゴリ候補にFX/TPLを混ぜない。
        _existingPresets = (existingPresets ?? new List<TextPreset>())
            .Where(preset => !preset.IsEffectPreset && !preset.IsItemTemplate)
            .ToList();
        TargetPreset = targetPreset;
        _editingPattern = editPattern;
        _isEditMode = editPattern != null;
        _selectedItemCount = selectedItemCount;
        AutoColorStartEffectSlider.Value = Math.Clamp(autoColorStartEffect, 0, 10);
        AutoColorStartEffectValue.Text = AutoColorStartEffect.ToString();
        AutoColorStartEffectSlider.ValueChanged += (_, _) => AutoColorStartEffectValue.Text = AutoColorStartEffect.ToString();
        CsvInputButtons.Populate(CategoryQuickButtons, _existingPresets.SelectMany(p => p.GetCategoryList()), value => CsvInputButtons.Append(TextCategory, value));
        CsvInputButtons.Populate(TagQuickButtons, _existingPresets.SelectMany(p => p.GetTagList()), value => CsvInputButtons.Append(TextTags, value));

        if (_selectedItemCount > 1)
        {
            TextMultiItemNotice.Visibility = Visibility.Visible;
            TextMultiItemNotice.Text = $"※タイムライン上の {selectedItemCount} 個のアイテムから、文字色・装飾色・エフェクト色を自動抽出してパターン登録します";
        }

        if (_isEditMode && editPattern != null && targetPreset != null)
        {
            // パターンの設定変更（編集モード）
            Title = $"パターンの設定変更 - {targetPreset.Name}";
            ButtonSubmit.Content = "保存";
            GroupTargetTemplate.Header = "親テンプレート";
            var parents = _existingPresets
                .Where(p => !p.IsEffectPreset && !p.IsItemTemplate)
                .OrderBy(p => p.Name)
                .Select(p => new TemplateComboItem { DisplayText = $"[TEX] {p.Name} ({p.Patterns.Count}色)", Preset = p })
                .ToList();
            ComboTargetTemplate.ItemsSource = parents;
            ComboTargetTemplate.Visibility = Visibility.Visible;
            TextEditTargetName.Visibility = Visibility.Collapsed;
            var parent = parents.FirstOrDefault(p => p.Preset?.Id == targetPreset.Id);
            if (parent == null)
            {
                parent = new TemplateComboItem { DisplayText = $"[TEX] {targetPreset.Name} ({targetPreset.Patterns.Count}色)", Preset = targetPreset };
                parents.Add(parent);
                ComboTargetTemplate.Items.Refresh();
            }
            ComboTargetTemplate.SelectedItem = parent;
            PanelNewName.Visibility = Visibility.Collapsed;
            TextCategory.Text = targetPreset.Category;
            TextTags.Text = targetPreset.Tags;

            // 既存パターンの色をセット
            try
            {
                var col = (Color)ColorConverter.ConvertFromString(editPattern.SampleColor);
                ColorPickerControl.Value = col;
            }
            catch
            {
                ColorPickerControl.Value = Colors.White;
            }

            // 既存パターンのフラグをセット
            SetLatchesFromFlags(editPattern.ItemFlags ?? new DetailedApplyFlags());
        }
        else
        {
            // 新規登録モード
            Title = _selectedItemCount > 1 ? $"テンプレートの登録 ({_selectedItemCount}アイテム一括)" : "テンプレートの登録";
            ButtonSubmit.Content = "登録";

            var items = new List<TemplateComboItem>
            {
                new TemplateComboItem { DisplayText = "[＋ 新規テンプレートを作成]", Preset = null }
            };

            foreach (var p in _existingPresets)
            {
                items.Add(new TemplateComboItem
                {
                    DisplayText = $"[{PresetTabLabel(p)}] {p.Name} ({p.Patterns.Count}色)",
                    Preset = p
                });
            }

            ComboTargetTemplate.ItemsSource = items;

            if (targetPreset != null)
            {
                var match = items.FirstOrDefault(i => i.Preset?.Id == targetPreset.Id);
                ComboTargetTemplate.SelectedItem = match ?? items[0];
            }
            else
            {
                ComboTargetTemplate.SelectedIndex = 0;
            }

            // 選択中アイテムからデフォルト名と色を取得
            var (defaultText, defaultColor) = PresetApplier.GetSelectedItemTextAndColor();
            TextPresetName.Text = defaultText;
            TextCategory.Text = "";
            TextTags.Text = "";

            try
            {
                var col = (Color)ColorConverter.ConvertFromString(defaultColor);
                ColorPickerControl.Value = col;
            }
            catch
            {
                ColorPickerControl.Value = Colors.White;
            }
        }
    }

    private static string PresetTabLabel(TextPreset preset) => preset.IsItemTemplate ? "TPL" : preset.IsEffectPreset ? "FX" : "TEX";

    private void ComboTargetTemplate_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PanelNewName == null || TextPatternCountInfo == null) return;

        if (IsNewPreset)
        {
            PanelNewName.Visibility = Visibility.Visible;
            TextPatternCountInfo.Visibility = Visibility.Collapsed;
            TargetPreset = null;
            TextTags.Text = "";
        }
        else
        {
            PanelNewName.Visibility = Visibility.Collapsed;
            TargetPreset = (ComboTargetTemplate.SelectedItem as TemplateComboItem)?.Preset;
            if (TargetPreset != null)
            {
                TextTags.Text = TargetPreset.Tags;
                TextPatternCountInfo.Visibility = Visibility.Visible;
                TextPatternCountInfo.Text = $"現在の登録色: {TargetPreset.Patterns.Count} / {PresetPattern.MaxPatternCount}";
            }
        }
    }

    private void SetLatchesFromFlags(DetailedApplyFlags flags)
    {
        LatchVideoEffects.IsChecked = flags.VideoEffects;
        LatchFontColor.IsChecked = flags.FontColor;
        LatchStyle.IsChecked = flags.Style;
        LatchFontStyles.IsChecked = flags.FontStyles;
        LatchCharDivision.IsChecked = flags.IsDevidedPerCharacter;

        LatchFontName.IsChecked = flags.FontName;
        LatchPosition.IsChecked = flags.Position;
        LatchFontSize.IsChecked = flags.FontSize;
        LatchZoom.IsChecked = flags.Zoom;

        LatchSpacing.IsChecked = flags.Spacing;
        LatchBasePoint.IsChecked = flags.BasePoint;
        LatchRotation.IsChecked = flags.Rotation;
        LatchOpacity.IsChecked = flags.Opacity;
        LatchBlend.IsChecked = flags.Blend;
        LatchFade.IsChecked = flags.Fade;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        SetAllLatches(true);
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        SetAllLatches(false);
    }

    private void SetAllLatches(bool isChecked)
    {
        LatchVideoEffects.IsChecked = isChecked;
        LatchFontColor.IsChecked = isChecked;
        LatchStyle.IsChecked = isChecked;
        LatchFontStyles.IsChecked = isChecked;
        LatchCharDivision.IsChecked = isChecked;

        LatchFontName.IsChecked = isChecked;
        LatchPosition.IsChecked = isChecked;
        LatchFontSize.IsChecked = isChecked;
        LatchZoom.IsChecked = isChecked;

        LatchSpacing.IsChecked = isChecked;
        LatchBasePoint.IsChecked = isChecked;
        LatchRotation.IsChecked = isChecked;
        LatchOpacity.IsChecked = isChecked;
        LatchBlend.IsChecked = isChecked;
        LatchFade.IsChecked = isChecked;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (IsNewPreset && string.IsNullOrWhiteSpace(PresetName))
        {
            MessageBox.Show(this, "新規テンプレート名を入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_isEditMode && !IsNewPreset)
        {
            if (TargetPreset == null)
            {
                MessageBox.Show(this, "追加先の既存テンプレートを選択してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (TargetPreset.Patterns.Count >= PresetPattern.MaxPatternCount)
            {
                MessageBox.Show(this, $"登録可能パターン数が上限({PresetPattern.MaxPatternCount})に達しています。", "上限エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // トグルラッチの状態を反映
        ItemFlags.VideoEffects = LatchVideoEffects.IsChecked == true;
        ItemFlags.FontColor = LatchFontColor.IsChecked == true;
        ItemFlags.Style = LatchStyle.IsChecked == true;
        ItemFlags.FontStyles = LatchFontStyles.IsChecked == true;
        ItemFlags.IsDevidedPerCharacter = LatchCharDivision.IsChecked == true;

        ItemFlags.FontName = LatchFontName.IsChecked == true;
        ItemFlags.Position = LatchPosition.IsChecked == true;
        ItemFlags.FontSize = LatchFontSize.IsChecked == true;
        ItemFlags.Zoom = LatchZoom.IsChecked == true;

        ItemFlags.Spacing = LatchSpacing.IsChecked == true;
        ItemFlags.BasePoint = LatchBasePoint.IsChecked == true;
        ItemFlags.Rotation = LatchRotation.IsChecked == true;
        ItemFlags.Opacity = LatchOpacity.IsChecked == true;
        ItemFlags.Blend = LatchBlend.IsChecked == true;
        ItemFlags.Fade = LatchFade.IsChecked == true;

        DialogResult = true;
        Close();
    }
}
