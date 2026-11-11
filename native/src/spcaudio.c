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
#include <string.h>

enum
{
    spc_audio_channel_count = 2,
    spc_audio_period_milliseconds = 10,
    spc_audio_period_count = 3
};

// Fills a buffer with a number of frames of interleaved stereo float32, each point in -1 to +1.
typedef void (*spc_audio_callback)(float* frames, unsigned int frame_count, void* user_data);

// Tells the managed side the device has stopped, whether it was asked to or not.
typedef void (*spc_audio_stopped_callback)(void* user_data);

// Hands the managed side one playback device: its name and whether the system plays through it by default.
typedef void (*spc_audio_device_listed_callback)(const char* device_name, int is_system_default, void* user_data);

// Visits one playback device of the list, answering non-zero to go on to the next.
typedef int (*spc_audio_device_visitor)(const ma_device_info* device_info, void* visit_state);

// One open output device and the managed callbacks it reports to.
typedef struct
{
    ma_device device;
    spc_audio_callback callback;
    spc_audio_stopped_callback stopped_callback;
    void* user_data;
} spc_audio_device;

// What finding a device by name looks for and what it found.
typedef struct
{
    const char* device_name;
    ma_device_id device_id;
    int is_found;
} spc_audio_device_search;

// What listing the devices hands each one to.
typedef struct
{
    spc_audio_device_listed_callback listed_callback;
    void* user_data;
} spc_audio_device_listing;

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

// Hands a stop of the device on to the managed callback, leaving every other notification alone.
static void spc_audio_forward_notification(const ma_device_notification* notification)
{
    spc_audio_device* output_device = (spc_audio_device*)notification->pDevice->pUserData;

    if (notification->type != ma_device_notification_type_stopped || output_device == NULL || output_device->stopped_callback == NULL)
    {
        return;
    }

    output_device->stopped_callback(output_device->user_data);
}

// Visits every playback device the system offers until the visitor answers zero, returning non-zero where they could be read.
static int spc_audio_visit_devices(spc_audio_device_visitor visitor, void* visit_state)
{
    ma_context context;
    ma_device_info* playback_infos;
    ma_uint32 playback_count;
    ma_uint32 device_index;
    int is_read;

    if (ma_context_init(NULL, 0, NULL, &context) != MA_SUCCESS)
    {
        return 0;
    }

    is_read = ma_context_get_devices(&context, &playback_infos, &playback_count, NULL, NULL) == MA_SUCCESS;
    for (device_index = 0; is_read && device_index < playback_count; device_index++)
    {
        if (!visitor(&playback_infos[device_index], visit_state))
        {
            break;
        }
    }

    ma_context_uninit(&context);
    return is_read;
}

// Keeps the id of the device whose name is searched for, answering zero once it is found.
static int spc_audio_keep_device_named(const ma_device_info* device_info, void* visit_state)
{
    spc_audio_device_search* search = (spc_audio_device_search*)visit_state;

    if (strcmp(device_info->name, search->device_name) != 0)
    {
        return 1;
    }

    search->device_id = device_info->id;
    search->is_found = 1;
    return 0;
}

// Hands one device on to the managed listing callback.
static int spc_audio_hand_device_on(const ma_device_info* device_info, void* visit_state)
{
    spc_audio_device_listing* listing = (spc_audio_device_listing*)visit_state;

    listing->listed_callback(device_info->name, device_info->isDefault ? 1 : 0, listing->user_data);
    return 1;
}

// Hands every playback device the system offers to the callback, returning non-zero where they could be read.
SPC_EXPORT int spc_audio_list_devices(spc_audio_device_listed_callback listed_callback, void* user_data)
{
    spc_audio_device_listing listing;

    if (listed_callback == NULL)
    {
        return 0;
    }

    listing.listed_callback = listed_callback;
    listing.user_data = user_data;
    return spc_audio_visit_devices(spc_audio_hand_device_on, &listing);
}

// Opens the playback device of the name given, or the system's default where the name is NULL, without starting it, at the rate asked for or the
// device's own where that is 0, and reports the rate it runs at - or returns NULL having allocated nothing, a name no device has included.
SPC_EXPORT void* spc_audio_open(spc_audio_callback callback, spc_audio_stopped_callback stopped_callback, void* user_data, const char* device_name,
    unsigned int sample_rate, unsigned int* out_sample_rate)
{
    spc_audio_device* output_device;
    spc_audio_device_search search;
    ma_device_config device_config;

    if (callback == NULL || out_sample_rate == NULL)
    {
        return NULL;
    }

    memset(&search, 0, sizeof(search));
    search.device_name = device_name;
    if (device_name != NULL && (!spc_audio_visit_devices(spc_audio_keep_device_named, &search) || !search.is_found))
    {
        return NULL;
    }

    output_device = (spc_audio_device*)calloc(1, sizeof(*output_device));
    if (output_device == NULL)
    {
        return NULL;
    }

    output_device->callback = callback;
    output_device->stopped_callback = stopped_callback;
    output_device->user_data = user_data;

    device_config = ma_device_config_init(ma_device_type_playback);
    device_config.playback.pDeviceID = search.is_found ? &search.device_id : NULL;
    device_config.playback.format = ma_format_f32;
    device_config.playback.channels = spc_audio_channel_count;
    device_config.sampleRate = sample_rate;
    device_config.periodSizeInMilliseconds = spc_audio_period_milliseconds;
    device_config.periods = spc_audio_period_count;
    device_config.dataCallback = spc_audio_forward_frames;
    device_config.notificationCallback = spc_audio_forward_notification;
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
