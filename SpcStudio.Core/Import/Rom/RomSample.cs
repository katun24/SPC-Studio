namespace SpcStudio.Import;

// One sample in an AddmusicK-patched ROM: the BRR bytes in AddmusicK's own .brr layout, the loop offset the ROM keeps apart put back in front of the blocks.
public sealed record RomSample(int Number, byte[] Brr);
