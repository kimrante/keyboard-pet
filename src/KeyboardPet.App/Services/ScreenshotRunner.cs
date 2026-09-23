using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeyboardPet.App.Windows;
using KeyboardPet.Core.Abstractions;
using KeyboardPet.Core.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace KeyboardPet.App.Services;

/// <summary>
/// --screenshot &lt;폴더&gt; 실행 모드: 실행 중인 앱의 펫 창과 설정 창을 PNG로 저장하고 종료한다.
/// 문서·리뷰용 스크린샷을 사람 손 없이(CI의 Windows 러너 등) 만들기 위한 것이다.
/// 효과 시계를 수동으로 돌려(<see cref="EffectClock.Override"/>) 정해진 순간들을 찍는다.
/// 폴더에 ready.flag를 남긴 뒤 done.flag가 생기거나 20초가 지나면 종료한다(그 사이 바깥에서 화면 전체를 찍을 수 있다).
/// </summary>
public static class ScreenshotRunner
{
    private static readonly int[] StripTimesMs = { 0, 100, 200, 300, 400, 500, 600, 700, 800 };

    public static async Task RunAsync(IServiceProvider services, string directory)
    {
        Directory.CreateDirectory(directory);
        DiagnosticsLog.Trace($"스크린샷 모드: {directory}");

        var pet = services.GetRequiredService<PetWindow>();
        var effects = services.GetRequiredService<EffectService>();
        var clock = services.GetRequiredService<EffectClock>();
        var animation = services.GetRequiredService<AnimationService>();

        await Settle();

        // 1) 평소 상태: 세트 효과(모든 프레임·특정 프레임)가 시간에 따라 움직이는 모습
        SaveStrip(Path.Combine(directory, "pet-set-effects.png"), "세트 효과 (키 입력 없음)", pet, effects, clock, baseMs: 1000);

        // 2) Enter: 규칙이 2번 프레임을 보여 주고, 규칙 효과 + 그 프레임에 걸린 세트 효과가 합성된다
        clock.Override = 5000;
        animation.OnKeyDown(KeyDown("Enter"));
        await Settle();
        SaveStrip(Path.Combine(directory, "pet-enter-rule.png"), "Enter 키 규칙 효과", pet, effects, clock, baseMs: 5000);

        // 3) Space: 다른 규칙 효과 조합
        clock.Override = 9000;
        animation.OnKeyDown(KeyDown("Space"));
        await Settle();
        SaveStrip(Path.Combine(directory, "pet-space-rule.png"), "Space 키 규칙 효과", pet, effects, clock, baseMs: 9000);

        // 4) 설정 창의 탭들
        var settingsWindow = services.GetRequiredService<SettingsWindow>();
        settingsWindow.Show();
        await Settle();
        if (settingsWindow.Content is TabControl tabs)
        {
            // 바깥의 전체 화면 캡처에서 효과 탭이 보이도록 마지막에 찍는다.
            var targets = new[] { ("이미지 세트", "settings-sets.png"), ("애니메이션", "settings-animation.png"), ("키 매핑", "settings-keymap.png"), ("효과", "settings-effects.png") };
            foreach (var (header, file) in targets)
            {
                var tab = tabs.Items.OfType<TabItem>().FirstOrDefault(t => Equals(t.Header, header));
                if (tab is null)
                {
                    continue;
                }

                tabs.SelectedItem = tab;
                await Settle();
                SavePng(Render(settingsWindow, Brushes.White), Path.Combine(directory, file));
            }
        }

        // 펫이 움직이는 상태로 바깥 캡처를 기다린다.
        clock.Override = null;
        File.WriteAllText(Path.Combine(directory, "ready.flag"), DateTime.Now.ToString("O"));
        var done = Path.Combine(directory, "done.flag");
        for (var i = 0; i < 200 && !File.Exists(done); i++)
        {
            await Task.Delay(100);
        }

        DiagnosticsLog.Trace("스크린샷 모드 완료");
        Application.Current.Shutdown();
    }

    /// <summary>데모 설정(scripts/screenshot-settings.json)의 규칙과 같은 키 이름으로 키 다운 이벤트를 만든다.</summary>
    private static KeyEvent KeyDown(string keyName)
    {
        if (!KeyNames.TryGetVirtualKey(keyName, out var vk))
        {
            throw new ArgumentException($"알 수 없는 키 이름: {keyName}", nameof(keyName));
        }

        return new KeyEvent(vk, true, KeyModifiers.None, false);
    }

    /// <summary>레이아웃과 렌더링이 한 바퀴 돌 때까지 기다린다.</summary>
    private static async Task Settle()
    {
        await Task.Delay(400);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>효과 시계를 여러 순간으로 옮겨 가며 펫 창을 찍어 가로로 이어 붙인다.</summary>
    private static void SaveStrip(string path, string title, PetWindow pet, EffectService effects, EffectClock clock, double baseMs)
    {
        var shots = new List<(int Ms, BitmapSource Image)>();
        foreach (var ms in StripTimesMs)
        {
            clock.Override = baseMs + ms;
            effects.Tick();
            pet.UpdateLayout();
            shots.Add((ms, Render(pet, null)));
        }

        const int pad = 12, header = 34, label = 22;
        var cellW = shots.Max(s => s.Image.PixelWidth);
        var cellH = shots.Max(s => s.Image.PixelHeight);
        var width = pad + shots.Count * (cellW + pad);
        var height = header + cellH + label + pad;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.DrawText(Text(title, 16, Brushes.Black), new Point(pad, 8));
            for (var i = 0; i < shots.Count; i++)
            {
                var x = pad + i * (cellW + pad);
                var cell = new Rect(x, header, cellW, cellH);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE9, 0xEC, 0xEF)), new Pen(Brushes.LightGray, 1), cell);
                dc.DrawImage(shots[i].Image, new Rect(x, header, shots[i].Image.PixelWidth, shots[i].Image.PixelHeight));
                dc.DrawText(Text($"{shots[i].Ms} ms", 12, Brushes.DimGray), new Point(x + 4, header + cellH + 3));
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        SavePng(bitmap, path);
    }

    private static FormattedText Text(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Malgun Gothic"), size, brush, 1.0);

    /// <summary>창의 내용을 96 DPI 비트맵으로 그린다. background가 있으면 먼저 칠한다.</summary>
    private static BitmapSource Render(Window window, Brush? background)
    {
        var element = (FrameworkElement)window.Content;
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth + element.Margin.Left + element.Margin.Right));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight + element.Margin.Top + element.Margin.Bottom));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (background is not null)
            {
                dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            }

            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(element.Margin.Left, element.Margin.Top, element.ActualWidth, element.ActualHeight));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        DiagnosticsLog.Trace($"스크린샷 저장: {path} ({bitmap.PixelWidth}x{bitmap.PixelHeight})");
    }
}
