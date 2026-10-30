using Avalonia.Controls;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// The value rows and the sections a note plays by, the same in both panels: the instrument panel shows an instrument's defaults in them and the note
// panel the selection's. Ties each slider to its field and fills the Surround drop-down; what an edit does is each panel's own.
public partial class NoteValuesView : UserControl
{
    // Builds the controls, gathers each row's and each list section's parts, ties every slider to its field and fills the Surround drop-down.
    public NoteValuesView()
    {
        InitializeComponent();

        GateRow = new NumericRowParts(GateSlider, GateBox, GateSwitch, SnesTiming.GateRange);
        VelocityRow = new NumericRowParts(VelocitySlider, VelocityBox, VelocitySwitch, SnesVolume.VelocityRange);
        DetuneRow = new NumericRowParts(DetuneSlider, DetuneBox, DetuneSwitch, SnesPitch.FineTuneRange);
        VolumeRow = new NumericRowParts(VolumeSlider, VolumeBox, VolumeSwitch, SnesVolume.ChannelVolumeRange);
        AmplifyRow = new NumericRowParts(AmplifySlider, AmplifyBox, AmplifySwitch, SnesVolume.AmplifyRange);
        PanRow = new NumericRowParts(PanSlider, PanBox, PanSwitch, SnesVolume.PanIndexRange);
        LegatoRow = new ToggleRowParts(LegatoCheck, LegatoSwitch);
        HoldRow = new ToggleRowParts(HoldCheck, HoldSwitch);
        EchoRow = new ToggleRowParts(EchoCheck, EchoSwitch);
        VolumeSection = new ListSectionParts(VolumeFrame, VolumeList, VolumeAddButton);
        PanSection = new ListSectionParts(PanFrame, PanList, PanAddButton);
        BendSection = new ListSectionParts(BendFrame, BendList, BendAddButton);
        VibratoSection = new ListSectionParts(VibratoFrame, VibratoList, VibratoAddButton);
        TremoloSection = new ListSectionParts(TremoloFrame, TremoloList, TremoloAddButton);

        foreach (var row in new[] { GateRow, VelocityRow, DetuneRow, VolumeRow, AmplifyRow, PanRow })
        {
            row.TieSliderToBox();
        }

        SurroundField.FillDropDown(SurroundBox);
    }

    public NumericRowParts GateRow { get; }
    public NumericRowParts VelocityRow { get; }
    public NumericRowParts DetuneRow { get; }
    public NumericRowParts VolumeRow { get; }
    public NumericRowParts AmplifyRow { get; }
    public NumericRowParts PanRow { get; }
    public ToggleRowParts LegatoRow { get; }
    public ToggleRowParts HoldRow { get; }
    public ToggleRowParts EchoRow { get; }
    public ListSectionParts VolumeSection { get; }
    public ListSectionParts PanSection { get; }
    public ListSectionParts BendSection { get; }
    public ListSectionParts VibratoSection { get; }
    public ListSectionParts TremoloSection { get; }

    // The Surround row's drop-down.
    public ComboBox SurroundDropDown
    {
        get
        {
            return SurroundBox;
        }
    }

    // The Envelope section's frame.
    public PanelSection EnvelopeSection
    {
        get
        {
            return EnvelopeFrame;
        }
    }

    // The Envelope section's body: its graph, its rows and its '+'.
    public EnvelopeRowsView EnvelopeRows
    {
        get
        {
            return EnvelopeBody;
        }
    }
}
