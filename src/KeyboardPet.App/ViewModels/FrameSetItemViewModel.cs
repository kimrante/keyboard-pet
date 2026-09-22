using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using KeyboardPet.App.Services;

namespace KeyboardPet.App.ViewModels;

/// <summary>설정 창 "이미지 세트" 탭의 한 행.</summary>
public sealed partial class FrameSetItemViewModel : ObservableObject
{
    private const int ThumbnailCount = 8;
    private const int ThumbnailPixelWidth = 48;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _folder;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public FrameSetItemViewModel(SettingsViewModel owner, string name, string folder)
    {
        Owner = owner;
        _name = name;
        _folder = folder;
        LoadThumbnails();
    }

    public SettingsViewModel Owner { get; }

    public ObservableCollection<ImageSource> Thumbnails { get; } = new();

    public void UpdateStatus(FrameSetStatus? status)
    {
        if (status is null)
        {
            HasError = true;
            Status = "로드되지 않음";
        }
        else if (status.HasError)
        {
            HasError = true;
            Status = status.Error!;
        }
        else
        {
            HasError = false;
            Status = $"{status.FrameCount} 프레임";
        }
    }

    partial void OnNameChanged(string value) => Owner.CommitFrameSets();

    partial void OnFolderChanged(string value)
    {
        LoadThumbnails();
        Owner.CommitFrameSets();
    }

    private void LoadThumbnails()
    {
        Thumbnails.Clear();
        if (!Directory.Exists(Folder))
        {
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(Folder)
                .Where(ImageCache.IsSupported)
                .OrderBy(Path.GetFileName, ImageCache.NaturalComparer)
                .Take(ThumbnailCount)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(file, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = ThumbnailPixelWidth;
                bitmap.EndInit();
                bitmap.Freeze();
                Thumbnails.Add(bitmap);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
            {
                // 깨진 파일은 미리보기에서만 건너뛴다. 실제 로드 오류는 Status에 표시된다.
            }
        }
    }
}
