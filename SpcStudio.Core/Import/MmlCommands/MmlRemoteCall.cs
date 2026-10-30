namespace SpcStudio.Import;

// A remote code call, (!n,type) or one of the (!!n) resets: the block it arms, or null where it clears a slot, its trigger type and its tick count.
public sealed record MmlRemoteCall(int Tick, int? BlockLabel, int TriggerType, int TimeTicks, int Line) : MmlEvent(Tick, Line)
{
    public const int KeyOn = -1;
    public const int ClearBoth = 0;
    public const int Timed = 1;
    public const int BeforeKeyOff = 2;
    public const int KeyOff = 3;
    public const int Immediate = 4;
    public const int BeforeKeyOffRestoring = 5;
    public const int KeyOffRestoring = 6;
    public const int ClearOrdinary = 7;
    public const int ClearKeyOn = 8;
    public const int TimerPeriod = 256;

    // Whether this call ends an arm rather than starting one.
    public bool DoesClear
    {
        get
        {
            return IsClearType(TriggerType);
        }
    }

    // Whether this call writes the key-on slot, arming it or clearing it.
    public bool DoesWriteKeyOnSlot
    {
        get
        {
            return TriggerType is KeyOn or KeyOffRestoring or ClearKeyOn or ClearBoth;
        }
    }

    // Whether this call writes the ordinary slot, arming it or clearing it.
    public bool DoesWriteOrdinarySlot
    {
        get
        {
            return TriggerType is KeyOff or Timed or BeforeKeyOff or BeforeKeyOffRestoring or ClearBoth or ClearOrdinary;
        }
    }

    // Whether a trigger type ends an arm rather than starting one.
    public static bool IsClearType(int triggerType)
    {
        return triggerType is ClearBoth or ClearOrdinary or ClearKeyOn;
    }

    // Whether a trigger type fires its block a tick count before the gate's cut.
    public static bool IsBeforeKeyOffType(int triggerType)
    {
        return triggerType is BeforeKeyOff or BeforeKeyOffRestoring;
    }

    // Whether a trigger type runs its block instead of the key-off.
    public static bool IsKeyOffType(int triggerType)
    {
        return triggerType is KeyOff or KeyOffRestoring;
    }

    // Whether a trigger type puts the voice's envelope back from the backup table at every key-on.
    public static bool IsRestoringType(int triggerType)
    {
        return triggerType is BeforeKeyOffRestoring or KeyOffRestoring;
    }

    // Whether a trigger type is one the driver has a handler for.
    public static bool IsReadType(int triggerType)
    {
        return triggerType is KeyOn or ClearBoth or Timed or BeforeKeyOff or KeyOff or Immediate or BeforeKeyOffRestoring or KeyOffRestoring or ClearOrdinary or ClearKeyOn;
    }
}
