using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Effects;
using KeyboardPet.Core.Rules;
using KeyboardPet.Core.Settings;
using KeyboardPet.Core.Tests.Animation;

namespace KeyboardPet.Core.Tests.Effects;

public sealed class EffectSettingsTests : IDisposable
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
    public void SetEffects_BelongToSet_AndRoundTrip()
    {
        var effects = new[]
        {
            new FrameEffect(FrameEffectKind.BobVertical, 40, 900),
            new FrameEffect(FrameEffectKind.Tilt, 70, 600, new[] { 2, 0 }),
        };
        var rules = new[] { new KeyRule("Enter", HoldMs: 500, FrameIndex: 1, Effects: new[] { new FrameEffect(FrameEffectKind.Bounce, 80, 400) }) };
        var original = new AppSettings { DefaultFrameSet = "cat" }.WithEffectiveEffects(effects).WithEffectiveRules(rules);

        var store = new SettingsStore(FilePath);
        store.Save(original);
        var loaded = store.Load();

        Assert.Null(store.LastLoadError);
        Assert.Equal(2, loaded.EffectiveEffects.Count);
        Assert.Equal(new[] { 0, 2 }, loaded.EffectiveEffects[1].Frames);          // 정규화: 정렬
        Assert.Null(loaded.EffectiveEffects[0].Frames);
        Assert.Equal(FrameEffectKind.Bounce, loaded.EffectiveRules[0].Effects![0].Kind);
        Assert.True(AppSettings.RulesEqual(original.Normalized().EffectiveRules, loaded.EffectiveRules));
        Assert.True(AppSettings.ProfilesEqual(original.Normalized().SetProfiles, loaded.SetProfiles));
        Assert.Empty((loaded with { DefaultFrameSet = "dog" }).EffectiveEffects);   // 다른 세트는 효과 없음
    }

    [Fact]
    public void RuleEffects_DropFrameSelection_AndEmptyListBecomesNull()
    {
        var s = new AppSettings().WithEffectiveRules(new[]
        {
            new KeyRule("A", Effects: new[] { new FrameEffect(FrameEffectKind.Shrink, 50, 300, new[] { 1 }) }),
            new KeyRule("B", Effects: Array.Empty<FrameEffect>()),
        }).Normalized();

        Assert.Null(s.EffectiveRules[0].Effects![0].Frames);
        Assert.Null(s.EffectiveRules[1].Effects);
    }

    [Fact]
    public void RulesEqual_And_ProfilesEqual_SeeEffectChanges()
    {
        var a = new[] { new KeyRule("A", Effects: new[] { new FrameEffect(FrameEffectKind.Blink, 50) }) };
        var b = new[] { new KeyRule("A", Effects: new[] { new FrameEffect(FrameEffectKind.Blink, 51) }) };
        Assert.False(AppSettings.RulesEqual(a, b));

        var p1 = new AppSettings().WithEffectiveEffects(new[] { new FrameEffect(FrameEffectKind.Grow) });
        var p2 = new AppSettings().WithEffectiveEffects(new[] { new FrameEffect(FrameEffectKind.Shrink) });
        Assert.False(AppSettings.ProfilesEqual(p1.SetProfiles, p2.SetProfiles));
    }

    [Fact]
    public void ScreenshotDemoSettings_LoadWithEffects()
    {
        var demo = Path.Combine(RepoRoot(), "scripts", "screenshot-settings.json");
        Directory.CreateDirectory(_dir);
        File.Copy(demo, FilePath);

        var store = new SettingsStore(FilePath);
        var s = store.Load();

        Assert.Null(store.LastLoadError);
        Assert.Equal(AppSettings.ExampleSetName, s.DefaultFrameSet);
        Assert.Equal(2, s.EffectiveEffects.Count);
        Assert.Equal(new[] { 1 }, s.EffectiveEffects[1].Frames);
        Assert.Equal(3, s.EffectiveRules[1].Effects!.Count);
    }

    [Fact]
    public void Controller_CountsActivations_AndRaisesRuleActivated()
    {
        var rule = new KeyRule("Enter", HoldMs: 0, ResetIndex: false, Effects: new[] { new FrameEffect(FrameEffectKind.Bounce) });
        var controller = new KeyRuleController(new FakeTimerFactory(), new RuleMatcher(new[] { rule }));
        var activated = new List<KeyRule>();
        controller.RuleActivated += activated.Add;

        controller.OnKeyDown(new KeyEvent(0x0D, true, KeyModifiers.None, false));
        controller.OnKeyDown(new KeyEvent(0x0D, true, KeyModifiers.None, false));

        Assert.Equal(2, controller.ActivationCount);
        Assert.Equal(2, activated.Count);   // 표시 대상이 그대로여도(재입력) 효과는 다시 시작해야 한다
        Assert.True(controller.ActiveRule!.HasEffects);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KeyboardPet.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("저장소 루트를 찾지 못했습니다.");
    }
}
