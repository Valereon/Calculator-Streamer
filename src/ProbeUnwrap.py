# This method of unwrapping uses more bits but remains loseless as well as its implementation uses more optimized calls
import numpy as np
from numba import njit
import statistics


def _point_in_polygon(x, y, polygon):
    inside = False
    n = len(polygon)
    x1, y1 = polygon[0]
    for i in range(1, n + 1):
        x2, y2 = polygon[i % n]
        if ((y1 > y) != (y2 > y)):
            x_intersect = (x2 - x1) * (y - y1) / (y2 - y1 + 1e-12) + x1
            if x < x_intersect:
                inside = not inside
        x1, y1 = x2, y2
    return inside


def generate_random_shape(height, width, kind="convex", vertices=None, seed=None):
    """
    Returns a binary numpy array of shape (height, width).
    kind: "convex" or "concave"
    """
    rng = np.random.default_rng(seed)

    if vertices is None:
        vertices = max(6, int(min(height, width) / 4))

    cx = rng.uniform(width * 0.35, width * 0.65)
    cy = rng.uniform(height * 0.35, height * 0.65)

    max_radius = min(width, height) * 0.35
    min_radius = max(2.0, max_radius * 0.45)

    angles = np.sort(rng.uniform(0, 2 * np.pi, vertices))

    if kind.lower() == "convex":
        radii = rng.uniform(min_radius, max_radius, vertices)
    elif kind.lower() == "concave":
        radii = rng.uniform(min_radius, max_radius, vertices)
        radii[::2] *= 0.45
    else:
        raise ValueError("kind must be 'convex' or 'concave'")

    polygon = []
    for angle, radius in zip(angles, radii):
        x = cx + radius * np.cos(angle)
        y = cy + radius * np.sin(angle)
        polygon.append((x, y))

    mask = np.zeros((height, width), dtype=np.uint8)

    for row in range(height):
        for col in range(width):
            if _point_in_polygon(col + 0.5, row + 0.5, polygon):
                mask[row, col] = 1

    return mask


def printShape(localShape):
    for i in localShape:
        print(i)


def fitArray(shape):
    row_mask = np.any(shape != 0, axis=1) 
    col_mask = np.any(shape != 0, axis=0)
    shape = shape[row_mask][:, col_mask]
    return shape

@njit
def ProbeRight(shape):
    deltas = []
    lastAdd = 0
    prevAdd = 0
    startY = len(shape[0]) - 1
    for x in range(len(shape)):
        for y in range(len(shape[0])-1,-1,-1):
            place = shape[x][y]            
            if(place == 1):
                lastAdd = startY - y
                break
        differenceDelta = prevAdd - lastAdd
        deltas.append(differenceDelta)
        prevAdd = lastAdd
    # print(deltas)
    return deltas
    
@njit         
def ProbeLeft(shape):
    deltas = []
    lastAdd = 0
    prevAdd = 0
    for x in range(len(shape)):
        for y in range(len(shape[0])):
            place = shape[x][y]            
            if(place == 1):
                lastAdd = y
                break
        differenceDelta = prevAdd - lastAdd
        deltas.append(differenceDelta)
        prevAdd = lastAdd
    # print(deltas)
    return deltas


@njit
def ProbeTop(shape):
    deltas = []
    lastAdd = 0
    prevAdd = 0
    for x in range(len(shape[0])):
        for y in range(len(shape)):
            place = shape[y][x]            
            if(place == 1):
                lastAdd = y
                break
        differenceDelta = prevAdd - lastAdd
        deltas.append(differenceDelta)
        prevAdd = lastAdd
    return deltas

@njit
def ProbeBottom(shape):
    deltas = []
    lastAdd = 0
    prevAdd = 0
    startY = len(shape) - 1
    for x in range(len(shape[0])):
        for y in range(len(shape) - 1, -1, -1):
            place = shape[y][x]            
            if(place == 1):
                lastAdd = startY - y
                break
        differenceDelta = prevAdd - lastAdd
        deltas.append(differenceDelta)
        prevAdd = lastAdd
    return deltas

@njit
def DecodeShape(deltaList, width,height):
    cords = []

    top = deltaList[:width]
    right = deltaList[width:height+width]
    bottom = deltaList[width+height:width+height+width]
    left = deltaList[width+width+height:]
    
    
    y = 0
    for index, delta in enumerate(top):
        y += -delta
        cords.append((index,y))
    
    x = width-1
    for index, delta in enumerate(right):
        x += delta
        cords.append((x,index))
        
    y = height-1
    for index, delta in enumerate(bottom):
        y += delta
        cords.append((index,y))
    
    x = 0
    for index, delta in enumerate(left):
        x += -delta
        cords.append((x,index))

    return cords


shape = generate_random_shape(50, 50,vertices=200, kind="convex", seed=352)
# printShape(shape)
print("STARTED RUNNING")
shape = fitArray(shape)
# print()
printShape(shape)


topDelta = ProbeTop(shape)
rightDelta = ProbeRight(shape)
bottomDelta = ProbeBottom(shape)
leftDelta = ProbeLeft(shape)

combined = topDelta + rightDelta + bottomDelta + leftDelta
print(combined)
# print(topDelta)
# print(rightDelta)
# print(bottomDelta)
# print(leftDelta)

stats = {}

for i in combined:
    if i not in stats:
        stats[i] = 0
    stats[i] += 1

sortedStats = dict(sorted(stats.items(), key=lambda item: item[1])[::-1])

print(sortedStats)

cords = DecodeShape(combined, len(shape[0]), len(shape))



import numpy as np
import matplotlib.pyplot as plt

def plot_coordinates_as_image(coords, width=None, height=None, cmap='gray',
                              figsize=(6,6), ax=None):
    """
    Rasterize coords into a binary image and display with imshow.
    coords: list of (x,y)
    width/height: optional - if None, inferred from max coords + 1
    """
    if not coords:
        raise ValueError("coords empty")
    xs = [c[0] for c in coords]
    ys = [c[1] for c in coords]
    w = width if width is not None else max(xs) + 1
    h = height if height is not None else max(ys) + 1
    img = np.full((h, w), 255, dtype=np.uint8)
    for x, y in coords:
        if 0 <= x < w and 0 <= y < h:
            img[y, x] = 0
    own_ax = ax is None
    if own_ax:
        fig, ax = plt.subplots(figsize=figsize)
    ax.imshow(img, cmap=cmap, origin='upper', interpolation='nearest')
    ax.set_xticks([])
    ax.set_yticks([])
    if own_ax:
        plt.show()
    return ax



plot_coordinates_as_image(cords)