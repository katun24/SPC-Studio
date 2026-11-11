using SpcStudio.Model;

namespace SpcStudio.Import;

// Every song in an AddmusicK-patched ROM, written out as song files, one song failing leaving the rest to be written.
public static class RomExtract
{
    // Reads a ROM and writes one song file per song the filter wants into a folder, keeping each song's warnings, reporting progress after each song and stopping when cancelled, mid-song included.
    public static IReadOnlyList<RomExtractResult> WriteSongFiles(string romPath, string folder, Func<RomSong, bool> isSongWanted, Action<int, int>? progress = null, CancellationToken cancel = default)
    {
        var rom = RomMusic.Read(File.ReadAllBytes(romPath));
        var wantedSongs = rom.Songs.Where(isSongWanted).ToList();
        var results = new List<RomExtractResult>();
        progress?.Invoke(0, wantedSongs.Count);
        foreach (var song in wantedSongs)
        {
            string songFilePath = Path.Combine(folder, $"song-{song.Slot:X2}{SongFile.Extension}");
            var warnings = new List<string>();
            try
            {
                cancel.ThrowIfCancellationRequested();
                var file = RomTrackReader.Read(song, rom.EchoFilters);

                // The bytes of the sample this reader gave a name, looked up by the number in it.
                byte[]? SampleBytesNamed(string name)
                {
                    for (int number = 0; number < rom.Samples.Count; number++)
                    {
                        if (RomTrackReader.SampleName(number) == name)
                        {
                            return rom.Samples[number]?.Brr;
                        }
                    }

                    return null;
                }

                var built = MmlImporter.Build(file, SampleBytesNamed, null, warnings.Add, null, cancel);
                built.SampleGroup = Song.OptimizedSampleGroup;
                SongFile.Save(songFilePath, built);
                results.Add(new RomExtractResult(song.Slot, songFilePath, null, warnings));
            }
            catch (MmlImportException refusal)
            {
                results.Add(new RomExtractResult(song.Slot, null, refusal.Reason, []));
            }
            catch (OperationCanceledException)
            {
                break;
            }

            progress?.Invoke(results.Count, wantedSongs.Count);
        }

        return results;
    }
}
