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

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Keyboard Pet이 이미 실행 중입니다.", "Keyboard Pet",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _services = ConfigureServices();

        var settings = _services.GetRequiredService<SettingsService>();
        settings.Load();

        _services.GetRequiredService<TrayService>().Show();
        // 애니메이션을 먼저 초기화해야 PetWindow가 첫 프레임 크기로 자리를 잡는다.
        _services.GetRequiredService<AnimationService>().Initialize();
        _services.GetRequiredService<PetWindow>().Show();
        _services.GetRequiredService<KeyboardInputService>().Start();
        _services.GetRequiredService<StartupService>().Apply();

        if (settings.LastLoadError is not null)
        {
            MessageBox.Show(
                "설정 파일이 손상되어 기본값으로 시작합니다.\n손상된 파일은 설정 폴더에 .corrupt-* 이름으로 보관됩니다.",
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
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyboardPet");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 로그 기록 실패는 무시하고 원래 예외를 표시한다.
        }

        MessageBox.Show($"예기치 않은 오류가 발생했습니다.\n\n{e.Exception.Message}", "Keyboard Pet",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
