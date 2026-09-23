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
    public void RulesPointingOnlyElsewhere_LeaveSetWithDefaults()
    {
        var s = LoadJson("""
            {
              "version": 1,
              "defaultFrameSet": "cat",
              "frameSets": [ { "name": "cat", "folder": "C:\\cat" } ],
              "rules": [ { "keys": ["A"], "frameSet": "dog" } ]
            }
            """);

        Assert.Empty(s.EffectiveRules);
        Assert.Null(s.ProfileOf("cat"));    // 옮길 값이 없으면 프로필도 만들지 않는다
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
