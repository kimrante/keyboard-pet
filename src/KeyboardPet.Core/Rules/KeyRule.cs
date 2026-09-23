using System.Text.Json.Serialization;
using KeyboardPet.Core.Effects;

namespace KeyboardPet.Core.Rules;

/// <summary>
/// 키 → 표시 규칙. 규칙은 자신이 속한 이미지 세트의 프로필에 저장되며, 그 세트의 프레임만 다룬다.
/// 설정 파일에 그대로 직렬화된다.
/// </summary>
/// <param name="Keys">반응할 키 스펙 문자열 목록("Enter", "Ctrl+S", "*"). 하나라도 맞으면 매칭</param>
/// <param name="HoldMs">이 시간이 지나면 루프 애니메이션으로 복귀. 0이면 다음 규칙이 매칭될 때까지 유지</param>
/// <param name="ResetIndex">애니메이션을 첫 프레임부터 다시 재생할지(FrameIndex가 null일 때 의미 있음)</param>
/// <param name="FrameIndex">null이면 세트의 루프 애니메이션을 재생. 값이 있으면 그 프레임(0부터) 한 장만 정지 표시</param>
/// <param name="Effects">키를 누른 순간부터 규칙이 유지되는 동안 재생할 움직임 효과(합성됨). null이면 없음</param>
[method: JsonConstructor]
public sealed record KeyRule(
    IReadOnlyList<string> Keys,
    int HoldMs = 0,
    bool ResetIndex = true,
    int? FrameIndex = null,
    IReadOnlyList<FrameEffect>? Effects = null)
{
    /// <summary>키 하나짜리 규칙. 명명 인수(HoldMs:, ResetIndex:)를 기본 생성자와 같은 이름으로 쓸 수 있게 맞췄다.</summary>
    public KeyRule(string Key, int HoldMs = 0, bool ResetIndex = true, int? FrameIndex = null, IReadOnlyList<FrameEffect>? Effects = null)
        : this(new[] { Key }, HoldMs, ResetIndex, FrameIndex, Effects)
    {
    }

    [JsonIgnore]
    public bool HasEffects => Effects is { Count: > 0 };

    [JsonIgnore]
    public bool IsSingleFrame => FrameIndex is not null;
}

/// <summary>규칙 컨트롤러가 "지금 무엇을 보여줘야 하는지"를 알리는 요청. 대상은 항상 현재 세트다.</summary>
/// <param name="ResetIndex">첫 프레임부터 다시 재생할지</param>
/// <param name="FrameIndex">null이면 루프 애니메이션, 값이 있으면 그 프레임 한 장만 정지 표시</param>
public readonly record struct DisplayRequest(bool ResetIndex, int? FrameIndex = null);
