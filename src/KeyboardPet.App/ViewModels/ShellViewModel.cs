using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeyboardPet.App.Services;
using KeyboardPet.App.Windows;
using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace KeyboardPet.App.ViewModels;

/// <summary>
/// 트레이 메뉴와 PetWindow가 공유하는 앱 상태.
/// 설정에 해당하는 속성은 SettingsService와 양방향으로 동기화된다:
/// 이 뷰모델에서 바꾸면 설정에 반영되고, 설정이 다른 곳(설정 창)에서 바뀌면 여기에 반영된다.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly SettingsService _settings;
    private readonly IServiceProvider _services;
    private bool _syncing;
    private SettingsWindow? _settingsWindow;

    [ObservableProperty]
    private bool _isTopmost = true;

    [ObservableProperty]
    private int _keystrokeCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameWidth), nameof(FrameHeight))]
    private ImageSource? _currentFrame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameWidth), nameof(FrameHeight))]
    private double _scale = 1.0;

    [ObservableProperty]
    private double _opacity = 1.0;

    [ObservableProperty]
    private bool _clickThrough;

    [ObservableProperty]
    private bool _showCounter = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixedMode), nameof(IsRandomMode), nameof(IsKeystrokeMode), nameof(IsAdaptiveMode))]
    private FrameMode _frameMode = FrameMode.Keystroke;

    public ShellViewModel(SettingsService settings, IServiceProvider services)
    {
        _settings = settings;
        _services = services;

        SyncFrom(settings.Current);
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>
    /// 표시 크기(DIP). 이미지 파일의 DPI 메타데이터를 무시하고 원본 픽셀 × 배율로 계산한다.
    /// </summary>
    public double FrameWidth => (CurrentFrame as BitmapSource)?.PixelWidth * Scale ?? 0;

    public double FrameHeight => (CurrentFrame as BitmapSource)?.PixelHeight * Scale ?? 0;

    public bool IsFixedMode => FrameMode == FrameMode.Fixed;

    public bool IsRandomMode => FrameMode == FrameMode.Random;

    public bool IsKeystrokeMode => FrameMode == FrameMode.Keystroke;

    public bool IsAdaptiveMode => FrameMode == FrameMode.Adaptive;

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
    }

    [RelayCommand]
    private void ToggleTopmost() => IsTopmost = !IsTopmost;

    [RelayCommand]
    private void SetFrameMode(FrameMode mode) => FrameMode = mode;

    [RelayCommand]
    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = _services.GetRequiredService<SettingsWindow>();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    partial void OnIsTopmostChanged(bool value) => Push(s => s with { IsTopmost = value });

    partial void OnScaleChanged(double value) => Push(s => s with { Window = s.Window with { Scale = value } });

    partial void OnOpacityChanged(double value) => Push(s => s with { Window = s.Window with { Opacity = value } });

    partial void OnClickThroughChanged(bool value) => Push(s => s with { Window = s.Window with { ClickThrough = value } });

    partial void OnShowCounterChanged(bool value) => Push(s => s with { Window = s.Window with { ShowCounter = value } });

    partial void OnFrameModeChanged(FrameMode value) => Push(s => s with { Animation = s.Animation with { Mode = value } });

    private void Push(Func<AppSettings, AppSettings> mutate)
    {
        if (!_syncing)
        {
            _settings.Update(mutate);
        }
    }

    private void OnSettingsChanged(AppSettings old, AppSettings @new) => SyncFrom(@new);

    private void SyncFrom(AppSettings s)
    {
        _syncing = true;
        try
        {
            IsTopmost = s.IsTopmost;
            Scale = s.Window.Scale;
            Opacity = s.Window.Opacity;
            ClickThrough = s.Window.ClickThrough;
            ShowCounter = s.Window.ShowCounter;
            FrameMode = s.Animation.Mode;
        }
        finally
        {
            _syncing = false;
        }
    }
}
