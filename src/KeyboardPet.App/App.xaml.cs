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

    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyboardPet");

    public static IServiceProvider Services =>
        ((App)Current)._services ?? throw new InvalidOperationException("DI 컨테이너가 아직 초기화되지 않았습니다.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 모든 경로의 미처리 예외를 crash.log에 남긴다.
        // - DispatcherUnhandledException: UI 스레드(대부분의 앱 코드)
        // - AppDomain.UnhandledException: 네이티브 콜백(훅, 창 프로시저)이나 다른 스레드에서 새어 나온 예외
        // - UnobservedTaskException: 백그라운드 작업(썸네일 디코딩 등)
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DiagnosticsLog.Write("치명적 미처리 예외 (프로세스 종료)", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagnosticsLog.Write("백그라운드 작업 예외", args.Exception);
            args.SetObserved();
        };

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Keyboard Pet이 이미 실행 중입니다.", "Keyboard Pet",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _services = ConfigureServices();

        // 시작 단계는 각각 격리한다. 트레이·훅·자동 실행은 실패해도 앱을 계속 띄우고, 핵심 단계만 종료한다.
        var settings = _services.GetRequiredService<SettingsService>();
        RunStep("설정 읽기", () => settings.Load(), fatal: false);

        var tray = _services.GetRequiredService<TrayService>();
        RunStep("트레이 아이콘", () => tray.Show(), fatal: false);

        if (!RunStep("애니메이션 초기화", () => _services.GetRequiredService<AnimationService>().Initialize(), fatal: true)
            || !RunStep("펫 창 표시", () => _services.GetRequiredService<PetWindow>().Show(), fatal: true))
        {
            return;
        }

        RunStep("키보드 입력 감지", () => _services.GetRequiredService<KeyboardInputService>().Start(), fatal: false,
            userMessage: "키보드 입력 감지를 시작하지 못했습니다. 펫은 표시되지만 타이핑에 반응하지 않습니다.\n앱을 다시 실행해 보세요.");
        RunStep("자동 실행 설정", () => _services.GetRequiredService<StartupService>().Apply(), fatal: false);

        if (settings.LastLoadError is not null)
        {
            MessageBox.Show(
                "설정 파일이 손상되어 기본값으로 시작합니다.\n손상된 파일은 설정 폴더에 .corrupt-* 이름으로 보관됩니다.",
                "Keyboard Pet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (!tray.IsCreated)
        {
            MessageBox.Show(
                "트레이 아이콘을 만들지 못해 트레이 없이 실행합니다.\n설정과 종료 메뉴는 펫 창을 마우스 오른쪽 버튼으로 눌러 열 수 있습니다.\n" +
                $"자세한 내용: {DiagnosticsLog.FilePath}",
                "Keyboard Pet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // ServiceProvider.Dispose가 IDisposable 싱글턴(훅, 트레이, 엔진, 설정 저장)을 역순으로 정리한다.
        _services?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>시작 단계 하나를 실행한다. 실패 시 로그를 남기고, fatal이면 안내 후 종료한다.</summary>
    private bool RunStep(string name, Action action, bool fatal, string? userMessage = null)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Write($"시작 단계 실패: {name}", ex);

            if (fatal)
            {
                MessageBox.Show(
                    $"{name} 중 오류가 발생해 Keyboard Pet을 시작할 수 없습니다.\n\n{ex.GetType().Name}: {ex.Message}\n\n자세한 내용: {DiagnosticsLog.FilePath}",
                    "Keyboard Pet", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
            else if (userMessage is not null)
            {
                MessageBox.Show(userMessage, "Keyboard Pet", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        MessageBox.Show(
            $"예기치 않은 오류가 발생했습니다. 앱은 계속 실행됩니다.\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n자세한 내용: {DiagnosticsLog.FilePath}",
            "Keyboard Pet", MessageBoxButton.OK, MessageBoxImage.Error);

        // 처리됨으로 표시하지 않으면 런타임이 프로세스를 종료한다(0xE0434352 대화상자).
        e.Handled = true;
    }
}
