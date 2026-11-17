using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpcStudio.Audio;
using SpcStudio.Controls;
using SpcStudio.Import;

namespace SpcStudio;

// The Create Chord Sample window: a line warning that the sample has no loop or loops from its very start, a keyboard the chord's notes are pressed onto
// and off, a key pressed onto it sounding its note while held, the Tune By Ratios box, the chord's size and a transport playing the chord as Create Chord
// makes it - made in the background, made and played again as it changes while it plays, and neither played nor made while it would not fit in ARAM.
public partial class CreateChordSampleWindow : DialogWindow
{
    private const string NoLoopWarning = "This sample has no loop. If it is intended to loop, set the loop before creating the chord.";
    private const string LoopAtStartWarning = "This sample has a loop point at the very start. Make sure to set a proper loop point before creating the chord.";
    private const int RootInSemitonesFromRoot = 0;

    private readonly SortedSet<int> _semitonesInChord = [RootInSemitonesFromRoot];
    private readonly HeldShiftKey _heldShiftKey;
    private ChordSource? _chordSource;
    private bool _isTunedByRatios;
    private bool _isChordAuditioned;
    private bool _isChordLooped;
    private bool _isChordTooBig;
    private CancellationTokenSource? _chordMaking;
    private Sample? _madeChord;

    // Builds the window, following Shift for the play button and the keyboard's presses.
    public CreateChordSampleWindow()
    {
        InitializeComponent();
        _heldShiftKey = new HeldShiftKey(this, ShowStartGlyph);
        Transport.UseTooltips(Tooltips.PlayOrLoopChord, null, Tooltips.StopChord);
        ChordKeys.KeyIncluded += OnKeyIncluded;
        ChordKeys.KeyExcluded += OnKeyExcluded;
        ChordKeys.KeyReleased += OnKeyReleased;
        Closed += (_, _) => StopAudition();
    }

    // Whether the chord is being made to play or is playing.
    private bool IsChordPlaying
    {
        get
        {
            return _isChordAuditioned && (_chordMaking is not null || _chordSource?.Mixer?.Preview.IsPlaying == true);
        }
    }

    // Whether there is a chord to play and create: one note or more, fitting in ARAM.
    private bool HasChordToMake
    {
        get
        {
            return _semitonesInChord.Count > 0 && !_isChordTooBig;
        }
    }

    // Shows the window over another, making chords of a sample that play through a mixer or none - a chord in piano tuning crossfaded over a fraction of
    // its loop - and answers the chord Create Chord made, or null where the window closed without one.
    public static Task<Sample?> Show(Window owner, Sample source, BrrMixer? mixer, double crossfadeLengthFraction)
    {
        var window = new CreateChordSampleWindow { _chordSource = new ChordSource(source, mixer, crossfadeLengthFraction) };
        window.ChordKeys.RootSemitone = SampleEditorWindow.SoundedPitchOf(source).Semitone;
        window._isTunedByRatios = source.Source.DoesLoop;
        window.LoopWarningText.Text = LoopWarningOf(source.Source);
        window.LoopWarningText.IsVisible = window.LoopWarningText.Text is not null;
        ShowCheckBox(window.TuneByRatiosBox, window._isTunedByRatios, window.OnTuningChanged);
        window.Transport.IsEnabled = mixer is not null;
        window.ShowChord();
        return window.ShowDialog<Sample?>(owner);
    }

    // Takes a key's note into the chord where it is not in it, and sounds the note while the key is held - or, while the chord plays, plays the chord as
    // it now is instead.
    private void OnKeyIncluded(int semitone)
    {
        bool wasChordPlaying = IsChordPlaying;
        if (_semitonesInChord.Add(semitone))
        {
            OnChordChanged();
        }

        if (!wasChordPlaying)
        {
            AuditionNote(semitone);
        }
    }

    // Takes a key's note out of the chord where it is in it, sounding nothing.
    private void OnKeyExcluded(int semitone)
    {
        if (_semitonesInChord.Remove(semitone))
        {
            OnChordChanged();
        }
    }

    // Stops a key's note as its key is let go, where it is the note that sounds rather than the chord.
    private void OnKeyReleased()
    {
        if (!_isChordAuditioned)
        {
            _chordSource?.Mixer?.Preview.Stop();
        }
    }

    // Takes the tuning the Tune By Ratios box was ticked or cleared into.
    private void OnTuningChanged(bool isTunedByRatios)
    {
        _isTunedByRatios = isTunedByRatios;
        OnChordChanged();
    }

    // Forgets the chord made last and shows the chord as it now is, making and playing it again where it was playing.
    private void OnChordChanged()
    {
        bool wasChordPlaying = IsChordPlaying;
        CancelChordMaking();
        _madeChord = null;
        ShowChord();
        if (wasChordPlaying)
        {
            PlayChord(_isChordLooped);
        }
    }

    // The line warning that a sample has no loop or loops from its very start, or null where its loop starts further in.
    private static string? LoopWarningOf(BrrSample source)
    {
        if (!source.DoesLoop)
        {
            return NoLoopWarning;
        }

        return source.LoopStartPoint == 0 ? LoopAtStartWarning : null;
    }

