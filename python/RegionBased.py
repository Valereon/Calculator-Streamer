import sys
import math

import cv2
import matplotlib.pyplot as plt
import mss
import numpy as np
import pygetwindow as pgw
from numba import njit

def getScreenShot(screenGrab):
    window = pgw.getActiveWindow()
    if window is None:
        return None
    bbox = (window.left, window.top, window.right, window.bottom)
    img = screenGrab.grab(bbox)
    return img



def display_region_comparison(greyImage, labeled, num_regions,levels):
    # 1. Fast random colormap look-up table (LUT)
    np.random.seed(42)
    # Generate random RGB colors for all regions + 1 for background
    colors = np.random.randint(0, 256, (num_regions + 1, 3), dtype=np.uint8)
    colors[0] = [0, 0, 0]  # Background is black

    # 2. VECTORIZED COLOR MAPPING (0-loop instant mapping)
    # Direct advanced indexing replaces your entire for-loop
    region_map = colors[labeled]

    # 3. Fast Plotting
    fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(12, 6))
    
    # Using interpolation='nearest' speeds up Matplotlib rendering significantly
    ax1.imshow(greyImage, cmap='gray', vmin=0, vmax=levels, interpolation='nearest')
    ax1.set_title('Grayscale Image')
    ax1.axis('off')
    
    ax2.imshow(region_map, interpolation='nearest')
    ax2.set_title(f'Region Map - {num_regions} regions')
    ax2.axis('off')
    
    plt.tight_layout()
    plt.show()



def buildData(grid, step):
    grid_uint8 = grid.astype(np.uint8)
    labeled_grid = np.zeros(grid.shape, dtype=np.int32)
    current_id = 1
    
    all_stats = []
    allCentroids = []
    
    for val in range(int(step)):
        mask = (grid_uint8 == val).astype(np.uint8)
        if not np.any(mask):
            continue
            
        num_labels, labels, stats, centroids = cv2.connectedComponentsWithStats(mask, connectivity=4)
        
        if num_labels > 1:
            # Shift label IDs so every region across all values gets a unique ID
            np.add(labels, current_id - 1, out=labels, where=(labels > 0))
            np.copyto(labeled_grid, labels, where=(labels > 0))
            
            # Slice out index 0 (the background for this specific mask) 
            # and accumulate the foreground region stats
            all_stats.append(stats[1:])
            allCentroids.append(centroids)
            
            current_id += (num_labels - 1)
    

    if all_stats:
        total_stats = np.vstack(all_stats)
    else:
        total_stats = np.empty((0, 5), dtype=np.int32)
    
    
    if(allCentroids):
        totalCentroids = np.vstack(allCentroids)
    else:
        totalCentroids = np.empty((0, 2), dtype=np.int32)
        
    total_regions = current_id - 1
    return labeled_grid, total_regions, total_stats, totalCentroids



