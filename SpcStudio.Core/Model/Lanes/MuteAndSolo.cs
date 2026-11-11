namespace SpcStudio.Model;

// Which members of a numbered set - a song's channels, the chip's voices or the samples it plays - are muted and which are soloed, and from the two
// which members do not sound.
public sealed class MuteAndSolo
{
    private readonly List<bool> _isMuted;
    private readonly List<bool> _isSoloed;

    // A set of members with nothing muted and nothing soloed.
    public MuteAndSolo(int memberCount)
    {
        _isMuted = [.. new bool[memberCount]];
        _isSoloed = [.. new bool[memberCount]];
    }

    // Whether any member is soloed.
    private bool HasSoloedMember
    {
        get
        {
            return _isSoloed.Any(isSoloed => isSoloed);
        }
    }

    // Whether a member is muted.
    public bool IsMuted(int member)
    {
        return _isMuted[member];
    }

    // Mutes or unmutes a member, muting it taking its solo off.
    public void SetMuted(int member, bool isMuted)
    {
        _isMuted[member] = isMuted;
        if (isMuted)
        {
            _isSoloed[member] = false;
        }
    }

    // Whether a member is soloed.
    public bool IsSoloed(int member)
    {
        return _isSoloed[member];
    }

    // Solos or unsolos a member, soloing it taking its mute off.
    public void SetSoloed(int member, bool isSoloed)
    {
        _isSoloed[member] = isSoloed;
        if (isSoloed)
        {
            _isMuted[member] = false;
        }
    }

    // Whether a member does not sound: muted, or left out by another member's solo.
    public bool IsSilenced(int member)
    {
        return _isMuted[member] || (HasSoloedMember && !_isSoloed[member]);
    }

    // Replaces every member's mute and solo as given.
    public void SetAll(IReadOnlyList<bool> isMuted, IReadOnlyList<bool> isSoloed)
    {
        for (int member = 0; member < _isMuted.Count; member++)
        {
            _isMuted[member] = isMuted[member];
            _isSoloed[member] = isSoloed[member];
        }
    }

    // Takes every mute and every solo off.
    public void Clear()
    {
        bool[] noMember = new bool[_isMuted.Count];
        SetAll(noMember, noMember);
    }

    // Adds a member after the last one, neither muted nor soloed.
    public void AddMember()
    {
        _isMuted.Add(false);
        _isSoloed.Add(false);
    }

    // Takes a member out, every member after it moving down one.
    public void RemoveMember(int member)
    {
        _isMuted.RemoveAt(member);
        _isSoloed.RemoveAt(member);
    }
}
