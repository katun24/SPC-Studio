using System.ComponentModel;

namespace SpcStudio;

// One row of a drop-down of the song's samples or instruments: the id it stands for, the name it reads as, and whether no note uses it.
public record DropDownRow(int Id, string Name) : INotifyPropertyChanged
{
    private bool _isUnused;

    // Raised when whether no note uses the row's sample or instrument changes, which redraws the row.
    public event PropertyChangedEventHandler? PropertyChanged;

    // Whether no note in the song uses the row's sample or instrument, which draws the row dimmer.
    public bool IsUnused
    {
        get
        {
            return _isUnused;
        }
        set
        {
            if (value == _isUnused)
            {
                return;
            }

            _isUnused = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUnused)));
        }
    }

    // The name, which is what the row reads as.
    public sealed override string ToString()
    {
        return Name;
    }
}
