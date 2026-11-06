using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace TemplateWithPreview.Models;

public sealed class LibraryAsset : INotifyPropertyChanged
{
    private string _path = "";
    private string _type = "画像";
    private string _category = "";
    private double _volumePercent = 100;
    private bool _useDefaultVolume = true;
    private bool _isSelected;
    private BitmapSource? _thumbnail;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Path
    {
        get => _path;
        set
        {
            _path = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(Name));
        }
    }

    public string Type
    {
        get => _type;
        set
        {
            _type = value ?? "画像";
            OnPropertyChanged();
            OnPropertyChanged(nameof(TypeLabel));
        }
    }

    public string ThumbnailPath { get; set; } = "";
    public double ThumbnailTimeSeconds { get; set; }
    public bool IsWatchFolderAsset { get; set; }
    public string WatchFolderPath { get; set; } = "";
    public string WatchFolderType { get; set; } = "";
    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            double normalized = Math.Clamp(value, 0, 400);
            if (Math.Abs(_volumePercent - normalized) < 0.001) return;
            _volumePercent = normalized;
            _useDefaultVolume = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseDefaultVolume));
        }
    }
    public bool UseDefaultVolume
    {
        get => _useDefaultVolume;
        set { _useDefaultVolume = value; OnPropertyChanged(); }
    }
    public string Category
    {
        get => _category;
        set { _category = value ?? ""; OnPropertyChanged(); }
    }
    public string Tags { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }
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
