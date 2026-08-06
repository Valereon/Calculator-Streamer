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
        # print(y)
        row = []
        lastHit = -1
        for x, pixel in enumerate(y):
            if pixel > 0:
                row.append(x - lastHit)
                lastHit = x
        positions.append(row)
        # print(row)
        # print(y)
    # for i in positions:
        # print(i)
    print(len(positions))
    return positions


def sendFrame(frame, controlCode, device):
    for i in frame:
        i.insert(0, controlCode)
        i.insert(1, len(i))
        # print(i)
        device.write(bytes(i))
        # print(len(bytes(i)))
    device.write(int(1).to_bytes(1, "little"))


def VarDeltaEncode(image, padding):
    totalPixels = 0
    totalBits = 0
    totalJumps = 0
    escapeJumps = 0

    for i in image:
        totalPixels += len(i)
        # averageMax += math.ceil(math.log2(max(i)))
        # print(i)
        for j in i:
            avgBits = math.ceil(totalBits / totalPixels)
            if j >= (2**avgBits + padding) - 1:  # get all posivitiles -1 for an escape code
                escapeJumps += 1
                # totalBits += math.ceil(math.log2(j))
            totalBits += math.ceil(math.log2(j))
            totalJumps += j
    return totalPixels, totalBits, totalJumps, escapeJumps


def main():
    targetResX = 255
    targetResY = 240

    screenGrab = mss.mss()
    # device = serial.Serial("COM5", 250000)

    frameSkip = 1
    deltaSkip = 1
    frameCount = 0
    prevFrame = []
    bitPadding = 2

    # setup = [3, targetResX, targetResY]
    # device.write(bytes(setup))

    while True:
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
        positions = encodeImageAsDeltas(edges)
        # positions = encodeImageAsPositions(edges)
        # frameCount += 1

        # if frameCount % frameSkip > 0:
        #     print("SKIP")
        #     continue

        # if frameCount % deltaSkip > 0:
        #     delta = buildDeltaFrame(prevFrame, positions)
        #     sendFrame(delta, 2, device)
        #     continue

        deltaAmount = 0
        maxDelta = 0
        bitPacked = 0
        nonPacked = 0
        packedRow = 0
        totalInsertions = 0
        totalRemovals = 0
        insertionsOnBitPack = 0
        turnOnImage = []
        turnOffImage = []
        combined = []
        totalBitMask = 0
        for i in range(0, len(positions) - 1):
            # totalPixels += len(positions[i])

            curRow = positions[i]
            nextRow = positions[i + 1]
            turnOff = list(set(curRow) - set(nextRow))
            turnOn = list(set(nextRow) - set(curRow))
            totalInsertions += len(turnOn)
            totalRemovals += len(turnOff)

            # if(len(turnOff) * 8 > len(curRow)):
            #     packedRow += 1
            #     bitPacked += len(curRow)
            #     insertionsOnBitPack += len(turnOn)
            #     nonPacked += len(turnOn) * 8
            # else:
            nonPacked += (len(turnOn) + len(turnOff)) * 8
            # deltaAmount += len(turnOn) + len(turnOff)
            # deltaImage.append(list(turnOn, turnOff).sort())
            turnOnImage.append(turnOn)
            turnOffImage.append(turnOff)
            bigList = turnOn + turnOff
            # bigList.sort()
            # 
            print(bigList)
            combined.append(bigList)
            # totalBitMask += len(bigList)
            # if(turnOn):
            #     totalBitMask += math.log2(len(turnOn))

        # print(turnOnImage)
        deltaImage = encodeImageAsDeltas(combined)
        totalPixels = 0
        totalBits = 0
        totalJumps = 0
        escapeJumps = 0
        pixelsTemp, bitsTemp, jumpsTemp, escJumpsTemp = VarDeltaEncode(deltaImage, bitPadding)
        # return totalPixels, totalBits, totalJumps, escapeJumps
        totalPixels += pixelsTemp
        totalBits += bitsTemp
        totalJumps += jumpsTemp
        escapeJumps += escJumpsTemp

        # deltaImage = encodeImageAsDeltas(turnOnImage)
        # pixelsTemp, bitsTemp, jumpsTemp, escJumpsTemp  = VarDeltaEncode(deltaImage, bitPadding)
        
        # totalPixels += pixelsTemp
        # totalBits += bitsTemp
        # totalJumps += jumpsTemp
        # escapeJumps += escJumpsTemp
        
        
        avgPixels = math.ceil(totalPixels / targetResY)
        avgBits = math.ceil(totalBits / totalPixels) + bitPadding
        avgJump = math.ceil(totalJumps / totalPixels)
        estBytes = ((avgBits * (totalPixels - escapeJumps)) + (escapeJumps * 8) + (escapeJumps * avgBits) + (8+8 * targetResY)) / (1024 * 8) # on legnth and length of message
        print(f"Total Pixels AVG : {avgPixels} :: Average Bits {avgBits} :: Average Jump Distance {avgJump} :: Escaped Jumps {escapeJumps} :: EST KB {round(estBytes,2)} :: total Pixels {totalPixels}")
        # estBytesWithDeltaMax = ((((escapeJumps * (avgPixels / targetResY)) + (escapeJumps * avgBits) + (avgBits * totalPixels))) / 8) / 1024
        # estBytes = (targetResX * targetResY) / (1024 * 8)

        # avgRow = (totalPixels / targetResY)
        # estBytes = (nonPacked) + (bitPacked)
        # deltaAmount = totalInsertions + totalRemovals
        # estBytes = nonPacked
        # print(f"Average Delta Between rows this frame :: {round(deltaAmount / targetResY, 2)} :: Total row average {round(avgRow, 2)}, MAX DELTA {maxDelta} :: BIT PACKED {packedRow} :: est Bytes {(round(estBytes / (8*1024),2))} :: TURNED ON {round(totalInsertions / targetResY, 2)} :: TURNED OFF {round(totalRemovals / targetResY, 2)} :: INST {insertionsOnBitPack / targetResY}")
        # print(f"Total Pixels AVG : {avgPixels} :: Average Bits {avgBits} :: Average Jump Distance {avgJump} :: Escaped Jumps {escapeJumps} :: EST BYTES {estBytesWithDeltaMax}")

        # sendFrame(positions, 0, device)

        # frameCount += 1
        print("RUNNING")


main()
