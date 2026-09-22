using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyboardPet.Core.Settings;

/// <summary>
/// settings.json 읽기/쓰기. 손상된 파일은 옆에 백업해 두고 기본값으로 복구한다.
/// 저장은 임시 파일에 쓴 뒤 교체해, 저장 도중 종료돼도 기존 파일이 깨지지 않게 한다.
/// </summary>
public sealed class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public string BackupPath => FilePath + ".bak";

    public bool Exists => File.Exists(FilePath);

    /// <summary>마지막 Load에서 파일이 손상되어 기본값으로 대체된 경우 그 사유. 정상이면 null.</summary>
    public string? LastLoadError { get; private set; }

    public AppSettings Load()
    {
        LastLoadError = null;

        if (!File.Exists(FilePath))
        {
            return AppSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null)
            {
                throw new JsonException("설정 파일이 비어 있습니다.");
            }

            return Migrate(loaded).Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastLoadError = ex.Message;
            TryQuarantineCorruptFile();
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings.Normalized(), JsonOptions);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(FilePath))
        {
            File.Copy(FilePath, BackupPath, overwrite: true);
        }

        File.Move(tempPath, FilePath, overwrite: true);
    }

    private static AppSettings Migrate(AppSettings loaded)
    {
        // 버전이 올라가면 여기서 단계별로 변환한다. v1이 최초 버전이므로 아직 변환 규칙은 없다.
        return loaded.Version == AppSettings.CurrentVersion
            ? loaded
            : loaded with { Version = AppSettings.CurrentVersion };
    }

    private void TryQuarantineCorruptFile()
    {
        try
        {
            var quarantine = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(FilePath, quarantine, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 격리 실패는 무시한다. 다음 Save가 파일을 덮어쓴다.
        }
    }
}
