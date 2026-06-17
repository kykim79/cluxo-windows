using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Cluxo.Core;

namespace Cluxo.Windows.App.Update;

/// <summary>
/// 업데이트 확인/적용. 소스는 두 형식 자동 인식:
///  - <b>GitHub 레포</b> "owner/repo" → <c>releases/latest</c> API에서 tag + 설치본(.exe Setup) 자산 (맥과 동일).
///  - <b>매니페스트 URL</b> "https://.../latest.json" → <c>{ version, url, notes }</c> (소스 비공개 시).
/// 확인 = GET → 버전 비교(<see cref="UpdateCheck"/>). 적용 = 설치본 다운로드 후 실행(권한 상승).
/// NuGet 불필요. 비공개 GitHub 레포는 토큰이 필요해 받지 않으므로 — 릴리스가 공개여야 동작.
/// </summary>
internal static class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    /// <summary>실행 중 앱 버전 "Major.Minor.Build" (csproj &lt;Version&gt;).</summary>
    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>소스(GitHub owner/repo 또는 매니페스트 URL)에서 최신 버전을 받아 비교.</summary>
    public static async Task<UpdateResult> CheckAsync(string? source, CancellationToken ct = default)
    {
        string cur = CurrentVersion;
        source = source?.Trim();
        if (string.IsNullOrWhiteSpace(source))
            return new UpdateResult(UpdateStatus.Error, cur, Error: "업데이트 소스가 설정되지 않았습니다.");
        try
        {
            bool isUrl = source.Contains("://");
            // "owner/repo" 형식 → GitHub
            if (!isUrl && source.Split('/').Length == 2 && !source.Contains(' '))
                return await CheckGitHubAsync(source, cur, ct);
            if (isUrl)
                return await CheckManifestAsync(source, cur, ct);
            return new UpdateResult(UpdateStatus.Error, cur, Error: "소스 형식 오류 — owner/repo 또는 https URL.");
        }
        catch (Exception ex)
        {
            return new UpdateResult(UpdateStatus.Error, cur, Error: ex.Message);
        }
    }

    // GitHub releases/latest — tag_name + 설치본(.exe with Setup) 자산. 비공개면 404.
    private static async Task<UpdateResult> CheckGitHubAsync(string repo, string cur, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        req.Headers.Add("User-Agent", "Cluxo-Updater");
        req.Headers.Add("Accept", "application/vnd.github+json");
        using var resp = await Http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return new UpdateResult(UpdateStatus.Error, cur, Error: "릴리스를 못 찾음 (비공개 레포면 공개 필요, 또는 릴리스 없음).");
        if (!resp.IsSuccessStatusCode)
            return new UpdateResult(UpdateStatus.Error, cur, Error: $"GitHub 응답 {(int)resp.StatusCode}");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string? notes = root.TryGetProperty("name", out var nm) ? nm.GetString() : null;

        string? url = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            // 설치 프로그램(.exe + Setup) 우선, 없으면 아무 .exe.
            url = FindAsset(assets, n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && n.Contains("Setup", StringComparison.OrdinalIgnoreCase))
               ?? FindAsset(assets, n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        }
        var status = UpdateCheck.Compare(cur, tag);
        return new UpdateResult(status, cur, tag.TrimStart('v', 'V'), url, notes);
    }

    private static string? FindAsset(JsonElement assets, Func<string, bool> match)
    {
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
            if (match(name)) return a.TryGetProperty("browser_download_url", out var du) ? du.GetString() : null;
        }
        return null;
    }

    // 매니페스트 JSON: { version, url, notes }
    private static async Task<UpdateResult> CheckManifestAsync(string manifestUrl, string cur, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
        req.Headers.Add("User-Agent", "Cluxo-Updater");
        req.Headers.Add("Accept", "application/json");
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return new UpdateResult(UpdateStatus.Error, cur, Error: $"서버 응답 {(int)resp.StatusCode}");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        string latest = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
        string? url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
        string? notes = root.TryGetProperty("notes", out var n) ? n.GetString() : null;

        var status = UpdateCheck.Compare(cur, latest);
        return new UpdateResult(status, cur, latest, url, notes,
            status == UpdateStatus.Error ? "버전 형식을 읽지 못했습니다." : null);
    }

    /// <summary>설치본을 임시폴더로 받아 실행(ShellExecute → UAC). 성공 시 앱은 종료해야 설치가 파일을 교체.</summary>
    public static async Task<string?> DownloadInstallerAsync(string url, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "Cluxo-Setup-update.exe");
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            var buf = new byte[81920];
            long read = 0; int r;
            while ((r = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, r), ct);
                read += r;
                if (total > 0) progress?.Report((double)read / total);
            }
        }
        return tmp;
    }

    /// <summary>받은 설치본 실행(권한 상승). 호출 후 앱 종료를 권장.</summary>
    public static void RunInstaller(string installerPath)
        => Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });

    /// <summary>브라우저로 다운로드/릴리스 페이지 열기(수동 폴백).</summary>
    public static void OpenInBrowser(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
