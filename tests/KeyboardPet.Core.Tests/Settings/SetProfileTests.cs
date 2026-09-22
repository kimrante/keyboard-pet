using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Rules;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.Core.Tests.Settings;

public class SetProfileTests
{
    [Fact]
    public void WithoutProfile_EffectiveValuesAreGlobal()
    {
        var s = new AppSettings { DefaultFrameSet = "cat", Animation = new AnimationOptions { Mode = FrameMode.Random } };

        Assert.Equal(FrameMode.Random, s.EffectiveAnimation.Mode);
        Assert.Same(s.Rules, s.EffectiveRules);
        Assert.Null(s.ProfileOf("cat"));
    }

    [Fact]
    public void WithEffectiveAnimation_CreatesProfileForDefaultSet_FromCurrentEffectiveValues()
    {
        var s = new AppSettings { DefaultFrameSet = "cat", Animation = new AnimationOptions { Mode = FrameMode.Fixed, FixedIntervalMs = 333 } };

        var next = s.WithEffectiveAnimation(s.EffectiveAnimation with { Mode = FrameMode.Adaptive });

        Assert.Equal(FrameMode.Adaptive, next.EffectiveAnimation.Mode);
        Assert.Equal(333, next.EffectiveAnimation.FixedIntervalMs);   // 나머지 값은 유지
        Assert.Equal(FrameMode.Fixed, next.Animation.Mode);             // 공통 값은 그대로
        Assert.NotNull(next.ProfileOf("cat"));
        Assert.True(AppSettings.RulesEqual(s.Rules, next.ProfileOf("cat")!.Rules!)); // 규칙은 현재 유효값 복사
    }

    [Fact]
    public void WithEffectiveRules_WritesOnlyToDefaultSetProfile()
    {
        var s = new AppSettings { DefaultFrameSet = "cat" };
        var rules = new[] { new KeyRule("Space", "cat", HoldMs: 100) };

        var next = s.WithEffectiveRules(rules);

        Assert.True(AppSettings.RulesEqual(rules, next.EffectiveRules));
        Assert.True(AppSettings.RulesEqual(AppSettings.DefaultRules, next.Rules));
        Assert.True(AppSettings.RulesEqual(AppSettings.DefaultRules, (next with { DefaultFrameSet = "dog" }).EffectiveRules));
    }

    [Fact]
    public void WithDefaultFrameSet_CopiesCurrentSettingsToSetWithoutProfile()
    {
        var s = new AppSettings { DefaultFrameSet = "cat" }
            .WithEffectiveAnimation(new AnimationOptions { Mode = FrameMode.Random, RandomMinMs = 50, RandomMaxMs = 80 });

        var next = s.WithDefaultFrameSet("dog");

        Assert.Equal("dog", next.DefaultFrameSet);
        Assert.Equal(FrameMode.Random, next.EffectiveAnimation.Mode);
        Assert.NotNull(next.ProfileOf("dog"));
        Assert.NotNull(next.ProfileOf("cat"));
    }

    [Fact]
    public void WithDefaultFrameSet_KeepsExistingProfile()
    {
        var s = new AppSettings { DefaultFrameSet = "cat" }
            .WithEffectiveAnimation(new AnimationOptions { Mode = FrameMode.Random })
            .WithDefaultFrameSet("dog")
            .WithEffectiveAnimation(new AnimationOptions { Mode = FrameMode.Fixed });

        var back = s.WithDefaultFrameSet("cat");

        Assert.Equal(FrameMode.Random, back.EffectiveAnimation.Mode);
        Assert.Equal(FrameMode.Fixed, back.WithDefaultFrameSet("dog").EffectiveAnimation.Mode);
    }

    [Fact]
    public void WithSetRenamed_MovesProfile_AndDefaultReference()
    {
        var s = new AppSettings { DefaultFrameSet = "cat" }
            .WithEffectiveAnimation(new AnimationOptions { Mode = FrameMode.Adaptive });

        var renamed = s.WithSetRenamed("cat", "kitty");

        Assert.Equal("kitty", renamed.DefaultFrameSet);
        Assert.Null(renamed.ProfileOf("cat"));
        Assert.Equal(FrameMode.Adaptive, renamed.ProfileOf("kitty")!.Animation!.Mode);
        Assert.Equal(FrameMode.Adaptive, renamed.EffectiveAnimation.Mode);
    }

    [Fact]
    public void ProfileLookup_IsCaseInsensitive_AfterNormalize()
    {
        var s = new AppSettings
        {
            DefaultFrameSet = "Cat",
            SetProfiles = new Dictionary<string, SetProfile> { ["cat"] = new(new AnimationOptions { Mode = FrameMode.Random }, null) },
        }.Normalized();

        Assert.Equal(FrameMode.Random, s.EffectiveAnimation.Mode);
        Assert.Same(s.Rules, s.EffectiveRules);   // 프로필의 Rules가 null이면 공통 규칙
    }

    [Fact]
    public void ProfilesEqual_ComparesByValue()
    {
        var a = new AppSettings().WithEffectiveAnimation(new AnimationOptions { Mode = FrameMode.Random });
        var same = new AppSettings().WithEffectiveAnimation(new AnimationOptions { Mode = FrameMode.Random });
        var other = new AppSettings().WithEffectiveAnimation(new AnimationOptions { Mode = FrameMode.Fixed });

        Assert.True(AppSettings.ProfilesEqual(a.SetProfiles, same.SetProfiles));
        Assert.False(AppSettings.ProfilesEqual(a.SetProfiles, other.SetProfiles));
        Assert.False(AppSettings.ProfilesEqual(a.SetProfiles, new AppSettings().SetProfiles));
    }
}
