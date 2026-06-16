using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cluxo.Core;

/// <summary>
/// JSON 기반 설정 저장소 — Swift <c>@Persisted</c>/UserDefaults 패턴의 플랫폼 무관 이식.
///
/// Windows에선 %APPDATA%/Cluxo/settings.json으로 직렬화한다. 파일 IO·디바운스(쓰기 코얼레싱)는
/// 플랫폼 계층 책임이고, 여기서는 Persisted가 보장하던 의미론만 담는다:
///   - 미저장 키 → 기본값 fallback
///   - 손상/타입 불일치(잘못된 enum 등) → 기본값 fallback
///   - 명시적 false/0도 저장됨(기본값과 구분) — JSON이라 자동
///   - write→read 라운드트립 보존(인스턴스 간 = Serialize/Load)
/// </summary>
public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }, // enum은 이름 문자열로 (잘못된 이름은 throw → fallback)
    };

    private readonly Dictionary<string, JsonElement> _values;

    public JsonSettingsStore() => _values = new();
    private JsonSettingsStore(Dictionary<string, JsonElement> values) => _values = values;

    /// <summary>JSON 문자열에서 로드(인스턴스 간 복원). 파싱 실패 시 빈 저장소.</summary>
    public static JsonSettingsStore Load(string json)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, Options);
            return new JsonSettingsStore(dict ?? new());
        }
        catch (JsonException)
        {
            return new JsonSettingsStore();
        }
    }

    /// <summary>저장된 전체를 JSON 문자열로.</summary>
    public string Serialize() => JsonSerializer.Serialize(_values, Options);

    public bool Contains(string key) => _values.ContainsKey(key);

    /// <summary>저장값 반환. 키 없거나 역직렬화 실패(타입 불일치·잘못된 enum)면 기본값.</summary>
    public T Get<T>(string key, T defaultValue)
    {
        if (!_values.TryGetValue(key, out var element)) return defaultValue;
        try
        {
            return element.Deserialize<T>(Options) ?? defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    /// <summary>값 저장. 명시적 false/0/""도 그대로 저장된다(기본값과 구분).</summary>
    public void Set<T>(string key, T value)
        => _values[key] = JsonSerializer.SerializeToElement(value, Options);
}
