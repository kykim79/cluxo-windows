using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// JsonSettingsStore — Persisted 의미론 검증. (Swift PersistedTests 이식)
// 인스턴스 간 = Serialize → Load. 디바운스는 IO 계층 몫이라 제외.
public class JsonSettingsStoreTests
{
    private enum Mode { A, B, C }

    // MARK: 기본값 (미저장 시)

    [Fact]
    public void ReturnsDefaultWhenUnset()
    {
        var s = new JsonSettingsStore();
        Assert.False(s.Get("flag", false));
        Assert.Equal(42, s.Get("count", 42));
        Assert.Equal(1.0, s.Get("ratio", 1.0));
        Assert.Equal("default", s.Get("label", "default"));
        Assert.Equal(Mode.A, s.Get("mode", Mode.A));
    }

    // MARK: write → read 라운드트립 (인스턴스 간)

    [Fact]
    public void WriteThenReadAcrossInstances()
    {
        var s = new JsonSettingsStore();
        s.Set("flag", true);
        s.Set("count", 123);
        s.Set("ratio", 3.14);
        s.Set("label", "hello");
        s.Set("code", (ushort)99);
        string json = s.Serialize();

        var reloaded = JsonSettingsStore.Load(json);
        Assert.True(reloaded.Get("flag", false));
        Assert.Equal(123, reloaded.Get("count", 0));
        Assert.Equal(3.14, reloaded.Get("ratio", 0.0), 4);
        Assert.Equal("hello", reloaded.Get("label", ""));
        Assert.Equal((ushort)99, reloaded.Get("code", (ushort)0));
    }

    // 명시적 false도 저장 — 기본값 fallback과 구분 (Bool 특수 케이스)
    [Fact]
    public void BoolFalseIsPersistedAndDistinctFromDefault()
    {
        var s = new JsonSettingsStore();
        s.Set("flag", false);
        Assert.True(s.Contains("flag"), "명시적 false도 저장돼야 함");
        Assert.False(s.Get("flag", true)); // 기본값 true여도 저장된 false 우선
    }

    // MARK: enum

    [Fact]
    public void EnumWriteThenReadAcrossInstances()
    {
        var s = new JsonSettingsStore();
        Assert.Equal(Mode.A, s.Get("mode", Mode.A)); // 기본값
        s.Set("mode", Mode.C);
        var reloaded = JsonSettingsStore.Load(s.Serialize());
        Assert.Equal(Mode.C, reloaded.Get("mode", Mode.A));
    }

    [Fact]
    public void EnumCorruptValueFallsBackToDefault()
    {
        var s = JsonSettingsStore.Load("{\"mode\":\"nonexistent_case\"}");
        Assert.Equal(Mode.A, s.Get("mode", Mode.A)); // 잘못된 raw → 기본값
    }

    // MARK: 엣지

    [Fact]
    public void TypeMismatchFallsBackToDefault()
    {
        var s = JsonSettingsStore.Load("{\"count\":\"not a number\"}");
        Assert.Equal(7, s.Get("count", 7)); // string을 int로 못 읽음 → 기본값
    }

    [Fact]
    public void MalformedJsonLoadsEmptyStore()
    {
        var s = JsonSettingsStore.Load("{ this is not json");
        Assert.False(s.Contains("anything"));
        Assert.Equal(99, s.Get("anything", 99));
    }
}
