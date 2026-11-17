namespace SpcStudio.Import;

// Something written into a channel at a tick, and the line it was written on.
public abstract record MmlEvent(int Tick, int Line);