def buildSpatialHash(data,cellSize):
    
    # {
    #   (cellX,cellY) : [top,left,width,height,area,mask,(centerX, centerY),lux]
    #}
    
    spatialHash = {
        
    }
    
    for i in range(len(data)):
        centerTup = data[i][6]
        newX = int(centerTup[0] // cellSize)
        newY = int(centerTup[1] // cellSize)
        cell = (newX, newY)
        
        if(cell not in spatialHash):
            spatialHash[cell] = []
        
        spatialHash[cell].append(data[i])
    
    # print(len(spatialHash.keys()))
    return spatialHash


#issue! this does not account for neighboring nodes in the spatial hash leading to harsh boundaries
def mergeViaSpatialHash(spatialHash, n=10, l=5):
    updated = []
    for key in spatialHash:
        underN = []
        for value in spatialHash[key]:
            if(value[4] < n):
                value.append(False)
                underN.append(value)
            else:
                updated.append(value)
                
        
        current = None
        for pos in underN:
            current = pos
            if(current[8] == True):
                continue
                
            for check in range(len(underN)):
                checkAgainst = underN[check]
                if(checkAgainst[8] == True):
                    continue
                if abs(int(current[7]) - int(checkAgainst[7])) > l:
                    continue
                
                result = checkAABB(current, checkAgainst)
                if(result):
                    updateRegion(current, checkAgainst)
                    checkAgainst[8] = True
                    
        for i in underN:
            if(i[8] == False):
                updated.append(i)
    return updated

def updateRegion(reg1, reg2):
    reg1[0] = max(reg1[0], reg2[0]) # top
    reg1[1] = max(reg1[1], reg2[1]) # left
    reg1[2] += reg2[2] # width?
    reg1[3] += reg2[3] # height?
    reg1[4] += reg2[4] # area
    
    if(type(reg1[5]) is int): # if first mask then make the mask a list
        reg1[5] = [reg1[5], reg2[5]] # mask
    else:
        reg1[5].append(reg2[5]) # else just append to the mask list
        

def checkAABB(pos1, pos2):
    if(pos1[0] < pos2[0] + pos2[2] + 1 and pos1[0] + pos1[2] + 1 > pos2[0] and  # noqa: SIM103
       pos1[1] < pos2[1] + pos2[3] + 1 and pos1[1] + pos1[3] + 1 > pos2[1]):
        return True
    return False


def prepareData(stats, labelAmount, centroids, greyImage):
    finalList = stats.tolist()
    # print(labelAmount)
    for i in range(labelAmount):
        sliced = finalList[i]
        sliced.append(i)
        center = centroids[i]
        
        intCenter = (int(center[0]), int(center[1]))
        # print(intCenter)
        lux = greyImage[int(center[1]),int(center[0])] #numpy is y,x
        sliced.append(intCenter)
        sliced.append(lux)
        
    return finalList
        


def mergeSmallAreas(labels, stats, labelAmount, centroids, greyImage):
    data = prepareData(stats, labelAmount, centroids, greyImage)
    spatialHash = buildSpatialHash(data,20)
    newData = mergeViaSpatialHash(spatialHash, 5, 5)
    count = len([x for x in newData if x[4] < 20])
    print(data[50])
    print(f"Total : {len(data)}, Under 20 : {count}, Total Above 20 : {len(data) - count}") 

def quantImage(image, levels=32):
    lower = np.percentile(image, 5)
    upper = np.percentile(image, 95)
    range_val = upper - lower
    range_val = max(upper - lower, 1.0)

    remapped = np.clip((image - lower) * (255.0 / range_val), 0, 255)
    
    factor = 255.0 / (levels - 1)
    quantized = np.round(remapped / factor)
    quantized = quantized.astype(np.uint8)
    return quantized



def main():
    targetResX = 255
    targetResY = 240

    cellSize = 10

    levels = 16

    screenGrab = mss.MSS()

    while(True):
        screenShot = getScreenShot(screenGrab)
        if screenShot is None:
            continue
        
        rgb_image = np.array(screenShot)
        
        try:
            ogGreyImage = cv2.cvtColor(rgb_image, cv2.COLOR_RGB2GRAY)
        except cv2.error:
            continue
        
        
        greyImage = cv2.resize(ogGreyImage, (1920, 1080), interpolation=cv2.INTER_AREA)
        
        # greyImage = cv2.resize(greyImage, (0, 0), fx=scalingX, fy=scalingY)
        # greyImage = cv2.GaussianBlur(greyImage, (3,3), 1)
        quantized = quantImage(greyImage, levels)

        
        labeled_grid, total_regions, stats, centers = buildData(quantized, levels-1)
        mergeSmallAreas(labeled_grid, stats, total_regions, centers, greyImage)

        
        # display_region_comparison(quantized, labeled_grid, total_regions, levels)
        # print(total_regions)



        # Quantization formula

        # cv2.imshow("Sobel Live", quantized)
        # # print(final)

        # if cv2.waitKey(1) & 0xFF == ord("q"):
        #     break
        # sock.sendto(bytes("END", "utf-8"), ("127.0.0.1", 25566))


main()
