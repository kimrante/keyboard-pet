using KeyboardPet.Core.Abstractions;

namespace KeyboardPet.Core.Rules;

/// <summary>
/// 규칙 목록을 컴파일해 두고, 키 이벤트에 대해 위에서부터 첫 번째로 매칭되는 규칙을 찾는다.
/// 해석할 수 없는 키 이름을 가진 규칙은 건너뛰고 <see cref="Errors"/>에 사유를 남긴다.
/// </summary>
public sealed class RuleMatcher
{
    private readonly List<(KeyRule Rule, KeySpec[] Specs)> _compiled = new();
    private readonly List<string> _errors = new();

    public RuleMatcher(IEnumerable<KeyRule> rules)
    {
        var index = 0;
        foreach (var rule in rules)
        {
            index++;
            var specs = new List<KeySpec>();
            foreach (var key in rule.Keys)
            {
                if (KeySpec.TryParse(key, out var spec))
                {
                    specs.Add(spec);
                }
                else
                {
                    _errors.Add($"규칙 #{index}: 키 이름 '{key}'을(를) 해석할 수 없어 무시합니다.");
                }
            }

            if (specs.Count > 0)
            {
                _compiled.Add((rule, specs.ToArray()));
            }
            else if (rule.Keys.Count == 0)
            {
                // 키가 하나도 없는 규칙만 별도 보고한다. 키가 전부 무효인 경우는 위의 키별 오류로 이미 설명된다.
                _errors.Add($"규칙 #{index}: 키가 지정되지 않아 무시합니다.");
            }
        }
    }

    public IReadOnlyList<KeyRule> Rules => _compiled.Select(c => c.Rule).ToList();

    public IReadOnlyList<string> Errors => _errors;

    public KeyRule? Match(KeyEvent e)
    {
        foreach (var (rule, specs) in _compiled)
        {
            foreach (var spec in specs)
            {
                if (spec.Matches(e))
                {
                    return rule;
                }
            }
        }

        return null;
    }
}
