namespace SpcStudio;

// The title of a message box and the theme class that gives the box its icon, which each kind of title sets, none where the box wears the app's own.
public abstract record BoxTitle(string Text, string? IconClass);
