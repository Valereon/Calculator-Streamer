import socket

from pyray import *

host = "0.0.0.0"
unityPort = 25566
unitySock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
unitySock.bind(("127.0.0.1", unityPort))
    


init_window(1920, 1080, "Hello")
while not window_should_close():
    begin_drawing()
    clear_background(WHITE)    
    y=0
    total = 0
    lines = 0
    while True:
        data = unitySock.recv(1024)
        # print(data)
        command = data.decode()

        if(command == "END"):
            print("End")
            break
        cords = command.split(",")
        
        x = -1
        total += len(cords)
        for num in cords:
            if(num == ""):
                continue
            value = int(num)
            x += value
            draw_rectangle(x, y, 1,1, BLACK)        
        lines += 1
        y+=1 
        print(total//lines)

    end_drawing()
close_window()


    
