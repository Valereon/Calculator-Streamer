# Calculator Streamer

This project allows you to stream edge detected video over the usb2.0 connection of a TI84 Plus CE calculator.

## Demo
![Demo video](demo.gif)<br>
[Longer Youtube Video (1:02)](https://www.youtube.com/shorts/b3qhhpAczCk)

This GIF is limited to 10fps but in practice it runs at 30-70 fps depending on content see the youtube video for full quality

## Requirements
- Any version of OBS
- a jailbroken TI84 Plus CE


## How to use
### Calculator Side
Download Streamer.8xp and Transfer the program to your calculator via TILP or TI connect CE. 

When you run the program the calculators screen will go black and then you have to unplug the usb cable and re plug it back into your calculator. 


### PC side
This setups uses OBS's virtual cameras to make capturing the screen very quickly very easy.<br>
Open OBS and select a scene with a capture. This can be a display or an actual camera. Then in the bottom right under start recording there is "Start Virtual Camera", start the camera and you will be all set for the screen capture 


Once you have started the virtual camera and reconnected the USB cable you can run CalcStreamer.exe

### Disconnecting
When disconnecting make sure to hit CLEAR on the calculator until it returns to the home screen. If you stop the encoder first it will freeze the calculator and you will have to push the reset on the back of the calculator.



## How it works
OBS Screen shot ->
Resized to 680x480 ->
Resized to 255x240 ->
Canny edge detected ->
Traces edges to make horizontal or vertical lines ->
groups lines into horizontal or vertical ->
Serializes ->
Calculator receives ->
calculator draws from serial buffer ->
Calculator sends ACK ->
Loop



## Byte Stream
In the first iterations i had implemented an Elias Gamma coding as well as a linear predictor sadly the elias gamma took too many cpu cycles to decode and just that decoding  alone took around 450ms. 

After that i switched to a no operation bye stream for the decoder. Basically this is what it looks like

[MagicByte1.2.3][MessageLength][HorizontalLength][VerticalLength][x][y][len]........

I simplified the payload heavily so that each operation gets its own byte, so basically x, y and len are their own bytes. This allows the calculator to do zero work per line other than accessing the array of bytes the lines are stored in.

The reason you only need len and not direction is i sorted them into a horizontal bucket and a vertical bucket so you know which way to draw without per line and then swap directions when you reach the end of horizontal.

