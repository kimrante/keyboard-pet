namespace KeyboardPet.Core.Animation;

/// <summary>
/// 이미지 세트 메타데이터. 실제 비트맵은 App 계층(ImageCache)이 보관하고,
/// Core는 프레임 개수와 루프 순서만 알면 인덱스를 순환시킬 수 있다.
/// GIF처럼 파일 하나가 여러 프레임을 갖는 경우 FrameCount는 SourceFiles 수보다 클 수 있다.
/// </summary>
/// <param name="LoopFrames">
/// 루프 애니메이션에 참여하는 프레임 인덱스 목록(순서대로). null이면 0..FrameCount-1 전체.
/// 여기에 없는 프레임은 키 매핑 규칙의 단일 프레임 표시로만 쓰인다.
/// </param>
public sealed record FrameSet(
    string Name,
    int FrameCount,
    IReadOnlyList<string>? SourceFiles = null,
    IReadOnlyList<int>? LoopFrames = null)
{
    public static readonly FrameSet Empty = new("(none)", 0, Array.Empty<string>());

    public bool IsEmpty => FrameCount <= 0;

    /// <summary>루프에 참여하는 프레임 수.</summary>
    public int LoopCount => LoopFrames?.Count ?? FrameCount;

    /// <summary>루프의 첫 프레임 인덱스(세트 전환·복귀 시 보여줄 프레임).</summary>
    public int FirstLoopIndex => LoopFrames is { Count: > 0 } loop ? loop[0] : 0;

    public bool IsInLoop(int index) =>
        LoopFrames is null ? index >= 0 && index < FrameCount : LoopFrames.Contains(index);

    /// <summary>
    /// 현재 인덱스 다음에 보여줄 루프 프레임. 현재가 루프 밖(고정 프레임에서 복귀 등)이면 루프의 첫 프레임.
    /// 루프에 프레임이 하나 이하이거나 없으면 현재 값을 그대로 돌려준다(전진 없음).
    /// </summary>
    public int NextLoopIndex(int current)
    {
        if (LoopFrames is null)
        {
            if (FrameCount <= 1)
            {
                return current;
            }

            return current < 0 || current >= FrameCount ? 0 : (current + 1) % FrameCount;
        }

        if (LoopFrames.Count == 0)
        {
            return current;
        }

        var position = IndexOfLoop(current);
        if (position < 0)
        {
            return LoopFrames[0];
        }

        return LoopFrames[(position + 1) % LoopFrames.Count];
    }

    private int IndexOfLoop(int frameIndex)
    {
        for (var i = 0; i < LoopFrames!.Count; i++)
        {
            if (LoopFrames[i] == frameIndex)
            {
                return i;
            }
        }

        return -1;
    }
}
