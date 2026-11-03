using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace TemplateWithPreview.Models;

public sealed class LibraryAsset : INotifyPropertyChanged
{
    private BitmapSource? _thumbnail;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string Type { get; set; } = "画像";
    public string ThumbnailPath { get; set; } = "";
    public double ThumbnailTimeSeconds { get; set; }
    public bool IsWatchFolderAsset { get; set; }
    public string WatchFolderPath { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;

    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; OnPropertyChanged(); }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public string TypeLabel => Type;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
