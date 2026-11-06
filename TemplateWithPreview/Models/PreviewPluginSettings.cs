using System.Collections.Generic;

namespace TemplateWithPreview.Models;

public sealed class PreviewPluginSettings
{
    public double PresetCardWidth { get; set; } = 150;
    public string PreviewBackgroundColor { get; set; } = "#FF0C0C0E";
    public bool IsSimpleView { get; set; }
    public int AutoColorStartEffect { get; set; }
    public string PreviewText { get; set; } = "あA";
    public string SelectedPresetTab { get; set; } = "T";
    public string SelectedCategory { get; set; } = "全て";
    public string SearchQuery { get; set; } = "";
    public string SortMode { get; set; } = "追加順";
    public bool SortDescending { get; set; } = true;
    public List<string> TemplateItemTypeFilter { get; set; } = new();
    public bool TemplateOnlySelectedTypes { get; set; }
    public bool ApplyEffectsTarget { get; set; } = true;
    public bool ApplyFontTarget { get; set; } = true;
    public bool ApplyPositionTarget { get; set; } = true;
    public bool ApplySizeTarget { get; set; } = true;
    public bool ApplyOthersTarget { get; set; } = true;
    public bool LibraryMonitorEnabled { get; set; }
    public List<string> LibraryWatchFolders { get; set; } = new();
    public Dictionary<string, List<string>> LibraryWatchFoldersByType { get; set; } = new();
    public Dictionary<string, List<string>> LibraryCategories { get; set; } = new();
    public Dictionary<string, double> LibraryDefaultVolumeDb { get; set; } = new();
    public Dictionary<string, double> LibraryDefaultVolumePercent { get; set; } = new();
    public bool LibraryAutoNormalizeVolume { get; set; }
    public double LibraryAutoNormalizeTargetPercent { get; set; } = 100;
}
