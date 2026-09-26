import random

import matplotlib.pyplot as plt
import numpy as np

xWidth = 100
yHeight = 100
depth = 100


data = np.array(
    [[random.randint(0, depth) for _ in range(yHeight)] for _ in range(xWidth)]
)
x, y = np.meshgrid(np.arange(xWidth), np.arange(yHeight), indexing="ij")

figure = plt.figure(figsize=(10, 7))
axis = figure.add_subplot(111, projection="3d")
axis.scatter(
    x.ravel(),
    y.ravel(),
    data.ravel(),
    c=data.ravel(),
    cmap="viridis",
    s=4,
    alpha=0.7,
)

axis.set_title("Random 3D Data")
axis.set_xlabel("X")
axis.set_ylabel("Y")
axis.set_zlabel("Depth")
figure.tight_layout()
plt.show()