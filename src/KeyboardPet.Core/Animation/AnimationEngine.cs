using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Animation;

/// <summary>
/// 활성 이미지 세트와 현재 프레임 인덱스를 관리하고, 설정된 모드의 스케줄러에 따라 프레임을 순환시킨다.
/// UI 스레드 단일 소유를 전제로 하며 스레드 안전하지 않다.
/// </summary>
public sealed class AnimationEngine : IDisposable
{
    private readonly IFrameTimerFactory _timers;
    private readonly IClock _clock;
    private IFrameScheduler? _scheduler;
    private bool _isRunning;

    public AnimationEngine(IFrameTimerFactory timers, AnimationOptions? options = null, IClock? clock = null)
    {
        _timers = timers;
        _clock = clock ?? new SystemClock();
        Options = (options ?? new AnimationOptions()).Normalized();
        _scheduler = CreateScheduler(Options);
    }

    public FrameSet ActiveSet { get; private set; } = FrameSet.Empty;

    public int FrameIndex { get; private set; }

    public AnimationOptions Options { get; private set; }

    public bool IsRunning => _isRunning;

    /// <summary>프레임 인덱스가 바뀔 때(세트 교체 포함) 발생. 인자는 새 인덱스.</summary>
    public event Action<int>? FrameChanged;

    public event Action<FrameSet>? ActiveSetChanged;

    public void SetActiveSet(FrameSet set, bool resetIndex = true)
    {
        ActiveSet = set;
        FrameIndex = resetIndex || FrameIndex >= set.FrameCount ? 0 : FrameIndex;
        ActiveSetChanged?.Invoke(set);
        FrameChanged?.Invoke(FrameIndex);
    }

    public void Advance()
    {
        if (ActiveSet.FrameCount <= 1)
        {
            return;
        }

        FrameIndex = (FrameIndex + 1) % ActiveSet.FrameCount;
        FrameChanged?.Invoke(FrameIndex);
    }

    public void ResetToFirst()
    {
        if (FrameIndex == 0)
        {
            return;
        }

        FrameIndex = 0;
        FrameChanged?.Invoke(FrameIndex);
    }

    public void OnKeystroke() => _scheduler?.OnKeystroke();

    /// <summary>모드/파라미터를 바꾼다. 실행 중이었다면 새 스케줄러로 즉시 이어서 실행한다.</summary>
    public void ApplyOptions(AnimationOptions options)
    {
        Options = options.Normalized();

        _scheduler?.Dispose();
        _scheduler = CreateScheduler(Options);

        if (_isRunning)
        {
            _scheduler.Start();
        }
    }

    public void Start()
    {
        _isRunning = true;
        _scheduler?.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        _scheduler?.Stop();
    }

    public void Dispose()
    {
        _isRunning = false;
        _scheduler?.Dispose();
        _scheduler = null;
        FrameChanged = null;
        ActiveSetChanged = null;
    }

    private IFrameScheduler CreateScheduler(AnimationOptions o) => o.Mode switch
    {
        FrameMode.Fixed => new FixedIntervalScheduler(
            _timers, TimeSpan.FromMilliseconds(o.FixedIntervalMs), Advance),

        FrameMode.Random => new RandomIntervalScheduler(
            _timers,
            TimeSpan.FromMilliseconds(o.RandomMinMs),
            TimeSpan.FromMilliseconds(o.RandomMaxMs),
            Advance,
            o.RandomSeed is int seed ? new Random(seed) : null),

        FrameMode.Keystroke => new KeystrokeScheduler(
            _timers, o.KeysPerFrame, TimeSpan.FromMilliseconds(o.IdleReturnMs), Advance, ResetToFirst),

        FrameMode.Adaptive => new AdaptiveScheduler(
            _timers,
            _clock,
            TimeSpan.FromMilliseconds(o.AdaptiveSlowMs),
            TimeSpan.FromMilliseconds(o.AdaptiveFastMs),
            o.AdaptiveTargetKeysPerSecond,
            TimeSpan.FromMilliseconds(o.AdaptiveWindowMs),
            TimeSpan.FromMilliseconds(o.IdleReturnMs),
            Advance,
            ResetToFirst),

        _ => throw new ArgumentOutOfRangeException(nameof(o), o.Mode, "알 수 없는 프레임 모드"),
    };
}
