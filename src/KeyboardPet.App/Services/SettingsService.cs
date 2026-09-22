using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.App.Services;

/// <summary>
/// 앱의 단일 설정 원천. <see cref="Current"/>는 불변이며 <see cref="Update"/>로만 바뀐다.
/// 변경은 즉시 <see cref="Changed"/>로 전파되고, 파일 저장은 0.5초 디바운스로 묶어서 수행한다.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly DispatcherTimer _saveTimer;
    private bool _dirty;

    public SettingsService(SettingsStore store)
    {
        _store = store;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    public AppSettings Current { get; private set; } = AppSettings.Default;

    public string FilePath => _store.FilePath;

    public string? LastLoadError { get; private set; }

    public string? LastSaveError { get; private set; }

    /// <summary>(이전 설정, 새 설정). 항상 UI 스레드에서 발생한다.</summary>
    public event Action<AppSettings, AppSettings>? Changed;

    public void Load()
    {
        var existed = _store.Exists;
        Current = _store.Load();
        LastLoadError = _store.LastLoadError;

        if (!existed)
        {
            // 첫 실행: M3까지 쓰던 %AppData%\KeyboardPet\sets\<이름> 폴더가 있으면 세트 목록으로 가져온다.
            Current = ImportLegacySetFolders(Current);
            _dirty = true;
            ScheduleSave();
        }
    }

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        var old = Current;
        var next = mutate(old).Normalized();
        Current = next;
        _dirty = true;
        Changed?.Invoke(old, next);
        ScheduleSave();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        if (!_dirty)
        {
            return;
        }

        try
        {
            _store.Save(Current);
            _dirty = false;
            LastSaveError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastSaveError = ex.Message;
            Debug.WriteLine($"[KeyboardPet] 설정 저장 실패: {ex.Message}");
        }
    }

    public void Dispose()
    {
        SaveNow();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static AppSettings ImportLegacySetFolders(AppSettings settings)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardPet", "sets");
        if (!Directory.Exists(root))
        {
            return settings;
        }

        var existing = settings.FrameSets.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = Directory.EnumerateDirectories(root)
            .Select(dir => new FrameSetSettings(Path.GetFileName(dir), dir))
            .Where(f => !existing.Contains(f.Name))
            .ToList();

        return imported.Count == 0
            ? settings
            : settings with { FrameSets = settings.FrameSets.Concat(imported).ToList() };
    }
}
