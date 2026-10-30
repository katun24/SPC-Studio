using Avalonia.Interactivity;
using Avalonia.Threading;
using SpcStudio.Audio;
using SpcStudio.Controls;

namespace SpcStudio;

// The sample editor's preview: the rest of the sample from the marker, or the selection, played through the song's mixer with a playhead over the waveform.
public partial class SampleEditorWindow
{
    private readonly DispatcherTimer _playheadTimer = PlayheadTimer.Build();
    private BrrMixer? _previewMixer;
    private int? _previewSampleId;
    private bool _isPreviewingSelection;
    private readonly HeldShiftKey _heldShiftKey;
    private bool _isPreviewLooped;

    // Points the window at the mixer previews play through, or at none where there is no audio device.
    public void SetPreviewMixer(BrrMixer? mixer)
    {
        _previewMixer = mixer;
    }

    // Plays, pauses or resumes the preview from the transport's button, looping it while Shift is held.
    private void OnPlayPauseClick(object? sender, RoutedEventArgs e)
    {
        TogglePreview(isLooping: _heldShiftKey.IsHeld);
    }

    // Shows on the transport's play button, while nothing plays, whether Shift is held, which turns the button into a loop.
    private void ShowShiftHeld()
    {
        if (_previewMixer?.Preview.IsPlaying != true)
        {
            ShowStartGlyph();
        }
    }

    // Shows on the transport's play button what a press does while nothing plays: a loop while Shift is held, and a play otherwise.
    private void ShowStartGlyph()
    {
        if (_heldShiftKey.IsHeld)
        {
            Transport.ShowLoop();
            return;
        }

        Transport.ShowPlay();
    }

