using Cluxo.Core;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Shell;

/// <summary>
/// <see cref="ISettingsStore"/> — %APPDATA%\Cluxo\settings.json (SHELL-LAYER.md §2).
/// 직렬화 의미론은 Core <see cref="JsonSettingsStore"/>가, 파일 IO·디바운스는 여기서.
///
/// - **원자적 쓰기**: temp에 쓰고 File.Move(overwrite) — 디바운스 중 크래시해도 손상 없음.
/// - **디바운스**: 슬라이더 등 잦은 Save를 0.5s로 코얼레싱(설계상 IO 계층 책임).
/// - **종료 flush**: Program이 종료 시 Dispose/Flush로 대기 중 변경을 보존.
/// - Load 손상/없음 → 빈 store(Core가 기본값 fallback).
/// </summary>
public sealed class JsonFileSettingsStore : ISettingsStore, IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private JsonSettingsStore? _pending;
    private bool _disposed;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cluxo", "settings.json");

    public JsonFileSettingsStore(string? path = null, TimeSpan? debounce = null)
    {
        _path = path ?? DefaultPath;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        _timer = new Timer(_ => FlushPending(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public JsonSettingsStore Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSettingsStore.Load(File.ReadAllText(_path))
                : new JsonSettingsStore();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new JsonSettingsStore(); // 읽기 실패 → 기본값 fallback
        }
    }

    public void Save(JsonSettingsStore store)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending = store;
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan); // 디바운스 재시작(코얼레싱)
        }
    }

    /// <summary>대기 중 저장을 즉시 기록(종료 시 Program이 호출).</summary>
    public void Flush() => FlushPending();

    private void FlushPending()
    {
        JsonSettingsStore? store;
        lock (_gate)
        {
            store = _pending;
            _pending = null;
        }
        if (store is not null) WriteAtomic(store.Serialize());
    }

    private void WriteAtomic(string json)
    {
        var dir = Path.GetDirectoryName(_path)!;
        var temp = Path.Combine(dir, $".settings.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // best-effort 영구화 — 쓰기 실패해도 크래시시키지 않는다.
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _timer.Dispose();
        FlushPending(); // 남은 변경 보존
    }
}
