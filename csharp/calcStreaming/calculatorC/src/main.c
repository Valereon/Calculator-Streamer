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
} USBCallBackSerial;

typedef struct
{
    int index;
    int count;
    int buffer;
} Accumulator;

typedef struct
{
    int payloadSize;     // so there is 3 bytes of overhead this iS NOT INCLUDING THAT
    int framingOverhead; // this is the overhead
    int totalSize;       // THIS is the total byte count including all framing
} Framing;

typedef struct
{
    uint8_t *data;
    Accumulator *accum;
    Framing *frame;
    bool hasEnded;
} Context;

bool has_srl_device = false;

static usb_error_t handle_usb_event(usb_event_t event, void *event_data, usb_callback_data_t *callback_data __attribute__((unused)))
{
    USBCallBackSerial *usbContext = (USBCallBackSerial *)callback_data;

    usb_error_t err;
    /* Delegate to srl USB callback */
    if ((err = srl_UsbEventCallback(event, event_data, callback_data)) != USB_SUCCESS)
        return err;
    /* Enable newly connected devices */
    if (event == USB_DEVICE_CONNECTED_EVENT && !(usb_GetRole() & USB_ROLE_DEVICE))
    {
        usb_device_t device = event_data;
        printf("device connected\n");
        usb_ResetDevice(device);
    }

    /* Call srl_Open on newly enabled device, if there is not currently a serial device in use */
    if (event == USB_HOST_CONFIGURE_EVENT || (event == USB_DEVICE_ENABLED_EVENT && !(usb_GetRole() & USB_ROLE_DEVICE)))
    {

        /* If we already have a serial device, ignore the new one */
        if (has_srl_device)
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
        srl_error_t error = srl_Open(&usbContext->ctx->srl, device, usbContext->srlBuff, usbContext->ctx->bufferSize, SRL_INTERFACE_ANY, usbContext->ctx->baud);
        if (error)
        {
            /* Print the error code to the homescreen */
            printf("Error %d initting serial\n", error);
            return USB_SUCCESS;
        }

        printf("serial initialized\n");

        has_srl_device = true;
    }

    if (event == USB_DEVICE_DISCONNECTED_EVENT)
    {
        usb_device_t device = event_data;
        if (device == usbContext->ctx->srl.dev)
        {
            printf("device disconnected\n");
            srl_Close(&usbContext->ctx->srl);
            has_srl_device = false;
        }
    }

    return USB_SUCCESS;
}

void EnsureBits(Context *ctx, int len)
{
    if (ctx->hasEnded)
        return;

    while (ctx->accum->count < len)
    {
        if (ctx->accum->index >= ctx->frame->totalSize)
        {
            ctx->hasEnded = true;
            return;
        }

        uint8_t byte = ctx->data[ctx->accum->index];
        ctx->accum->buffer |= (byte << ctx->accum->count); // move the incoming byte up bitcount len to fill unused bits
        ctx->accum->index += 1;
        ctx->accum->count += 8;
    }
}

int GetNextBits(Context *ctx, int len)
{
    if (ctx->hasEnded)
        return 1;

    if (len > ctx->accum->count)
    {
        EnsureBits(ctx, len);
    }
    if (len > ctx->accum->count)
    {
        ctx->hasEnded = true;
        return 1;
    }

    int result = ctx->accum->buffer & ((1U << len) - 1);
    ctx->accum->buffer >>= len;
    ctx->accum->count -= len;
    return result;
}

int *DecodeBitStream(Context *ctx, int totalNumbers) // total numbers here should be multipled by 4 for the 4 identical length bitstreams
{
    int *results = malloc(totalNumbers * sizeof(int));
    for (int i = 0; i < totalNumbers; i++)
    {
        int count = 0;
        while (GetNextBits(ctx, 1) == 0 && !ctx->hasEnded)
        {
            count += 1;
        }
        if (ctx->hasEnded)
            return results;
        uint16_t fixed = ((1u << count) | GetNextBits(ctx, count)) - 1;
        int number = (fixed >> 1) ^ (-(fixed & 1));
        results[i] = number;
    }
    return results;
}

void DrawLines(int *inputArray, int length)
{

    int predictedDx = 0;
    int predictedDy = 0;

    int endX = 0;
    int endY = 0;

    int predictedX;
    int predictedY;

    int startX;
    int startY;

    int actualDx;
    int actualDy;

    int renderStartX;
    int renderStartY;

    int renderDx;
    int renderDy;

    int doubleLen = length * 2;
    int tripleLen = length * 3;

    for (int i = 0; i < length; i++)
    {
        predictedX = endX + predictedDx;
        predictedY = endY + predictedDy;

        startX = predictedX + inputArray[i];
        startY = predictedY + inputArray[length + i];

        actualDx = inputArray[doubleLen + i];
        actualDy = inputArray[tripleLen + i];

        renderStartX = startX << 1;
        renderStartY = startY << 1;
        renderDx = actualDx << 1;
        renderDy = actualDy << 1;

        if (actualDx != 0)
        {
            if (renderDx < 0)
            {
                renderDx = -renderDx;
                renderStartX -= renderDx;
            }

            gfx_HorizLine_NoClip(renderStartX, renderStartY, renderDx);
        }
        else if (actualDy != 0)
        {
            if (renderDy < 0)
            {
                renderDy = -renderDy;
                renderStartY -= renderDy;
            }
            gfx_VertLine_NoClip(renderStartX, renderStartY, renderDy);
        }

        predictedDx = actualDx;
        predictedDy = actualDy;
        endX = startX + actualDx;
        endY = startY + actualDy;
    }
}

