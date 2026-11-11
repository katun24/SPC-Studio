#include "spc.h"
#include "spc_interop.h"

#include <stdlib.h>
#include <string.h>

enum
{
    spc_play_skip_scratch_samples = 4096
};

// One opened dump: the emulator, its output filter, the file a restart reloads, and what every reload puts back.
typedef struct
{
    SNES_SPC* emulator;
    SPC_Filter* output_filter;
    unsigned char* file_bytes;
    spc_byte_count file_size;
    int mute_mask;
    int playback_tempo;
    short skip_scratch[spc_play_skip_scratch_samples];
} spc_play_player;

// Whether a count of samples is one the emulator plays: above zero and even.
static int spc_play_is_playable_sample_count(int sample_count)
{
    return sample_count > 0 && sample_count % 2 == 0;
}

// Whether a dump was handed over: its bytes there and its size above zero.
static int spc_play_is_given_dump(const void* dump_bytes, spc_byte_count dump_size)
{
    return dump_bytes != NULL && dump_size > 0;
}

// Loads a dump into the emulator and clears what outlives one, returning non-zero on success.
static int spc_play_load_dump(spc_play_player* player, const void* dump_bytes, spc_byte_count dump_size)
{
    if (spc_load_spc(player->emulator, dump_bytes, dump_size) != NULL)
    {
        return 0;
    }

    spc_clear_echo(player->emulator);
    spc_filter_clear(player->output_filter);
    spc_mute_voices(player->emulator, player->mute_mask);

    return 1;
}

// Loads the held file again, returning non-zero on success.
static int spc_play_load_held_file(spc_play_player* player)
{
    return spc_play_load_dump(player, player->file_bytes, player->file_size);
}

// Frees a player and whatever of it was built, a partly built one included.
SPC_EXPORT void spc_play_close(void* player_handle)
{
    spc_play_player* player = (spc_play_player*)player_handle;

    if (player == NULL)
    {
        return;
    }

    if (player->output_filter != NULL)
    {
        spc_filter_delete(player->output_filter);
    }

    if (player->emulator != NULL)
    {
        spc_delete(player->emulator);
    }

    free(player->file_bytes);
    free(player);
}

// Builds an empty player's parts and loads a copy of a dump into them, returning non-zero on success and leaving whatever was built to the caller.
static int spc_play_build(spc_play_player* player, const void* file_bytes, spc_byte_count file_size)
{
    player->file_bytes = (unsigned char*)malloc((size_t)file_size);
    player->emulator = spc_new();
    player->output_filter = spc_filter_new();

    if (player->file_bytes == NULL || player->emulator == NULL || player->output_filter == NULL)
    {
        return 0;
    }

    memcpy(player->file_bytes, file_bytes, (size_t)file_size);
    player->file_size = file_size;
    player->playback_tempo = spc_tempo_unit;

    return spc_play_load_held_file(player);
}

// Opens a dump held in memory at its first sample, or returns NULL having left nothing allocated where the emulator refuses it.
SPC_EXPORT void* spc_play_open(const void* file_bytes, spc_byte_count file_size)
{
    spc_play_player* player;

    if (!spc_play_is_given_dump(file_bytes, file_size))
    {
        return NULL;
    }

    player = (spc_play_player*)calloc(1, sizeof(*player));
    if (player == NULL)
    {
        return NULL;
    }

    if (!spc_play_build(player, file_bytes, file_size))
    {
        spc_play_close(player);
        return NULL;
    }

    return player;
}

// Takes the song back to its first sample, returning non-zero on success.
SPC_EXPORT int spc_play_restart(void* player_handle)
{
    spc_play_player* player = (spc_play_player*)player_handle;

    if (player == NULL)
    {
        return 0;
    }

    return spc_play_load_held_file(player);
}

// Plays a count of samples through the output filter into a buffer, as interleaved stereo 16-bit at the chip's own rate, returning non-zero on success.
SPC_EXPORT int spc_play_read(void* player_handle, short* out_samples, int sample_count)
{
    spc_play_player* player = (spc_play_player*)player_handle;

    if (player == NULL || out_samples == NULL || !spc_play_is_playable_sample_count(sample_count))
    {
        return 0;
    }

    if (spc_play(player->emulator, sample_count, out_samples) != NULL)
    {
        return 0;
    }

    spc_filter_run(player->output_filter, out_samples, sample_count);

    return 1;
}

// Plays a count of samples at the song's own speed and throws the audio away, returning non-zero on success.
SPC_EXPORT int spc_play_skip(void* player_handle, int sample_count)
{
    spc_play_player* player = (spc_play_player*)player_handle;
    int has_failed = 0;

    if (player == NULL || !spc_play_is_playable_sample_count(sample_count))
    {
        return 0;
    }

    spc_set_tempo(player->emulator, spc_tempo_unit);

    while (sample_count > 0)
    {
        int samples_this_round = sample_count < spc_play_skip_scratch_samples ? sample_count : spc_play_skip_scratch_samples;

        if (spc_play(player->emulator, samples_this_round, player->skip_scratch) != NULL)
        {
            has_failed = 1;
            break;
        }

        sample_count -= samples_this_round;
    }

    spc_set_tempo(player->emulator, player->playback_tempo);

    return !has_failed;
}

// Runs the chip's timers at a multiple of their own speed, counted in the library's tempo unit.
SPC_EXPORT void spc_play_set_tempo(void* player_handle, int playback_tempo)
{
    spc_play_player* player = (spc_play_player*)player_handle;

    if (player == NULL)
    {
        return;
    }

    player->playback_tempo = playback_tempo;
    spc_set_tempo(player->emulator, playback_tempo);
}

// Loads a state a snapshot took, leaving alone the held file a restart reloads, returning non-zero on success.
SPC_EXPORT int spc_play_load_state(void* player_handle, const void* state_bytes, spc_byte_count state_size)
{
    spc_play_player* player = (spc_play_player*)player_handle;

    if (player == NULL || !spc_play_is_given_dump(state_bytes, state_size))
    {
        return 0;
    }

    return spc_play_load_dump(player, state_bytes, state_size);
}

// Keys off and holds silent the voices a mask names, a bit per voice from voice 0 up, and sounds the rest.
SPC_EXPORT void spc_play_mute(void* player_handle, int mute_mask)
{
    spc_play_player* player = (spc_play_player*)player_handle;

    if (player == NULL)
    {
        return;
    }

    player->mute_mask = mute_mask;
    spc_mute_voices(player->emulator, mute_mask);
}

// Whether any voice has keyed on since the last time this was asked.
SPC_EXPORT int spc_play_check_kon(void* player_handle)
{
    spc_play_player* player = (spc_play_player*)player_handle;

    if (player == NULL)
    {
        return 0;
    }

    return spc_check_kon(player->emulator);
}

// Writes the emulator's current state out as a whole SPC file, returning non-zero on success.
SPC_EXPORT int spc_play_snapshot(void* player_handle, void* out_file_bytes, spc_byte_count out_size)
{
    spc_play_player* player = (spc_play_player*)player_handle;

    if (player == NULL || out_file_bytes == NULL || out_size < (spc_byte_count)spc_file_size)
    {
        return 0;
    }

    spc_init_header(out_file_bytes);
    spc_save_spc(player->emulator, out_file_bytes);

    return 1;
}

// How many bytes a snapshot writes.
SPC_EXPORT spc_byte_count spc_play_snapshot_size(void)
{
    return (spc_byte_count)spc_file_size;
}
