using Cluxo.Core;

namespace Cluxo.Windows.App.Input;

/// <summary>
/// LL 키보드 후킹이 보는 키 down/up 스트림으로부터 모디파이어 상태를 직접 추적한다.
///
/// 왜 GetKeyState/GetAsyncKeyState 매 콜백 조회 대신 자체 추적?
///   - LL 후킹 콜백은 경량이어야 한다(INPUT-LAYER.md §1). 조회를 줄인다.
///   - 후킹은 전역 모든 키를 보므로 전이를 놓치지 않는다. 좌/우 모디파이어를 독립 추적해
///     "왼쪽 Ctrl 떼도 오른쪽 Ctrl 눌렸으면 Ctrl 유지"가 정확하다.
///   - 후킹 설치 순간 이미 눌린 모디파이어는 <see cref="Seed"/>로 GetAsyncKeyState 1회 주입.
///
/// 순수 로직 → 단위 테스트 대상.
/// </summary>
internal sealed class ModifierTracker
{
    private readonly HashSet<uint> _pressed = new();

    /// <summary>키 전이 반영. 모디파이어가 아니면 무시.</summary>
    public void OnKey(uint vk, bool down)
    {
        if (!VirtualKeys.IsModifier(vk)) return;
        if (down) _pressed.Add(vk);
        else _pressed.Remove(vk);
    }

    /// <summary>후킹 설치 시 이미 눌린 모디파이어 주입(GetAsyncKeyState 결과).</summary>
    public void Seed(uint vk, bool down)
    {
        if (down) _pressed.Add(vk);
        else _pressed.Remove(vk);
    }

    /// <summary>현재 모디파이어 플래그.</summary>
    public KeyModifiers Current
    {
        get
        {
            var m = KeyModifiers.None;
            if (Held(VirtualKeys.VK_CONTROL, VirtualKeys.VK_LCONTROL, VirtualKeys.VK_RCONTROL)) m |= KeyModifiers.Control;
            if (Held(VirtualKeys.VK_MENU, VirtualKeys.VK_LMENU, VirtualKeys.VK_RMENU)) m |= KeyModifiers.Alt;
            if (Held(VirtualKeys.VK_SHIFT, VirtualKeys.VK_LSHIFT, VirtualKeys.VK_RSHIFT)) m |= KeyModifiers.Shift;
            if (Held(VirtualKeys.VK_LWIN, VirtualKeys.VK_RWIN)) m |= KeyModifiers.Win;
            return m;
        }
    }

    private bool Held(params uint[] vks)
    {
        foreach (var vk in vks)
            if (_pressed.Contains(vk)) return true;
        return false;
    }
}
