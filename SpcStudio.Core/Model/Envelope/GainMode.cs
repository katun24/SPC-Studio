namespace SpcStudio.Model;

// The shape GAIN produces when it is in use, from bit 7 and bits 6-5 of the GAIN byte, Direct being the flat case where the byte is a level.
public enum GainMode
{
    Direct,
    LinearDecrease,
    ExponentialDecrease,
    LinearIncrease,
    BentIncrease,
}
