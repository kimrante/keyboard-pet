using System.IO;
using System.Runtime.InteropServices;

namespace KeyboardPet.App.Services;

/// <summary>
/// 진단 로그. 어떤 상황에서도 예외를 던지지 않는다. 키 입력 값은 절대 기록하지 않는다(개인정보 원칙).
/// - crash.log   : 오류·예외 (누적)
/// - startup.log : 시작 단계 추적 (실행할 때마다 새로 씀). 앱이 멈추는 지점을 찾는 데 쓴다.
/// </summary>
public static class DiagnosticsLog
{
    private static readonly object Sync = new();
    private static StreamWriter? _trace;

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyboardPet");

    public static string FilePath => Path.Combine(Directory, "crash.log");

    public static string TracePath => Path.Combine(Directory, "startup.log");

    public static void Write(string context, Exception exception) =>
        Write($"{context}: {exception}");

    public static void Write(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var line = $"[{DateTimeOffset.Now:O}] [{Environment.OSVersion}] {message}{Environment.NewLine}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(FilePath, line);
            }
        }
        catch
        {
            // 로그 기록 실패는 무시한다.
        }

        Trace($"오류: {message.Split('\n')[0]}");
    }

    /// <summary>시작 단계 한 줄을 startup.log에 남긴다. 첫 호출 때 파일을 새로 만들고 환경 정보를 적는다.</summary>
    public static void Trace(string step)
    {
        try
        {
            lock (Sync)
            {
                // 호출마다 파일을 열고 닫지 않도록 첫 호출에 열어 둔다(다른 프로세스가 읽을 수 있게 공유 읽기 허용).
                if (_trace is null)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    _trace = OpenTrace();
                    _trace.Write(BuildHeader());
                    _trace.Flush();
                }

                _trace.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] {step}");
                _trace.Flush();   // 줄마다 한 번만 실제 쓰기
            }
        }
        catch
        {
            // 무시
        }
    }

    /// <summary>실행 중인 인스턴스가 startup.log를 쥐고 있으면(두 번째 실행) 프로세스별 파일에 남긴다.</summary>
    private static StreamWriter OpenTrace()
    {
        try
        {
            return new StreamWriter(new FileStream(TracePath, FileMode.Create, FileAccess.Write, FileShare.Read));
        }
        catch (IOException)
        {
            var path = Path.Combine(Directory, $"startup-{Environment.ProcessId}.log");
            return new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read));
        }
    }

    private static string BuildHeader()
    {
        string version;
        try
        {
            version = typeof(DiagnosticsLog).Assembly.GetName().Version?.ToString(3) ?? "?";
        }
        catch
        {
            version = "?";
        }

        return string.Join(Environment.NewLine, new[]
        {
            $"Keyboard Pet {version} 시작 추적 로그",
            $"시각      : {DateTimeOffset.Now:O}",
            $"OS        : {RuntimeInformation.OSDescription} ({Environment.OSVersion.Version})",
            $"런타임    : {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}",
            $"실행 파일 : {Environment.ProcessPath}",
            $"인수      : {string.Join(" ", Environment.GetCommandLineArgs().Skip(1))}",
            $"세션      : 대화형={Environment.UserInteractive}, 64비트 OS={Environment.Is64BitOperatingSystem}, 프로세서={Environment.ProcessorCount}",
            string.Empty,
        });
    }
}
