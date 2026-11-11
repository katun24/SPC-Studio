namespace SpcStudio.Import;

// Which of a channel's two remote slots holds an arm that runs its block instead of the key-off: a type 3 in the ordinary slot, a type 6 in the key-on slot.
internal readonly record struct KeyOffArming(bool IsOrdinarySlotArmed, bool IsKeyOnSlotArmed)
{
    // Whether either slot holds such an arm.
    public bool IsArmed
    {
        get
        {
            return IsOrdinarySlotArmed || IsKeyOnSlotArmed;
        }
    }

    // The arming a call leaves, each slot the call writes taking the call's own type.
    public KeyOffArming After(MmlRemoteCall call)
    {
        bool isArmingType = MmlRemoteCall.IsKeyOffType(call.TriggerType);
        return new KeyOffArming(
            call.DoesWriteOrdinarySlot ? isArmingType : IsOrdinarySlotArmed,
            call.DoesWriteKeyOnSlot ? isArmingType : IsKeyOnSlotArmed);
    }
}
