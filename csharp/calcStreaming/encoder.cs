using System.Numerics;
using OpenCvSharp;
using System.Diagnostics;
using HPPH;
using System.Data;
using System.IO.Ports;


class Encoder
{
    public void Encode(Vector2 targetRes, int frameSkip, int msToSleepFrameSkip)
    {
        Stopwatch watch = Stopwatch.StartNew();
        // int frames = 0;

        VideoCapture cap = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
        cap.Set(VideoCaptureProperties.FrameWidth, 1920);
        cap.Set(VideoCaptureProperties.FrameHeight, 1080);

        // SerialPort port = new SerialPort("COM5", 115200, Parity.None, 8, StopBits.One);
        // port.Open();

        while (true)
        {
            // if (frames <= frameSkip)
            // {
            //     Thread.Sleep(msToSleepFrameSkip);
            //     frames += 1;
            //     continue;
            // }
            // else
            //     frames = 0;

            watch.Reset();
            watch.Start();

            Mat screenShot = new();
            cap.Read(screenShot);


            Mat greyScale = new();
            ResizeImage(screenShot, greyScale, targetRes);
            Cv2.CvtColor(greyScale, greyScale, ColorConversionCodes.BGRA2GRAY);


            Mat edges = new Mat();
            Cv2.GaussianBlur(greyScale, greyScale, new(5, 5), 1.4);
            double otsuThresh = Cv2.Threshold(greyScale, edges, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            Cv2.Canny(greyScale, edges, otsuThresh * 0.5f, otsuThresh);







            List<List<int>> connected = CustomLineTrace(edges);

            int colsCount = edges.Cols;
            List<List<Vec2i>> lines = new();
            for (int line = 0; line < connected.Count; line++)
            {
                List<Vec2i> points = new();
                for (int point = 0; point < connected[line].Count; point++)
                {
                    int actRow = connected[line][point] / colsCount;
                    int actCol = connected[line][point] % colsCount;
                    points.Add(new Vec2i(actCol, actRow));
                }

                lines.Add(points);
            }

            List<List<Vec2i>> twoPoint = MakeTwoPointLines(lines);

            List<List<Vec2i>> sortedLines = SortLines(twoPoint, 10);


            Mat recon = DrawLines(sortedLines, edges.Cols, edges.Rows);
            List<List<int>> deltas = GetDeltas(sortedLines);



            List<byte> encoded = SerializeImage(deltas);


            // port.Write(encoded.ToArray(), 0, encoded.Count);

            watch.Stop();
            Cv2.ImShow("wah", recon);
            Cv2.WaitKey(1);
            Console.WriteLine($"Frame Time : {watch.ElapsedMilliseconds} :: FPS {MathF.Round((float)(11.5 * 1024 / encoded.Count), 2)} :: Bytes {encoded.Count}");
        }
    }


    private List<byte> SerializeImage(List<List<int>> lines)
    {
        BitEncoder postionEncoder = new();
        // sort the lines by count and then rle the counts but also sort them by delta distance in terms of their groups so sort the 2 point lines so the deltas are the smallest between them to improve it

        // ACTUALLY sort the lines by delta maximize the thing is the lenght encoder thing with the codes only takes about 100-200 bytes per frame and is the wrong thing to optimize
        // if you order them to maximize delta you are optmizing for the thing that has a much bigger cieling to be shrunk so 
        // order by shortest deltas and then get the length code bitstream before that :)

        postionEncoder.Write(lines[0].Count, 16);

        for (int i = 0; i < lines.Count; i++)
        {
            // int code = (1 << (len - 1)) - 2;
            for (int j = 0; j < lines[i].Count; j++)
            {
                int num = lines[i][j];
                uint zagged = ZigZagEncode(num) + 1; // bc of elias needs >=1


                int l = (int)Math.Floor(Math.Log2(zagged));
                int unary = (1 << l) - 2;
                
                int remaningBits = (int)(zagged & ((1 << l)-1));
                postionEncoder.Write(unary, l);
                postionEncoder.Write(remaningBits, l);
            }
        }


        postionEncoder.Flush();
        List<byte> positionEncoded = postionEncoder.GetData();

        return positionEncoded;
    }
    //https://lemire.me/blog/2022/11/25/making-all-your-integers-positive-with-zigzag-encoding/
    private uint ZigZagEncode(int value)
    {
        return (uint)((value << 1) ^ (value >> 31));
    }
    private List<List<int>> GetDeltas(List<List<Vec2i>> lines)
    {
        List<int> x = new();
        List<int> y = new();


        int lastStartX = 0;
        int lastStartY = 0;
        foreach(List<Vec2i> line in lines)
        {
            Vec2i start = line[0];
            Vec2i end = line.Last();
            
            x.Add(lastStartX - start.Item0);
            x.Add(start.Item0 - end.Item0);
            lastStartX = end.Item0;

            y.Add(lastStartY - start.Item1);
            y.Add(start.Item1 - end.Item1);
            lastStartY = end.Item1;
        }
        return [x, y];
    }

    private List<List<Vec2i>> SortLines(List<List<Vec2i>> lines, int startingChecks)
    {
        List<List<Vec2i>> order = new();


        bool[] visited = new bool[lines.Count];

        List<Vec2i> current = lines[0];
        visited[0] = true;
        order.Add(current);

        while (true)
        {
            int bestCost = 999999999;
            bool foundNext = false;
            List<Vec2i> bestCandidate = new();
            int index = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                if (visited[i])
                    continue;
                int cost = LineDeltaCost(current, lines[i]);
                if (cost < bestCost)
                {
                    index = i;
                    bestCost = cost;
                    bestCandidate = lines[i];
                    foundNext = true;
                }
            }
            if (!foundNext)
                break;
            order.Add(bestCandidate);
            current = bestCandidate;
            visited[index] = true;
        }
        return order;
    }