    // Shows which keys are in the chord, what it costs, and whether there is a chord to play and create, graying both where it would not fit in ARAM.
    private void ShowChord()
    {
        ChordKeys.SemitonesInChord = new HashSet<int>(_semitonesInChord);
        long? chordBytes = _semitonesInChord.Count > 0 && _chordSource is { } source
            ? ChordSample.AramBytesOf(source.Sample.Source, _semitonesInChord, _isTunedByRatios, source.CrossfadeLengthFraction)
            : null;
        ChordSizeText.Text = chordBytes is long bytes ? CountedNoun.Of(bytes, CountedNoun.ByteNoun) : string.Empty;
        _isChordTooBig = chordBytes > SpcReader.AramSize;
        Transport.IsPlayPauseEnabled = !_isChordTooBig;
        ButtonRow.IsConfirmEnabled = HasChordToMake;
    }

    // Plays one note of the chord in the tuning it takes there, through the sample's effects but not yet encoded, so it sounds at once.
    private void AuditionNote(int semitone)
    {
        if (_chordSource is not { Mixer: { } mixer } source)
        {
            return;
        }

        StopAudition();
        var note = ChordSample.AudioOf(source.Sample.Source, [semitone], _isTunedByRatios, source.CrossfadeLengthFraction);
        var points = source.Sample.EffectsAtRate.Apply(note.Points, source.Sample.RateHz);
        mixer.Preview.Start(points, 0, points.Length, SamplePreview.LoopPointOfWhole(note), source.Sample.PreviewPitchRegister);
    }

    // Stops whatever plays, then plays the chord as Create Chord makes it, from its start, over and over where it is asked to loop - once it has been
    // made, and where it holds a note and fits in ARAM.
    private async void PlayChord(bool isLooping)
    {
        StopAudition();
        if (_chordSource is not { Mixer: { } mixer } || !HasChordToMake)
        {
            return;
        }

        _isChordAuditioned = true;
        _isChordLooped = isLooping;
        if (await MadeChord() is not { } chord || !_isChordAuditioned)
        {
            return;
        }

        if (isLooping)
        {
            mixer.Preview.StartWholeLooped(chord);
            return;
        }

        mixer.Preview.StartWhole(chord);
    }

    // The chord as Create Chord makes it, made in the background unless the one made last is still it, or null where a change or a stop cancelled the
    // making.
    private async Task<Sample?> MadeChord()
    {
        if (_madeChord is not null || _chordSource is not { } source)
        {
            return _madeChord;
        }

        var making = new CancellationTokenSource();
        _chordMaking = making;
        var semitones = _semitonesInChord.ToList();
        bool isTunedByRatios = _isTunedByRatios;
        try
        {
            var chord = await Task.Run(() => MakeChord(source, semitones, isTunedByRatios, making.Token), making.Token);
            if (making.IsCancellationRequested)
            {
                return null;
            }

            _chordMaking = null;
            _madeChord = chord;
            return chord;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    // A chord of a sample's notes in a tuning, made into the new sample Create Chord adds.
    private static Sample MakeChord(ChordSource source, IReadOnlyCollection<int> semitones, bool isTunedByRatios, CancellationToken cancel)
    {
        var chordAudio = ChordSample.AudioOf(source.Sample.Source, semitones, isTunedByRatios, source.CrossfadeLengthFraction);
        return source.Sample.ChordSampleOf(chordAudio, cancel);
    }

    // Plays or plays again the chord from the transport's button, looping it while Shift is held.
    private void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        PlayChord(_heldShiftKey.IsHeld);
    }

    // Stops the chord or the note from the transport's button.
    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        StopAudition();
    }

    // Closes the window answering the chord, once it has been made, keeping Create Chord grayed while it is.
    private async void OnCreateChordClick(object? sender, RoutedEventArgs e)
    {
        if (!HasChordToMake)
        {
            return;
        }

        StopAudition();
        ButtonRow.IsConfirmEnabled = false;
        if (await MadeChord() is { } chord)
        {
            Close(chord);
            return;
        }

        ShowChord();
    }

    // Stops whatever plays, the chord's making included.
    private void StopAudition()
    {
        CancelChordMaking();
        _isChordAuditioned = false;
        _chordSource?.Mixer?.Preview.Stop();
    }

    // Cancels a chord being made.
    private void CancelChordMaking()
    {
        _chordMaking?.Cancel();
        _chordMaking = null;
    }

    // Shows on the transport's play button what a press starts: a loop while Shift is held, and a play otherwise.
    private void ShowStartGlyph()
    {
        Transport.ShowStart(_heldShiftKey.IsHeld);
    }

    // The window's own keys besides Escape, ahead of whatever has the focus: the play key plays the chord, looping it with Shift.
    protected override void HandleWindowKeyDown(KeyEventArgs e)
    {
        if (!Shortcuts.IsPressed(e, Shortcuts.Play, Shortcuts.PlayLooping))
        {
            return;
        }

        PlayChord(isLooping: Shortcuts.IsPressed(e, Shortcuts.PlayLooping));
        e.Handled = true;
    }

    // The sample the chords are made of, the mixer they play through or none, and the fraction of a loop a chord in piano tuning is crossfaded over.
    private sealed record ChordSource(Sample Sample, BrrMixer? Mixer, double CrossfadeLengthFraction);
}