void Reset(Context *ctx, SerialContext *serialCtx, uint8_t *srlBuffPtr, int *bytesRead)
{
    *bytesRead = 0;
    ctx->frame->payloadSize = 0;
    ctx->accum->count = 0;
    ctx->accum->buffer = 0;
    ctx->hasEnded = false;
    memset(ctx->data, 0, serialCtx->bufferSize);
    // memset(srlBuffPtr, 0, serialCtx->bufferSize);
}

bool SetupFrame(Context *ctx, SerialContext *serialCtx, int bytesRead)
{
    if (bytesRead < 3)
    {
        if (bytesRead > 0 && ctx->data[0] != 255)
        {
            return false;
        }
        return true;
    }

    if (ctx->frame->payloadSize == 0 && ctx->data[0] == 255)
    {
        ctx->frame->payloadSize = ((uint16_t)ctx->data[2] << 8) | ctx->data[1];
        ctx->frame->totalSize = ctx->frame->payloadSize + ctx->frame->framingOverhead;
        if (ctx->frame->totalSize > serialCtx->bufferSize)
        {
            return false;
        }
    }
    if (ctx->data[0] != 255)
    {
        return false;
    }
    return true;
}

int ReadSerial(Context *ctx, SerialContext *serialCtx, int bytesRead)
{
    int n = srl_Read(&serialCtx->srl, ctx->data + bytesRead, serialCtx->bufferSize - bytesRead);

    if (n < 0)
        return n;
    return n;
}

void SendACK(SerialContext *serialCtx)
{
    srl_Write(&serialCtx->srl, (uint8_t[]){200, 220, 250}, 3);
}

int StartConnection(USBCallBackSerial *callbackContext)
{
    os_ClrHome();
    gfx_Begin();
    gfx_SetDrawBuffer();
    gfx_FillScreen(0);
    gfx_SwapDraw();
    gfx_SetColor(255);
    const usb_standard_descriptors_t *desc = srl_GetCDCStandardDescriptors();
    usb_error_t usb_error = usb_Init(handle_usb_event, callbackContext, desc, USB_DEFAULT_INIT_FLAGS);
    if (usb_error)
    {
        usb_Cleanup();
        printf("usb init error %u\n", usb_error);
        do
            kb_Scan();
        while (!kb_IsDown(kb_KeyClear));
        return 1;
    }
    return 0;
}

int main(void)
{

    int bytesRead = 0;
    // ALL THREE OF THESE
    // THESE NEED TO BE THE SAME SIZE!!!!!
    int bufferSize = 4096;
    uint8_t srlBuff[4096];
    uint8_t buff[4096];
    // END

    SerialContext serialCtx = {.baud = 115200, .bufferSize = bufferSize};
    Accumulator accum = {.buffer = 0, .count = 0, .index = 0};
    Framing frame = {.framingOverhead = 3, .payloadSize = 0, .totalSize = 0};
    USBCallBackSerial callBackContext = {.ctx = &serialCtx, .srlBuff = srlBuff};

    Context ctx = {.data = buff, .hasEnded = false, .accum = &accum, .frame = &frame};

    int code = StartConnection(&callBackContext);
    if (code == 1)
        return 1;

    do
    {
        kb_Scan();

        usb_HandleEvents();

        if (!has_srl_device)
            continue;

        int amountRead = ReadSerial(&ctx, &serialCtx, bytesRead);

        if (amountRead < 0)
            break;
        bytesRead += amountRead;

        if (!SetupFrame(&ctx, &serialCtx, bytesRead))
            Reset(&ctx, &serialCtx, srlBuff, &bytesRead);
        else if (bytesRead < ctx.frame->totalSize)
            continue;

        int pointLengths = GetNextBits(&ctx, 16);
        int *data = DecodeBitStream(&ctx, pointLengths * 4);
        DrawLines(data, pointLengths);

        gfx_SwapDraw();
        gfx_FillScreen(0);

        free(data);
        data = NULL;

        SendACK(&serialCtx);
        usb_HandleEvents();
        Reset(&ctx, &serialCtx, srlBuff, &bytesRead);

    } while (!kb_IsDown(kb_KeyClear));

    usb_Cleanup();
    gfx_End();
    return 0;
}