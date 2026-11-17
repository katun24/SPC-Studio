using SpcStudio.Model;

namespace SpcStudio;

// How an AddmusicK .txt, once found, is read into a song: the stages the reading reports, the title its failures are shown under, and the reading
// itself, handed the .txt's path, the AddmusicK folder, the progress and the cancellation.
internal sealed record MmlReading(int StageCount, FailureTitle FailureTitle, Func<string, string?, Action<string, int, int>, CancellationToken, Song> Read);
