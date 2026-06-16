using System.Text;
using Cluxo.Core;
using Cluxo.Windows.App.Shell;

namespace Cluxo.Windows.App.Tests;

// 코브랜딩 로드 + HMAC 무결성 (SHELL-LAYER.md §3). BrandingLoader 자체는 Core에서 테스트됨 —
// 여기선 파일 배치/검증 결선만.
public sealed class FileBrandingProviderTests : IDisposable
{
    private readonly string _dir;
    private readonly byte[] _key = "test-key"u8.ToArray();

    public FileBrandingProviderTests()
        => _dir = Path.Combine(Path.GetTempPath(), "cluxo-brand-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void WriteBranding(string json, string tagHex)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), json);
        File.WriteAllText(Path.Combine(_dir, "config.hmac"), tagHex);
    }

    [Fact]
    public void NoBrandingDir_ReturnsDefault()
    {
        var p = new FileBrandingProvider(_dir, _key);
        Assert.Equal(BrandingConfig.Default, p.Current);
    }

    [Fact]
    public void ValidSignedBranding_Loads()
    {
        var json = """{"CompanyName":"Acme Corp","AboutText":"hi","LogoAssetName":"acme.png"}""";
        var content = Encoding.UTF8.GetBytes(json);
        var tag = BrandingIntegrity.ComputeTag(content, _key);
        WriteBranding(json, tag);

        var p = new FileBrandingProvider(_dir, _key);
        Assert.Equal("Acme Corp", p.Current.CompanyName);
        Assert.Equal("acme.png", p.Current.LogoAssetName);
    }

    [Fact]
    public void TamperedContent_FallsBackToDefault()
    {
        var json = """{"CompanyName":"Acme Corp"}""";
        var tag = BrandingIntegrity.ComputeTag(Encoding.UTF8.GetBytes(json), _key);
        WriteBranding("""{"CompanyName":"EVIL Corp"}""", tag); // 태그는 원본 것 → 불일치

        var p = new FileBrandingProvider(_dir, _key);
        Assert.Equal(BrandingConfig.Default, p.Current); // 변조 탐지 → 순정
    }

    [Fact]
    public void MissingTagFile_ReturnsDefault()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), """{"CompanyName":"Acme"}""");
        // config.hmac 없음

        var p = new FileBrandingProvider(_dir, _key);
        Assert.Equal(BrandingConfig.Default, p.Current);
    }
}
