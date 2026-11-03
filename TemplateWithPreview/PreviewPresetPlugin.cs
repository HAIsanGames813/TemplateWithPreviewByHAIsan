using System;
using YukkuriMovieMaker.Plugin;

namespace TemplateWithPreview;

public class PreviewPresetPlugin : IToolPlugin
{
    public string Name => "プレビュー付きテンプレートbyハイさん";

    public Type ViewModelType => typeof(PreviewPresetViewModel);

    public Type ViewType => typeof(PreviewPresetView);

    public bool AllowMultipleInstances => false;

    public string DefaultGroupName => "テンプレート";

    public int DefaultOrder => 300;
}
