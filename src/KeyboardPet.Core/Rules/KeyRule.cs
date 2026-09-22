using System.Text.Json.Serialization;

namespace KeyboardPet.Core.Rules;

/// <summary>
/// 키 → 이미지 세트 매핑 규칙. 설정 파일에 그대로 직렬화된다.
/// </summary>
/// <param name="Keys">반응할 키 스펙 문자열 목록("Enter", "Ctrl+S", "*"). 하나라도 맞으면 매칭</param>
/// <param name="FrameSet">전환할 이미지 세트 이름</param>
/// <param name="HoldMs">이 시간이 지나면 기본 세트로 복귀. 0이면 다음 규칙이 매칭될 때까지 유지</param>
/// <param name="ResetIndex">전환 시 프레임 인덱스를 0으로 초기화할지</param>
/// <param name="FrameIndex">null이면 세트 전체를 애니메이션으로 재생. 값이 있으면 그 프레임(0부터) 한 장만 정지 표시</param>
[method: JsonConstructor]
public sealed record KeyRule(
    IReadOnlyList<string> Keys,
    string FrameSet,
    int HoldMs = 0,
    bool ResetIndex = true,
    int? FrameIndex = null)
{
    /// <summary>키 하나짜리 규칙. 명명 인수(HoldMs:, ResetIndex:)를 기본 생성자와 같은 이름으로 쓸 수 있게 맞췄다.</summary>
    public KeyRule(string Key, string FrameSet, int HoldMs = 0, bool ResetIndex = true, int? FrameIndex = null)
        : this(new[] { Key }, FrameSet, HoldMs, ResetIndex, FrameIndex)
    {
    }

    [JsonIgnore]
    public bool IsSingleFrame => FrameIndex is not null;
}

/// <summary>규칙 컨트롤러가 "지금 무엇을 보여줘야 하는지"를 알리는 요청.</summary>
/// <param name="FrameSet">보여줄 세트 이름</param>
/// <param name="ResetIndex">첫 프레임부터 다시 재생할지</param>
/// <param name="FrameIndex">null이면 세트 애니메이션, 값이 있으면 그 프레임 한 장만 정지 표시</param>
public readonly record struct ActiveSetRequest(string FrameSet, bool ResetIndex, int? FrameIndex = null);
