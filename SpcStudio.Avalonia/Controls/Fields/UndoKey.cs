using System.Runtime.CompilerServices;
using SpcStudio.Model;

namespace SpcStudio.Controls;

// What an edit is recorded under: the gesture it belongs to and the parts naming the control it came from, equal to another key naming the same
// gesture and the same parts however the parts were grouped where the edit was made; and continuing the step a claim made in its gesture.
public sealed class UndoKey : IEquatable<UndoKey>, IContinuingUndoSource
{
    private readonly object _gesture;
    private readonly object?[] _parts;

    // Takes a source apart into the parts it names, a group of them and any group inside it included.
    public UndoKey(object gesture, object source)
    {
        _gesture = gesture;
        _parts = [.. PartsOf(source)];
    }

    // Whether the key is a claim's: a press turning an inert control live, which the first edit the press goes on to make joins.
    private bool IsClaim
    {
        get
        {
            return _parts.Contains(EditStep.ClaimSource);
        }
    }

    // Whether an edit under this key continues the last edit's step: the same key, or a claim made in the same gesture.
    public bool ContinuesStepOf(object? lastSource)
    {
        return Equals(lastSource) || lastSource is UndoKey { IsClaim: true } claim && Equals(claim._gesture, _gesture);
    }

    // Whether another key names the same gesture and the same parts in the same order.
    public bool Equals(UndoKey? other)
    {
        return other is not null && Equals(_gesture, other._gesture) && _parts.SequenceEqual(other._parts);
    }

    // Whether another object is a key naming the same gesture and the same parts in the same order.
    public override bool Equals(object? obj)
    {
        return Equals(obj as UndoKey);
    }

    // A hash over the gesture and every part, equal for equal keys.
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_gesture);
        foreach (var part in _parts)
        {
            hash.Add(part);
        }

        return hash.ToHashCode();
    }

    // The parts a source names: the source itself, or what a group holds, each item taken apart in turn.
    private static IEnumerable<object?> PartsOf(object? source)
    {
        if (source is not ITuple group)
        {
            return [source];
        }

        return Enumerable.Range(0, group.Length).SelectMany(index => PartsOf(group[index]));
    }
}
