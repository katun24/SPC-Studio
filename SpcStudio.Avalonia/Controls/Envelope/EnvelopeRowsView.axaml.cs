using Avalonia.Controls;

namespace SpcStudio.Controls;

// The body of an Envelope section, the same in both panels: the graph, the rows under it and the '+' that adds one.
public partial class EnvelopeRowsView : UserControl
{
    // Builds the controls.
    public EnvelopeRowsView()
    {
        InitializeComponent();
    }

    // The graph over the whole of the rows.
    public AdsrGraph RowsGraph
    {
        get
        {
            return Graph;
        }
    }

    // The list the rows are built into.
    public StackPanel RowList
    {
        get
        {
            return Rows;
        }
    }

    // The '+' under the rows.
    public AddRowButton RowAddButton
    {
        get
        {
            return AddButton;
        }
    }
}
