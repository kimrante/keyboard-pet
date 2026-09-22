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
        Assert.Equal(2, settings.Rules.Count);
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
            Animation = new AnimationOptions { Mode = FrameMode.Random, RandomMinMs = 50, RandomMaxMs = 900, KeysPerFrame = 3, IdleReturnMs = 1500, FixedIntervalMs = 333 },
            FrameSets = new[] { new FrameSetSettings("cat", @"C:\pets\cat"), new FrameSetSettings("dog", @"C:\pets\dog") },
            DefaultFrameSet = "cat",
            Rules = new[]
            {
                new KeyRule(new[] { "Enter", "Ctrl+S" }, "dog", HoldMs: 500, ResetIndex: false),
                new KeyRule("*", "cat", HoldMs: 0, ResetIndex: true),
            },
        };

        store.Save(original);
        var loaded = new SettingsStore(FilePath).Load();

        Assert.True(store.Exists);
        Assert.Equal(original.IsTopmost, loaded.IsTopmost);
        Assert.Equal(original.CountAutoRepeat, loaded.CountAutoRepeat);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
        Assert.Equal(original.Window, loaded.Window);
        Assert.Equal(original.Animation, loaded.Animation);
        Assert.True(AppSettings.FrameSetsEqual(original.FrameSets, loaded.FrameSets));
        Assert.Equal("cat", loaded.DefaultFrameSet);
        Assert.True(AppSettings.RulesEqual(original.Rules, loaded.Rules));
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
              "version": 1,
              "isTopmost": false,
              "someFutureField": { "x": 1 },
              "animation": { "mode": "Keystroke", "keysPerFrame": 2, },
            }
            """);
        var store = new SettingsStore(FilePath);

        var settings = store.Load();

        Assert.Null(store.LastLoadError);
        Assert.False(settings.IsTopmost);
        Assert.Equal(FrameMode.Keystroke, settings.Animation.Mode);
        Assert.Equal(2, settings.Animation.KeysPerFrame);
    }

    [Fact]
    public void Load_NormalizesOutOfRangeValues()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            {
              "window": { "scale": 99, "opacity": -1 },
              "animation": { "fixedIntervalMs": 1, "randomMinMs": 800, "randomMaxMs": 100, "keysPerFrame": 0 },
              "defaultFrameSet": "   ",
              "frameSets": [ { "name": "", "folder": "C:\\x" }, { "name": "ok", "folder": " C:\\y " } ],
              "rules": [ { "keys": [ " Enter ", "" ], "frameSet": " jump ", "holdMs": -10 }, { "keys": ["A"], "frameSet": "" } ]
            }
            """);
        var store = new SettingsStore(FilePath);

        var s = store.Load();

        Assert.Equal(WindowSettings.MaxScale, s.Window.Scale);
        Assert.Equal(WindowSettings.MinOpacity, s.Window.Opacity);
        Assert.Equal(AnimationOptions.MinIntervalMs, s.Animation.FixedIntervalMs);
        Assert.Equal(100, s.Animation.RandomMinMs);
        Assert.Equal(800, s.Animation.RandomMaxMs);
        Assert.Equal(1, s.Animation.KeysPerFrame);
        Assert.Equal(AppSettings.BuiltInDefaultSet, s.DefaultFrameSet);
        Assert.Single(s.FrameSets);
        Assert.Equal(new FrameSetSettings("ok", @"C:\y"), s.FrameSets[0]);
        Assert.Single(s.Rules);
        Assert.Equal(new[] { "Enter" }, s.Rules[0].Keys);
        Assert.Equal("jump", s.Rules[0].FrameSet);
        Assert.Equal(0, s.Rules[0].HoldMs);
    }

    [Fact]
    public void Load_MissingSections_UsesDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "version": 1 }""");
        var store = new SettingsStore(FilePath);

        var s = store.Load();

        Assert.Null(store.LastLoadError);
        Assert.NotNull(s.Window);
        Assert.NotNull(s.Animation);
        Assert.Equal(2, s.Rules.Count);
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
