import cv2
import numpy as np
import pygetwindow as pgw
import mss
from numba import njit
import socket
import statistics


# GEEKS FOR GEEKS https://www.geeksforgeeks.org/python/run-length-encoding-python/

totalPixelsAllTime = 0
totalFrames = 0
def encodeImageAsPositions(image):
    global totalFrames, totalPixelsAllTime
    lengths = []
    totalThisFrame = 0
    positions = []
    for y in image:
        row = []
        for x, pixel in enumerate(y):
            if pixel > 0:
                row.append(x + 1)
        positions.append(row)
        totalThisFrame += len(row)
        lengths.append(len(row))
        # print(row)
    totalPixelsAllTime += totalThisFrame
    totalFrames += 1
    # print(
    #     f"median row is {statistics.median(lengths)} :: average row is {round(statistics.mean(lengths),2)} :: min {min(lengths)} :: max {max(lengths)} :: estBits {round((totalThisFrame * 8) / (8 * 1024),2)} :: estTotalBytes is {((totalPixelsAllTime * 8)) / (8*1024)}"
    # )
    return positions


def encodeImageAsDeltas(image):
    positions = []
    lengths = []
    amountOfTwo = 0
    totalThisFrame = 0
    for y in image:
        row = []
        lastHit = -1
        for x, pixel in enumerate(y):
            if pixel > 0:
                delta = x - lastHit
                if delta == 2:
                    amountOfTwo += 1
                row.append(delta)
                lastHit = x
        totalThisFrame += len(row)
        lengths.append(len(row))
        positions.append(row)
        print(row)
    # print(lengths)
    print(
         f"median row is {statistics.median(lengths)} :: average row is {round(statistics.mean(lengths),2)} :: min {min(lengths)} :: max {max(lengths)} :: estBits {round((totalThisFrame * 8) / (8 * 1024),2)} :: estTotalBytes is {((totalPixelsAllTime * 8)) / (8*1024)}"
    )
    # )

    return positions


def getScreenShot(screenGrab):
    window = pgw.getActiveWindow()
    if window is None:
        return None
    bbox = (window.left, window.top, window.right, window.bottom)
    img = screenGrab.grab(bbox)
    return img


def auto_canny(image, sigma=0.33):
    v = np.median(image)

    lower = int(max(0, (1.0 - sigma) * v))
    upper = int(min(255, (1.0 + sigma) * v))

    return lower, upper


def main():
    targetResX = 255
    targetResY = 240

    currentResX = 2560
    currentResY = 1440

    firstStepX = 1920 / currentResX
    firstStepY = 1080 / currentResY

    scalingX = targetResX / currentResX
    scalingY = targetResY / currentResY

    screenGrab = mss.mss()
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)  # UDP

    for i in range(60):
        screenShot = getScreenShot(screenGrab)
        if screenShot is None:
            continue
        rgb_image = np.array(screenShot)

        greyImage = cv2.cvtColor(rgb_image, cv2.COLOR_BGR2GRAY)
        greyImage = cv2.resize(
            greyImage,
            (0, 0),
            fx=firstStepX,
            fy=firstStepY,
            interpolation=cv2.INTER_AREA,
        )
        
        
        greyImage = cv2.GaussianBlur(greyImage, (25, 25), 0)
        greyImage = cv2.GaussianBlur(greyImage, (25, 25), 0)
        # greyImage = cv2.GaussianBlur(greyImage, (5,5), 0)
        greyImage = cv2.resize(greyImage, (0, 0), fx=scalingX, fy=scalingY)
        # greyImage = cv2.GaussianBlur(greyImage, (5,5), 0)

        lower, upper = auto_canny(greyImage)
        edges = cv2.Canny(greyImage, lower, upper)
        final = encodeImageAsPositions(edges)
        final2 = encodeImageAsDeltas(final)
        cv2.imshow("Sobel Live", edges)
        # print(final)

        if cv2.waitKey(1) & 0xFF == ord("q"):
            break
        # sock.sendto(bytes("END", "utf-8"), ("127.0.0.1", 25566))
        print("Ran")


main()
