/*
 *--------------------------------------
 * Program Name:
 * Author:
 * License:
 * Description:
 *--------------------------------------
 */

#include <srldrvce.h>
#include <debug.h>
#include <keypadc.h>
#include <stdbool.h>
#include <string.h>
#include <tice.h>
#include <graphx.h>
#include <sys/timers.h>

#define BUFFER_SIZE 5000
#define MAGIC_BYTES_LENGTH 3

#define PAYLOAD_INDEX 9
#define FRAMING_WIDTH 5

#define HORIZ_OFFSET 5
#define VERT_OFFSET 7
#define MESSAGE_SIZE_OFFSET 3

// magic, magic, magic, len1, len2, hor1, hor2, vert1, vert2
typedef struct
{
    int baud;
    srl_device_t srl;
    int bufferSize;

} SerialContext;

typedef struct
{
    SerialContext *ctx;
    uint8_t *srlBuff;
    bool has_srl_device;
} USBCallBackSerial;

typedef struct
{
    uint16_t totalSize;
    uint8_t startMagicBytes[MAGIC_BYTES_LENGTH];
    uint8_t ackMagicBytes[MAGIC_BYTES_LENGTH];
    uint16_t horizLength;
    uint16_t vertLength;
} Framing;

typedef struct
{
    uint8_t *data;
    Framing *frame;
} Context;

typedef enum
{
    FRAME_OKAY,
    FRAME_MALFORMED,
    FRAME_ERROR
} FrameResults;

typedef enum
{
    FRAME_LENGTH_OKAY,
    FRAME_LENGTH_OVERSIZED,
    FRAME_HEADER_INCORRECT
} FrameSize;

USBCallBackSerial *globalSerialCtx;

static uint8_t srlBuff[BUFFER_SIZE];
static uint8_t buff[BUFFER_SIZE];

static usb_error_t handle_usb_event(usb_event_t event, void *event_data, usb_callback_data_t *callback_data __attribute__((unused)))
{

    usb_error_t err;
    /* Delegate to srl USB callback */
    if ((err = srl_UsbEventCallback(event, event_data, callback_data)) != USB_SUCCESS)
        return err;
    /* Enable newly connected devices */
    if (event == USB_DEVICE_CONNECTED_EVENT && !(usb_GetRole() & USB_ROLE_DEVICE))
    {
        usb_device_t device = event_data;

        usb_ResetDevice(device);
    }

    /* Call srl_Open on newly enabled device, if there is not currently a serial device in use */
    if (event == USB_HOST_CONFIGURE_EVENT || (event == USB_DEVICE_ENABLED_EVENT && !(usb_GetRole() & USB_ROLE_DEVICE)))
    {

        /* If we already have a serial device, ignore the new one */
        if (globalSerialCtx->has_srl_device)
            return USB_SUCCESS;

        usb_device_t device;
        if (event == USB_HOST_CONFIGURE_EVENT)
        {
            /* Use the device representing the USB host. */
            device = usb_FindDevice(NULL, NULL, USB_SKIP_HUBS);
            if (device == NULL)
                return USB_SUCCESS;
        }
        else
        {
            /* Use the newly enabled device */
            device = event_data;
        }

        /* Initialize the serial library with the newly attached device */
        srl_error_t error = srl_Open(&globalSerialCtx->ctx->srl, device, globalSerialCtx->srlBuff, globalSerialCtx->ctx->bufferSize, SRL_INTERFACE_ANY, globalSerialCtx->ctx->baud);
        if (error)
        {
            /* Print the error code to the homescreen */

            return USB_SUCCESS;
        }

        globalSerialCtx->has_srl_device = true;
    }

    if (event == USB_DEVICE_DISCONNECTED_EVENT)
    {
        usb_device_t device = event_data;
        if (device == globalSerialCtx->ctx->srl.dev)
        {

            srl_Close(&globalSerialCtx->ctx->srl);
            globalSerialCtx->has_srl_device = false;
        }
    }

    return USB_SUCCESS;
}

void DrawLines(Context *ctx)
{
    uint16_t horizLength = ctx->frame->horizLength;
    uint16_t vertLength = ctx->frame->vertLength;

    uint16_t start = PAYLOAD_INDEX;
    for (uint16_t i = start; i < start + horizLength; i += 3)
    {
        uint8_t startX = ctx->data[i];
        uint8_t startY = ctx->data[i + 1];
        uint8_t dX = ctx->data[i + 2];
        gfx_HorizLine_NoClip(startX, startY, dX);
    }

    uint16_t vertOffset = start + horizLength;
    for (uint16_t i = vertOffset; i < vertOffset + vertLength; i += 3)
    {
        uint8_t startX = ctx->data[i];
        uint8_t startY = ctx->data[i + 1];
        uint8_t dY = ctx->data[i + 2];
        gfx_VertLine_NoClip(startX, startY, dY);
    }
}

