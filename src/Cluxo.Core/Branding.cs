using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cluxo.Core;

/// <summary>
/// 코브랜딩 설정 — 고객사별 런타임 주입(설계 T8). 빌드 1개 + 에셋 교체로 온보딩.
/// 비-브랜딩(순정) 기본값으로 안전 fallback한다.
/// </summary>
public sealed record BrandingConfig
{
    public string CompanyName { get; init; } = "";
    public string AboutText { get; init; } = "";
    public Rgba AccentColor { get; init; } = new(0x00, 0x7A, 0xFF);
    public string LogoAssetName { get; init; } = "";

    /// <summary>브랜딩 없음(순정) — tamper/parse 실패 시 fallback.</summary>
    public static readonly BrandingConfig Default = new()
    {
        CompanyName = "Cluxo",
        AboutText = "",
        AccentColor = new(0x00, 0x7A, 0xFF),
        LogoAssetName = "",
    };
}

/// <summary>
/// 브랜딩 에셋 무결성 검증 — 외부보이스 P1(설계 T3) 대응.
/// 서명된 단일 바이너리가 검증 없는 임의 브랜딩을 우리 서명으로 "보증"하는 화이트라벨 신뢰 구멍을 막는다.
/// 서명 빌드에 embed된 비밀 key로 HMAC-SHA256 태그를 비교.
/// </summary>
public static class BrandingIntegrity
{
    /// <summary>content의 HMAC-SHA256 태그(대문자 hex).</summary>
    public static string ComputeTag(byte[] content, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(content));
    }

    /// <summary>상수시간 비교로 검증(타이밍 공격 방지). 태그가 비었거나 형식 오류면 false.</summary>
    public static bool Verify(byte[] content, byte[] key, string? expectedTagHex)
    {
        if (string.IsNullOrEmpty(expectedTagHex)) return false;
        byte[] expected;
        try { expected = Convert.FromHexString(expectedTagHex); }
        catch (FormatException) { return false; }

        using var hmac = new HMACSHA256(key);
        byte[] actual = hmac.ComputeHash(content);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>
/// 코브랜딩 로더 — 무결성 검증 통과 시에만 커스텀 브랜딩, 어떤 실패(미서명·변조·파싱오류)든 순정 Default.
/// (설계 T3/T8)
/// </summary>
public static class BrandingLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>브랜딩 JSON 바이트 + 무결성 태그를 검증·파싱. 실패 시 BrandingConfig.Default.</summary>
    public static BrandingConfig Load(byte[] contentBytes, byte[] key, string? expectedTagHex)
    {
        // 1) 무결성 — 변조/미서명이면 우리 서명으로 임의 브랜딩 보증하지 않음
        if (!BrandingIntegrity.Verify(contentBytes, key, expectedTagHex))
            return BrandingConfig.Default;

        // 2) 파싱 — 검증 통과해도 형식 오류면 안전 fallback
        try
        {
            var cfg = JsonSerializer.Deserialize<BrandingConfig>(
                Encoding.UTF8.GetString(contentBytes), Options);
            return cfg ?? BrandingConfig.Default;
        }
        catch (JsonException)
        {
            return BrandingConfig.Default;
        }
    }
}
