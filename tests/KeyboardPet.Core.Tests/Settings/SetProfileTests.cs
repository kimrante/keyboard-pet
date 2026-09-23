using KeyboardPet.Core.Animation;
using KeyboardPet.Core.Rules;
using KeyboardPet.Core.Settings;

namespace KeyboardPet.Core.Tests.Settings;

public class SetProfileTests
{
    [Fact]
    public void WithoutProfile_UsesSetDefaults()
    {
        var cat = new AppSettings { DefaultFrameSet = "cat" };
        var example = new AppSettings { DefaultFrameSet = AppSettings.ExampleSetName };

        Assert.Equal(new AnimationOptions(), cat.EffectiveAnimation);
        Assert.Empty(cat.EffectiveRules);                                       // 사용자 세트는 규칙 없이 시작
        Assert.True(AppSettings.RulesEqual(AppSettings.ExampleRules, example.EffectiveRules));
        Assert.Null(cat.ProfileOf("cat"));
    }

    [Fact]
    public void WithEffectiveAnimation_CreatesProfileForCurrentSet_WithoutTouchingRules()
    {
        var s = new AppSettings { DefaultFrameSet = "cat" };

        var next = s.WithEffectiveAnimation(s.EffectiveAnimation with { Mode = FrameMode.Fixed, FixedIntervalMs = 333 });

        Assert.Equal(FrameMode.Fixed, next.EffectiveAnimation.Mode);
        Assert.Equal(333, next.EffectiveAnimation.FixedIntervalMs);
        Assert.NotNull(next.ProfileOf("cat"));
        Assert.Null(next.ProfileOf("cat")!.Rules);                               // 규칙은 세트 기본값 유지
        Assert.Equal(new AnimationOptions(), next.AnimationOf("dog"));          // 다른 세트에는 영향 없음
    }

    [Fact]
    public void WithEffectiveRules_WritesOnlyToCurrentSet()
    {
        var s = new AppSettings { DefaultFrameSet = "cat" };
        var rules = new[] { new KeyRule("Space", HoldMs: 100, FrameIndex: 2) };

        var next = s.WithEffectiveRules(rules);

        Assert.True(AppSettings.RulesEqual(rules, next.EffectiveRules));
        Assert.Empty(next.RulesOf("dog"));
        Assert.True(AppSettings.RulesEqual(AppSettings.ExampleRules, next.RulesOf(AppSettings.ExampleSetName)));
    }

    [Fact]
    public void SwitchingSet_SwitchesAnimationAndRules_Independently()
    {
        var s = new AppSettings { DefaultFrameSet = "cat" }
            .WithEffectiveAnimation(new AnimationOptions { Mode = FrameMode.Random })
            .WithEffectiveRules(new[] { new KeyRule("A", FrameIndex: 0) });

        var dog = s with { DefaultFrameSet = "dog" };
        var back = dog.WithEffectiveAnimation(new AnimationOptions { Mode = FrameMode.Fixed }) with { DefaultFrameSet = "cat" };

        Assert.Equal(new AnimationOptions().Mode, dog.EffectiveAnimation.Mode);  // 새 세트는 자기 기본값
        Assert.Empty(dog.EffectiveRules);
        Assert.Equal(FrameMode.Random, back.EffectiveAnimation.Mode);            // 돌아오면 자기 설정
        Assert.Single(back.EffectiveRules);
        Assert.Equal(FrameMode.Fixed, back.AnimationOf("dog").Mode);
    }

    [Fact]
    public void WithSetRenamed_MovesProfile_AndCurrentReference()
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
    public void WithSetRemoved_DropsProfile_AndFallsBackToExample()
    {
        var s = new AppSettings { DefaultFrameSet = "cat" }
            .WithEffectiveRules(new[] { new KeyRule("A") });

        var removed = s.WithSetRemoved("CAT");

        Assert.Null(removed.ProfileOf("cat"));
        Assert.Equal(AppSettings.ExampleSetName, removed.DefaultFrameSet);
        Assert.Empty((removed with { DefaultFrameSet = "cat" }).EffectiveRules);   // 같은 이름으로 다시 만들어도 새로 시작
    }

    [Fact]
    public void WithSetRemoved_KeepsExampleProfile_AndOtherCurrentSet()
    {
        var s = new AppSettings().WithEffectiveRules(new[] { new KeyRule("B") }) with { DefaultFrameSet = "dog" };

        Assert.Same(s, s.WithSetRemoved(AppSettings.ExampleSetName));
        Assert.Equal("dog", s.WithSetRemoved("cat").DefaultFrameSet);
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
        Assert.Empty(s.EffectiveRules);   // 프로필의 Rules가 null이면 세트 기본 규칙
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
