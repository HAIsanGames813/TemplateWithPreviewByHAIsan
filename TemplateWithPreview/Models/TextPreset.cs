using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace TemplateWithPreview.Models;

public class DetailedApplyFlags
{
    // エフェクト分類
    public bool VideoEffects { get; set; } = true;
    public bool FontColor { get; set; } = true;
    public bool Style { get; set; } = true;
    public bool FontStyles { get; set; } = true; // Bold, Italic, Underline, Strikethrough
    public bool IsDevidedPerCharacter { get; set; } = true;

    // フォント分類
    public bool FontName { get; set; } = true;

    // 位置分類
    public bool Position { get; set; } = true; // X, Y, Z

    // サイズ分類
    public bool FontSize { get; set; } = true;
    public bool Zoom { get; set; } = true;

    // その他分類
    public bool Spacing { get; set; } = true; // LineHeight, LetterSpacing
    public bool BasePoint { get; set; } = true;
    public bool Rotation { get; set; } = true;
    public bool Opacity { get; set; } = true;
    public bool Blend { get; set; } = true;
    public bool Fade { get; set; } = true;
}

public class ApplyFlags
{
    public bool Effects { get; set; } = true;
    public bool Font { get; set; } = true;
    public bool Position { get; set; } = true;
    public bool Size { get; set; } = true;
    public bool Others { get; set; } = true;
}

public class FontPresetData
{
    public string FontName { get; set; } = "メイリオ";
    public Animation? FontSize { get; set; }
    public System.Windows.Media.Color FontColor { get; set; } = System.Windows.Media.Colors.White;
    public Style Style { get; set; } = Style.Normal;
    public System.Windows.Media.Color StyleColor { get; set; } = System.Windows.Media.Colors.Black;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }
    public bool IsDevidedPerCharacter { get; set; }
    public Animation? LineHeight { get; set; }
    public Animation? LetterSpacing { get; set; }
    public BasePoint BasePoint { get; set; } = BasePoint.CenterCenter;
}

public class PositionPresetData
{
    public Animation? X { get; set; }
    public Animation? Y { get; set; }
    public Animation? Z { get; set; }
    public Animation? Zoom { get; set; }
    public Animation? Rotation { get; set; }
    public Animation? Opacity { get; set; }
    public Blend Blend { get; set; } = Blend.Normal;
    public double FadeIn { get; set; }
    public double FadeOut { get; set; }
}

public class PresetPattern
{
    public const int MaxPatternCount = 30;

    public int Slot { get; set; } = 1; // 1〜30
    public string SampleColor { get; set; } = "#FFFFFF"; // Hex color
    public DetailedApplyFlags ItemFlags { get; set; } = new();
    public ApplyFlags Apply { get; set; } = new();

    public FontPresetData? Font { get; set; }
    public PositionPresetData? Position { get; set; }
    public List<IVideoEffect> Effects { get; set; } = new();
    public List<IItem> Items { get; set; } = new();

    public string? PreviewImagePath { get; set; }
    public string? PreviewVideoPath { get; set; }
    public string? PreviewAudioPath { get; set; }
    public double PreviewVideoDurationSeconds { get; set; }
    public double ThumbnailTimeSeconds { get; set; }

    // プレビュー用キャッシュ（保存対象外）
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public BitmapSource? CachedThumbnail { get; set; }
}

public class TextPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新規プリセット";
    public string Category { get; set; } = "";
    public string Tags { get; set; } = "";
    public bool IsEffectPreset { get; set; }
    public bool IsItemTemplate { get; set; }
    public int? SortOrder { get; set; }
    public int Version { get; set; } = 3;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// カンマ区切りのカテゴリをリストとして取得
    /// </summary>
    public List<string> GetCategoryList()
    {
        if (string.IsNullOrWhiteSpace(Category))
            return new List<string>();

        return Category.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Where(c => !string.Equals(c, "全て", StringComparison.OrdinalIgnoreCase))
                       .Distinct()
                       .ToList();
    }

    public List<string> GetTagList() => string.IsNullOrWhiteSpace(Tags)
        ? new List<string>()
        : Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// 指定されたカテゴリに一致するか判定
    /// </summary>
    public bool MatchesCategory(string? selectedCategory)
    {
        if (string.IsNullOrEmpty(selectedCategory) || selectedCategory == "全て")
            return true;

        var cats = GetCategoryList();
        return cats.Any(c => string.Equals(c, selectedCategory, StringComparison.OrdinalIgnoreCase));
    }

    public int ActivePatternSlot { get; set; } = 1;

    public List<PresetPattern> Patterns { get; set; } = new();

    // 後方互換用の旧プロパティ（JSONデシリアライズ用）
    public ApplyFlags? Apply { get; set; }
    public FontPresetData? Font { get; set; }
    public PositionPresetData? Position { get; set; }
    public List<IVideoEffect>? Effects { get; set; }

    /// <summary>
    /// 次に利用可能な空きスロット番号(1〜30)を取得
    /// </summary>
    public int GetNextFreeSlot()
    {
        var used = new HashSet<int>(Patterns.Select(p => p.Slot));
        for (int i = 1; i <= PresetPattern.MaxPatternCount; i++)
        {
            if (!used.Contains(i)) return i;
        }
        return -1; // 上限到達
    }

    /// <summary>
    /// 現在アクティブなパターンを取得（なければ初期化）
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public PresetPattern ActivePattern
    {
        get
        {
            EnsurePatternsInitialized();
            var p = Patterns.FirstOrDefault(x => x.Slot == ActivePatternSlot);
            return p ?? Patterns[0];
        }
    }

    /// <summary>
    /// パターンリストの初期化と旧データからのマイグレーション
    /// </summary>
    public void EnsurePatternsInitialized()
    {
        if (Patterns == null)
        {
            Patterns = new List<PresetPattern>();
        }

        if (Patterns.Count == 0)
        {
            // 旧フォーマットからの移行、または初期パターンの作成
            string sampleColor = "#FFFFFF";
            if (Font != null)
            {
                sampleColor = $"#{Font.FontColor.R:X2}{Font.FontColor.G:X2}{Font.FontColor.B:X2}";
            }

            var pattern = new PresetPattern
            {
                Slot = 1,
                SampleColor = sampleColor,
                Apply = Apply ?? new ApplyFlags(),
                Font = Font,
                Position = Position,
                Effects = Effects ?? new List<IVideoEffect>()
            };
            Patterns.Add(pattern);
        }

        // ActivePatternSlot の正規化
        if (!Patterns.Any(p => p.Slot == ActivePatternSlot))
        {
            ActivePatternSlot = Patterns[0].Slot;
        }
    }
}
