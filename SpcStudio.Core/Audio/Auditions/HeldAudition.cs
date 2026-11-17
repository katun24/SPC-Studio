namespace SpcStudio.Audio;

// A note held on an audition voice until it is let go of - the voice, what it keyed on with or was last pointed at, and the lanes it follows where it has them.
internal sealed class HeldAudition
{
    public SnesVoice? Voice { get; set; }
    public AuditionNote? Note { get; set; }
    public AuditionWalk? Walk { get; set; }

    // Whether the held note still sounds: keyed on, and not fallen silent for good with nothing left of its lanes to raise it again.
    public bool IsSounding
    {
        get
        {
            if (Voice is not { } voice)
            {
                return false;
            }

            return voice.IsActive && (HasLanesAhead || !voice.IsSilentForGood);
        }
    }

    // Whether the held note sounds on unchanging: its envelope's level will not move again and nothing is left of its lanes to move it.
    public bool IsSettled
    {
        get
        {
            return Voice is { IsEnvelopeSettled: true } && !HasLanesAhead;
        }
    }

    // Whether the lanes the held note follows still have ticks ahead of it.
    private bool HasLanesAhead
    {
        get
        {
            return Walk is { } walk && walk.Tick < walk.Lanes.EndTick;
        }
    }

    // Keys the held voice off and forgets it and the lanes it was following.
    public void KeyOff()
    {
        Voice?.KeyOff();
        Voice = null;
        Walk = null;
    }
}
