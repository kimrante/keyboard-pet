using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using KeyboardPet.Core.Animation;

namespace KeyboardPet.App.Services;

/// <summary>Core의 FrameSet 메타와 실제 디코딩된 프레임 비트맵의 쌍.</summary>
/// <param name="MissingFiles">설정에 적혀 있지만 폴더에 없어 건너뛴 파일 이름</param>
public sealed record LoadedFrameSet(
    FrameSet Set,
    IReadOnlyList<BitmapSource> Frames,
    IReadOnlyList<string>? MissingFiles = null);

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
    /// </summary>
    public LoadedFrameSet LoadFolder(string name, string folder, IReadOnlyList<string>? frames = null)
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

        var bitmaps = new List<BitmapSource>();
        foreach (var file in files)
        {
            bitmaps.AddRange(GetOrDecodeFile(file));
            if (bitmaps.Count >= MaxFramesPerSet)
            {
                break;
            }
        }

        return Build(name, files, bitmaps, missing);
    }

    /// <summary>앱에 내장된 리소스(pack URI)로 세트를 만든다.</summary>
    public LoadedFrameSet LoadBuiltIn(string name, IEnumerable<string> packUris)
    {
        var uris = packUris.ToList();
        var frames = uris.SelectMany(GetOrDecodeResource).ToList();
        return Build(name, uris, frames, Array.Empty<string>());
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

    private static LoadedFrameSet Build(string name, IReadOnlyList<string> sources, List<BitmapSource> frames, IReadOnlyList<string> missing)
    {
        if (frames.Count > MaxFramesPerSet)
        {
            frames = frames.Take(MaxFramesPerSet).ToList();
        }

        return new LoadedFrameSet(new FrameSet(name, frames.Count, sources), frames, missing);
    }

    private static IEnumerable<BitmapSource> Decode(Uri uri)
    {
        var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        foreach (var frame in decoder.Frames)
        {
            // BitmapFrame은 메타데이터 때문에 Freeze가 거부될 수 있으므로 순수 비트맵으로 복사한다.
            BitmapSource bitmap = frame.CanFreeze ? frame : new WriteableBitmap(frame);
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
    }

    private sealed class NaturalStringComparer : IComparer<string?>
    {
        public static readonly NaturalStringComparer Instance = new();

        public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);
    }
}
