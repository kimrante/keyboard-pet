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

    /// <summary>단일 프레임 고정 상태. 고정 중에는 스케줄러가 전진을 요청해도 프레임이 바뀌지 않는다.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>프레임 인덱스가 바뀔 때(세트 교체 포함) 발생. 인자는 새 인덱스.</summary>
    public event Action<int>? FrameChanged;

    public event Action<FrameSet>? ActiveSetChanged;

    /// <summary>
    /// 활성 세트를 바꾼다. <paramref name="pinnedFrame"/>이 주어지면 그 프레임 한 장에 고정하고
    /// 애니메이션을 멈춘다(범위를 벗어나면 마지막 프레임으로 보정). null이면 고정을 풀고 애니메이션한다.
    /// </summary>
    public void SetActiveSet(FrameSet set, bool resetIndex = true, int? pinnedFrame = null)
    {
        ActiveSet = set;

        if (pinnedFrame is int pin && set.FrameCount > 0)
        {
            IsPinned = true;
            FrameIndex = Math.Clamp(pin, 0, set.FrameCount - 1);
        }
        else
        {
            IsPinned = false;
            FrameIndex = resetIndex || FrameIndex >= set.FrameCount ? set.FirstLoopIndex : FrameIndex;
        }

        ActiveSetChanged?.Invoke(set);
        FrameChanged?.Invoke(FrameIndex);
    }

    /// <summary>루프의 다음 프레임으로 넘어간다. 고정 중이거나 루프에 넘어갈 프레임이 없으면 아무것도 하지 않는다.</summary>
    public void Advance()
    {
        if (IsPinned)
        {
            return;
        }

        var next = ActiveSet.NextLoopIndex(FrameIndex);
        if (next == FrameIndex)
        {
            return;
        }

        FrameIndex = next;
        FrameChanged?.Invoke(FrameIndex);
    }

    /// <summary>무입력 복귀: 세트의 복귀 프레임(지정이 없으면 루프의 첫 프레임)으로 돌아간다.</summary>
    public void ReturnToIdle()
    {
        var idle = ActiveSet.IdleIndex;
        if (IsPinned || FrameIndex == idle)
        {
            return;
        }

        FrameIndex = idle;
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
            _timers, o.KeysPerFrame, TimeSpan.FromMilliseconds(o.IdleReturnMs), Advance, ReturnToIdle),

        FrameMode.Adaptive => new AdaptiveScheduler(
            _timers,
            _clock,
            TimeSpan.FromMilliseconds(o.AdaptiveSlowMs),
            TimeSpan.FromMilliseconds(o.AdaptiveFastMs),
            o.AdaptiveTargetKeysPerSecond,
            TimeSpan.FromMilliseconds(o.AdaptiveWindowMs),
            TimeSpan.FromMilliseconds(o.IdleReturnMs),
            Advance,
            ReturnToIdle),

        _ => throw new ArgumentOutOfRangeException(nameof(o), o.Mode, "알 수 없는 프레임 모드"),
    };
}
