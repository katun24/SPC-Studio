namespace SpcStudio.Model;

// What a saved file's contents need for the version check both saved files make: the layout version they were written in.
internal interface IVersionedContents
{
    public int Version { get; }
}
