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

    public FrameSetItemViewModel Owner { get; }

    public string FileName { get; }

    /// <summary>설정에는 있지만 폴더에 없는 파일. 삭제(×)로 목록에서 정리할 수 있다.</summary>
    public bool IsMissing { get; }

    partial void OnInAnimationChanged(bool value) => Owner.OnAnimationMembershipChanged();
}

/// <summary>"무입력 복귀 프레임" 콤보박스 항목. Entry가 null이면 기본(루프 첫 프레임).</summary>
public sealed record IdleFrameChoice(FrameEntryViewModel? Entry, string Label)
{
    public static IdleFrameChoice Default { get; } = new(null, "루프 첫 프레임 (기본)");

    public override string ToString() => Label;
}

/// <summary>
/// 설정 창 "이미지 세트" 탭의 한 행. 프레임 순서 편집(드래그)과 제외(×), 애니메이션 포함 여부,
/// 무입력 복귀 프레임 지정, 파일 드래그앤드롭 가져오기를 지원하며 결과는 FrameSetSettings로 저장된다.
/// </summary>
public sealed partial class FrameSetItemViewModel : ObservableObject
{
    private const int ThumbnailPixelWidth = 48;

    private int _thumbnailGeneration;
    private bool _refreshingIdleChoices;
    private string? _idleFrameName;

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

    [ObservableProperty]
    private IdleFrameChoice? _selectedIdleFrame;

    public FrameSetItemViewModel(SettingsViewModel owner, FrameSetSettings settings)
    {
        Owner = owner;
        _name = settings.Name;
        _folder = settings.Folder;
        _hasCustomFrames = settings.Frames is not null;
        _idleFrameName = settings.IdleFrame;
        CommittedName = settings.Name;
        LoadFrames(settings.Frames, settings.AnimationFrames);
    }

    public SettingsViewModel Owner { get; }

    /// <summary>마지막으로 설정에 저장된 이름. 이름이 바뀌면 세트 프로필과 기본 세트 참조를 옮기는 데 쓴다.</summary>
    public string CommittedName { get; set; }

    public ObservableCollection<FrameEntryViewModel> Frames { get; } = new();

    public ObservableCollection<IdleFrameChoice> IdleFrameChoices { get; } = new();

    public FrameSetSettings ToSettings()
    {
        var idle = _idleFrameName is not null
                   && Frames.Any(f => string.Equals(f.FileName, _idleFrameName, StringComparison.OrdinalIgnoreCase))
            ? _idleFrameName
            : null;

        return new FrameSetSettings(
            Name,
            Folder,
            HasCustomFrames ? Frames.Select(f => f.FileName).ToList() : null,
            Frames.All(f => f.InAnimation) ? null : Frames.Where(f => f.InAnimation).Select(f => f.FileName).ToList(),
            idle);
    }

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
        else if (status.FrameCount == 0)
        {
            HasError = false;
            Status = status.MissingCount > 0
                ? $"프레임 없음 (없는 파일 {status.MissingCount}개) · 이미지 파일을 이 카드에 끌어다 놓으세요"
                : "프레임 없음 · 이미지 파일을 이 카드에 끌어다 놓으세요";
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
        RefreshIdleChoices();
        Owner.CommitFrameSets();
    }

    /// <summary>
    /// 이미지 파일들을 이 세트의 폴더로 복사해 프레임으로 추가한다(드래그앤드롭). 가져온 파일 수를 반환한다.
    /// 같은 이름이 있으면 " (2)" 식으로 바꿔 저장한다.
    /// </summary>
    public int ImportFiles(IEnumerable<string> sourcePaths)
    {
        if (!Directory.Exists(Folder))
        {
            Directory.CreateDirectory(Folder);
        }

        var imported = new List<string>();
        foreach (var source in sourcePaths.Where(p => File.Exists(p) && ImageCache.IsSupported(p)))
        {
            var destination = UniqueDestination(Folder, Path.GetFileName(source));
            File.Copy(source, destination);
            imported.Add(Path.GetFileName(destination));
        }

        if (imported.Count == 0)
        {
            return 0;
        }

        // 편집한 순서가 있으면 뒤에 붙이고, 없으면 폴더를 다시 읽는다(자연 정렬). 애니메이션 포함 상태는 유지하고 새 파일은 포함으로 둔다.
        var animation = ToSettings().AnimationFrames?.Concat(imported).ToList();
        if (HasCustomFrames)
        {
            var frames = Frames.Select(f => f.FileName).Concat(imported).ToList();
            LoadFrames(frames, animation);
        }
        else
        {
            LoadFrames(null, animation);
        }

        Owner.CommitFrameSets();
        return imported.Count;
    }

    [RelayCommand]
    private void RemoveFrame(FrameEntryViewModel entry)
    {
        if (!Frames.Remove(entry))
        {
            return;
        }

        if (string.Equals(entry.FileName, _idleFrameName, StringComparison.OrdinalIgnoreCase))
        {
            _idleFrameName = null;
        }

        HasCustomFrames = true;
        Renumber();
        RefreshIdleChoices();
        Owner.CommitFrameSets();
    }

    /// <summary>편집(순서·제외·애니메이션 포함·복귀 프레임)을 버리고 폴더의 파일을 자연 정렬 순으로 다시 읽는다.</summary>
    [RelayCommand]
    private void ResetFrames()
    {
        HasCustomFrames = false;
        _idleFrameName = null;
        LoadFrames(null, null);
        Owner.CommitFrameSets();
    }

    partial void OnNameChanged(string value) => Owner.CommitFrameSets();

    partial void OnFolderChanged(string value)
    {
        HasCustomFrames = false;
        _idleFrameName = null;
        LoadFrames(null, null);
        Owner.CommitFrameSets();
    }

    partial void OnSelectedIdleFrameChanged(IdleFrameChoice? value)
    {
        if (_refreshingIdleChoices)
        {
            return;
        }

        _idleFrameName = value?.Entry?.FileName;
        Owner.CommitFrameSets();
    }

    private void Renumber()
    {
        for (var i = 0; i < Frames.Count; i++)
        {
            Frames[i].Number = i + 1;
        }
    }

    private void RefreshIdleChoices()
    {
        _refreshingIdleChoices = true;
        try
        {
            IdleFrameChoices.Clear();
            IdleFrameChoices.Add(IdleFrameChoice.Default);
            foreach (var entry in Frames)
            {
                IdleFrameChoices.Add(new IdleFrameChoice(entry, $"{entry.Number}번 {entry.FileName}"));
            }

            SelectedIdleFrame = IdleFrameChoices.FirstOrDefault(c =>
                                    c.Entry is not null
                                    && string.Equals(c.Entry.FileName, _idleFrameName, StringComparison.OrdinalIgnoreCase))
                                ?? IdleFrameChoice.Default;
        }
        finally
        {
            _refreshingIdleChoices = false;
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

        RefreshIdleChoices();
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

    private static string UniqueDestination(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            candidate = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
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
