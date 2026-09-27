using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.App.Services;

/// <summary>
/// 앱의 단일 설정 원천. <see cref="Current"/>는 불변이며 <see cref="Update"/>로만 바뀐다.
/// 변경은 즉시 <see cref="Changed"/>로 전파되고, 파일 저장은 0.5초 디바운스로 묶은 뒤 백그라운드 스레드에서 수행한다
/// (UI 스레드가 디스크·백신 검사를 기다리지 않도록). 저장은 한 번에 하나만 돌고, 그 사이 바뀐 설정은 마지막 것만 이어서 쓴다.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly SettingsStore _store;
    private readonly DispatcherTimer _saveTimer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly object _gate = new();
    private AppSettings? _pending;      // 아직 쓰지 않은 최신 스냅샷
    private bool _saving;               // 저장 루프가 돌고 있는지
    private Task _inFlight = Task.CompletedTask;
    private bool _dirty;

    public SettingsService(SettingsStore store)
    {
        _store = store;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _saveTimer.Tick += (_, _) => QueueSave();
    }

    public AppSettings Current { get; private set; } = AppSettings.Default;

    public string FilePath => _store.FilePath;

    /// <summary>앱이 관리하는 세트 폴더들의 부모(설정 파일 옆의 sets). --data-dir을 쓰면 그 아래로 바뀐다.</summary>
    public string SetsRoot => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(FilePath)) ?? string.Empty, "sets");

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
            Current = ImportLegacySetFolders(Current, SetsRoot);
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

    /// <summary>바뀐 설정을 지금 큐에 넣고, 진행 중인 저장이 끝날 때까지 기다린다(종료 시).</summary>
    public void SaveNow()
    {
        QueueSave();
        Task inFlight;
        lock (_gate)
        {
            inFlight = _inFlight;
        }

        try
        {
            inFlight.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // 저장 루프 안에서 이미 기록·보고한 오류
        }
    }

    public void Dispose()
    {
        SaveNow();
    }

    private void QueueSave()
    {
        _saveTimer.Stop();
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        lock (_gate)
        {
            _pending = Current;   // 불변 레코드라 스냅샷을 다른 스레드에서 직렬화해도 안전하다
            if (_saving)
            {
                return;           // 돌고 있는 루프가 _pending을 집어 간다
            }

            _saving = true;
            _inFlight = Task.Run(SaveLoop);
        }
    }

    private void SaveLoop()
    {
        var exited = false;
        try
        {
            while (true)
            {
                AppSettings snapshot;
                lock (_gate)
                {
                    if (_pending is null)
                    {
                        // 종료 판단과 _saving 해제를 같은 잠금 안에서 한다. 잠금을 풀고 나서 내리면 그 사이 QueueSave가
                        // "루프가 돌고 있다"고 보고 스냅샷만 남긴 채 돌아가, 마지막 변경이 저장되지 않는다.
                        _saving = false;
                        exited = true;
                        return;
                    }

                    snapshot = _pending;
                    _pending = null;
                }

                string? error = null;
                try
                {
                    _store.Save(snapshot);
                }
                catch (Exception ex)
                {
                    // 어떤 예외든 루프를 죽이지 않는다. 실패한 저장은 다음 변경이나 종료 시 다시 시도한다.
                    error = ex.Message;
                    DiagnosticsLog.Write("설정 저장 실패", ex);
                }

                // 속성은 UI 스레드 소유. 종료 중이라 Dispatcher가 닫혔으면 조용히 버려진다.
                _dispatcher.BeginInvoke(() =>
                {
                    LastSaveError = error;
                    if (error is not null)
                    {
                        _dirty = true;
                    }
                });
            }
        }
        finally
        {
            if (!exited)
            {
                // 예기치 않은 예외로 빠져나온 경우에도 다음 QueueSave가 루프를 다시 띄울 수 있게 한다.
                lock (_gate)
                {
                    _saving = false;
                }
            }
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static AppSettings ImportLegacySetFolders(AppSettings settings, string root)
    {
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
