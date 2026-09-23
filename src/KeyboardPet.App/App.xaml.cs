using System.IO;
using System.Windows;
using System.Windows.Threading;
using KeyboardPet.App.Input;
using KeyboardPet.App.Services;
using KeyboardPet.App.ViewModels;
using KeyboardPet.App.Windows;
using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace KeyboardPet.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\KeyboardPet.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private ServiceProvider? _services;

    /// <summary>
    /// 관리 코드가 실행되는 가장 이른 시점. WPF 초기화(App.xaml 리소스 로드)보다 먼저 실행되므로
    /// 여기서 startup.log를 만들고 전역 예외 기록을 걸어 둔다. 이 파일조차 없으면 .NET 런타임이 뜨지 못한 것이다.
    /// </summary>
    static App()
    {
        DiagnosticsLog.Trace("프로세스 시작 (App 형식 초기화)");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DiagnosticsLog.Write("치명적 미처리 예외 (프로세스 종료)", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagnosticsLog.Write("백그라운드 작업 예외", args.Exception);
            args.SetObserved();
        };
    }

    /// <summary>설정 폴더. --data-dir &lt;폴더&gt;로 바꿀 수 있다(스크린샷·테스트용 설정을 실제 설정과 분리).</summary>
    public static string AppDataDirectory =>
        ArgValue("--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardPet");

    public static IServiceProvider Services =>
        ((App)Current)._services ?? throw new InvalidOperationException("DI 컨테이너가 아직 초기화되지 않았습니다.");

    /// <summary>실행 인수. --no-tray: 트레이 아이콘 생략, --no-hook: 키보드 훅 생략 (문제 원인 분리용).</summary>
    private static bool HasArg(string name) =>
        Environment.GetCommandLineArgs().Skip(1).Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>"--이름 값" 형태 인수의 값. 없으면 null.</summary>
    private static string? ArgValue(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>--screenshot &lt;폴더&gt;: 스크린샷을 저장하고 종료하는 무인 실행 모드. 메시지 상자를 띄우지 않는다.</summary>
    private static string? ScreenshotDirectory { get; } = ArgValue("--screenshot");

    private static bool IsUnattended => ScreenshotDirectory is not null;

    /// <summary>메시지 상자. 무인 실행 모드에서는 로그만 남긴다(CI에서 멈추지 않도록).</summary>
    private static void ShowMessage(string text, string caption, MessageBoxButton button, MessageBoxImage image)
    {
        if (IsUnattended)
        {
            DiagnosticsLog.Trace($"[메시지 생략] {text}");
            return;
        }

        MessageBox.Show(text, caption, button, image);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DiagnosticsLog.Trace("OnStartup 진입");

        // UI 스레드 미처리 예외(대부분의 앱 코드). AppDomain/Task 예외는 정적 생성자에서 이미 걸어 두었다.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            DiagnosticsLog.Trace("다른 인스턴스가 이미 실행 중 → 종료");
            ShowMessage("Keyboard Pet이 이미 실행 중입니다.\n트레이에 아이콘이 없다면 작업 관리자에서 KeyboardPet.exe를 끝낸 뒤 다시 실행하세요.", "Keyboard Pet",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _services = ConfigureServices();
        DiagnosticsLog.Trace("DI 컨테이너 구성 완료");

        // 시작 단계는 각각 격리한다. 트레이·훅·자동 실행은 실패해도 앱을 계속 띄우고, 핵심 단계만 종료한다.
        var settings = _services.GetRequiredService<SettingsService>();
        RunStep("설정 읽기", () => settings.Load(), fatal: false);

        var tray = _services.GetRequiredService<TrayService>();
        if (HasArg("--no-tray"))
        {
            DiagnosticsLog.Trace("--no-tray: 트레이 아이콘 생략");
        }
        else
        {
            RunStep("트레이 아이콘", () => tray.Show(), fatal: false);
        }

        if (!RunStep("애니메이션 초기화", () => _services.GetRequiredService<AnimationService>().Initialize(), fatal: true)
            || !RunStep("효과 초기화", () => _services.GetRequiredService<EffectService>().Initialize(), fatal: true)
            || !RunStep("펫 창 표시", () => _services.GetRequiredService<PetWindow>().Show(), fatal: true))
        {
            return;
        }

        if (HasArg("--no-hook"))
        {
            DiagnosticsLog.Trace("--no-hook: 키보드 훅 생략");
        }
        else
        {
            RunStep("키보드 입력 감지", () => _services.GetRequiredService<KeyboardInputService>().Start(), fatal: false,
                userMessage: "키보드 입력 감지를 시작하지 못했습니다. 펫은 표시되지만 타이핑에 반응하지 않습니다.\n앱을 다시 실행해 보세요.");
        }

        RunStep("자동 실행 설정", () => _services.GetRequiredService<StartupService>().Apply(), fatal: false);
        DiagnosticsLog.Trace("시작 완료");

        if (settings.LastLoadError is not null)
        {
            ShowMessage(
                "설정 파일이 손상되어 기본값으로 시작합니다.\n손상된 파일은 설정 폴더에 .corrupt-* 이름으로 보관됩니다.",
                "Keyboard Pet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (ScreenshotDirectory is { } screenshotDir)
        {
            // 효과 시계를 0에 고정해 두고, 첫 렌더링이 끝난 뒤 시작한다. 실패하면 로그를 남기고 종료 코드 1로 끝낸다.
            _services.GetRequiredService<EffectClock>().Override = 0;
            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await ScreenshotRunner.RunAsync(_services, screenshotDir);
                }
                catch (Exception ex)
                {
                    DiagnosticsLog.Write("스크린샷 모드 실패", ex);
                    Shutdown(1);
                }
            }, DispatcherPriority.ApplicationIdle);
            return;
        }

        if (!tray.IsCreated)
        {
            ShowMessage(
                "트레이 아이콘을 만들지 못해 트레이 없이 실행합니다.\n설정과 종료 메뉴는 펫 창을 마우스 오른쪽 버튼으로 눌러 열 수 있습니다.\n" +
                $"자세한 내용: {DiagnosticsLog.FilePath}",
                "Keyboard Pet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticsLog.Trace($"종료 (코드 {e.ApplicationExitCode})");
        // ServiceProvider.Dispose가 IDisposable 싱글턴(훅, 트레이, 엔진, 설정 저장)을 역순으로 정리한다.
        _services?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>시작 단계 하나를 실행한다. 실패 시 로그를 남기고, fatal이면 안내 후 종료한다.</summary>
    private bool RunStep(string name, Action action, bool fatal, string? userMessage = null)
    {
        DiagnosticsLog.Trace($"단계 시작: {name}");
        try
        {
            action();
            DiagnosticsLog.Trace($"단계 완료: {name}");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write($"시작 단계 실패: {name}", ex);

            if (fatal)
            {
                ShowMessage(
                    $"{name} 중 오류가 발생해 Keyboard Pet을 시작할 수 없습니다.\n\n{ex.GetType().Name}: {ex.Message}\n\n자세한 내용: {DiagnosticsLog.FilePath}",
                    "Keyboard Pet", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(IsUnattended ? 1 : 0);
            }
            else if (userMessage is not null)
            {
                ShowMessage(userMessage, "Keyboard Pet", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }
    }

    private ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IFrameTimerFactory, DispatcherTimerFactory>();
        services.AddSingleton(sp => new AnimationEngine(
            sp.GetRequiredService<IFrameTimerFactory>(),
            options: null,
            clock: sp.GetRequiredService<IClock>()));

        // Settings
        services.AddSingleton(new SettingsStore(Path.Combine(AppDataDirectory, "settings.json")));
        services.AddSingleton<SettingsService>();
        services.AddSingleton<StartupService>();

        // Input
        services.AddSingleton<IKeyboardSource>(_ => new LowLevelKeyboardHook(Dispatcher));
        services.AddSingleton<KeyboardInputService>();

        // Animation
        services.AddSingleton<ImageCache>();
        services.AddSingleton<AnimationService>();
        services.AddSingleton<EffectClock>();
        services.AddSingleton<EffectService>();

        // ViewModels
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<SettingsViewModel>();

        // UI
        services.AddSingleton<TrayService>();
        services.AddSingleton<PetWindow>();
        services.AddTransient<SettingsWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticsLog.Write("UI 스레드 미처리 예외", e.Exception);
        if (IsUnattended)
        {
            e.Handled = true;
            Current.Shutdown(1);
            return;
        }

        ShowMessage(
            $"예기치 않은 오류가 발생했습니다. 앱은 계속 실행됩니다.\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n자세한 내용: {DiagnosticsLog.FilePath}",
            "Keyboard Pet", MessageBoxButton.OK, MessageBoxImage.Error);

        // 처리됨으로 표시하지 않으면 런타임이 프로세스를 종료한다(0xE0434352 대화상자).
        e.Handled = true;
    }
}
