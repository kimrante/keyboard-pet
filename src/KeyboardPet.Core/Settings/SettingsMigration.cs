using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyboardPet.Core.Settings;

/// <summary>
/// 이전 버전 settings.json을 현재 구조로 바꾼다. 역직렬화 전에 JSON 트리 단계에서 변환하므로
/// 현재 모델에 없는 옛 필드(공통 animation/rules, 규칙의 frameSet)도 읽을 수 있다.
/// </summary>
public static class SettingsMigration
{
    /// <summary>v1까지 있던 내장 세트. v2에서는 jump 이미지만 '예시' 세트로 남았다.</summary>
    private static readonly string[] RemovedBuiltInSets = { "idle", "jump", "typing" };

    /// <summary>이미지가 '예시' 세트로 이어진 옛 내장 세트. 나머지(idle, typing)는 이미지가 삭제되었다.</summary>
    private const string SurvivingBuiltInSet = "jump";

    /// <summary>
    /// 파일이 현재 버전보다 오래되어 변환이 필요한지. 최상위가 객체가 아니면 JsonException.
    /// version이 없으면 v1 전용 필드(최상위 animation/rules)가 있을 때만 옛 파일로 본다.
    /// 숫자가 아닌 version은 역직렬화에서 손상 파일로 처리되도록 변환하지 않는다.
    /// </summary>
    public static bool NeedsMigration(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("설정 파일이 객체가 아닙니다.");
        }

        var hasV1Fields = false;
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.TryGetInt32(out var version) && version < AppSettings.CurrentVersion;
            }

            hasV1Fields |= string.Equals(property.Name, "animation", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(property.Name, "rules", StringComparison.OrdinalIgnoreCase);
        }

        return hasV1Fields;
    }

    /// <summary>옛 버전(v1) 파일을 v2 구조로 바꾼다. <see cref="NeedsMigration"/>가 true일 때만 호출한다.</summary>
    public static void Migrate(JsonObject root)
    {
        MigrateV1ToV2(root);
        root["version"] = 2;
    }

    /// <summary>
    /// v1 → v2: 공통 animation/rules를 세트 프로필로 옮기고, 규칙은 자기 세트를 가리키던 것만 남긴다
    /// (다른 세트로 전환하던 규칙은 세트에 귀속될 수 없으므로 버린다). 없어진 내장 세트 이름은 '예시'로 바꾼다.
    /// </summary>
    private static void MigrateV1ToV2(JsonObject root)
    {
        var userSets = (root["frameSets"] as JsonArray ?? new JsonArray())
            .Select(n => n is JsonObject set ? Str(set["name"])?.Trim() : null)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool IsRemovedBuiltIn(string name) =>
            RemovedBuiltInSets.Contains(name, StringComparer.OrdinalIgnoreCase) && !userSets.Contains(name);

        // 세트 이름(사용 중인 세트, 프로필 키): 없어진 내장 세트는 모두 예시로.
        string MapName(string name)
        {
            name = name.Trim();
            return IsRemovedBuiltIn(name) ? AppSettings.ExampleSetName : name;
        }

        // 규칙 대상: 프레임 번호가 그대로 통하는 jump만 예시로 잇고, 이미지가 사라진 idle/typing 대상 규칙은 버린다.
        string? MapRuleTarget(string name)
        {
            name = name.Trim();
            if (!IsRemovedBuiltIn(name))
            {
                return name;
            }

            return string.Equals(name, SurvivingBuiltInSet, StringComparison.OrdinalIgnoreCase) ? AppSettings.ExampleSetName : null;
        }

        var oldDefault = Str(root["defaultFrameSet"])?.Trim();
        var defaultSet = string.IsNullOrEmpty(oldDefault) ? AppSettings.BuiltInDefaultSet : MapName(oldDefault);

        var globalAnimation = root["animation"];
        var globalRules = root["rules"] as JsonArray;
        var oldProfiles = root["setProfiles"] as JsonObject ?? new JsonObject();

        var targets = userSets.Append(defaultSet).Distinct(StringComparer.OrdinalIgnoreCase);
        var profiles = new JsonObject();
        foreach (var set in targets)
        {
            // 같은 세트로 매핑되는 옛 프로필이 여럿이면(예: idle, typing) 세트 이름과 정확히 같은 것,
            // 그다음 v1에서 실제로 쓰던 기본 세트의 것을 우선한다.
            var oldProfile = oldProfiles
                .Where(p => !string.IsNullOrWhiteSpace(p.Key) && string.Equals(MapName(p.Key), set, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => string.Equals(p.Key.Trim(), set, StringComparison.OrdinalIgnoreCase) ? 0
                    : string.Equals(p.Key.Trim(), oldDefault, StringComparison.OrdinalIgnoreCase) ? 1
                    : 2)
                .Select(p => p.Value as JsonObject)
                .FirstOrDefault(p => p is not null);

            var profile = new JsonObject();
            if ((oldProfile?["animation"] ?? globalAnimation) is { } animation)
            {
                profile["animation"] = animation.DeepClone();
            }

            if (ConvertRules(oldProfile?["rules"] as JsonArray ?? globalRules, set, MapRuleTarget) is { } rules)
            {
                profile["rules"] = rules;
            }

            if (profile.Count > 0)
            {
                profiles[set] = profile;
            }
        }

        root["setProfiles"] = profiles;
        root["defaultFrameSet"] = defaultSet;
        root.Remove("animation");
        root.Remove("rules");
    }

    /// <summary>
    /// 세트 <paramref name="set"/>를 가리키던 규칙만 남긴다. 규칙 목록이 없거나 v1 기본 규칙 그대로면 null(세트 기본 규칙 사용).
    /// 사용자가 규칙을 편집했었다면 남는 규칙이 없더라도 빈 목록으로 두어 샘플 규칙이 되살아나지 않게 한다.
    /// </summary>
    private static JsonArray? ConvertRules(JsonArray? rules, string set, Func<string, string?> mapTarget)
    {
        if (rules is null || IsV1DefaultRules(rules))
        {
            return null;
        }

        var kept = new JsonArray();
        foreach (var rule in rules.OfType<JsonObject>())
        {
            if (Str(rule["frameSet"]) is { } target && string.Equals(mapTarget(target), set, StringComparison.OrdinalIgnoreCase))
            {
                var copy = (JsonObject)rule.DeepClone();
                copy.Remove("frameSet");
                kept.Add(copy);
            }
        }

        return kept;
    }

    /// <summary>v1 기본 규칙(Enter → jump, * → typing)을 손대지 않은 경우. 예시 세트의 새 기본 규칙으로 대체한다.</summary>
    private static bool IsV1DefaultRules(JsonArray rules) =>
        rules.Count == 2
        && IsRule(rules[0], "Enter", "jump")
        && IsRule(rules[1], "*", "typing");

    private static bool IsRule(JsonNode? node, string key, string set) =>
        node is JsonObject o
        && string.Equals(Str(o["frameSet"])?.Trim(), set, StringComparison.OrdinalIgnoreCase)
        && o["keys"] is JsonArray keys && keys.Count == 1
        && string.Equals(Str(keys[0])?.Trim(), key, StringComparison.OrdinalIgnoreCase)
        && o["frameIndex"] is null
        && Int(o["holdMs"]) == (key == "*" ? 600 : 800)
        && Bool(o["resetIndex"]) == (key != "*");

    private static int? Int(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    /// <summary>v1 KeyRule의 기본값(resetIndex = true)을 반영해 읽는다.</summary>
    private static bool Bool(JsonNode? node) =>
        node is not JsonValue v || !v.TryGetValue<bool>(out var b) || b;

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
