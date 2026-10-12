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

srl_device_t srl;

bool has_srl_device = false;

uint8_t srl_buf[4096];
uint8_t buff[4096];

int baud = 256000;

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
        srl_error_t error = srl_Open(&srl, device, srl_buf, sizeof srl_buf, SRL_INTERFACE_ANY, baud);
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
        if (device == srl.dev)
        {
            printf("device disconnected\n");
            srl_Close(&srl);
            has_srl_device = false;
        }
    }

    return USB_SUCCESS;
}

int buffer = 0;
int streamIndex = 0;
int payloadSize = 2;
int bitCount = 0;
int bytes_read = 0;
int scaling = 0;
int paddingScaling = 0;
int doubleThresh = 4;
bool hasReachedEnd = false;

int UnZigZag(int n)
{
    return (n >> 1) ^ (-(n & 1));
}

void EnsureBits(int len)
{
    if (hasReachedEnd)
        return;

    while (bitCount < len)
    {
        if (streamIndex > payloadSize)
        {
            hasReachedEnd = true;
            return;
        }

        uint8_t byte = buff[streamIndex];
        buffer = buffer | (byte << bitCount); // move the incoming byte up bitcount len to fill unused bits
        streamIndex += 1;
        bitCount += 8;
    }
}
void Consume(int len)
{
    buffer = buffer >> len; // move buffer down len bits
    bitCount -= len;        // sub that
}

int GetNextBits(int len)
{
    if (hasReachedEnd)
        return 1;

    if (len > bitCount)
    {
        EnsureBits(len);
    }
    if (len > bitCount)
    {
        hasReachedEnd = true;
        return 1;
    }

    uint32_t result = buffer & ((1U << len) - 1);
    Consume(len);
    return result; // return bits of len by shifting the bit len to the right then subtracting one to fill all lower bits to make a mask
}

int GetNextBit()
{
    return GetNextBits(1);
}

int *DecodeBitStream(int totalNumbers) // total numbers here should be multipled by 4 for the 4 identical length bitstreams
{
    int *results = malloc(totalNumbers * sizeof(int));
    for (int i = 0; i < totalNumbers; i++)
    {
        int count = 0;
        while (GetNextBit() == 0 && !hasReachedEnd)
        {
            count += 1;
        }
        if (hasReachedEnd)
            return results;
        uint16_t fixed = (1u << count) | GetNextBits(count);
        int number = UnZigZag(fixed - 1);
        results[i] = number;
    }
    return results;
}

void DrawLines(int *inputArray, int length)
{

    int predictedDx = 0;
    int predictedDy = 0;

    int endX = inputArray[0];
    int endY = inputArray[length];

    for (int i = 1; i < length; i++)
    {
        int predictedX = endX + predictedDx;
        int predictedY = endY + predictedDy;

        int startX = predictedX + inputArray[i];
        int startY = predictedY + inputArray[length + i];

        int actualDx = inputArray[length * 2 + i];
        int actualDy = inputArray[length * 3 + i];

        if (actualDx != 0)
        { // line is horizotnal

            int len = abs(actualDx);
            int padding = 0;

            if (len <= doubleThresh)
            {
                padding = 2;
            }
            else
            {
                padding = 0;
            }
            int renderStartX = (startX << scaling) - ((padding << paddingScaling) >> 2);
            int renderStartY = startY << scaling;
            int renderDx = (len << scaling) + (padding << paddingScaling);

            if (renderDx < 0)
            {
                renderDx = -renderDx;
                renderStartX -= renderDx;
            }
            // this will NOT draw lines with a negatvie width, so you have to account for that
            gfx_HorizLine_NoClip(renderStartX, renderStartY, renderDx);
        }
        else if (actualDy != 0)
        { // line is vertical

            int len = abs(actualDx);
            int padding = 0;
            if (len <= doubleThresh)
            {
                padding = 2;
            }
            else
            {
                padding = 1;
            }
            int renderStartX = (startX << scaling) - ((padding << paddingScaling) >> 2);
            int renderStartY = (startY << scaling);
            int renderDy = (len << scaling) + (padding << paddingScaling);
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

void Reset()
{
    streamIndex = 0;
    payloadSize = 2;
    bitCount = 0;
    buffer = 0;
    bytes_read = 0;
    hasReachedEnd = false;
    memset(buff, 0, sizeof(buff));
    memset(srl_buf, 0, sizeof(srl_buf));
}

void HandleKeyPresses()
{
    if (kb_IsDown(kb_Key1))
    {
        scaling = 0;
    }
    else if (kb_IsDown(kb_Key2))
    {
        scaling = 1;
    }
    else if (kb_IsDown(kb_Key3))
    {
        scaling = 2;
    }
    else if (kb_IsDown(kb_KeyUp))
    {
        doubleThresh += 1;
    }
    else if (kb_IsDown(kb_KeyDown))
    {
        doubleThresh -= 1;
    }
    else if (kb_IsDown(kb_KeyLeft))
    {
        paddingScaling -= 1;
    }
    else if (kb_IsDown(kb_KeyRight))
    {
        paddingScaling += 1;
    }
}

bool PrepPayloadSize()
{
    if (payloadSize == 2 && buff[0] == 255)
    {
        GetNextBits(8);
        payloadSize = (uint16_t)GetNextBits(16) + 2;
        if (payloadSize > sizeof(buff))
        {
            Reset();
            return false;
        }
    }
    if (bytes_read < payloadSize)
    {
        return false;
    }
    if (buff[0] != 255)
    {
        Reset();
        return false;
    }
    return true;
}

bool ReadSerial()
{
    int n = srl_Read(&srl, buff + bytes_read, sizeof(buff) - bytes_read);

    if (n < 0)
        return false;

    bytes_read += n;
    return true;
}

int main(void)
{
    os_ClrHome();
    gfx_Begin();
    gfx_SetDrawBuffer();
    gfx_FillScreen(0);
    gfx_SwapDraw();
    gfx_SetColor(255);
    const usb_standard_descriptors_t *desc = srl_GetCDCStandardDescriptors();
    /* Initialize the USB driver with our event handler and the serial device descriptors */
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

    do
    {
        kb_Scan();

        usb_HandleEvents();

        if (!has_srl_device)
            continue;

        if (!ReadSerial())
            break;

        if (!PrepPayloadSize())
            continue;

        int pointLenghts = GetNextBits(16);
        int *data = DecodeBitStream(pointLenghts * 4); // this is *4 because there is 4 arrays in the bitstream and they are all the same length so its just a single int of how big one is
        DrawLines(data, pointLenghts);

        gfx_SwapDraw();
        gfx_FillScreen(0);

        free(data);
        data = NULL;
        Reset();

    } while (!kb_IsDown(kb_KeyClear));

    usb_Cleanup();
    gfx_End();
    return 0;
}