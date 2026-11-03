using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TemplateWithPreview.Models;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.ItemEditor;
using YukkuriMovieMaker.Json;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace TemplateWithPreview;

public class PreviewRenderer
{
    private const int MaxCachedFrames = 12;
    private readonly ConcurrentDictionary<string, BitmapSource> _cache = new();
    private readonly ConcurrentDictionary<string, BitmapSource> _renderedFrameCache = new();

    public async Task<BitmapSource?> RenderPresetPreviewAsync(TextPreset preset, string previewText, Color backgroundColor, int width = 640, int height = 360)
    {
        return await RenderPresetPatternPreviewAsync(preset.Id, preset.ActivePattern, previewText, backgroundColor, width, height);
    }

    public async Task<BitmapSource?> RenderPresetPatternPreviewAsync(string presetId, PresetPattern pattern, string previewText, Color backgroundColor, int width = 640, int height = 360)
    {
        previewText = new string((previewText ?? string.Empty).Take(3).ToArray());
        string frameKey = $"{presetId}_slot{pattern.Slot}_{previewText}_{width}x{height}_{backgroundColor}";
        string cacheKey = $"{frameKey}_{backgroundColor}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        return await Task.Run(() =>
        {
            try
            {
                if (!_renderedFrameCache.TryGetValue(frameKey, out var renderedFrame))
                {
                // 1. 16:9のプレビュー領域全体を使ってレンダリングする
                var scenes = new Scenes(isUndoEnabled: false);
                var videoInfo = new VideoInfo
                {
                    Width = width,
                    Height = height,
                    FPS = 30,
                    Hz = 48000,
                    BackgroundColor = backgroundColor
                };
                scenes.CreateScene("PreviewScene", 0, videoInfo, new TimelineVerticalLine());

                var scene = scenes.AllScenes.FirstOrDefault();
                if (scene == null) return null;

                string text = string.IsNullOrEmpty(previewText) ? "あA" : previewText;

                // 2. プレビュー用 TextItem の作成
                var textItem = new TextItem
                {
                    Text = text,
                    Frame = 0,
                    Length = 30,
                    Layer = 1,
                    BasePoint = BasePoint.CenterCenter // 必ず画面中央基準
                };

                // プレビューでは位置を中央 (0, 0) にリセット
                textItem.X.CopyFrom(new Animation(0.0));
                textItem.Y.CopyFrom(new Animation(0.0));
                textItem.Z.CopyFrom(new Animation(0.0));
                textItem.Rotation.CopyFrom(new Animation(0.0));
                textItem.Opacity.CopyFrom(new Animation(100.0));

                // フォント情報の反映
                double baseFontSize = 34.0;
                if (pattern.Font != null)
                {
                    textItem.Font = pattern.Font.FontName;
                    textItem.FontColor = pattern.Font.FontColor;
                    textItem.Style = pattern.Font.Style;
                    textItem.StyleColor = pattern.Font.StyleColor;
                    textItem.Bold = pattern.Font.Bold;
                    textItem.Italic = pattern.Font.Italic;
                    textItem.Underline = pattern.Font.Underline;
                    textItem.Strikethrough = pattern.Font.Strikethrough;
                    textItem.IsDevidedPerCharacter = pattern.Font.IsDevidedPerCharacter;

                    if (pattern.Font.FontSize != null)
                    {
                        textItem.FontSize.CopyFrom(pattern.Font.FontSize);
                        var firstVal = pattern.Font.FontSize.Values?.FirstOrDefault();
                        if (firstVal != null)
                        {
                            baseFontSize = firstVal.Value;
                        }
                    }
                    else
                    {
                        textItem.FontSize.CopyFrom(new Animation(baseFontSize));
                    }

                    if (pattern.Font.LineHeight != null) textItem.LineHeight2.CopyFrom(pattern.Font.LineHeight);
                    if (pattern.Font.LetterSpacing != null) textItem.LetterSpacing2.CopyFrom(pattern.Font.LetterSpacing);
                }
                else
                {
                    textItem.FontSize.CopyFrom(new Animation(baseFontSize));
                }

                if (baseFontSize <= 0) baseFontSize = 34.0;

                // オートフィット計算: プレビュー枠 (width x height) に文字全体が綺麗に収まる倍率を計算
                double targetTextHeight = height * 0.40;
                double scaleByHeight = targetTextHeight / baseFontSize;

                int charCount = Math.Max(1, text.Length);
                double estimatedWidth = baseFontSize * charCount;
                double targetTextWidth = width * 0.75;
                double scaleByWidth = targetTextWidth / estimatedWidth;

                double fitScale = Math.Min(scaleByHeight, scaleByWidth);
                textItem.Zoom.CopyFrom(new Animation(fitScale * 100.0));

                // ブレンドモード
                if (pattern.Position != null)
                {
                    textItem.Blend = pattern.Position.Blend;
                }

                // エフェクトの反映（ディープコピーで追加）
                if (pattern.Effects != null && pattern.Effects.Count > 0)
                {
                    var clonedEffects = pattern.Effects
                        .Select(e => Json.GetClone(e))
                        .Where(e => e != null)
                        .Select(e => e!)
                        .ToList();

                    textItem.VideoEffects = System.Collections.Immutable.ImmutableList.CreateRange(clonedEffects);
                }

                // タイムラインに追加
                var backgroundItem = PreviewBackgroundItem.Create(backgroundColor, 30);
                scene.Timeline.TryAddItems(new IItem[] { backgroundItem, textItem }, 0, 0);

                // 3. YMM4本体のDirect2Dレンダラー TimelineSourceAndDevices で描画
                using var renderer = new TimelineSourceAndDevices(scene);
                renderer.Update(TimeSpan.Zero, TimelineSourceUsage.Paused);

                // BitmapSource を生成
                    renderedFrame = renderer.RenderBitmapSource();
                    renderedFrame.Freeze();
                    _renderedFrameCache[frameKey] = renderedFrame;
                    TrimCache(_renderedFrameCache);
                }

                BitmapSource bitmap = renderedFrame;

                _cache[cacheKey] = bitmap;
                TrimCache(_cache);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Preview render error: {ex}");
                return null;
            }
        });
    }

    private static void TrimCache(ConcurrentDictionary<string, BitmapSource> cache)
    {
        int excess = cache.Count - MaxCachedFrames;
        if (excess <= 0) return;
        foreach (string key in cache.Keys.Take(excess)) cache.TryRemove(key, out _);
    }

    public void ClearCache()
    {
        _cache.Clear();
        _renderedFrameCache.Clear();
    }

    public void ClearColorizedCache()
    {
        _cache.Clear();
        _renderedFrameCache.Clear();
    }

    public void InvalidatePreset(string presetId)
    {
        string prefix = $"{presetId}_slot";
        foreach (string key in _cache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal)) _cache.TryRemove(key, out _);
        }
        foreach (string key in _renderedFrameCache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal)) _renderedFrameCache.TryRemove(key, out _);
        }
    }
}