void SendACK(SerialContext *serialCtx, Framing *frame)
{
    srl_Write(&serialCtx->srl, frame->ackMagicBytes, MAGIC_BYTES_LENGTH);
}

void Reset(Context *ctx, int *bytesRead)
{
    *bytesRead = 0;
    ctx->frame->totalSize = 0;
    ctx->frame->vertLength = 0;
    ctx->frame->horizLength = 0;
}

void DecodeFraming(Context *ctx)
{
    uint16_t horizLength = ctx->data[HORIZ_OFFSET] | ((uint16_t)ctx->data[HORIZ_OFFSET + 1] << 8);
    uint16_t vertLength = ctx->data[VERT_OFFSET] | ((uint16_t)ctx->data[VERT_OFFSET + 1] << 8);

    ctx->frame->horizLength = horizLength;
    ctx->frame->vertLength = vertLength;
}

FrameSize ParseHeader(Context *ctx)
{
    uint8_t *magicHeader = ctx->frame->startMagicBytes;
    bool isHeaderCorrect = ctx->data[0] == magicHeader[0] && ctx->data[1] == magicHeader[1] && ctx->data[2] == magicHeader[2];
    if (isHeaderCorrect)
    {
        uint16_t totalSize = ctx->data[MESSAGE_SIZE_OFFSET] | ((uint16_t)ctx->data[MESSAGE_SIZE_OFFSET + 1] << 8);
        ctx->frame->totalSize = totalSize;
        if (totalSize > BUFFER_SIZE)
            return FRAME_LENGTH_OVERSIZED;

        return FRAME_LENGTH_OKAY;
    }
    return FRAME_HEADER_INCORRECT;
}

int ReadSerial(Context *ctx, SerialContext *serialCtx, int bytesRead)
{
    return srl_Read(&serialCtx->srl, ctx->data + bytesRead, serialCtx->bufferSize - bytesRead);
}

bool StartConnection()
{
    os_ClrHome();
    gfx_Begin();
    gfx_SetDrawBuffer();
    gfx_FillScreen(0);
    gfx_SwapDraw();
    gfx_SetColor(255);
    const usb_standard_descriptors_t *desc = srl_GetCDCStandardDescriptors();
    usb_error_t usb_error = usb_Init(handle_usb_event, NULL, desc, USB_DEFAULT_INIT_FLAGS);
    if (usb_error)
    {
        usb_Cleanup();

        do
            kb_Scan();
        while (!kb_IsDown(kb_KeyClear));
        return false;
    }
    return true;
}

FrameResults GatherFrame(Context *ctx, SerialContext *serialCtx, int *bytesRead)
{
    ctx->frame->totalSize = FRAMING_WIDTH;

    while (*bytesRead < FRAMING_WIDTH)
    {
        if (!globalSerialCtx->has_srl_device)
            return FRAME_ERROR;

        usb_HandleEvents();

        int amountRead = ReadSerial(ctx, serialCtx, *bytesRead);
        if (amountRead < 0)
            return FRAME_ERROR;

        *bytesRead += amountRead;
    }

    FrameSize size = ParseHeader(ctx);
    if (size != FRAME_LENGTH_OKAY)
        return FRAME_MALFORMED;

    while (*bytesRead < ctx->frame->totalSize)
    {
        if (!globalSerialCtx->has_srl_device)
            return FRAME_ERROR;

        usb_HandleEvents();

        int amountRead = ReadSerial(ctx, serialCtx, *bytesRead);
        if (amountRead < 0)
            return FRAME_ERROR;

        *bytesRead += amountRead;
    }
    return FRAME_OKAY;
}

int main(void)
{

    int bytesRead = 0;

    SerialContext serialCtx = {.baud = 115200, .bufferSize = BUFFER_SIZE};
    Framing frame = {.totalSize = 0, .ackMagicBytes = {200, 220, 250}, .startMagicBytes = {255, 200, 255}};
    USBCallBackSerial callBackContext = {.ctx = &serialCtx, .srlBuff = srlBuff};
    globalSerialCtx = &callBackContext;
    Context ctx = {.data = buff, .frame = &frame};

    bool isReady = StartConnection();
    if (!isReady)
        return 1;

    do
    {
        kb_Scan();

        usb_HandleEvents();

        if (!globalSerialCtx->has_srl_device)
            continue;

        FrameResults result = GatherFrame(&ctx, &serialCtx, &bytesRead);
        if (result == FRAME_MALFORMED)
        {
            SendACK(&serialCtx, ctx.frame);
            Reset(&ctx, &bytesRead);
            continue;
        }
        else if (result == FRAME_ERROR)
            break;

        DecodeFraming(&ctx);
        DrawLines(&ctx);

        gfx_SwapDraw();
        gfx_FillScreen(0);
        SendACK(&serialCtx, ctx.frame);
        Reset(&ctx, &bytesRead);

    } while (!kb_IsDown(kb_KeyClear));

    usb_Cleanup();
    gfx_End();
    return 0;
}