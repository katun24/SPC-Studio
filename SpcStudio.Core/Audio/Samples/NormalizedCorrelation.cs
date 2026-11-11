namespace SpcStudio.Audio;

// How alike two runs of points are in shape whatever their loudness: their summed products divided by both runs' energy, one for the same shape.
public static class NormalizedCorrelation
{
    // The correlation of two runs from their summed products and each run's summed squares, zero where either run is silent.
    public static double Of(double summedProducts, double earlierEnergy, double laterEnergy)
    {
        double norm = Math.Sqrt(earlierEnergy * laterEnergy);
        return norm <= 0 ? 0 : summedProducts / norm;
    }
}
