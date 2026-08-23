# This method of unwrapping uses more bits but remains loseless as well as its implementation uses more optimized calls



import numpy as np

shape = np.array([
    [0, 1, 1, 1, 0, 1, 1, 1, 1],
    [1, 1, 0, 1, 1, 1, 0, 0, 1],
    [1, 0, 0, 0, 0, 0, 0, 0, 1],
    [1, 1, 0, 0, 0, 0, 1, 1, 1],
    [0, 1, 0, 0, 0, 0, 1, 0, 0],
    [0, 1, 0, 0, 0, 0, 1, 0, 0],
    [0, 1, 0, 0, 0, 0, 1, 0, 0],
    [0, 1, 1, 1, 1, 1, 1, 0, 0],
])




def printShape(localShape):
    for i in localShape:
        print(i)

def rotate90CCW(pivotX, pivotY, x, y):
    newX = -y + pivotY + pivotX
    newY = x - pivotX + pivotY
    return (newX, newY)






def EncodeShape(shape):
    deltas = []
    lastAdd = 0
    prevAdd = 0
    lastX = 0
    for z in range(4):
        rotated = np.rot90(shape, k=z)
        rows, cols = rotated.shape
        # print("ROTATED " + str(z))
        # printShape(shape)
        # print()
        # print()
        inBetween = []
        lastAdd = 0
        prevAdd = 0
        for j in range(cols):
            for i in range(rows):
                place = rotated[i][j]            
                if(place == 1):
                    lastAdd = i
                    break
            differenceDelta = prevAdd - lastAdd
            inBetween.append(differenceDelta)
            prevAdd = lastAdd
        deltas.append(inBetween)
    print()
    print()
    print()

    printShape(deltas)
    return deltas



def DecodeShape(deltas,rows,cols):
    decoded = np.zeros((rows,cols))
    # this rot part needs to be done via cordiante translation on the calculator as its faster than array rotting and i dont have access to 2d arrays on the calc!
    for z in range(4):
        rotated = np.rot90(decoded, k=z)
        rows, cols = rotated.shape
        y = rows - 1
        for i in range(len(deltas[z])):
            y += deltas[z][i]
            rotated[y][i] = 1
    print()
    print()
    print()
    return decoded
    
            



printShape(shape)

                
deltas = EncodeShape(shape)
rows,cols = shape.shape

recon = DecodeShape(deltas, rows, cols)

print()
print()
print()

printShape(shape)
print()
printShape(recon)
        
        