namespace SpcStudio.Model;

// What a vibrato and a tremolo stretch both carry in a saved file: where the stretch sits, and how fast it moves.
internal abstract class SweepDto : SweepSpanDto
{
    public int Rate { get; set; }
}
