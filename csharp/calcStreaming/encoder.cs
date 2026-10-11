using System.Numerics;
using OpenCvSharp;
using System.Diagnostics;
using HPPH;
using System.Data;
using System.IO.Ports;
using Microsoft.VisualBasic.ApplicationServices;
using Microsoft.VisualBasic;
using System.Windows.Input;


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

            Mat edges = PrepImage(screenShot, targetRes);

            List<List<Vec2i>> connected = CustomLineTrace(edges);

            // List<List<Vec2i>> properLines = To2D(connected, edges.Cols);

            List<List<Vec2i>> twoPoint = MakeTwoPointLines(connected);

            List<List<Vec2i>> sortedLines = SortLines(twoPoint, 10);

            int predicitedDx = 0;
            int predicitedDy = 0;
            List<int> predictionsX = new(); 
            List<int> predictionsY = new(); 
            Vec2i endLastLine = new(0,0);
            foreach(List<Vec2i> line in sortedLines)
            {
                int predictedX = endLastLine.Item0 + predicitedDx;
                int predictedY = endLastLine.Item1 + predicitedDy;

                int errorX = line[0].Item0 - predictedX;
                int errorY = line[0].Item1 - predictedY;

                predictionsX.Add(errorX);
                predictionsY.Add(errorY);

                int actualDx = line[1].Item0 - line[0].Item0;
                int actualDy = line[1].Item1 - line[0].Item1;

                predicitedDx = actualDx;
                predicitedDy = actualDy;
                endLastLine = line[1];
            }


            Mat recon = DrawLines(sortedLines, edges.Cols, edges.Rows);

            // List<List<int>> deltas = GetDeltas(sortedLines);
            List<List<int>> residuals = [predictionsX, predictionsY];
            List<byte> encoded = SerializeImage(residuals);


            // lowkey gemeini
            // int[] histogram = Enumerable.Range(0, 256).Select(i => encoded.Count(b => b == (byte)i)).OrderByDescending(key => key).ToArray();
            // var histogram2 = deltas[0].GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()).OrderByDescending(key => key.Value);
            // var histogram3 = deltas[1].GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()).OrderByDescending(key => key.Value);
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

        Dictionary<int, int> keyValuePairs = new();
        for (int i = 0; i < lines.Count; i++)
        {
            // int code = (1 << (len - 1)) - 2;
            for (int j = 0; j < lines[i].Count; j++)
            {
                int num = lines[i][j];
                uint zagged = ZigZagEncode(num) + 1; // bc of elias needs >=1


                int l = (int)Math.Floor(Math.Log2(zagged));
                int remaningBits = (int)(zagged & ((1 << l) - 1));

                postionEncoder.Write(0, l);
                postionEncoder.Write(1, 1);
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
        foreach (List<Vec2i> line in lines)
        {
            Vec2i start = line[0];
            Vec2i end = line.Last();

            x.Add(start.Item0 - lastStartX);
            lastStartX = end.Item0;

            y.Add(start.Item1 - lastStartY);
            lastStartY = end.Item1;


            x.Add(end.Item0 - start.Item0);
            y.Add(end.Item1 - start.Item1);
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
        return Math.Abs(endA.Item0 - startB.Item0) + Math.Abs(endA.Item1 - startB.Item1);
    }

    private List<List<Vec2i>> To2D(List<List<int>> ints, int colsCount)

    {
        List<List<Vec2i>> lines = new();
        for (int line = 0; line < ints.Count; line++)
        {
            List<Vec2i> points = new();
            for (int point = 0; point < ints[line].Count; point++)
            {
                int actRow = ints[line][point] / colsCount;
                int actCol = ints[line][point] % colsCount;
                points.Add(new Vec2i(actCol, actRow));
            }

            lines.Add(points);
        }
        return lines;
    }

    private Mat PrepImage(Mat screenShot, Vector2 targetRes)
    {
        Mat greyScale = new();
        ResizeImage(screenShot, greyScale, targetRes);
        Cv2.CvtColor(greyScale, greyScale, ColorConversionCodes.BGRA2GRAY);


        Mat edges = new Mat();
        Cv2.GaussianBlur(greyScale, greyScale, new(5, 5), 1.4);
        double otsuThresh = Cv2.Threshold(greyScale, edges, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        Cv2.Canny(greyScale, edges, otsuThresh * 0.5f, otsuThresh);
        return edges;
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
    private List<List<Vec2i>> CustomLineTrace(Mat src)
    {
        //todo later make it so that it can run in two directions basically right now it just picks and runs but i think
        // it should be able to do 8 dircetions like north and northwest but not north and east you get it? like so that it can follow a simple
        //slope
        int blackLength = 4;

        int[][] directions =
        {
            [0,1],
            [0,-1],
            [1,0],
            [-1,0],
        };

        int rowCount = src.Rows;
        int colsCount = src.Cols;
        bool[,] visited = new bool[rowCount, colsCount];

        List<List<Vec2i>> listOfregions = new();

        MatRowAccessor<byte> imageRows = src.AsRows<byte>();

        for (int r = 0; r < rowCount; r++)
        {
            for (int c = 0; c < colsCount; c++)
            {
                if (visited[r, c])
                    continue;

                visited[r, c] = true;

                if (imageRows[r][c] == 0)
                    continue;

                List<Vec2i> group = new();

                int innerR = r;
                int innerC = c;
                int[] trueDirection = [];
                int totalBlack = 0;
                while (true)
                {
                    int node = innerR * colsCount + innerC;
                    int row = node / colsCount;
                    int col = node % colsCount;

                    if (trueDirection.Length == 0)
                    {
                        foreach (int[] direction in directions)
                        {
                            int innerNextRow = row + direction[0];
                            int innerNextCol = col + direction[1];

                            if (!IsPointValid(innerNextRow, innerNextCol, rowCount, colsCount))
                                continue;

                            int innerNextValue = imageRows[innerNextRow][innerNextCol];

                            if (innerNextValue > 0)
                            {
                                trueDirection = [direction[0], direction[1]];
                                break;
                            }
                        }
                        if (trueDirection.Length == 0)
                            break;

                    }



                    int nextRow = row + trueDirection[0];
                    int nextCol = col + trueDirection[1];

                    if (!IsPointValid(nextRow, nextCol, rowCount, colsCount))
                        break;

                    if (visited[nextRow, nextCol])
                        break;

                    int nextValue = imageRows[nextRow][nextCol];

                    if (nextValue == 0)
                    {
                        totalBlack += 1;
                        if (totalBlack >= blackLength)
                            break;
                        continue;
                    }

                    visited[nextRow, nextCol] = true;
                    innerR += trueDirection[0];
                    innerC += trueDirection[1];
                    group.Add(new Vec2i(nextCol,nextRow));
                }
                if (group.Count >= 2)
                {
                    listOfregions.Add(group);
                }
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

        foreach (List<Vec2i> line in lines)
        {
            if (line.Count >= 2)
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
