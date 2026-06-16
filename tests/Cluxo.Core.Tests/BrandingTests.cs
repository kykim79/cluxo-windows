using System.Text;
using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// BrandingConfig / BrandingIntegrity / BrandingLoader — 코브랜딩 런타임 주입 + 무결성(설계 T3/T8).
// 보안 불변식: 변조/미서명/형식오류 → 절대 커스텀 브랜딩 안 띄움, 순정 Default fallback.
public class BrandingTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("embedded-signing-secret-v1");
    private const string Json =
        "{\"companyName\":\"Acme Corp\",\"aboutText\":\"Acme가 드립니다\"," +
        "\"logoAssetName\":\"acme-logo.png\",\"accentColor\":{\"R\":12,\"G\":34,\"B\":56,\"A\":255}}";

    private static byte[] Content => Encoding.UTF8.GetBytes(Json);

    // MARK: Integrity

    [Fact]
    public void ComputeTag_Deterministic()
    {
        Assert.Equal(BrandingIntegrity.ComputeTag(Content, Key),
                     BrandingIntegrity.ComputeTag(Content, Key));
    }

    [Fact]
    public void Verify_CorrectTag_True()
    {
        var tag = BrandingIntegrity.ComputeTag(Content, Key);
        Assert.True(BrandingIntegrity.Verify(Content, Key, tag));
    }

    [Fact]
    public void Verify_WrongKey_False()
    {
        var tag = BrandingIntegrity.ComputeTag(Content, Key);
        var wrongKey = Encoding.UTF8.GetBytes("attacker-key");
        Assert.False(BrandingIntegrity.Verify(Content, wrongKey, tag));
    }

    [Fact]
    public void Verify_TamperedContent_False()
    {
        var tag = BrandingIntegrity.ComputeTag(Content, Key);
        var tampered = Encoding.UTF8.GetBytes(Json.Replace("Acme Corp", "Evil Corp"));
        Assert.False(BrandingIntegrity.Verify(tampered, Key, tag));
    }

    [Fact]
    public void Verify_MalformedHex_False()
        => Assert.False(BrandingIntegrity.Verify(Content, Key, "not-hex!!"));

    [Fact]
    public void Verify_EmptyTag_False()
    {
        Assert.False(BrandingIntegrity.Verify(Content, Key, ""));
        Assert.False(BrandingIntegrity.Verify(Content, Key, null));
    }

    // MARK: Loader — 보안 fallback

    [Fact]
    public void Load_ValidSignedConfig_ReturnsCustom()
    {
        var tag = BrandingIntegrity.ComputeTag(Content, Key);
        var cfg = BrandingLoader.Load(Content, Key, tag);
        Assert.Equal("Acme Corp", cfg.CompanyName);
        Assert.Equal("Acme가 드립니다", cfg.AboutText);
        Assert.Equal("acme-logo.png", cfg.LogoAssetName);
        Assert.Equal(new Rgba(12, 34, 56, 255), cfg.AccentColor);
    }

    [Fact]
    public void Load_TamperedContent_ReturnsDefault()
    {
        // 공격자가 내용을 바꿔도 태그가 안 맞음 → 순정 (서명 보증 구멍 차단)
        var tag = BrandingIntegrity.ComputeTag(Content, Key);
        var tampered = Encoding.UTF8.GetBytes(Json.Replace("Acme Corp", "Evil Corp"));
        var cfg = BrandingLoader.Load(tampered, Key, tag);
        Assert.Equal(BrandingConfig.Default, cfg);
    }

    [Fact]
    public void Load_MissingTag_ReturnsDefault()
        => Assert.Equal(BrandingConfig.Default, BrandingLoader.Load(Content, Key, null));

    [Fact]
    public void Load_WrongKey_ReturnsDefault()
    {
        var tag = BrandingIntegrity.ComputeTag(Content, Key);
        var wrongKey = Encoding.UTF8.GetBytes("attacker-key");
        Assert.Equal(BrandingConfig.Default, BrandingLoader.Load(Content, wrongKey, tag));
    }

    [Fact]
    public void Load_MalformedJson_WithValidTag_ReturnsDefault()
    {
        // 태그는 그 바이트에 유효하지만 JSON 파싱 실패 → 그래도 순정
        var bad = Encoding.UTF8.GetBytes("{ broken json ");
        var tag = BrandingIntegrity.ComputeTag(bad, Key);
        Assert.Equal(BrandingConfig.Default, BrandingLoader.Load(bad, Key, tag));
    }
}
