using System.IO;
using System.Security;
using KeyboardPet.Core.Settings;
using Microsoft.Win32;

namespace KeyboardPet.App.Services;

/// <summary>
/// "Windows 로그인 시 자동 실행" 설정을 HKCU\...\Run 레지스트리 값으로 반영한다.
/// </summary>
public sealed class StartupService : IDisposable
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyboardPet";

    private readonly SettingsService _settings;

    public StartupService(SettingsService settings)
    {
        _settings = settings;
        _settings.Changed += OnSettingsChanged;
    }

    public string? LastError { get; private set; }

    public void Apply() => Apply(_settings.Current.StartWithWindows);

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(AppSettings old, AppSettings @new)
    {
        if (old.StartWithWindows != @new.StartWithWindows)
        {
            Apply(@new.StartWithWindows);
        }
    }

    private void Apply(bool enabled)
    {
        try
        {
            var desired = enabled
                ? $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 알 수 없습니다.")}\""
                : null;

            // 실행할 때마다 Run 키를 쓰지 않도록, 현재 값과 다를 때만 쓴다(일부 백신은 Run 키 쓰기를 표시한다).
            using (var read = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false))
            {
                if (Equals(read?.GetValue(ValueName) as string, desired))
                {
                    LastError = null;
                    return;
                }
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (desired is not null)
            {
                key.SetValue(ValueName, desired);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            LastError = null;
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LastError = ex.Message;
        }
    }
}
