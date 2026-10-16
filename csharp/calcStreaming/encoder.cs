using System.Numerics;
using OpenCvSharp;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;


class Encoder
{
    public void Encode(Vector2 targetRes)
    {
        Stopwatch watch = Stopwatch.StartNew();

        VideoCapture cap = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
        cap.Set(VideoCaptureProperties.FrameWidth, 1920);
        cap.Set(VideoCaptureProperties.FrameHeight, 1080);

        SerialPort port = new SerialPort("COM5", 115200, Parity.None, 8, StopBits.One);
        port.Handshake = Handshake.None;
        port.Open();


        while (true)
        {




            Mat screenShot = new();
            cap.Read(screenShot);

            
            Mat edges = PrepImage(screenShot, targetRes);
            List<List<Vec2i>> connected = CustomLineTrace(edges);

            if (connected.Count == 0)
                continue;


            List<Line> twoPoint = MakeTwoPointLines(connected);
            List<List<Line>> groupedLines = SortHorizVertical(twoPoint);
            List<byte> encoded = SerializeImage(groupedLines);



            watch.Restart();
            port.Write(encoded.ToArray(), 0, encoded.Count);
            watch.Stop();
            if (watch.ElapsedMilliseconds != 0)
                Console.WriteLine($"Frame Time : {1000 / watch.ElapsedMilliseconds} :: FPS {MathF.Round((float)(11.5 * 1024 / encoded.Count), 2)} :: Bytes {encoded.Count}");


            byte[] result = new byte[3];
            port.Read(result, 0, result.Length);



            // just encoder side display
            Mat recon = DrawLines(twoPoint, 255, 240);
            Cv2.ImShow("wah", recon);
            Cv2.WaitKey(1);
        }
    }

    private List<List<Line>> SortHorizVertical(List<Line> lines)
    {
        List<Line> vertical = new();
        List<Line> horizontal = new();
        foreach (Line line in lines)
        {
            int dX = line.endX - line.startX;
            int dY = line.endY - line.startY;

            if (dX != 0)
            {
                horizontal.Add(line);
            }
            else if (dY != 0)
            {
                vertical.Add(line);
            }
        }
        return [horizontal, vertical];
    }

    private List<byte> SerializeImage(List<List<Line>> lines)
    {
        List<byte> bytes = new();

        for (int i = 0; i < lines.Count; i++)
        {
            foreach (Line line in lines[i])
            {

                int deltaX = line.endX - line.startX;
                int deltaY = line.endY - line.startY;
                int delta = 0;

                if (deltaX != 0)
                {
                    if (deltaX < 0)
                    {
                        delta = Math.Abs(deltaX);
                        line.startX -= delta / 2;
                    }
                    delta = Math.Abs(deltaX);
                }
                else if (deltaY != 0)
                {
                    if (deltaY < 0)
                    {
                        delta = Math.Abs(deltaY);
                        line.startY -= delta / 2;
                    }
                    delta = Math.Abs(deltaY);
                }
                bytes.Add((byte)line.startX);
                bytes.Add((byte)line.startY);
                bytes.Add((byte)delta);
            }
        }


        int horizCount = lines[0].Count;
        int vertCount = lines[1].Count;
        List<byte> header = EncodeHeader(horizCount, vertCount, bytes.Count);
        bytes.InsertRange(0, header);
        return bytes;
    }

    private List<byte> GetIntAsShortBytes(int number)
    {
        byte first = (byte)(number & 0xFF);
        byte last = (byte)(number >> 8);
        return [first, last];
    }


    private List<byte> EncodeHeader(int horiz, int vert, int size)
    {

        int horizLength = horiz * 3;
        List<byte> horizBytes = GetIntAsShortBytes(horizLength);

        int vertLength = vert * 3;
        List<byte> vertBytes = GetIntAsShortBytes(vertLength);

        int totalSize = size + 2;
        List<byte> sizeBytes = GetIntAsShortBytes(totalSize);

        List<byte> header = [255, 200, 255];
        header.AddRange(sizeBytes);
        header.AddRange(horizBytes);
        header.AddRange(vertBytes);
        return header;
    }

