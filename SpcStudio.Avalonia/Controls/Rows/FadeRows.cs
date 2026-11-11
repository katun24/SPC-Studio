using Avalonia.Controls;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The two kinds of volume and pan rows, each stated once for both panels: the channel volume's and the pan's.
public static class FadeRows
{
    private static readonly RowSectionTooltips VolumeTooltips = new(Tooltips.AddVolumeRow, Tooltips.ClearVolumeEffects);
    private static readonly RowSectionTooltips PanTooltips = new(Tooltips.AddPanRow, Tooltips.ClearPanEffects);

    // The channel volume's rows, MML v: one byte and no surround column.
    public static ChainRows<int> Volume(ListSectionParts parts, Func<RowSubjects<IFadeHost<int>>> subjects, InertClaim? inertClaim = null)
    {
        return Over(NoteVolume.Lane, SettingLabels.ChannelVolume, VolumeTooltips, false, _ => SnesSurround.None, (volume, _) => volume, parts, subjects, inertClaim);
    }

    // The pan's rows, MML y: the table index and the surround pair beside it.
    public static ChainRows<PanState> Pan(ListSectionParts parts, Func<RowSubjects<IFadeHost<PanState>>> subjects, InertClaim? inertClaim = null)
    {
        return Over(NotePan.Lane, SettingLabels.Pan, PanTooltips, true, state => state.Surround, (state, pair) => state with { Surround = pair }, parts, subjects, inertClaim);
    }

    // One kind of rows over a list section and what they edit.
    private static ChainRows<TState> Over<TState>(NoteFadeLane<TState> lane, string valueLabel, RowSectionTooltips sectionTooltips, bool hasSurround, Func<TState, SnesSurround> surroundOf,
        Func<TState, SnesSurround, TState> withSurround, ListSectionParts parts, Func<RowSubjects<IFadeHost<TState>>> subjects, InertClaim? inertClaim)
        where TState : struct
    {
        return new ChainRows<TState>
        {
            SectionParts = parts,
            SectionTooltips = sectionTooltips,
            Lane = lane,
            ValueLabel = valueLabel,
            HasSurround = hasSurround,
            SurroundOf = surroundOf,
            WithSurround = withSurround,
            Subjects = subjects,
            InertClaim = inertClaim,
        };
    }
}
