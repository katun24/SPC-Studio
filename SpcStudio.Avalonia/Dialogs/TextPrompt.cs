using SpcStudio.Controls;

namespace SpcStudio;

// What a box asking for one line of text asks with: its title, its message, the characters its field takes, what its confirming button says where not the button
// row's own, and which texts it can use.
public sealed record TextPrompt(string Title, string Message, FieldChars Accepts, string? ConfirmText, Func<string?, bool> IsUsable);
