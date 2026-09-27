import socket
from pyray import *

host = "0.0.0.0"
unityPort = 25566
unitySock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
unitySock.bind(("127.0.0.1", unityPort))
    


init_window(255, 240, "Hello")
while not window_should_close():
    begin_drawing()
    clear_background(BLACK)    

    while True:
        data = unitySock.recv(1000000)
        command = data.decode()

        if(command == "END"):
            # print("End")
            break
        
        points = command.split(";")
        # print(command)
        prevPointX = int(points[0].split(',')[0])
        prevPointY = int(points[0].split(',')[1])
        
        for point in points:
            if(point == "ENDLINE"):
                prevPointX = -1
                prevPointY = -1
                continue
            cords = point.split(",")
            if(not cords[0] or not cords[1]):
                continue
            if(prevPointX == -1 and prevPointY == -1):
                prevPointX = int(cords[0])
                prevPointY = int(cords[1])
            x = int(cords[0])
            y = int(cords[1])
            draw_line(prevPointX,prevPointY,x,y,WHITE)
            prevPointX = x
            prevPointY = y

    end_drawing()
close_window()


    
