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
    uint16_t totalSize; // so there is 3 bytes of overhead this iS NOT INCLUDING THAT
} Framing;

typedef struct
{
    uint8_t *data;
    Framing *frame;
    bool hasEnded;
} Context;

USBCallBackSerial *globalSerialCtx;
bool has_srl_device = false;

#define BEFORE_DATA_LENGTH 4
#define BUFFER_SIZE 8192
#define MAX_LINES 1000
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
        srl_error_t error = srl_Open(&globalSerialCtx->ctx->srl, device, globalSerialCtx->srlBuff, globalSerialCtx->ctx->bufferSize, SRL_INTERFACE_ANY, globalSerialCtx->ctx->baud);
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
        if (device == globalSerialCtx->ctx->srl.dev)
        {
            printf("device disconnected\n");
            srl_Close(&globalSerialCtx->ctx->srl);
            has_srl_device = false;
        }
    }

    return USB_SUCCESS;
}

void DrawLines(Context *ctx, int horizLength, int vertLength)
{
    int start = 3+BEFORE_DATA_LENGTH;
    for (int i = start; i < horizLength; i += 3)
    {
        uint8_t startX = ctx->data[i];
        uint8_t startY = ctx->data[i + 1];
        uint8_t dX = ctx->data[i + 2];
        gfx_HorizLine_NoClip(startX, startY, dX);
    }

    int vertOffset = start + horizLength;
    for (int i = vertOffset; i < vertOffset+vertLength; i += 3)
    {
        uint8_t startX = ctx->data[i];
        uint8_t startY = ctx->data[i + 1];
        uint8_t dY = ctx->data[i + 2];
        gfx_VertLine_NoClip(startX, startY, dY);
    }
}

void Reset(Context *ctx, SerialContext *serialCtx, uint8_t *srlBuffPtr, int *bytesRead)
{
    *bytesRead = 0;
    ctx->frame->totalSize = 0;
    ctx->frame->totalSize = 0;
    ctx->hasEnded = false;
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
    if (ctx->data[0] != 255)
    {
        return false;
    }
    if (ctx->frame->totalSize == 0 && ctx->data[0] == 255)
    {
        ctx->frame->totalSize = ((uint16_t)ctx->data[2] << 8) | ctx->data[1];
        if (ctx->frame->totalSize > serialCtx->bufferSize)
        {
            return false;
        }
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

int StartConnection()
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

    SerialContext serialCtx = {.baud = 115200, .bufferSize = BUFFER_SIZE};
    Framing frame = {.totalSize = 0};
    USBCallBackSerial callBackContext = {.ctx = &serialCtx, .srlBuff = srlBuff};
    globalSerialCtx = &callBackContext;
    Context ctx = {.data = buff, .hasEnded = false, .frame = &frame};

    int code = StartConnection();
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
        
        // gfx_SetTextXY(10, 10);
        // gfx_PrintInt(bytesRead, 1);
        // gfx_SetTextXY(10, 20);
        // gfx_PrintInt(ctx.frame->totalSize, 1);
        // gfx_SetTextXY(10, 30);
        // gfx_PrintInt(amountRead, 1);
        // gfx_SwapDraw();
        // gfx_FillScreen(255);

        if (!SetupFrame(&ctx, &serialCtx, bytesRead))
            Reset(&ctx, &serialCtx, srlBuff, &bytesRead);
        else if (bytesRead < ctx.frame->totalSize)
            continue;

        int horizLength = ((uint16_t)ctx.data[4] << 8) | ctx.data[3];
        int vertLength = ((uint16_t)ctx.data[6] << 8) | ctx.data[5];
        int totalLines = (horizLength + vertLength) / 6;
        if(totalLines > MAX_LINES)
            continue;
        DrawLines(&ctx, horizLength, vertLength);

        gfx_SwapDraw();
        gfx_FillScreen(0);

        SendACK(&serialCtx);
        Reset(&ctx, &serialCtx, srlBuff, &bytesRead);

    } while (!kb_IsDown(kb_KeyClear));

    usb_Cleanup();
    gfx_End();
    return 0;
}