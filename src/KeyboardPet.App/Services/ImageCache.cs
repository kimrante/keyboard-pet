using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeyboardPet.Core.Animation;

namespace KeyboardPet.App.Services;

/// <summary>Core의 FrameSet 메타와 실제 디코딩된 프레임 비트맵의 쌍.</summary>
/// <param name="MissingFiles">설정에 적혀 있지만 폴더에 없어 건너뛴 파일 이름</param>
public sealed record LoadedFrameSet(
    FrameSet Set,
    IReadOnlyList<BitmapSource> Frames,
    IReadOnlyList<string>? MissingFiles = null)
{
    /// <summary>세트 순서의 썸네일(Frames와 같은 인덱스). ImageCache.TryGetThumbnails가 한 번 만든 뒤 재사용한다.</summary>
    public IReadOnlyList<BitmapSource>? Thumbnails { get; set; }
}

/// <summary>디코딩 없이 폴더만 훑어 본 세트 상태(사용 중이 아닌 세트용).</summary>
public sealed record FrameSetSummary(int FrameCount, int MissingCount, int LoopCount);

/// <summary>
/// 이미지 파일을 프레임 비트맵으로 디코딩한다. 모든 프레임은 Freeze되어 어느 스레드에서든 안전하게 공유된다.
/// GIF는 파일 하나가 여러 프레임으로 전개된다.
///
/// 디코딩 결과는 파일 단위로 캐시된다(경로 + 수정 시각 + 크기). 세트 순서 편집처럼 파일 자체는 그대로인
/// 재로드에서는 디스크를 다시 읽지 않는다. <see cref="BeginGeneration"/>/<see cref="EndGeneration"/> 사이에
/// 사용되지 않은 항목은 EndGeneration에서 버려진다.
/// </summary>
public sealed class ImageCache
{
    public const int MaxFramesPerSet = 500;

