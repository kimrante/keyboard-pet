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

    public static void Migrate(JsonObject root)
    {
        if (ReadVersion(root) < 2)
        {
            MigrateV1ToV2(root);
        }

        root["version"] = AppSettings.CurrentVersion;
    }

    /// <summary>
    /// v1 → v2: 공통 animation/rules를 세트 프로필로 옮기고, 규칙은 자기 세트를 가리키던 것만 남긴다
    /// (다른 세트로 전환하던 규칙은 세트에 귀속될 수 없으므로 버린다). 없어진 내장 세트 이름은 '예시'로 바꾼다.
    /// </summary>
    private static void MigrateV1ToV2(JsonObject root)
    {
        var userSets = (root["frameSets"] as JsonArray ?? new JsonArray())
            .Select(n => Str(n?["name"])?.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string MapName(string name)
        {
            name = name.Trim();
            return RemovedBuiltInSets.Contains(name, StringComparer.OrdinalIgnoreCase) && !userSets.Contains(name)
                ? AppSettings.ExampleSetName
                : name;
        }

        var defaultSet = Str(root["defaultFrameSet"]) is { } d && !string.IsNullOrWhiteSpace(d)
            ? MapName(d)
            : AppSettings.BuiltInDefaultSet;

        var globalAnimation = root["animation"];
        var globalRules = root["rules"] as JsonArray;
        var oldProfiles = root["setProfiles"] as JsonObject ?? new JsonObject();

        var targets = userSets.Append(defaultSet).Distinct(StringComparer.OrdinalIgnoreCase);
        var profiles = new JsonObject();
        foreach (var set in targets)
        {
            // 같은 세트로 매핑되는 옛 프로필이 여럿이면(예: idle, jump) 세트 이름과 정확히 같은 것을 우선한다.
            var oldProfile = oldProfiles
                .Where(p => !string.IsNullOrWhiteSpace(p.Key) && string.Equals(MapName(p.Key), set, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => string.Equals(p.Key.Trim(), set, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .Select(p => p.Value as JsonObject)
                .FirstOrDefault(p => p is not null);

            var profile = new JsonObject();
            if ((oldProfile?["animation"] ?? globalAnimation) is { } animation)
            {
                profile["animation"] = animation.DeepClone();
            }

            if (ConvertRules(oldProfile?["rules"] as JsonArray ?? globalRules, set, MapName) is { } rules)
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

    /// <summary>세트 <paramref name="set"/>를 가리키던 규칙만 남긴다. 남는 규칙이 없으면 null(세트 기본 규칙 사용).</summary>
    private static JsonArray? ConvertRules(JsonArray? rules, string set, Func<string, string> mapName)
    {
        if (rules is null || IsV1DefaultRules(rules))
        {
            return null;
        }

        var kept = new JsonArray();
        foreach (var rule in rules.OfType<JsonObject>())
        {
            if (Str(rule["frameSet"]) is { } target && string.Equals(mapName(target), set, StringComparison.OrdinalIgnoreCase))
            {
                var copy = (JsonObject)rule.DeepClone();
                copy.Remove("frameSet");
                kept.Add(copy);
            }
        }

        return kept.Count == 0 ? null : kept;
    }

    /// <summary>v1 기본 규칙(Enter → jump, * → typing)을 손대지 않은 경우. 예시 세트의 새 기본 규칙으로 대체한다.</summary>
    private static bool IsV1DefaultRules(JsonArray rules) =>
        rules.Count == 2
        && IsRule(rules[0], "Enter", "jump")
        && IsRule(rules[1], "*", "typing");

    private static bool IsRule(JsonNode? node, string key, string set) =>
        node is JsonObject o
        && string.Equals(Str(o["frameSet"])?.Trim(), set, StringComparison.OrdinalIgnoreCase)
        && o["keys"] is JsonArray keys && keys.Count == 1 && Str(keys[0])?.Trim() == key;

    private static int ReadVersion(JsonObject root) =>
        root["version"] is JsonValue v && v.TryGetValue<int>(out var version) ? version : 0;

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
