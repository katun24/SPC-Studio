using System.IO;
using SpcStudio.Audio;

namespace SpcStudio.Model;

// One effect on a sample as both saved files write it: its kind, whether it is switched on, and one value per setting its kind declares.
internal sealed class SampleEffectDto
{
    public SampleEffectKind Kind { get; set; }
    public bool IsEnabled { get; set; }
    public double[] Values { get; set; } = [];

    // The effects of a sample at a rate as they are written - the ones switched on or moved off their starting settings - or null where there are none.
    public static List<SampleEffectDto>? ListOf(SampleRack effects, int rateHz)
    {
        var touchedEffects = effects.Effects.Where(effect => !effect.IsUntouchedAt(rateHz)).ToArray();
        return SavedFileJson.EntriesOrNull(touchedEffects, effect => new SampleEffectDto { Kind = effect.Kind, IsEnabled = effect.IsEnabled, Values = effect.Values });
    }

    // The effects of a sample at a rate read back into their rack, every one the file leaves out switched off and every value above its setting's top
    // moved down to it, refusing an effect the app does not have, one given twice, and a value missing, extra or below its setting's range.
    public static SampleRack RackOf(List<SampleEffectDto>? effects, string where, int rateHz)
    {
        var rack = SampleRack.AllOffAt(rateHz);
        var kindsRead = new HashSet<SampleEffectKind>();
        foreach (var effectContents in effects ?? [])
        {
            var effect = effectContents.ToEffect(where, rateHz);
            if (!kindsRead.Add(effect.Kind))
            {
                throw new InvalidDataException($"{where} has two {SampleEffect.NameOf(effect.Kind)} effects.");
            }

            rack = rack.WithReplaced(effect);
        }

        return rack;
    }

    // The effect this entry describes on a sample at a rate, every value above its setting's top moved down to it, refusing a kind the app does not have
    // and a value missing, extra or below its setting's range.
    private SampleEffect ToEffect(string where, int rateHz)
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new InvalidDataException($"{where} has an effect of kind {(int)Kind}, which is not one of the effects.");
        }

        var settings = SampleEffect.SettingsOf(Kind, rateHz);
        string effectWhere = $"{where} {SampleEffect.NameOf(Kind)} effect";
        if (Values.Length != settings.Count)
        {
            throw new InvalidDataException($"{effectWhere} has {Values.Length} values where it takes {settings.Count}.");
        }

        for (int index = 0; index < settings.Count; index++)
        {
            SavedFileChecks.RequireInRange(effectWhere, settings[index].Name, Values[index], settings[index].Minimum, double.PositiveInfinity);
        }

        return new SampleEffect(Kind, IsEnabled, Values).HeldTo(rateHz);
    }
}
