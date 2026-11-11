namespace SpcStudio.Model;

// The five #instruments bytes of an instrument: ADSR1, ADSR2, GAIN and the tuning as dd.ee.
public readonly record struct InstrumentBytes(byte Adsr1, byte Adsr2, byte Gain, byte TuningHigh, byte TuningLow);
