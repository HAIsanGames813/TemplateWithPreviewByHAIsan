using System;
using System.Windows.Media;
using YukkuriMovieMaker.Plugin.Brush;
using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Shape;

namespace TemplateWithPreview;

internal static class PreviewBackgroundItem
{
    public static ShapeItem Create(Color color, int length)
    {
        var item = new ShapeItem
        {
            ShapeType2 = typeof(BackgroundShapePlugin),
            Frame = 0,
            Layer = 0,
            Length = length
        };
        var parameter = ShapeFactory.GetPlugin(typeof(BackgroundShapePlugin)).CreateShapeParameter(null);
        if (parameter is not BackgroundShapeParameter background)
            throw new InvalidOperationException("背景図形を作成できませんでした");
        background.Brush.Type = typeof(SolidColorBrushParameter);
        background.Brush.Parameter = new SolidColorBrushParameter { Color = color };
        item.ShapeParameter = background;
        return item;
    }
}
