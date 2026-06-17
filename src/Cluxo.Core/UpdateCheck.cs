namespace Cluxo.Core;

/// <summary>업데이트 확인 결과 상태. (맥 PreferencesView.checkForUpdate 대응)</summary>
public enum UpdateStatus
{
    UpToDate,        // 현재 = 최신
    UpdateAvailable, // 현재 &lt; 최신 → 업데이트 가능
    LocalAhead,      // 현재 &gt; 최신 → 개발 빌드
    Error,           // 확인 실패(네트워크·파싱·URL 미설정)
}

/// <summary>업데이트 확인 결과. DownloadUrl/Notes는 매니페스트에서 온다.</summary>
public readonly record struct UpdateResult(
    UpdateStatus Status,
    string CurrentVersion,
    string? LatestVersion = null,
    string? DownloadUrl = null,
    string? Notes = null,
    string? Error = null);

/// <summary>
/// 버전 비교 — 순수 로직(HTTP/IO 없음, 테스트 가능). 앱 계층 UpdateService가 매니페스트를 받아 이걸 쓴다.
/// 맥은 GitHub releases tag를 .numeric 비교 — 여기선 dot 구분 숫자 비교로 동일 동작.
/// </summary>
public static class UpdateCheck
{
    /// <summary>현재 vs 최신 버전 비교. "v" 접두사 허용. current &lt; latest면 UpdateAvailable.</summary>
    public static UpdateStatus Compare(string current, string latest)
    {
        if (string.IsNullOrWhiteSpace(latest)) return UpdateStatus.Error;
        int c = CompareNumeric(Normalize(current), Normalize(latest));
        return c < 0 ? UpdateStatus.UpdateAvailable
             : c > 0 ? UpdateStatus.LocalAhead
             : UpdateStatus.UpToDate;
    }

    private static string Normalize(string v) => v.Trim().TrimStart('v', 'V').Trim();

    // dot 구분 각 구획을 숫자로 비교(1.10 > 1.9). 숫자 아닌 구획은 0 취급.
    private static int CompareNumeric(string a, string b)
    {
        string[] pa = a.Split('.'), pb = b.Split('.');
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int x = i < pa.Length && int.TryParse(pa[i], out var xv) ? xv : 0;
            int y = i < pb.Length && int.TryParse(pb[i], out var yv) ? yv : 0;
            if (x != y) return x < y ? -1 : 1;
        }
        return 0;
    }
}
