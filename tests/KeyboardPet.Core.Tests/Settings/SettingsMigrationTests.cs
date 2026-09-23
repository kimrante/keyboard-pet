using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.Core.Tests.Settings;

/// <summary>v1(공통 animation/rules, 규칙이 세트를 가리킴) → v2(모든 설정이 세트에 귀속) 변환.</summary>
public sealed class SettingsMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "KeyboardPet.Tests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private AppSettings LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, json);
        var store = new SettingsStore(FilePath);
        var settings = store.Load();
        Assert.Null(store.LastLoadError);
        return settings;
    }

    [Fact]
    public void V1Defaults_BecomeExampleSet_WithExampleRules()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "defaultFrameSet": "idle",
              "animation": { "mode": "Fixed", "fixedIntervalMs": 250 },
              "rules": [
                { "keys": ["Enter"], "frameSet": "jump", "holdMs": 800, "resetIndex": true },
                { "keys": ["*"], "frameSet": "typing", "holdMs": 600, "resetIndex": false }
              ]
            }
            """);

        Assert.Equal(AppSettings.CurrentVersion, s.Version);
        Assert.Equal(AppSettings.ExampleSetName, s.DefaultFrameSet);
        Assert.Equal(FrameMode.Fixed, s.EffectiveAnimation.Mode);          // 공통 애니메이션은 세트로 옮겨진다
        Assert.Equal(250, s.EffectiveAnimation.FixedIntervalMs);
        Assert.True(AppSettings.RulesEqual(AppSettings.ExampleRules, s.EffectiveRules));
    }

    [Fact]
    public void GlobalValues_AreCopiedToEverySet_AndOnlySelfRulesAreKept()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "defaultFrameSet": "cat",
              "frameSets": [ { "name": "cat", "folder": "C:\\cat" }, { "name": "dog", "folder": "C:\\dog" } ],
              "animation": { "mode": "Random", "randomMinMs": 70, "randomMaxMs": 90 },
              "rules": [
                { "keys": ["Enter"], "frameSet": "cat", "holdMs": 300, "frameIndex": 2 },
                { "keys": ["Space"], "frameSet": "dog", "holdMs": 100 },
                { "keys": ["Tab"], "frameSet": "jump" }
              ]
            }
            """);

        Assert.Equal("cat", s.DefaultFrameSet);
        Assert.Equal(FrameMode.Random, s.AnimationOf("cat").Mode);
        Assert.Equal(FrameMode.Random, s.AnimationOf("dog").Mode);

        var catRules = s.RulesOf("cat");
        Assert.Single(catRules);
        Assert.Equal(new[] { "Enter" }, catRules[0].Keys);
        Assert.Equal(2, catRules[0].FrameIndex);
        Assert.Equal(300, catRules[0].HoldMs);

        Assert.Single(s.RulesOf("dog"));
        Assert.Equal(new[] { "Space" }, s.RulesOf("dog")[0].Keys);
    }

    [Fact]
    public void ExistingProfiles_WinOverGlobals()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "defaultFrameSet": "cat",
              "frameSets": [ { "name": "cat", "folder": "C:\\cat" } ],
              "animation": { "mode": "Random" },
              "rules": [ { "keys": ["A"], "frameSet": "cat" } ],
              "setProfiles": {
                "cat": {
                  "animation": { "mode": "Adaptive" },
                  "rules": [ { "keys": ["B"], "frameSet": "cat", "frameIndex": 0 }, { "keys": ["C"], "frameSet": "other" } ]
                }
              }
            }
            """);

        Assert.Equal(FrameMode.Adaptive, s.EffectiveAnimation.Mode);
        Assert.Single(s.EffectiveRules);
        Assert.Equal(new[] { "B" }, s.EffectiveRules[0].Keys);
    }

    [Fact]
    public void UserSetNamedLikeOldBuiltIn_KeepsItsName()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "defaultFrameSet": "idle",
              "frameSets": [ { "name": "idle", "folder": "C:\\idle" } ],
              "rules": [ { "keys": ["A"], "frameSet": "idle", "frameIndex": 1 } ]
            }
            """);

        Assert.Equal("idle", s.DefaultFrameSet);
        Assert.Single(s.EffectiveRules);
    }

    [Fact]
    public void RulesPointingOnlyElsewhere_LeaveSetWithoutRules_EvenForExample()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "defaultFrameSet": "idle",
              "rules": [ { "keys": ["A"], "frameSet": "cat", "holdMs": 100 } ]
            }
            """);

        Assert.Equal(AppSettings.ExampleSetName, s.DefaultFrameSet);
        Assert.Empty(s.EffectiveRules);     // 편집했던 규칙이 있었으면 샘플 규칙을 되살리지 않는다
    }

    [Fact]
    public void RulesForDeletedIdleAndTypingImages_AreDropped_JumpRulesKept()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "defaultFrameSet": "idle",
              "rules": [
                { "keys": ["Space"], "frameSet": "idle", "frameIndex": 3 },
                { "keys": ["Tab"], "frameSet": "typing" },
                { "keys": ["Enter"], "frameSet": "jump", "holdMs": 500, "frameIndex": 1 }
              ]
            }
            """);

        Assert.Single(s.EffectiveRules);
        Assert.Equal(new[] { "Enter" }, s.EffectiveRules[0].Keys);
        Assert.Equal(1, s.EffectiveRules[0].FrameIndex);
    }

    [Fact]
    public void NonObjectFrameSetEntries_DoNotCrash()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "version": 1, "frameSets": [ "cat", 1, null ], "rules": [] }""");
        var store = new SettingsStore(FilePath);

        var s = store.Load();   // 역직렬화 단계에서 손상 파일로 처리되고 예외가 새지 않아야 한다

        Assert.NotNull(store.LastLoadError);
        Assert.Equal(AppSettings.BuiltInDefaultSet, s.DefaultFrameSet);
    }

    [Fact]
    public void MissingVersion_WithoutV1Fields_IsReadAsCurrent()
    {
        var s = LoadJson("""
            {
              "defaultFrameSet": "cat",
              "frameSets": [ { "name": "cat", "folder": "C:\\cat" } ],
              "setProfiles": { "cat": { "rules": [ { "keys": ["A"], "frameIndex": 2 } ] } }
            }
            """);

        Assert.Single(s.EffectiveRules);
        Assert.Equal(2, s.EffectiveRules[0].FrameIndex);
    }

    [Fact]
    public void MissingVersion_WithV1Fields_IsMigrated()
    {
        var s = LoadJson("""{ "defaultFrameSet": "idle", "animation": { "mode": "Random" } }""");

        Assert.Equal(AppSettings.ExampleSetName, s.DefaultFrameSet);
        Assert.Equal(FrameMode.Random, s.EffectiveAnimation.Mode);
    }

    [Fact]
    public void DeliberatelyEmptyRules_StayEmpty()
    {
        var s = LoadJson("""{ "version": 1, "defaultFrameSet": "idle", "rules": [] }""");

        Assert.Empty(s.EffectiveRules);
    }

    [Fact]
    public void CustomizedV1DefaultRules_AreNotTreatedAsDefaults()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "defaultFrameSet": "jump",
              "rules": [
                { "keys": ["Enter"], "frameSet": "jump", "holdMs": 2000, "resetIndex": true, "frameIndex": 1 },
                { "keys": ["*"], "frameSet": "typing", "holdMs": 600, "resetIndex": false }
              ]
            }
            """);

        // jump 규칙은 편집 내용 그대로 예시 세트에 남고, 이미지가 사라진 typing 규칙은 버려진다.
        Assert.Single(s.EffectiveRules);
        Assert.Equal(2000, s.EffectiveRules[0].HoldMs);
        Assert.Equal(1, s.EffectiveRules[0].FrameIndex);
    }

    [Fact]
    public void V1DefaultRules_MatchCaseInsensitively()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "rules": [
                { "keys": ["enter"], "frameSet": "JUMP", "holdMs": 800, "resetIndex": true },
                { "keys": ["*"], "frameSet": "typing", "holdMs": 600, "resetIndex": false }
              ]
            }
            """);

        Assert.True(AppSettings.RulesEqual(AppSettings.ExampleRules, s.EffectiveRules));
    }

    [Fact]
    public void SeveralOldBuiltInProfiles_PreferTheOldDefaultSet()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "defaultFrameSet": "typing",
              "setProfiles": {
                "idle": { "animation": { "mode": "Adaptive" } },
                "typing": { "animation": { "mode": "Fixed" } }
              }
            }
            """);

        Assert.Equal(FrameMode.Fixed, s.EffectiveAnimation.Mode);
    }

    [Fact]
    public void V1File_WithDuplicateKeys_IsTreatedAsCorrupt_ButV2DuplicatesAreTolerated()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "version": 2, "isTopmost": true, "isTopmost": false }""");
        var v2 = new SettingsStore(FilePath);
        Assert.False(v2.Load().IsTopmost);
        Assert.Null(v2.LastLoadError);

        File.WriteAllText(FilePath, """{ "version": 1, "isTopmost": true, "isTopmost": false }""");
        var v1 = new SettingsStore(FilePath);
        v1.Load();
        Assert.NotNull(v1.LastLoadError);
    }

    [Fact]
    public void ProfilesOfVanishedSets_AreDropped()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "defaultFrameSet": "cat",
              "frameSets": [ { "name": "cat", "folder": "C:\\cat" } ],
              "setProfiles": { "gone": { "animation": { "mode": "Fixed" } } }
            }
            """);

        Assert.Null(s.ProfileOf("gone"));
    }

    [Fact]
    public void MigratedFile_RoundTrips_AsV2()
    {
        LoadJson("""{ "version": 1, "defaultFrameSet": "typing", "animation": { "mode": "Fixed" } }""");
        var store = new SettingsStore(FilePath);
        store.Save(store.Load());

        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        var again = new SettingsStore(FilePath).Load();

        Assert.False(root.ContainsKey("animation"));   // 공통 값은 더 이상 저장되지 않는다
        Assert.False(root.ContainsKey("rules"));
        Assert.Equal(AppSettings.CurrentVersion, (int)root["version"]!);
        Assert.Equal(AppSettings.ExampleSetName, again.DefaultFrameSet);
        Assert.Equal(FrameMode.Fixed, again.EffectiveAnimation.Mode);
    }
}
