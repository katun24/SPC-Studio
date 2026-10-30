namespace SpcStudio.Model;

// Note-to-pitch conversion, replicating AddmusicK's driver exactly.
public static class SnesPitch
{
    public const int UnityPitchRegister = 0x1000;
    public const int MaxPitchRegister = 0x3FFF;
    public const int MinPitchRegister = 1;
    public const int MinNoteValue = 0;
    public const int MaxNoteValue = 69;
    public const int MaxPlayableNoteValue = 83;
    public const int C4NoteValue = 36;
    public const int SemitonesPerOctave = 12;
    public const int CentsPerSemitone = 100;
    public const int CentsPerOctave = CentsPerSemitone * SemitonesPerOctave;
    public const int FineTuneStepsPerSemitone = 256;
    public const int NoteByteBase = 0x80;
    public const int MmlStartOctave = 4;
    public const int MinFineTune = 0;
    public const int MaxFineTune = 255;
    public const int NoteValueMask = 0x7F;
    public const double ConcertC4Hz = 261.6255653;
    private const int TopOctaveIndex = 6;
    private const int ShiftsPastPitch = 16;
    private const int LowestOctave = 1;

    public static readonly ValueRange FineTuneRange = new(MinFineTune, MaxFineTune);
    public static readonly ValueRange NoteValueRange = new(MinNoteValue, MaxNoteValue);
    public static readonly ValueRange PlayableNoteValueRange = new(MinNoteValue, MaxPlayableNoteValue);
    public static readonly ValueRange PitchRegisterRange = new(MinPitchRegister, MaxPitchRegister);

    // One octave of base pitches from the driver, plus a thirteenth entry an exact octave above the first, which the driver interpolates fractional notes towards.
    public static readonly int[] PitchTable = [0x085F, 0x08DE, 0x0965, 0x09F4, 0x0A8C, 0x0B2C, 0x0BD6, 0x0C8B, 0x0D4A, 0x0E14, 0x0EEA, 0x0FCD, 0x10BE];

    // The note names of an octave as MML writes them, C first, each sharp as its natural and a plus.
    public static readonly string[] NoteNames = ["c", "c+", "d", "d+", "e", "f", "f+", "g", "g+", "a", "a+", "b"];

    // The note value a key-on sum actually sounds at: the part of it that reaches the pitch table.
    public static int SoundedNoteValue(int noteValue)
    {
        return noteValue & NoteValueMask;
    }

    // The semitone in the octave an MML note letter names, or -1 for a character that is not one.
    public static int SemitoneForLetter(char letter)
    {
        for (int semitone = 0; semitone < NoteNames.Length; semitone++)
        {
            if (NoteNames[semitone] is [var naturalLetter] && naturalLetter == letter)
            {
                return semitone;
            }
        }

        return -1;
    }

    // The driver's note value for an MML octave and a semitone in it, the lowest octave's C being the lowest note value.
    public static int NoteValueFor(int octave, int semitoneInOctave)
    {
        return semitoneInOctave + (octave - LowestOctave) * SemitonesPerOctave;
    }

    // The note value for a note given as semitones above C4, as the piano roll stores it.
    public static int NoteValueFromC4(int semitonesAboveC4)
    {
        return C4NoteValue + semitonesAboveC4;
    }

    // What a note is written as in the byte stream: the note byte base over its note value.
    public static int NoteByte(int semitonesAboveC4)
    {
        return NoteByteBase | NoteValueFromC4(semitonesAboveC4);
    }

    // The semitone above C4 a note byte names, its high bit masked off.
    public static int SemitoneFromNoteByte(int noteByte)
    {
        return (noteByte & NoteValueMask) - C4NoteValue;
    }

    // A semitone offset above C4 split into the MML octave and the semitone within it, correct below C4 as well.
    public static (int Octave, int SemitoneInOctave) OctaveAndSemitone(int semitonesAboveC4)
    {
        int noteValue = NoteValueFromC4(semitonesAboveC4);
        return (noteValue / SemitonesPerOctave + LowestOctave, noteValue % SemitonesPerOctave);
    }

