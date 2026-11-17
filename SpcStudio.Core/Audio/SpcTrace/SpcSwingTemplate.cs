namespace SpcStudio.Audio;

// A swing a block was heard making from its key-on, as another block on its sample would make it: how long after the key-on its sweep set off, how long
// one cycle took, how far it reached either side in its scale's units, and how long it took to reach that width.
public sealed record SpcSwingTemplate(long DelayFrames, double CycleFrames, double Peak, long FadeFrames);
