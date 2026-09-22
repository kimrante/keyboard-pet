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
/// </summary>
public sealed class ImageCache
{
    public const int MaxFramesPerSet = 500;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif",
    };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>"frame-2.png" &lt; "frame-10.png" 처럼 숫자를 값으로 비교하는 Windows 탐색기 정렬.</summary>
    public static IComparer<string?> NaturalComparer => NaturalStringComparer.Instance;

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
            bitmaps.AddRange(Decode(new Uri(file, UriKind.Absolute)));
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
        var frames = uris.SelectMany(u => Decode(new Uri(u, UriKind.Absolute))).ToList();
        return Build(name, uris, frames, Array.Empty<string>());
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

    private sealed class NaturalStringComparer : IComparer<string?>
    {
        public static readonly NaturalStringComparer Instance = new();

        public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);
    }
}
