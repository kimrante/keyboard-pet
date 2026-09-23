using System.Windows.Threading;
using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.App.Services;

/// <summary>
/// Core의 IFrameTimer를 WPF DispatcherTimer로 구현한다. 콜백은 UI 스레드에서 실행된다.
/// </summary>
public sealed class DispatcherTimerFactory : IFrameTimerFactory
{
    public IFrameTimer Create() => new DispatcherTimerAdapter();

    /// <summary>
    /// DispatcherTimer는 실행 중에 Interval을 바꾸면 만료 시각을 지금부터 다시 계산한다. 타이핑 속도 연동 모드처럼
    /// 키 입력마다 간격을 조정하면 빠르게 칠수록 틱이 영영 오지 않는 문제가 생기므로, 여기서는 이미 흐른 시간을 빼고
    /// 남은 시간만 기다리게 한다(간격이 같으면 아무것도 하지 않는다).
    /// </summary>
    private sealed class DispatcherTimerAdapter : IFrameTimer
    {
        private static readonly TimeSpan MinRemaining = TimeSpan.FromMilliseconds(1);

        private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
        private TimeSpan _interval;
        private long _periodStartedAt;

        public DispatcherTimerAdapter()
        {
            _timer.Tick += OnTick;
        }

        public TimeSpan Interval
        {
            get => _interval;
            set
            {
                if (value == _interval)
                {
                    return;
                }

                _interval = value;
                if (!_timer.IsEnabled)
                {
                    _timer.Interval = value;
                    return;
                }

                var remaining = value - TimeSpan.FromMilliseconds(Environment.TickCount64 - _periodStartedAt);
                _timer.Interval = remaining > MinRemaining ? remaining : MinRemaining;
            }
        }

        public bool IsRunning => _timer.IsEnabled;

        public event Action? Tick;

        public void Start()
        {
            _periodStartedAt = Environment.TickCount64;
            _timer.Interval = _interval;
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        private void OnTick(object? sender, EventArgs e)
        {
            _periodStartedAt = Environment.TickCount64;
            if (_timer.Interval != _interval)
            {
                _timer.Interval = _interval;   // 남은 시간만 기다리던 주기가 끝났으니 원래 간격으로
            }

            Tick?.Invoke();
        }

        public void Dispose()
        {
            _timer.Stop();
            Tick = null;
        }
    }
}