    private int LineDeltaCost(List<Vec2i> a, List<Vec2i> b)
    {
        Vec2i startA = a[0];
        Vec2i endA = a.Last();

        Vec2i startB = b[0];
        Vec2i endB = b.Last();
        // manhattan distance :)
        return Math.Abs(startA.Item0 - startB.Item0) + Math.Abs(startA.Item1 - startB.Item1) + Math.Abs(endA.Item0 - endB.Item0) + Math.Abs(endA.Item1 - endB.Item1);
    }

    private Mat DrawLines(List<List<Vec2i>> lines, int width, int height)
    {
        Mat output = Mat.Zeros(height, width, MatType.CV_8UC1);

        foreach (List<Vec2i> line in lines)
        {
            OpenCvSharp.Point[] points = new OpenCvSharp.Point[line.Count];

            for (int i = 0; i < line.Count; i++)
            {
                points[i] = new OpenCvSharp.Point(
                    line[i].Item0, // x
                    line[i].Item1  // y
                );
            }

            Cv2.Polylines(
                output,
                new[] { points },
                false,              // do not close the line
                Scalar.White,
                1,
                LineTypes.Link8
            );
        }

        return output;
    }
    // basically a greedy search that given the 4 connectedness 
    private List<List<int>> CustomLineTrace(Mat src)
    {
        //todo later make it so that it can run in two directions basically right now it just picks and runs but i think
        // it should be able to do 8 dircetions like north and northwest but not north and east you get it? like so that it can follow a simple
        //slope

        int[][] directions =
        {
            [0,1],
            [0,-1],
            [1,0],
            [-1,0],
            [1,1],
            [1,-1],
            [-1,1],
            [-1,-1]
        };

        int rowCount = src.Rows;
        int colsCount = src.Cols;
        bool[] visited = new bool[rowCount * colsCount];

        List<List<int>> listOfregions = new();

        MatRowAccessor<byte> imageRows = src.AsRows<byte>();

        for (int r = 0; r < rowCount; r++)
        {
            for (int c = 0; c < colsCount; c++)
            {
                int startIndex = r * colsCount + c;
                if (visited[startIndex] == true)
                    continue;

                visited[startIndex] = true;

                if (imageRows[r][c] == 0)
                    continue;

                List<int> group = new();

                int innerR = r;
                int innerC = c;
                int[] trueDirection = [];
                while (true)
                {
                    int node = innerR * colsCount + innerC;
                    int row = node / colsCount;
                    int col = node % colsCount;

                    bool isFound = false;
                    foreach (int[] direction in directions)
                    {

                        int nextRow = 0;
                        int nextCol = 0;
                        if (trueDirection.Length == 0)
                        {
                            nextRow = row + direction[0];
                            nextCol = col + direction[1];
                        }
                        else
                        {
                            nextRow = row + trueDirection[0];
                            nextCol = col + trueDirection[1];
                        }

                        if (!IsPointValid(nextRow, nextCol, rowCount, colsCount))
                            continue;

                        int index = nextRow * colsCount + nextCol;

                        if (visited[index])
                            continue;


                        int nextValue = imageRows[nextRow][nextCol];

                        if (nextValue > 0)
                        {
                            visited[index] = true;
                            isFound = true;
                            group.Add(index);
                            trueDirection = [direction[0], direction[1]];
                            innerR += trueDirection[0];
                            innerC += trueDirection[1];
                            break;
                        }
                    }
                    if (!isFound)
                        break;
                }
                listOfregions.Add(group);

            }
        }
        return listOfregions;
    }

    private bool IsPointValid(int row, int col, int rowCount, int colCount)
    {
        return row >= 0 &&
               row < rowCount &&
               col >= 0 &&
               col < colCount;
    }
    private List<List<Vec2i>> MakeTwoPointLines(List<List<Vec2i>> lines)
    {
        List<List<Vec2i>> result = new();

        foreach(List<Vec2i> line in lines)
        {
            if(line.Count >= 2)
            {
                result.Add([line[0], line.Last()]);
            }
        }

        return result;
    }

    private Mat ResizeImage(Mat image, Mat outMat, Vector2 targetRes)
    {
        OpenCvSharp.Size size = new(targetRes.X, targetRes.Y);
        Cv2.Resize(image, outMat, size, 0, 0, InterpolationFlags.Area);
        return outMat;
    }

}