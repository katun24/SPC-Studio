using SpcStudio.Import;

namespace SpcStudio;

// One entry of the Song panel's Sample Group drop-down: a group the install defines, or none.
public sealed record SampleGroupChoice(string? Name)
{
    private const string NoGroupText = "none";

    // The group as the drop-down shows it: as its directive, or as none.
    public override string ToString()
    {
        return Name is null ? NoGroupText : SampleGroups.DirectiveOf(Name);
    }
}