    // The base pitch for a note and a fine tune before the tuning multiplier is applied: the driver's own arithmetic, integer shifts included.
    public static int BasePitch(int noteValue, int fineTune = 0)
    {
        return BasePitchFixed(SnesFade.Value16Of(noteValue, fineTune));
    }

    // The base pitch for the driver's own 16-bit note value, the semitone in the high byte and its fraction in the low one, which is what the driver's pitch routine is handed.
    public static int BasePitchFixed(int noteValueFixed)
    {
        // The semitone doubled with its top bit dropped, which is the driver's table offset in bytes.
        int doubledSemitone = (SnesFade.ValueOf(noteValueFixed) * 2) & byte.MaxValue;

        int octaveIndex = doubledSemitone / (SemitonesPerOctave * 2);
        int semitoneInOctave = doubledSemitone % (SemitonesPerOctave * 2) / 2;
        int tableDelta = PitchTable[semitoneInOctave + 1] - PitchTable[semitoneInOctave];
        int interpolatedPitch = PitchTable[semitoneInOctave] + ((tableDelta * SnesFade.FractionOf(noteValueFixed)) >> SnesFade.FractionBits);
        int shiftCount = (TopOctaveIndex - octaveIndex) & byte.MaxValue;
        return shiftCount >= ShiftsPastPitch ? 0 : (interpolatedPitch * 2) >> shiftCount;
    }

    // The PITCH register the driver would write for a note, a fine tune and a tuning, moved by a vibrato's signed fine-tune steps.
    public static int PitchRegister(int noteValue, double tuningMultiplier, int fineTune = 0, int vibratoOffset = 0)
    {
        double pitch = UnclampedPitch(noteValue, tuningMultiplier, fineTune, vibratoOffset);
        return PitchRegisterRange.Clamp((int)Math.Round(pitch));
    }

    // Whether a note at a tuning asks for a pitch the register cannot hold, which the register's clamp then answers with the highest pitch there is.
    public static bool DoesPitchOverflow(int noteValue, double tuningMultiplier, int fineTune = 0, int vibratoOffset = 0)
    {
        return UnclampedPitch(noteValue, tuningMultiplier, fineTune, vibratoOffset) > MaxPitchRegister;
    }

    // The highest note a tuning actually reaches: the highest playable note, or lower where the register runs out first.
    public static int HighestPlayableNoteValue(double tuningMultiplier)
    {
        for (int noteValue = MaxPlayableNoteValue; noteValue > MinNoteValue; noteValue--)
        {
            if (!DoesPitchOverflow(noteValue, tuningMultiplier))
            {
                return noteValue;
            }
        }

        return MinNoteValue;
    }

    // The note value whose register comes nearest a PITCH register at a tuning, saying where the register asked for a pitch outside what the tuning plays,
    // the answer then being the nearer end of that range.
    public static int NoteValueForRegister(int pitchRegister, double tuningMultiplier, out bool isOutOfRange)
    {
        isOutOfRange = false;
        if (!(tuningMultiplier > 0))
        {
            return C4NoteValue;
        }

        int targetRegister = PitchRegisterRange.Clamp(pitchRegister);
        int highestNoteValue = HighestPlayableNoteValue(tuningMultiplier);
        if (targetRegister < PitchRegister(MinNoteValue, tuningMultiplier))
        {
            isOutOfRange = true;
            return MinNoteValue;
        }

        if (targetRegister > PitchRegister(highestNoteValue, tuningMultiplier))
        {
            isOutOfRange = true;
            return highestNoteValue;
        }

        return Enumerable.Range(MinNoteValue, highestNoteValue - MinNoteValue + 1)
            .MinBy(noteValue => Math.Abs(Math.Log((double)PitchRegister(noteValue, tuningMultiplier) / targetRegister)));
    }

