namespace SpcStudio.Import;

// One song in an AddmusicK-patched ROM: the ARAM image the SNES uploads for it, the ARAM address it goes to, and the sample numbers its instruments count through.
public sealed record RomSong(int Slot, int AramBase, byte[] Data, IReadOnlyList<int> SampleNumbers);