    /// <summary>설정 창 미리보기용 썸네일의 긴 변(픽셀). 전체 해상도 프레임을 20~48px 타일에 직접 바인딩하지 않기 위한 것.</summary>
    public const int ThumbnailPixels = 64;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif",
    };

    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>"frame-2.png" &lt; "frame-10.png" 처럼 숫자를 값으로 비교하는 Windows 탐색기 정렬.</summary>
    public static IComparer<string?> NaturalComparer => NaturalStringComparer.Instance;

    /// <summary>캐시된 파일 수(진단·테스트용).</summary>
    public int CachedFileCount => _entries.Count;

    /// <summary>폴더 안의 지원 이미지 파일 이름을 자연 정렬 순으로 나열한다(폴더가 없으면 빈 목록).</summary>
    public static IReadOnlyList<string> ListFolderFiles(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(folder)
            .Where(IsSupported)
            .Select(Path.GetFileName)
            .OrderBy(n => n, NaturalComparer)
            .ToList()!;
    }

    /// <summary>
    /// 디코딩하지 않고 폴더와 설정만으로 프레임 수·없는 파일 수·루프 프레임 수를 센다(GIF는 파일당 1로 센다).
    /// 사용 중이 아닌 세트는 화면에 그리지 않으므로 이렇게만 살펴 시작 시간과 메모리를 아낀다.
    /// </summary>
    public static FrameSetSummary Summarize(string folder, IReadOnlyList<string>? frames, IReadOnlyList<string>? animationFrames)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"이미지 세트 폴더를 찾을 수 없습니다: {folder}");
        }

        var present = ListFolderFiles(folder);
        var presentSet = present.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = frames ?? present;
        var existing = names.Where(presentSet.Contains).Take(MaxFramesPerSet).ToList();
        var loop = animationFrames is null
            ? -1
            : existing.Count(n => animationFrames.Contains(n, StringComparer.OrdinalIgnoreCase));
        return new FrameSetSummary(existing.Count, names.Count - existing.Count, loop);
    }

    /// <summary>새 로드 세대를 시작한다. 이후 EndGeneration까지 사용된 항목만 살아남는다.</summary>
    public void BeginGeneration() => _generation++;

    /// <summary>현재 세대에서 한 번도 쓰이지 않은 캐시 항목을 버린다.</summary>
    public void EndGeneration()
    {
        var stale = _entries.Where(kv => kv.Value.Generation != _generation).Select(kv => kv.Key).ToList();
        foreach (var key in stale)
        {
            _entries.Remove(key);
        }
    }

    /// <summary>
    /// 폴더의 이미지를 읽는다. <paramref name="frames"/>가 null이면 폴더의 모든 지원 파일을 자연 정렬 순으로,
    /// 값이 있으면 그 파일들만 그 순서대로 읽는다. 없는 파일은 건너뛰고 <see cref="LoadedFrameSet.MissingFiles"/>에 남긴다.
    /// <paramref name="animationFrames"/>가 주어지면 그 파일들의 프레임만 루프 애니메이션에 참여한다(GIF는 파일 단위로 함께).
    /// </summary>
    public LoadedFrameSet LoadFolder(
        string name,
        string folder,
        IReadOnlyList<string>? frames = null,
        IReadOnlyList<string>? animationFrames = null,
        string? idleFrame = null)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"이미지 세트 폴더를 찾을 수 없습니다: {folder}");
        }

        var files = new List<string>();
        var missing = new List<string>();

        if (frames is null)
        {
            files.AddRange(ListFolderFiles(folder).Select(f => Path.Combine(folder, f)));
        }
        else
        {
            foreach (var frame in frames)
            {
                var path = Path.Combine(folder, frame);
                if (File.Exists(path) && IsSupported(path))
                {
                    files.Add(path);
                }
                else
                {
                    missing.Add(frame);
                }
            }
        }

        var animationSet = animationFrames?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loop = animationSet is null ? null : new List<int>();
        int? idleIndex = null;
        var bitmaps = new List<BitmapSource>();
        foreach (var file in files)
        {
            var decoded = GetOrDecodeFile(file);
            var start = bitmaps.Count;
            bitmaps.AddRange(decoded);

            var fileName = Path.GetFileName(file);
            if (loop is not null && animationSet!.Contains(fileName))
            {
                loop.AddRange(Enumerable.Range(start, decoded.Count));
            }

            if (idleIndex is null && idleFrame is not null && decoded.Count > 0
                && string.Equals(fileName, idleFrame, StringComparison.OrdinalIgnoreCase))
            {
                idleIndex = start;   // GIF면 그 파일의 첫 프레임
            }

            if (bitmaps.Count >= MaxFramesPerSet)
            {
                break;
            }
        }

        return Build(name, files, bitmaps, missing, loop, idleIndex);
    }

    /// <summary>앱에 내장된 리소스(pack URI)로 세트를 만든다.</summary>
    public LoadedFrameSet LoadBuiltIn(string name, IEnumerable<string> packUris)
    {
        var uris = packUris.ToList();
        var frames = uris.SelectMany(GetOrDecodeResource).ToList();
        return Build(name, uris, frames, Array.Empty<string>(), null, null);
    }

    // ── 썸네일 ──

    /// <summary>
    /// 아직 썸네일이 없는 캐시 항목들의 썸네일을 백그라운드에서 만든다. 원본 프레임은 Freeze된 상태라 다른 스레드에서 읽어도 안전하고,
    /// 결과도 Freeze한 뒤 넘긴다. 그 사이 EndGeneration으로 항목이 버려지면 결과가 그냥 쓰이지 않을 뿐이다.
    /// </summary>
    public Task BuildMissingThumbnailsAsync()
    {
        var pending = _entries.Values.Where(e => e.Thumbnails is null).ToList();
        return pending.Count == 0
            ? Task.CompletedTask
            : Task.Run(() =>
            {
                foreach (var entry in pending)
                {
                    entry.Thumbnails = entry.Frames.Select(MakeThumbnail).ToList();
                }
            });
    }

    /// <summary>세트 순서의 썸네일. 아직 만들어지지 않은 파일이 있으면 null(BuildMissingThumbnailsAsync 완료 후 다시 요청).</summary>
    public IReadOnlyList<BitmapSource>? TryGetThumbnails(LoadedFrameSet set)
    {
        if (set.Thumbnails is not null)
        {
            return set.Thumbnails;
        }

        var list = new List<BitmapSource>(set.Frames.Count);
        foreach (var source in set.Set.SourceFiles ?? Array.Empty<string>())
        {
            if (!_entries.TryGetValue(source, out var entry) || entry.Thumbnails is not { } thumbnails)
            {
                return null;
            }

            list.AddRange(thumbnails);
            if (list.Count >= set.Frames.Count)
            {
                break;
            }
        }

        if (list.Count > set.Frames.Count)
        {
            list.RemoveRange(set.Frames.Count, list.Count - set.Frames.Count);
        }

        return set.Thumbnails = list;
    }

    /// <summary>파일이 캐시에 있는지(썸네일이 준비 중이거나 이미 있음).</summary>
    public bool IsCached(string path) => _entries.ContainsKey(path);

    /// <summary>파일 하나의 첫 프레임 썸네일(캐시에 있고 썸네일이 만들어진 경우). 없으면 null.</summary>
    public BitmapSource? TryGetFileThumbnail(string path) =>
        _entries.TryGetValue(path, out var entry) && entry.Thumbnails is { Count: > 0 } thumbnails ? thumbnails[0] : null;

    /// <summary>
    /// 긴 변이 <see cref="ThumbnailPixels"/>가 되도록 축소한 독립 비트맵. TransformedBitmap은 원본을 계속 참조하므로
    /// WriteableBitmap으로 픽셀을 복사해(CPU 리샘플링) 원본과 분리한다. 이미 작은 프레임은 그대로 공유한다.
    /// </summary>
    public static BitmapSource MakeThumbnail(BitmapSource frame)
    {
        var scale = (double)ThumbnailPixels / Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (scale >= 1.0)
        {
            return frame;
        }

        var thumbnail = new WriteableBitmap(new TransformedBitmap(frame, new ScaleTransform(scale, scale)));
        thumbnail.Freeze();
        return thumbnail;
    }

    private IReadOnlyList<BitmapSource> GetOrDecodeFile(string path)
    {
        var info = new FileInfo(path);
        var stamp = (info.LastWriteTimeUtc.Ticks, info.Length);

        if (_entries.TryGetValue(path, out var entry) && entry.Stamp == stamp)
        {
            entry.Generation = _generation;
            return entry.Frames;
        }

        var frames = Decode(new Uri(path, UriKind.Absolute)).ToList();
        _entries[path] = new CacheEntry(frames, stamp, _generation);
        return frames;
    }

    private IReadOnlyList<BitmapSource> GetOrDecodeResource(string packUri)
    {
        // 내장 리소스는 바뀌지 않으므로 스탬프 없이 캐시한다.
        if (_entries.TryGetValue(packUri, out var entry))
        {
            entry.Generation = _generation;
            return entry.Frames;
        }

        var frames = Decode(new Uri(packUri, UriKind.Absolute)).ToList();
        _entries[packUri] = new CacheEntry(frames, (0L, 0L), _generation);
        return frames;
    }

    private static LoadedFrameSet Build(
        string name,
        IReadOnlyList<string> sources,
        List<BitmapSource> frames,
        IReadOnlyList<string> missing,
        List<int>? loop,
        int? idleIndex)
    {
        if (frames.Count > MaxFramesPerSet)
        {
            frames = frames.Take(MaxFramesPerSet).ToList();
        }

        var loopFrames = loop?.Where(i => i < frames.Count).ToList();
        var idle = idleIndex is int i && i < frames.Count ? idleIndex : null;
        return new LoadedFrameSet(new FrameSet(name, frames.Count, sources, loopFrames, idle), frames, missing);
    }

    private static IEnumerable<BitmapSource> Decode(Uri uri)
    {
        var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        foreach (var frame in decoder.Frames)
        {
            // BitmapFrame을 그대로 두면 디코더와 OnLoad로 읽어 둔 파일 원본 바이트까지 붙잡는다.
            // 픽셀만 복사해 두면 프레임 하나당 디코딩된 표면만 남는다(메타데이터 때문에 Freeze가 거부되는 경우도 해결).
            BitmapSource bitmap = new WriteableBitmap(frame);
            bitmap.Freeze();
            yield return bitmap;
        }
    }

    private sealed class CacheEntry
    {
        public CacheEntry(IReadOnlyList<BitmapSource> frames, (long Ticks, long Length) stamp, int generation)
        {
            Frames = frames;
            Stamp = stamp;
            Generation = generation;
        }

        public IReadOnlyList<BitmapSource> Frames { get; }

        public (long Ticks, long Length) Stamp { get; }

        public int Generation { get; set; }

        /// <summary>Frames와 같은 순서의 썸네일. 워커 스레드가 한 번 쓰고, UI 스레드는 null 여부만 본다.</summary>
        public volatile IReadOnlyList<BitmapSource>? Thumbnails;
    }

    private sealed class NaturalStringComparer : IComparer<string?>
    {
        public static readonly NaturalStringComparer Instance = new();

        public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);
    }
}