    private List<List<Vec2i>> CustomLineTrace(Mat src)
    {
        int[][] directions =
        {
            [0,1],
            [0,-1],
            [1,0],
            [-1,0],
        };
        BreadthFSContext ctx = new();
        ctx.rowCount = src.Rows;
        ctx.colsCount = src.Cols;
        ctx.visited = new bool[ctx.rowCount, ctx.colsCount];

        List<List<Vec2i>> listOfregions = new();

        MatRowAccessor<byte> imageRows = src.AsRows<byte>();

        for (int r = 0; r < ctx.rowCount; r++)
        {
            for (int c = 0; c < ctx.colsCount; c++)
            {
                if (ctx.visited[r, c])
                    continue;

                ctx.visited[r, c] = true;

                if (imageRows[r][c] == 0)
                    continue;

                List<Vec2i> group = new();

                int innerR = r;
                int innerC = c;
                int[] trueDirection = [];
                while (true)
                {
                    int node = innerR * ctx.colsCount + innerC;
                    ctx.row = node / ctx.colsCount;
                    ctx.col = node % ctx.colsCount;

                    if (trueDirection.Length == 0)
                    {
                        trueDirection = FindTrueDirection(ctx, directions, imageRows);
                        if (trueDirection.Length == 0)
                            break;
                    }

                    if (!GoInTrueDirection(ctx, trueDirection, imageRows))
                        break;

                    int nextRow = ctx.row + trueDirection[0];
                    int nextCol = ctx.col + trueDirection[1];

                    ctx.visited[nextRow, nextCol] = true;
                    innerR += trueDirection[0];
                    innerC += trueDirection[1];
                    group.Add(new Vec2i(nextCol, nextRow));
                }
                if (group.Count >= 2)
                {
                    listOfregions.Add(group);
                }
            }

        }
        return listOfregions;
    }

    private int[] FindTrueDirection(BreadthFSContext ctx, int[][] directions, MatRowAccessor<byte> imageRows)
    {

        int[] trueDirection = [];
        foreach (int[] direction in directions)
        {
            int innerNextRow = ctx.row + direction[0];
            int innerNextCol = ctx.col + direction[1];

            if (!IsPointValid(innerNextRow, innerNextCol, ctx.rowCount, ctx.colsCount))
                continue;

            int innerNextValue = imageRows[innerNextRow][innerNextCol];

            if (innerNextValue > 0)
            {
                trueDirection = [direction[0], direction[1]];
                break;
            }
        }
        return trueDirection;
    }

    private bool GoInTrueDirection(BreadthFSContext ctx, int[] trueDirection, MatRowAccessor<byte> imageRows)
    {
        int nextRow = ctx.row + trueDirection[0];
        int nextCol = ctx.col + trueDirection[1];

        if (!IsPointValid(nextRow, nextCol, ctx.rowCount, ctx.colsCount))
            return false;

        if (ctx.visited[nextRow, nextCol])
            return false;

        int nextValue = imageRows[nextRow][nextCol];

        if (nextValue == 0)
            return false;

        return true;
    }


    private List<Line> MakeTwoPointLines(List<List<Vec2i>> lines)
    {
        List<Line> result = new();

        foreach (List<Vec2i> line in lines)
        {
            if (line.Count >= 2)
            {
                Vec2i start = line[0];
                Vec2i end = line.Last();


                if (start.Item0 != end.Item0 || start.Item1 != end.Item1)
                {
                    result.Add(new Line(start, end));
                }
            }
        }

        return result;
    }

    private Mat DrawLines(List<Line> lines, int width, int height)
    {
        Mat output = Mat.Zeros(height, width, MatType.CV_8UC1);

        foreach (Line line in lines)
        {
            OpenCvSharp.Point[] points =
            [
                new OpenCvSharp.Point(
                    line.startX, // x
                    line.startY  // y
                ),
                new OpenCvSharp.Point(
                    line.endX, // x
                    line.endY  // y
                ),
            ];

            Cv2.Polylines(
                output,
                new[] { points },
                false,              // do not close the line
                Scalar.White,
                1,
                LineTypes.Link4
            );
        }

        return output;
    }

    private Mat PrepImage(Mat screenShot, Vector2 targetRes)
    {
        Mat greyScale = new();
        ResizeImage(screenShot, greyScale, new Vector2(640, 480));
        ResizeImage(greyScale, greyScale, targetRes);
        Cv2.CvtColor(greyScale, greyScale, ColorConversionCodes.BGRA2GRAY);


        Mat edges = new Mat();
        Cv2.GaussianBlur(greyScale, greyScale, new(5, 5), 1.4);
        double otsuThresh = Cv2.Threshold(greyScale, edges, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        Cv2.Canny(greyScale, edges, otsuThresh * 0.5f, otsuThresh);
        return edges;
    }

    private Mat ResizeImage(Mat image, Mat outMat, Vector2 targetRes)
    {
        OpenCvSharp.Size size = new(targetRes.X, targetRes.Y);
        Cv2.Resize(image, outMat, size, 0, 0, InterpolationFlags.Area);
        return outMat;
    }
    private bool IsPointValid(int row, int col, int rowCount, int colCount)
    {
        return row >= 0 &&
               row < rowCount &&
               col >= 0 &&
               col < colCount;
    }
}
