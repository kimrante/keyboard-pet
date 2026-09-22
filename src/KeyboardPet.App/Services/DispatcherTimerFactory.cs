using System.Windows.Threading;
using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.App.Services;

/// <summary>
/// Core의 IFrameTimer를 WPF DispatcherTimer로 구현한다. 콜백은 UI 스레드에서 실행된다.
/// </summary>
public sealed class DispatcherTimerFactory : IFrameTimerFactory
{
    public IFrameTimer Create() => new DispatcherTimerAdapter();

    private sealed class DispatcherTimerAdapter : IFrameTimer
    {
        private readonly DispatcherTimer _timer = new(DispatcherPriority.Render);

        public DispatcherTimerAdapter()
        {
            _timer.Tick += (_, _) => Tick?.Invoke();
        }

        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public bool IsRunning => _timer.IsEnabled;

        public event Action? Tick;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            _timer.Stop();
            Tick = null;
        }
    }
}
