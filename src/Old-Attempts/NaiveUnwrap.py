# This method of unwrapping does not account for shapes that have concave geometry that will simply be accepted as a loss for speed and lower cost




shape = [
    [0, 1, 1, 1, 0, 1, 1, 1, 1],
    [1, 1, 0, 1, 1, 1, 0, 0, 1],
    [1, 0, 0, 0, 0, 0, 0, 0, 1],
    [1, 1, 0, 0, 0, 0, 1, 1, 1],
    [0, 1, 0, 0, 0, 0, 1, 0, 0],
    [0, 1, 0, 0, 0, 0, 1, 0, 0],
    [0, 1, 0, 0, 0, 0, 1, 0, 0],
    [0, 1, 0, 0, 0, 0, 1, 0, 0],
    [0, 1, 1, 1, 1, 1, 1, 0, 0],
]

sliced = []
unfold = 3


def rotate90CCW(pivotX, pivotY, x, y):
    newX = -y + pivotY + pivotX
    newY = x - pivotX + pivotY
    return (newX, newY)


def rotate180CCW(pivotX, pivotY, x, y):
    newX = -x + 2 * pivotX
    newY = -y + 2 * pivotY
    return (newX, newY)


def printShape(localShape):
    for i in localShape:
        print(i)




printShape(shape)

print()
print()
print()
sliced = shape[unfold::]
upper = shape[:unfold:]
difference = len(sliced) - len(upper)
print(difference)
for i in range(difference):
    upper.insert(0,[0]*len(upper[0]))
    
    

sliced = sliced[::-1]

final = []
for i in range(len(upper)):
    final.append(upper[i] + sliced[i])
printShape(final)



deltas = []
lastAdd = 0
prevAdd = 0
for j in range(len(final[0])):
    for i in range(len(final)):
        place = final[i][j]            
        if(place == 1):
            lastAdd = i
            break
    differenceDelta = prevAdd - lastAdd
    deltas.append(differenceDelta)
    prevAdd = lastAdd
print()
print()
print()

print(deltas)
            
        
    
        
        
        