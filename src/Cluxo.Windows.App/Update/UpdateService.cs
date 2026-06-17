using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Cluxo.Core;

namespace Cluxo.Windows.App.Update;

/// <summary>
/// 업데이트 확인/적용 (직접 배포·비공개 소스용 매니페스트 방식). 소스 노출 없이 작은 JSON만 공개:
/// <code>{ "version": "1.0.2", "url": "https://.../Cluxo-Setup-1.0.2.exe", "notes": "..." }</code>
/// 확인 = 매니페스트 GET → 버전 비교(<see cref="UpdateCheck"/>). 적용 = 설치본 다운로드 후 실행(권한 상승).
/// NuGet 불필요(HttpClient + System.Text.Json 내장), GitHub 토큰 불필요.
/// </summary>
internal static class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    /// <summary>실행 중 앱 버전 "Major.Minor.Build" (csproj &lt;Version&gt;).</summary>
    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>매니페스트 URL에서 최신 버전을 받아 비교.</summary>
    public static async Task<UpdateResult> CheckAsync(string? manifestUrl, CancellationToken ct = default)
    {
        string cur = CurrentVersion;
        if (string.IsNullOrWhiteSpace(manifestUrl))
            return new UpdateResult(UpdateStatus.Error, cur, Error: "업데이트 URL이 설정되지 않았습니다.");
        try
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
        catch (Exception ex)
        {
            return new UpdateResult(UpdateStatus.Error, cur, Error: ex.Message);
        }
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
