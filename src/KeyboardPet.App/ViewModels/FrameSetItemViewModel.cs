using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeyboardPet.App.Services;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.App.ViewModels;

/// <summary>설정 창 "이미지 세트" 탭의 프레임 타일 하나(파일 하나).</summary>
public sealed partial class FrameEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    /// <summary>true면 루프 애니메이션에 참여, false면 키 매핑 규칙의 단일 프레임 표시로만 쓰인다.</summary>
    [ObservableProperty]
    private bool _inAnimation;

    public FrameEntryViewModel(FrameSetItemViewModel owner, string fileName, int number, bool isMissing, bool inAnimation)
    {
        Owner = owner;
        FileName = fileName;
        _number = number;
        IsMissing = isMissing;
        _inAnimation = inAnimation;
    }

    partial void OnInAnimationChanged(bool value) => Owner.OnAnimationMembershipChanged();

    public FrameSetItemViewModel Owner { get; }

    public string FileName { get; }

    /// <summary>설정에는 있지만 폴더에 없는 파일. 삭제(×)로 목록에서 정리할 수 있다.</summary>
    public bool IsMissing { get; }
}

/// <summary>
/// 설정 창 "이미지 세트" 탭의 한 행. 프레임 순서 편집(드래그)과 제외(×)를 지원하며,
/// 편집한 결과는 FrameSetSettings.Frames로 저장된다.
/// </summary>
public sealed partial class FrameSetItemViewModel : ObservableObject
{
    private const int ThumbnailPixelWidth = 48;

    private int _thumbnailGeneration;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _folder;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>true면 사용자가 편집한 순서/제외 목록을 저장하고, false면 폴더의 파일을 자연 정렬 순으로 쓴다.</summary>
    [ObservableProperty]
    private bool _hasCustomFrames;

    public FrameSetItemViewModel(SettingsViewModel owner, FrameSetSettings settings)
    {
        Owner = owner;
        _name = settings.Name;
        _folder = settings.Folder;
        _hasCustomFrames = settings.Frames is not null;
        LoadFrames(settings.Frames, settings.AnimationFrames);
    }

    public SettingsViewModel Owner { get; }

    public ObservableCollection<FrameEntryViewModel> Frames { get; } = new();

    public FrameSetSettings ToSettings() =>
        new(
            Name,
            Folder,
            HasCustomFrames ? Frames.Select(f => f.FileName).ToList() : null,
            Frames.All(f => f.InAnimation) ? null : Frames.Where(f => f.InAnimation).Select(f => f.FileName).ToList());

    /// <summary>타일의 "애니메이션 포함" 체크가 바뀌었을 때.</summary>
    public void OnAnimationMembershipChanged() => Owner.CommitFrameSets();

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
            var parts = new List<string> { $"{status.FrameCount} 프레임" };
            if (status.EffectiveLoopCount != status.FrameCount)
            {
                parts.Add(status.EffectiveLoopCount == 0
                    ? "애니메이션 프레임 없음 (첫 프레임에 정지)"
                    : $"애니메이션 {status.EffectiveLoopCount}, 키 전용 {status.FrameCount - status.EffectiveLoopCount}");
            }

            if (status.MissingCount > 0)
            {
                parts.Add($"없는 파일 {status.MissingCount}개");
            }

            Status = string.Join(" · ", parts);
        }
    }

    /// <summary>드래그한 프레임을 대상 프레임 자리로 옮긴다.</summary>
    public void MoveFrame(FrameEntryViewModel source, FrameEntryViewModel target)
    {
        var from = Frames.IndexOf(source);
        var to = Frames.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        Frames.Move(from, to);
        HasCustomFrames = true;
        Renumber();
        Owner.CommitFrameSets();
    }

    [RelayCommand]
    private void RemoveFrame(FrameEntryViewModel entry)
    {
        if (!Frames.Remove(entry))
        {
            return;
        }

        HasCustomFrames = true;
        Renumber();
        Owner.CommitFrameSets();
    }

    /// <summary>편집(순서·제외·애니메이션 포함)을 버리고 폴더의 파일을 자연 정렬 순으로 다시 읽는다(새로 추가된 파일도 포함).</summary>
    [RelayCommand]
    private void ResetFrames()
    {
        HasCustomFrames = false;
        LoadFrames(null, null);
        Owner.CommitFrameSets();
    }

    partial void OnNameChanged(string value) => Owner.CommitFrameSets();

    partial void OnFolderChanged(string value)
    {
        HasCustomFrames = false;
        LoadFrames(null, null);
        Owner.CommitFrameSets();
    }

    private void Renumber()
    {
        for (var i = 0; i < Frames.Count; i++)
        {
            Frames[i].Number = i + 1;
        }
    }

    private void LoadFrames(IReadOnlyList<string>? explicitFrames, IReadOnlyList<string>? animationFrames)
    {
        Frames.Clear();
        var folderExists = Directory.Exists(Folder);
        var names = explicitFrames ?? (folderExists ? ImageCache.ListFolderFiles(Folder) : Array.Empty<string>());
        var animationSet = animationFrames?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var number = 0;
        foreach (var name in names)
        {
            var exists = folderExists && File.Exists(Path.Combine(Folder, name));
            var inAnimation = animationSet is null || animationSet.Contains(name);
            Frames.Add(new FrameEntryViewModel(this, name, ++number, isMissing: !exists, inAnimation));
        }

        LoadThumbnailsAsync();
    }

    /// <summary>썸네일은 파일 수가 많을 수 있으므로 백그라운드에서 디코딩한 뒤 UI 스레드에 반영한다.</summary>
    private void LoadThumbnailsAsync()
    {
        var generation = ++_thumbnailGeneration;
        var folder = Folder;
        var entries = Frames.Where(f => !f.IsMissing).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var paths = entries.Select(e => Path.Combine(folder, e.FileName)).ToList();
        Task.Run(() => paths.Select(TryLoadThumbnail).ToList())
            .ContinueWith(task =>
            {
                if (task.IsFaulted || generation != _thumbnailGeneration)
                {
                    return;
                }

                var thumbnails = task.Result;
                for (var i = 0; i < entries.Count && i < thumbnails.Count; i++)
                {
                    entries[i].Thumbnail = thumbnails[i];
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static ImageSource? TryLoadThumbnail(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = ThumbnailPixelWidth;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
