namespace SpcStudio.Import;

// One loop edge of a channel: the tick the byte in front of it ends on, and what that byte's read-ahead finds.
public readonly record struct MmlLoopEdge(int Tick, ReadAheadStop ReadAheadStop);
