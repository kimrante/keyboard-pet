namespace KeyboardPet.Core.Animation;

/// <summary>
/// 이미지 세트 메타데이터. 실제 비트맵은 App 계층(ImageCache)이 보관하고,
/// Core는 프레임 개수만 알면 인덱스를 순환시킬 수 있다.
/// GIF처럼 파일 하나가 여러 프레임을 갖는 경우 FrameCount는 SourceFiles 수보다 클 수 있다.
/// </summary>
public sealed record FrameSet(string Name, int FrameCount, IReadOnlyList<string>? SourceFiles = null)
{
    public static readonly FrameSet Empty = new("(none)", 0, Array.Empty<string>());

    public bool IsEmpty => FrameCount <= 0;
}