    // The playback rate a PITCH register gives, relative to the sample's native rate.
    public static double RateOf(int pitchRegister)
    {
        return pitchRegister / (double)UnityPitchRegister;
    }

    // The tuning multiplier that makes a note value play the sample at a rate.
    public static double TuningForRate(int noteValue, double rate)
    {
        return rate * UnityPitchRegister / BasePitch(noteValue);
    }

    // The concert pitch of a note value, given the sample's frequency at the unity rate.
    public static double FrequencyAt(int noteValue, double tuningMultiplier, double sampleHzAtUnity)
    {
        return sampleHzAtUnity * RateOf(PitchRegister(noteValue, tuningMultiplier));
    }

    // The frequency ratio a number of semitones makes, fractions included.
    public static double FrequencyRatioForSemitones(double semitones)
    {
        return Math.Pow(2, semitones / SemitonesPerOctave);
    }

    // The number of semitones a frequency ratio makes, fractions included.
    public static double SemitonesForFrequencyRatio(double ratio)
    {
        return SemitonesPerOctave * Math.Log2(ratio);
    }

    // The tuning multiplier that makes the roll's note names true, given the sample's own frequency at the unity pitch.
    public static double TuningForConcertPitch(double sampleHzAtUnity)
    {
        return TuningForRate(NoteValueFromC4(0), ConcertC4Hz / sampleHzAtUnity);
    }

    // The pitch a PITCH register sounds a sample at, in semitones above C4, fractions included, given the tuning that makes the sample's note names true.
    public static double SemitonesAboveC4At(int pitchRegister, double tuningMultiplier)
    {
        return SemitonesForFrequencyRatio(TuningForRate(C4NoteValue, RateOf(pitchRegister)) / tuningMultiplier);
    }

    // The PITCH register that sounds a sample at a pitch in semitones above C4, given the tuning that makes the sample's note names true, before the
    // register's clamp.
    public static int PitchRegisterAt(double semitonesAboveC4, double tuningMultiplier)
    {
        return (int)Math.Round(BasePitch(C4NoteValue) * tuningMultiplier * FrequencyRatioForSemitones(semitonesAboveC4));
    }

    // A count of a pitch value moved by a number of steps of an interval in cents from the whole cents it is shown at, given the cents it stands at
    // exactly, rounded to a whole count and moved at least one count a step.
    public static int CountSteppedByCents(double count, double exactCents, int steps, int stepCents)
    {
        int currentCount = (int)Math.Round(count);
        double centsToMove = steps * stepCents - (exactCents - Math.Round(exactCents));
        int steppedCount = (int)Math.Round(count * FrequencyRatioForSemitones(centsToMove / CentsPerSemitone));
        return steppedCount == currentCount ? currentCount + steps : steppedCount;
    }

    // The pitch a note asks for before the register's clamp.
    private static double UnclampedPitch(int noteValue, double tuningMultiplier, int fineTune, int vibratoOffset)
    {
        return BasePitchFixed(SnesFade.Value16Of(noteValue, fineTune) + vibratoOffset) * tuningMultiplier;
    }

    // Everything a sounding voice needs to work its own pitch register out: the note, the fine tune it keyed on with and its instrument's tuning.
    public readonly record struct NotePitch(int NoteValue, double TuningMultiplier, int FineTune)
    {
        // The pitch a note keys on with under the tuning its instrument sounds with: its row, its own fine tune and that tuning.
        public static NotePitch Of(Note note, double tuningMultiplier)
        {
            return new NotePitch(NoteValueFromC4(note.Semitone), tuningMultiplier, note.FineTune);
        }

        // The PITCH register, moved by a vibrato offset where there is one.
        public int Register(int vibratoOffset = 0)
        {
            return PitchRegister(NoteValue, TuningMultiplier, FineTune, vibratoOffset);
        }
    }
}
