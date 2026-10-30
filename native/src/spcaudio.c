#define MA_NO_DECODING
#define MA_NO_ENCODING
#define MA_NO_GENERATION
#define MA_NO_RESOURCE_MANAGER
#define MA_NO_NODE_GRAPH
#define MA_NO_ENGINE

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include "spc_interop.h"

#include <stdlib.h>

enum
{
    spc_audio_channel_count = 2,
    spc_audio_sample_rate_of_device = 0,
    spc_audio_period_milliseconds = 10,
    spc_audio_period_count = 3
};

// Fills a buffer with a number of frames of interleaved stereo float32, each point in -1 to +1.
typedef void (*spc_audio_callback)(float* frames, unsigned int frame_count, void* user_data);

// One open output device and the managed callback that fills it.
typedef struct
{
    ma_device device;
    spc_audio_callback callback;
    void* user_data;
} spc_audio_device;

// Hands miniaudio's request for frames on to the managed callback.
static void spc_audio_forward_frames(ma_device* miniaudio_device, void* output_frames, const void* input_frames, ma_uint32 frame_count)
{
    spc_audio_device* output_device = (spc_audio_device*)miniaudio_device->pUserData;

    (void)input_frames;

    if (output_device == NULL || output_device->callback == NULL)
    {
        return;
    }

    output_device->callback((float*)output_frames, frame_count, output_device->user_data);
}

// Opens the default playback device without starting it and reports the rate it runs at, or returns NULL having allocated nothing.
SPC_EXPORT void* spc_audio_open(spc_audio_callback callback, void* user_data, unsigned int* out_sample_rate)
{
    spc_audio_device* output_device;
    ma_device_config device_config;

    if (callback == NULL || out_sample_rate == NULL)
    {
        return NULL;
    }

    output_device = (spc_audio_device*)calloc(1, sizeof(*output_device));
    if (output_device == NULL)
    {
        return NULL;
    }

    output_device->callback = callback;
    output_device->user_data = user_data;

    device_config = ma_device_config_init(ma_device_type_playback);
    device_config.playback.format = ma_format_f32;
    device_config.playback.channels = spc_audio_channel_count;
    device_config.sampleRate = spc_audio_sample_rate_of_device;
    device_config.periodSizeInMilliseconds = spc_audio_period_milliseconds;
    device_config.periods = spc_audio_period_count;
    device_config.dataCallback = spc_audio_forward_frames;
    device_config.pUserData = output_device;

    if (ma_device_init(NULL, &device_config, &output_device->device) != MA_SUCCESS)
    {
        free(output_device);
        return NULL;
    }

    *out_sample_rate = output_device->device.sampleRate;
    return output_device;
}

// Starts the device asking for frames, returning non-zero on success.
SPC_EXPORT int spc_audio_start(void* device_handle)
{
    spc_audio_device* output_device = (spc_audio_device*)device_handle;

    if (output_device == NULL)
    {
        return 0;
    }

    return ma_device_start(&output_device->device) == MA_SUCCESS;
}

// Stops the device asking for frames, returning non-zero on success, which a device already stopped is.
SPC_EXPORT int spc_audio_stop(void* device_handle)
{
    spc_audio_device* output_device = (spc_audio_device*)device_handle;

    if (output_device == NULL)
    {
        return 0;
    }

    if (!ma_device_is_started(&output_device->device))
    {
        return 1;
    }

    return ma_device_stop(&output_device->device) == MA_SUCCESS;
}

// Stops and frees the device, no callback being in flight once it returns.
SPC_EXPORT void spc_audio_close(void* device_handle)
{
    spc_audio_device* output_device = (spc_audio_device*)device_handle;

    if (output_device == NULL)
    {
        return;
    }

    ma_device_uninit(&output_device->device);
    free(output_device);
}
