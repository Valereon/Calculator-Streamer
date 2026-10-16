using OpenCvSharp;

public class Line
{
    public Line(Vec2i start, Vec2i end)
    {
        startX = start.Item0;
        startY = start.Item1;

        endX = end.Item0;
        endY = end.Item1;
    }
    public int startX;
    public int startY;
    public int endX;
    public int endY;
}