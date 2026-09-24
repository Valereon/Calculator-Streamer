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

uint8_t srl_buf[1024];


int baud = 250000;
uint8_t y = 0;
bool deltaMode = false;
bool upscaling = false;
uint8_t maxY;
uint8_t maxX;

int *prevFrame = NULL; // will become 2d array of pixel positions

static usb_error_t handle_usb_event(usb_event_t event, void *event_data,
                                    usb_callback_data_t *callback_data __attribute__((unused)))
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

void HandleControl(uint8_t code, uint8_t in_buf[])
{
    if (code == 1)
    {
        y = 0;
        deltaMode = false;
        gfx_SwapDraw();
        gfx_FillScreen(255);
    }
    else if (code == 2)
    {
        deltaMode = true;
    }
    else if (code == 3)
    {
        maxX = in_buf[1];
        maxY = in_buf[2];

        if (prevFrame == NULL)
        {
            prevFrame = malloc(maxY * maxX * sizeof(uint8_t));
        }
    }
    else if (code == 4)
    {
        upscaling = !upscaling;
    }
}

bool DoesPixelExistInFrame(uint8_t x)
{
    uint8_t value;
    for (size_t j = 0; j < maxX; j++)
    {
        value = prevFrame[(y * maxX) + j];
        if (value == 0)
        {
            return false;
        }
        if (x > value)
        {
            return false;
        }
        if (x == value)
        {
            return true;
        }
    }
    return false;
}

void DrawFrame(uint8_t in_buf[])
{
    uint8_t length = in_buf[1];
    uint8_t x = 0;
    uint8_t index;
    if (deltaMode)
    {
        bool same;
        int *newFrame = malloc(maxY * maxX * sizeof(uint8_t));
        for (size_t i = 2; i < length; i++)
        {
            x = in_buf[i];
            same = DoesPixelExistInFrame(x);
            if (same)
                continue;

            index = (y * maxX) + i - 2;
            newFrame[index] = x;

            gfx_SetPixel((uint24_t)x, (uint8_t)y);
        }
        memcpy(prevFrame, newFrame, sizeof(newFrame) / sizeof(uint8_t));
    }
    else
    {
        for (size_t i = 2; i <= length; i++)
        {
            x = in_buf[i];

            // index = (y * maxX) + i - 2;
            // prevFrame[index] = x;

            gfx_SetPixel((uint24_t)x, (uint8_t)y);
        }
    }
    y++;
}

int main(void)
{
    os_ClrHome();
    gfx_Begin();
    gfx_SetDrawBuffer();
    gfx_FillScreen(255);
    gfx_SwapDraw();
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

        uint8_t in_buf[1024];
        int bytes_read = srl_Read(&srl, in_buf, sizeof in_buf);

        if (bytes_read < 0)
            break;

        uint8_t control = in_buf[0];

        if (control != 0)
        {
            HandleControl(control, in_buf);
            continue;
        }

        DrawFrame(in_buf);

    } while (!kb_IsDown(kb_KeyClear));

    usb_Cleanup();
    gfx_End();
    return 0;
}