using Avalonia.Controls;
using Avalonia.Layout;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// A drop-down that turns into a text field holding the name of what it shows: Enter or the field losing the focus finish the rename and hand the name
// typed on, and Escape puts the drop-down back with nothing handed on. The markup gives it the drop-down as its child.
public sealed class RenamableDropDown : Panel
{
    private readonly TextBox _nameBox = new() { IsVisible = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _isRenaming;

    // Hangs the field's keys and its focus on the rename.
    public RenamableDropDown()
    {
        RenameField.Wire(_nameBox, FinishRename, EndRename);
    }

    // Raised with the name typed, its outer spaces taken off, once a rename is finished.
    public event Action<string>? NameCommitted;

    // The characters the name field takes.
    public FieldChars NameCharacters { get; set; } = FieldChars.Text;

    // The drop-down the markup gave it.
    private ComboBox DropDown
    {
        get
        {
            return Children.OfType<ComboBox>().First();
        }
    }

    // Turns the drop-down into the name field, holding a name selected whole so it is typed over.
    public void StartRename(string name)
    {
        _nameBox.Text = name;
        _isRenaming = true;
        _nameBox.IsVisible = true;
        DropDowns.SetCanShow(DropDown, false);
        FieldInput.FocusField(_nameBox);
    }

    // Puts the drop-down back in place of the name field, handing nothing on.
    public void EndRename()
    {
        _isRenaming = false;
        _nameBox.IsVisible = false;
        DropDowns.SetCanShow(DropDown, true);
    }

    // Adds the name field over the drop-down, taking the characters it is given.
    protected override void OnInitialized()
    {
        base.OnInitialized();
        FieldInput.SetAccepts(_nameBox, NameCharacters);
        Children.Add(_nameBox);
    }

    // Closes an open rename and hands the name typed on.
    private void FinishRename()
    {
        if (!_isRenaming)
        {
            return;
        }

        string name = DiskNameRules.Normalize(_nameBox.Text);
        EndRename();
        NameCommitted?.Invoke(name);
    }
}
