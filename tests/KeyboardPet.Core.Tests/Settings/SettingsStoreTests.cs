using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Rules;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
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

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var store = new SettingsStore(FilePath);

        var settings = store.Load();

        Assert.True(settings.IsTopmost);
        Assert.Equal(AppSettings.BuiltInDefaultSet, settings.DefaultFrameSet);
        Assert.True(AppSettings.RulesEqual(AppSettings.ExampleRules, settings.EffectiveRules));
        Assert.Null(store.LastLoadError);
        Assert.False(store.Exists);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new SettingsStore(FilePath);
        var original = new AppSettings
        {
            IsTopmost = false,
            CountAutoRepeat = true,
            StartWithWindows = true,
            Window = new WindowSettings { X = 100.5, Y = 200, Scale = 1.5, Opacity = 0.8, ClickThrough = true, ShowCounter = false },
            FrameSets = new[]
            {
                new FrameSetSettings("cat", @"C:\pets\cat", new[] { "c.png", "a.png" }, AnimationFrames: new[] { "c.png" }, IdleFrame: "a.png"),
                new FrameSetSettings("dog", @"C:\pets\dog"),
            },
            DefaultFrameSet = "cat",
            SetProfiles = new Dictionary<string, SetProfile>
            {
                ["cat"] = new(
                    new AnimationOptions { Mode = FrameMode.Random, RandomMinMs = 50, RandomMaxMs = 900, KeysPerFrame = 3, IdleReturnMs = 1500, FixedIntervalMs = 333 },
                    new[]
                    {
                        new KeyRule(new[] { "Enter", "Ctrl+S" }, HoldMs: 500, ResetIndex: false),
                        new KeyRule("*", HoldMs: 0, ResetIndex: true),
                        new KeyRule("Space", HoldMs: 300, ResetIndex: true, FrameIndex: 1),
                    }),
                ["dog"] = new(null, null),
            },
        };

        store.Save(original);
        var loaded = new SettingsStore(FilePath).Load();

        Assert.True(store.Exists);
        Assert.Equal(original.IsTopmost, loaded.IsTopmost);
        Assert.Equal(original.CountAutoRepeat, loaded.CountAutoRepeat);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
        Assert.Equal(original.Window, loaded.Window);
        Assert.True(AppSettings.FrameSetsEqual(original.FrameSets, loaded.FrameSets));
        Assert.Equal(new[] { "c.png", "a.png" }, loaded.FrameSets[0].Frames);
        Assert.Equal(new[] { "c.png" }, loaded.FrameSets[0].AnimationFrames);
        Assert.Equal("a.png", loaded.FrameSets[0].IdleFrame);
        Assert.Null(loaded.FrameSets[1].Frames);
        Assert.Null(loaded.FrameSets[1].AnimationFrames);
        Assert.Null(loaded.FrameSets[1].IdleFrame);
        Assert.Equal("cat", loaded.DefaultFrameSet);
        Assert.True(AppSettings.ProfilesEqual(original.SetProfiles, loaded.SetProfiles));
        Assert.Equal(original.EffectiveAnimation, loaded.EffectiveAnimation);   // 사용 중인 세트 cat의 프로필
        Assert.Equal(1, loaded.EffectiveRules[2].FrameIndex);
        Assert.Null(loaded.EffectiveRules[0].FrameIndex);
        Assert.Null(loaded.ProfileOf("dog")!.Animation);
        Assert.Null(loaded.ProfileOf("dog")!.Rules);
        Assert.Equal(AppSettings.CurrentVersion, loaded.Version);
    }

    [Fact]
    public void Load_NegativeFrameIndex_BecomesNull_AndBlankFrameEntriesAreDropped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            {
              "version": 2,
              "defaultFrameSet": "cat",
              "frameSets": [ { "name": "cat", "folder": "C:\\c", "frames": [ " a.png ", "", "b.png" ] } ],
              "setProfiles": { "cat": { "rules": [ { "keys": ["A"], "frameIndex": -1 } ] } }
            }
            """);

        var s = new SettingsStore(FilePath).Load();

        Assert.Equal(new[] { "a.png", "b.png" }, s.FrameSets[0].Frames);
        Assert.Null(s.EffectiveRules[0].FrameIndex);
    }

    [Fact]
    public void Save_KeepsBackupOfPreviousFile()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new AppSettings { DefaultFrameSet = "first" });

        store.Save(new AppSettings { DefaultFrameSet = "second" });

        Assert.True(File.Exists(store.BackupPath));
        Assert.Contains("first", File.ReadAllText(store.BackupPath));
        Assert.Contains("second", File.ReadAllText(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultsAndQuarantinesFile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json");
        var store = new SettingsStore(FilePath);

        var settings = store.Load();

        Assert.Equal(AppSettings.BuiltInDefaultSet, settings.DefaultFrameSet);
        Assert.NotNull(store.LastLoadError);
        Assert.Contains(Directory.GetFiles(_dir), f => f.Contains(".corrupt-"));
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "");
        var store = new SettingsStore(FilePath);

        var settings = store.Load();

        Assert.NotNull(store.LastLoadError);
        Assert.True(settings.IsTopmost);
    }

    [Fact]
    public void Load_IgnoresUnknownProperties_AndAcceptsComments()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            {
              // 주석
              "version": 2,
              "isTopmost": false,
              "someFutureField": { "x": 1 },
              "setProfiles": { "예시": { "animation": { "mode": "Keystroke", "keysPerFrame": 2, }, }, },
            }
            """);
        var store = new SettingsStore(FilePath);

        var settings = store.Load();

        Assert.Null(store.LastLoadError);
        Assert.False(settings.IsTopmost);
        Assert.Equal(FrameMode.Keystroke, settings.EffectiveAnimation.Mode);
        Assert.Equal(2, settings.EffectiveAnimation.KeysPerFrame);
    }

    [Fact]
    public void Load_NormalizesOutOfRangeValues()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            {
              "version": 2,
              "window": { "scale": 99, "opacity": -1 },
              "defaultFrameSet": "   ",
              "frameSets": [ { "name": "", "folder": "C:\\x" }, { "name": "ok", "folder": " C:\\y " } ],
              "setProfiles": {
                " 예시 ": {
                  "animation": { "fixedIntervalMs": 1, "randomMinMs": 800, "randomMaxMs": 100, "keysPerFrame": 0 },
                  "rules": [ { "keys": [ " Enter ", "" ], "holdMs": -10 }, null ]
                },
                "  ": { "rules": [] }
              }
            }
            """);
        var store = new SettingsStore(FilePath);

        var s = store.Load();

        Assert.Equal(WindowSettings.MaxScale, s.Window.Scale);
        Assert.Equal(WindowSettings.MinOpacity, s.Window.Opacity);
        Assert.Equal(AppSettings.BuiltInDefaultSet, s.DefaultFrameSet);
        Assert.Equal(AnimationOptions.MinIntervalMs, s.EffectiveAnimation.FixedIntervalMs);
        Assert.Equal(100, s.EffectiveAnimation.RandomMinMs);
        Assert.Equal(800, s.EffectiveAnimation.RandomMaxMs);
        Assert.Equal(1, s.EffectiveAnimation.KeysPerFrame);
        Assert.Single(s.FrameSets);
        Assert.Equal(new FrameSetSettings("ok", @"C:\y"), s.FrameSets[0]);
        Assert.Single(s.SetProfiles);
        Assert.Single(s.EffectiveRules);
        Assert.Equal(new[] { "Enter" }, s.EffectiveRules[0].Keys);
        Assert.Equal(0, s.EffectiveRules[0].HoldMs);
    }

    [Fact]
    public void Load_MissingSections_UsesDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "version": 2 }""");
        var store = new SettingsStore(FilePath);

        var s = store.Load();

        Assert.Null(store.LastLoadError);
        Assert.NotNull(s.Window);
        Assert.NotNull(s.EffectiveAnimation);
        Assert.True(AppSettings.RulesEqual(AppSettings.ExampleRules, s.EffectiveRules));
    }

    [Fact]
    public void Load_JsonArrayRoot_IsTreatedAsCorrupt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "[1, 2]");
        var store = new SettingsStore(FilePath);

        var settings = store.Load();

        Assert.NotNull(store.LastLoadError);
        Assert.Equal(AppSettings.BuiltInDefaultSet, settings.DefaultFrameSet);
    }

    [Fact]
    public void Load_OlderVersion_IsBumpedToCurrent()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "version": 0, "isTopmost": false }""");
        var store = new SettingsStore(FilePath);

        var s = store.Load();

        Assert.Equal(AppSettings.CurrentVersion, s.Version);
        Assert.False(s.IsTopmost);
    }
}