    // Stops the preview from the transport's button and puts the marker back at the sample's start.
    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        StopPreview();
        Waveform.MoveMarkerTo(0);
    }

    // Plays, pauses or resumes the preview, whichever the transport is up to, a start or a resume asked to loop going on over and over.
    private void TogglePreview(bool isLooping)
    {
        if (_previewMixer is not { } mixer)
        {
            return;
        }

        if (mixer.Preview.IsPlaying)
        {
            mixer.Preview.Pause();
            _playheadTimer.Stop();
            ShowPlayPoint();
            ShowStartGlyph();
            return;
        }

        if (mixer.Preview.Point is not null)
        {
            ResumePreview(mixer, isLooping);
            return;
        }

        _isPreviewingSelection = false;
        StartPreview(mixer, isLooping);
    }

    // Carries a paused preview on from where it stopped, looping it from then on where it is asked to.
    private void ResumePreview(BrrMixer mixer, bool isLooping)
    {
        if (isLooping && !_isPreviewLooped)
        {
            _isPreviewLooped = true;
            RetargetPreview();
        }

        mixer.Preview.Resume();
        ShowPreviewRunning();
    }

    // Plays the selection from its start with the marker put there, over and over where it is asked to loop - from its start again where a preview is
    // playing or paused - or plays, pauses or resumes as the space bar does where nothing is selected.
    private void PlaySelection(bool isLooping)
    {
        if (_previewMixer is not { } mixer)
        {
            return;
        }

        if (!Waveform.HasSelection)
        {
            TogglePreview(isLooping);
            return;
        }

        StopPreview();
        Waveform.MoveMarkerTo(Waveform.SelectionStart);
        _isPreviewingSelection = true;
        StartPreview(mixer, isLooping);
    }

    // Starts a preview of what would be played now, over and over where it is asked to loop, and shows it running, where there is anything to play.
    private void StartPreview(BrrMixer mixer, bool isLooping)
    {
        _isPreviewLooped = isLooping;
        if (RangeToPreview() is not { } range)
        {
            return;
        }

        _previewSampleId = range.SampleId;
        mixer.Preview.Start(range.Points, range.StartPoint, range.EndPoint, range.LoopPoint, range.PitchRegister);
        ShowPreviewRunning();
    }

    // Shows the transport's pause button and sets the playhead moving.
    private void ShowPreviewRunning()
    {
        Transport.ShowPause();
        _playheadTimer.Start();
    }

    // Plays a drop-down row's sample whole from its start at its own rate, looping where it loops, for as long as the row's play button is held, in place of
    // any preview.
    private void AuditionSampleRow(int index)
    {
        if (_previewMixer is not { } mixer || _song is null)
        {
            return;
        }

        StopPreview();
        mixer.Preview.StartWhole(_song.Samples[index]);
    }

    // Re-aims a running preview at the rest of the sample from the marker that moved, jumping the audio to it.
    private void OnMarkerMoved()
    {
        _isPreviewingSelection = false;
        RetargetPreviewToMarker();
    }

    // Re-aims a running preview of the selection at the selection as it now is, or at the rest of the sample from the marker where nothing is left
    // selected, leaving the audio where it is.
    private void OnSelectionChanged()
    {
        if (!_isPreviewingSelection)
        {
            return;
        }

        _isPreviewingSelection = Waveform.HasSelection;
        RetargetPreview();
    }

    // Re-aims a running preview at what would be played now, leaving the audio where it is inside the new range.
    private void RetargetPreview()
    {
        if (RetargetedPreview() is not null)
        {
            ShowPlayPoint();
        }
    }

    // Re-aims a running preview at what would be played now and jumps the audio to the start of it.
    private void RetargetPreviewToMarker()
    {
        if (RetargetedPreview() is not { } preview)
        {
            return;
        }

        preview.SeekToStart();
        ShowPlayPoint();
    }

    // Hands a running preview what would be played now, the rack's own writes waiting for the loop to wrap, and answers the preview - or null where none
    // runs, stopping one that has nothing left to play.
    private SamplePreview? RetargetedPreview()
    {
        if (_previewMixer is not { } mixer || _previewSampleId is null)
        {
            return null;
        }

        if (RangeToPreview() is not { } range)
        {
            StopPreview();
            return null;
        }

        if (_isRackWriting)
        {
            mixer.Preview.RetargetAtLoop(range.Points, range.StartPoint, range.EndPoint, range.LoopPoint, range.PitchRegister);
        }
        else
        {
            mixer.Preview.Retarget(range.Points, range.StartPoint, range.EndPoint, range.LoopPoint, range.PitchRegister);
        }

        return mixer.Preview;
    }

    // What would be played as things stand - the selection where it is what plays, or the rest of the sample from the marker - or null where there is
    // nothing to play.
    private PreviewRange? RangeToPreview()
    {
        if (CurrentSample is not { } sample)
        {
            return null;
        }

        var points = sample.Decoded.Points;
        int start = _isPreviewingSelection ? Waveform.SelectionStart : Waveform.MarkerPoint;
        int end = _isPreviewingSelection ? Waveform.SelectionEnd : points.Length;
        if (start >= Math.Min(end, points.Length))
        {
            return null;
        }

        return new PreviewRange(sample.Id, points, start, end, LoopPointOf(sample, start), sample.PreviewPitchRegister);
    }

    // Where a preview starting at a point goes back to at its end: the selection's own start, the sample's loop point, the sample's first point for a
    // preview looped by Shift of a sample that does not loop, or nowhere for any other preview of a sample that does not loop.
    private int LoopPointOf(Sample sample, int startPoint)
    {
        bool doesSampleLoop = sample.Decoded.DoesLoop;
        if (_isPreviewingSelection && (doesSampleLoop || _isPreviewLooped))
        {
            return startPoint;
        }

        return _isPreviewLooped && !doesSampleLoop ? 0 : SamplePreview.LoopPointOfWhole(sample);
    }

    // Stops the preview and takes its playhead off the waveform.
    private void StopPreview()
    {
        _playheadTimer.Stop();
        _previewMixer?.Preview.Stop();
        _previewSampleId = null;
        Waveform.PlayPoint = null;
        ShowStartGlyph();
    }

    // Moves the playhead to where the preview has reached, and notices a preview that ran to its end.
    private void OnPlayheadTick(object? sender, EventArgs e)
    {
        if (_previewMixer is not { } mixer)
        {
            return;
        }

        ShowPlayPoint();
        if (mixer.Preview.IsPlaying)
        {
            return;
        }

        _playheadTimer.Stop();
        ShowStartGlyph();
    }

    // Draws the playhead where the preview has reached.
    private void ShowPlayPoint()
    {
        Waveform.PlayPoint = _previewMixer?.Preview.Point;
    }

    // Numbers the ruler in the time the preview takes to play a sample, at its pitch.
    private void ShowRulerTime(Sample? sample)
    {
        if (sample is null)
        {
            return;
        }

        Ruler.SetPointsPerSecond(HzOfPitchRegister(sample.PreviewPitchRegister));
    }

    // One stretch of a sample to preview: its points, where the stretch starts and ends, where it loops back to, and the PITCH register it plays at.
    private readonly record struct PreviewRange(int SampleId, short[] Points, int StartPoint, int EndPoint, int LoopPoint, int PitchRegister);
}
