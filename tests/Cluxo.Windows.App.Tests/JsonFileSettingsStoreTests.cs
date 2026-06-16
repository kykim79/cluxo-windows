using Cluxo.Core;
using Cluxo.Windows.App.Shell;

namespace Cluxo.Windows.App.Tests;

// %APPDATA% JSON 영구화 — 원자적 쓰기·디바운스·라운드트립 (SHELL-LAYER.md §2).
// 디바운스 타이밍은 Flush()로 결정적으로 검증.
public sealed class JsonFileSettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public JsonFileSettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cluxo-test-" + Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_dir, "sub", "settings.json"); // 중첩 — 디렉터리 자동 생성 검증
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyStore()
    {
        using var store = new JsonFileSettingsStore(_path);
        var loaded = store.Load();
        Assert.Equal(42, loaded.Get("missing", 42)); // 기본값 fallback
    }

    [Fact]
    public void Save_Flush_PersistsRoundTrip()
    {
        using (var store = new JsonFileSettingsStore(_path))
        {
            var s = new JsonSettingsStore();
            s.Set("drawing.lineWidth", 7.5);
            s.Set("ring.color", "Cyan");
            store.Save(s);
            store.Flush(); // 디바운스 우회 — 결정적
        }

        Assert.True(File.Exists(_path));
        using var store2 = new JsonFileSettingsStore(_path);
        var loaded = store2.Load();
        Assert.Equal(7.5, loaded.Get("drawing.lineWidth", 0.0));
        Assert.Equal("Cyan", loaded.Get("ring.color", ""));
    }

    [Fact]
    public void Save_Coalesces_OnlyLastPersisted()
    {
        using var store = new JsonFileSettingsStore(_path);
        var a = new JsonSettingsStore(); a.Set("v", 1);
        var b = new JsonSettingsStore(); b.Set("v", 2);
        store.Save(a);
        store.Save(b); // 디바운스 창 안 재호출 → a 덮어씀
        store.Flush();

        Assert.Equal(2, store.Load().Get("v", 0));
    }

    [Fact]
    public void Dispose_FlushesPending()
    {
        using (var store = new JsonFileSettingsStore(_path, TimeSpan.FromHours(1))) // 타이머는 사실상 안 울림
        {
            var s = new JsonSettingsStore(); s.Set("v", 99);
            store.Save(s);
        } // Dispose가 flush해야

        using var reopened = new JsonFileSettingsStore(_path);
        Assert.Equal(99, reopened.Load().Get("v", 0));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyStore()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not valid json ]");
        using var store = new JsonFileSettingsStore(_path);
        Assert.Equal(5, store.Load().Get("anything", 5)); // 손상 → 기본값
    }
}
