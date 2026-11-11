namespace SpcStudio;

// The title of a progress box and the theme class that gives the box its icon, none where it wears the app's own; made from a plain title or from a
// message box's.
public readonly record struct ProgressTitle(string Text, string? IconClass)
{
    // A plain title, the box wearing the app's own icon.
    public static implicit operator ProgressTitle(string text)
    {
        return new ProgressTitle(text, null);
    }

    // A message box's title, the box wearing the icon its kind of title carries.
    public static implicit operator ProgressTitle(BoxTitle title)
    {
        return new ProgressTitle(title.Text, title.IconClass);
    }
}
