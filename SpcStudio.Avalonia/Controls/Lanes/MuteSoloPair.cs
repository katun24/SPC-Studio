using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SpcStudio.Controls;

// A member's mute toggle and solo toggle side by side, each with its tooltip, drawn together in the state of the member's mute and solo.
public sealed class MuteSoloPair
{
    private readonly MuteSoloToggle _mute;
    private readonly MuteSoloToggle _solo;

    // Builds a member's two toggles with the tooltips given, side by side and centered in the height they are given, each reporting a press to its
    // handler with the member and its new state.
    public MuteSoloPair(int member, string muteTooltip, string soloTooltip, Action<int, bool> muteToggled, Action<int, bool> soloToggled)
    {
        _mute = MuteSoloToggle.CreateMute(member, muteToggled);
        _solo = MuteSoloToggle.CreateSolo(member, soloToggled);
        ToolTip.SetTip(_mute.Button, muteTooltip);
        ToolTip.SetTip(_solo.Button, soloTooltip);
        Panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _mute.Button, _solo.Button } };
    }

    public StackPanel Panel { get; }

    // Whether the pointer is over either toggle.
    public bool IsPointerOver
    {
        get
        {
            return _mute.Button.IsPointerOver || _solo.Button.IsPointerOver;
        }
    }

    // Draws both toggles in the state of the member's mute and solo, each in its own color while on and in the idle brush given while off.
    public void ShowState(bool isMuted, bool isSoloed, IBrush idleBrush)
    {
        _mute.ShowState(isMuted, idleBrush);
        _solo.ShowState(isSoloed, idleBrush);
    }
}
