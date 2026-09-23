using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeyboardPet.App.Services;
using KeyboardPet.App.Windows;
using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Effects;
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
    private ImageSource? _currentFrame;

    [ObservableProperty]
    private double _scale = 1.0;

    /// <summary>효과가 이미지 밖으로 움직일 여백(이미지 긴 변에 대한 비율). EffectService가 설정한다.</summary>
    [ObservableProperty]
    private EffectPadding _effectPadding = EffectPadding.None;

    private double _frameWidth;
    private double _frameHeight;
    private Thickness _effectMargin;

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
    /// 프레임이 바뀔 때마다 통지하면 크기가 같은 프레임에서도 레이아웃이 다시 돌므로, 값이 달라졌을 때만 통지한다.
    /// </summary>
    public double FrameWidth => _frameWidth;

    public double FrameHeight => _frameHeight;

    /// <summary>이미지 둘레의 투명 여백(DIP). 흔들리거나 튀어오르는 이미지가 창 밖으로 잘리지 않게 한다.</summary>
    public Thickness EffectMargin => _effectMargin;

    partial void OnCurrentFrameChanged(ImageSource? value) => UpdateFrameSize();

    partial void OnEffectPaddingChanged(EffectPadding value) => UpdateFrameSize();

    private void UpdateFrameSize()
    {
        var bitmap = CurrentFrame as BitmapSource;
        var width = bitmap?.PixelWidth * Scale ?? 0;
        var height = bitmap?.PixelHeight * Scale ?? 0;
        if (width != _frameWidth)
        {
            _frameWidth = width;
            OnPropertyChanged(nameof(FrameWidth));
        }

        if (height != _frameHeight)
        {
            _frameHeight = height;
            OnPropertyChanged(nameof(FrameHeight));
        }

        var size = Math.Max(width, height);
        var p = EffectPadding;
        var margin = new Thickness(Math.Ceiling(p.Side * size), Math.Ceiling(p.Top * size), Math.Ceiling(p.Side * size), Math.Ceiling(p.Bottom * size));
        if (margin != _effectMargin)
        {
            _effectMargin = margin;
            OnPropertyChanged(nameof(EffectMargin));
        }
    }

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
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
            return;
        }

        // 루트 컨테이너에서 만들면 IDisposable인 SettingsViewModel이 앱 종료까지 컨테이너에 붙잡혀(프레임 비트맵과 함께)
        // 창을 열 때마다 누적된다. 창마다 스코프를 만들고 닫힐 때 함께 버린다.
        var scope = _services.CreateScope();
        ShowPet();
        _settingsWindow = scope.ServiceProvider.GetRequiredService<SettingsWindow>();
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            scope.Dispose();
        };
        _settingsWindow.Show();
    }

    /// <summary>Alt+F4 등으로 숨겨진 펫 창을 다시 보여 준다.</summary>
    [RelayCommand]
    private void ShowPet()
    {
        var pet = _services.GetRequiredService<PetWindow>();
        if (!pet.IsVisible)
        {
            pet.Show();
        }
    }

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    partial void OnIsTopmostChanged(bool value) => Push(s => s with { IsTopmost = value });

    partial void OnScaleChanged(double value)
    {
        UpdateFrameSize();
        Push(s => s with { Window = s.Window with { Scale = value } });
    }

    partial void OnOpacityChanged(double value) => Push(s => s with { Window = s.Window with { Opacity = value } });

    partial void OnClickThroughChanged(bool value) => Push(s => s with { Window = s.Window with { ClickThrough = value } });

    partial void OnShowCounterChanged(bool value) => Push(s => s with { Window = s.Window with { ShowCounter = value } });

    partial void OnFrameModeChanged(FrameMode value) =>
        Push(s => s.WithEffectiveAnimation(s.EffectiveAnimation with { Mode = value }));

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
            FrameMode = s.EffectiveAnimation.Mode;
        }
        finally
        {
            _syncing = false;
        }
    }
}
