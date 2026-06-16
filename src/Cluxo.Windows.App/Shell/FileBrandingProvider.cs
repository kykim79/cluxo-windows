using Cluxo.Core;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Shell;

/// <summary>
/// <see cref="IBrandingProvider"/> — 코브랜딩 에셋 로드 + HMAC 무결성(SHELL-LAYER.md §3, 설계 T3).
///
/// <c>{appDir}\branding\config.json</c> + <c>config.hmac</c>(hex 태그)를 <see cref="BrandingLoader"/>로
/// 검증·파싱. 미서명·변조·형식오류·파일없음 → 순정 <see cref="BrandingConfig.Default"/>.
/// 시작 시 1회 로드(<see cref="Current"/>). 로고 이미지는 렌더 계층이 별도 로드한다(여기선 config만).
/// </summary>
public sealed class FileBrandingProvider : IBrandingProvider
{
    // 서명 빌드에 embed되는 비밀 키 — 변조 *탐지*가 목적(완벽한 추출 방지는 불가).
    // TODO(배포): 서명 파이프라인에서 실제 키로 교체 + 최소 난독/분할(SHELL-LAYER.md §9).
    private static readonly byte[] EmbeddedSigningKey =
        "CLUXO-DEV-PLACEHOLDER-BRANDING-KEY-REPLACE-AT-SIGNING"u8.ToArray();

    private readonly byte[] _key;
    private readonly string _brandingDir;

    public BrandingConfig Current { get; }

    public FileBrandingProvider(string? brandingDir = null, byte[]? signingKey = null)
    {
        _brandingDir = brandingDir ?? Path.Combine(AppContext.BaseDirectory, "branding");
        _key = signingKey ?? EmbeddedSigningKey;
        Current = Load();
    }

    private BrandingConfig Load()
    {
        var contentPath = Path.Combine(_brandingDir, "config.json");
        var tagPath = Path.Combine(_brandingDir, "config.hmac");
        try
        {
            if (!File.Exists(contentPath) || !File.Exists(tagPath))
                return BrandingConfig.Default;
            return BrandingLoader.Load(
                File.ReadAllBytes(contentPath), _key, File.ReadAllText(tagPath).Trim());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return BrandingConfig.Default;
        }
    }
}
