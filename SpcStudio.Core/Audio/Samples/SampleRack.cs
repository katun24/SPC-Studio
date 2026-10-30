namespace SpcStudio.Audio;

// A sample's effects rack, never changed once made: one row of every kind, in the order their kinds are declared, applied to its points one after another.
public sealed class SampleRack
{
    private readonly SampleEffect[] _effects;

    private SampleRack(SampleEffect[] effects)
    {
        _effects = effects;
    }

    public IReadOnlyList<SampleEffect> Effects
    {
        get
        {
            return _effects;
        }
    }

    // How many rows are switched on.
    public int EnabledCount
    {
        get
        {
            return _effects.Count(effect => effect.IsEnabled);
        }
    }

    // The rack a sample at a rate starts with: every row switched off, at the settings that change nothing.
    public static SampleRack AllOffAt(int rateHz)
    {
        return new SampleRack(Enum.GetValues<SampleEffectKind>().Select(kind => SampleEffect.FreshRow(kind, rateHz)).ToArray());
    }

    // Whether any row in the rack would change the audio of a sample at a rate.
    public bool DoesChangeAudioAt(int rateHz)
    {
        return _effects.Any(effect => effect.IsEnabled && effect.DoesChangeAudioAt(rateHz));
    }

    // The rack with the row of an effect's kind replaced by it, left where it is.
    public SampleRack WithReplaced(SampleEffect effect)
    {
        return new SampleRack(_effects.Select(existing => existing.Kind == effect.Kind ? effect : existing).ToArray());
    }

    // The rack with a kind's row switched on or off, its settings left as they are.
    public SampleRack WithEnabled(SampleEffectKind kind, bool isEnabled)
    {
        return WithReplaced(EffectOf(kind) with { IsEnabled = isEnabled });
    }

    // Whether another rack holds the same rows, each the same as this one's.
    public bool IsSameAs(SampleRack other)
    {
        return _effects.Length == other._effects.Length && _effects.Zip(other._effects).All(pair => pair.First.IsSameAs(pair.Second));
    }

    // Runs points at a rate through every row in order, the array given handed back where nothing changes it.
    public short[] Apply(short[] points, int rateHz)
    {
        foreach (var effect in _effects)
        {
            points = effect.Apply(points, rateHz);
        }

        return points;
    }

    // The rack with every value above its setting's top on a sample at a rate moved down to that top, or this rack where none is.
    public SampleRack HeldTo(int rateHz)
    {
        var heldEffects = _effects.Select(effect => effect.HeldTo(rateHz)).ToArray();
        return heldEffects.SequenceEqual(_effects, ReferenceEqualityComparer.Instance) ? this : new SampleRack(heldEffects);
    }

    // The rack's row of a kind.
    public SampleEffect EffectOf(SampleEffectKind kind)
    {
        return _effects.First(effect => effect.Kind == kind);
    }
}
