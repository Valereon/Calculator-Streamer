import cv2
import numpy as np
import pygetwindow as pgw
import mss
from numba import njit
import serial
import math


# Control Codes
# 0 : do nothing but draw the frame i send you
# 1 : End of Frame
# 2 : this frame is a delta frame based on the last frame
# 3 : Set Frame Size for storing previous frame
#   - When this is set you will need to send a fresh frame before you can send deltas
# 4 : toggle NN Upscaling
#   - when enabled will scale the input image to fit the screen of 320x240
def getScreenShot(screenGrab):
    window = pgw.getActiveWindow()
    if window is None:
        return None
    bbox = (window.left, window.top, window.right, window.bottom)
    img = screenGrab.grab(bbox)
    if(not img):
        return None
    width = window.right - window.left
    height = window.bottom - window.top
    return img, width, height


# lowkey think this was gemeni
def auto_canny(image, sigma=0.33):
    v = np.median(image)

    lower = int(max(0, (1.0 - sigma) * v))
    upper = int(min(255, (1.0 + sigma) * v))

    return lower, upper


@njit
def encodeImageAsPositions(image):
    positions = []
    for y in image:
        row = []
        for x, pixel in enumerate(y):
            if pixel > 0:
                row.append(x + 1)
        positions.append(row)
    return positions


def encodeImageAsDeltas(image):
    positions = []
    for y in image:
        row = []
        lastHit = -1
        for x, pixel in enumerate(y):
            if pixel > 0:
                row.append(x - lastHit)
                lastHit = x
        positions.append(row)
    print(len(positions))
    return positions


def sendFrame(frame, controlCode, device):
    sizeOf = 0
    device.write(int(1).to_bytes(1, "little"))
    for i in frame:
        i.insert(0, len(i))
        i.insert(0, controlCode)
        device.write(bytes(i))
        sizeOf += len(bytes(i))
    print(sizeOf//1024)


def main():
    targetResX = 255
    targetResY = 240

    screenGrab = mss.mss()
    device = serial.Serial("COM5", 250000)

    frameSkip = 4
    deltaSkip = 1
    frameCount = 0
    # prevFrame = []

    setup = [3, targetResX, targetResY]
    device.write(bytes(setup))

    while True:
        # if frameCount % frameSkip > 0:
        #     # print("SKIP")
        #     frameCount += 1
        #     continue
        screenShot, inputX, inputY = getScreenShot(screenGrab)
        scaledX = targetResX / inputX
        scaledY = targetResY / inputY
        if screenShot is None:
            continue

        rgb_image = np.array(screenShot)
        greyImage = cv2.cvtColor(rgb_image, cv2.COLOR_RGB2GRAY)
        scaledImage = cv2.resize(
            greyImage, (0, 0), fx=scaledX, fy=scaledY, interpolation=cv2.INTER_AREA
        )
        croppedImage = cv2.resize(
            scaledImage, (targetResX, targetResY), interpolation=cv2.INTER_AREA
        )

        lower, upper = auto_canny(croppedImage)
        edges = cv2.Canny(croppedImage, lower, upper)
        # positions = encodeImageAsDeltas(edges)
        positions = encodeImageAsPositions(edges)

        # if frameCount % deltaSkip > 0:
        #     delta = buildDeltaFrame(prevFrame, positions)
        #     sendFrame(delta, 2, device)
        #     continue


        sendFrame(positions, 0, device)
        print("RUNNING")
main()