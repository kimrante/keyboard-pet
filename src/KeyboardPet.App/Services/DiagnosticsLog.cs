using System.IO;

namespace KeyboardPet.App.Services;

/// <summary>
/// %LocalAppData%\KeyboardPet\crash.log 에 오류를 남긴다. 어떤 상황에서도 예외를 던지지 않는다.
/// 키 입력 값은 절대 기록하지 않는다(개인정보 원칙).
/// </summary>
public static class DiagnosticsLog
{
    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyboardPet");

    public static string FilePath => Path.Combine(Directory, "crash.log");

    public static void Write(string context, Exception exception) =>
        Write($"{context}: {exception}");

    public static void Write(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var line = $"[{DateTimeOffset.Now:O}] [{Environment.OSVersion}] {message}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(FilePath, line);
        }
        catch
        {
            // 로그 기록 실패는 무시한다.
        }
    }
}
