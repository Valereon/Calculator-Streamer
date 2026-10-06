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
        int frames = 0;

        VideoCapture cap = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
        cap.Set(VideoCaptureProperties.FrameWidth, 1920);
        cap.Set(VideoCaptureProperties.FrameHeight, 1080);

        SerialPort port = new SerialPort("COM5", 115200, Parity.None, 8, StopBits.One);
        port.Open();

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

            List<List<Vec2i>> decimatedLines = DecimateLines(lines, 20f);
            List<List<Vec2i>> averageTwoPoint = decimatedLines.Where(key => key.Count == 2).ToList();

            List<List<Vec2i>> sortedLines = SortLines(decimatedLines, 10);


            Mat recon = DrawLines(sortedLines, edges.Cols, edges.Rows);
            List<List<int>> deltas = GetDeltas(sortedLines);



            List<byte> encoded = SerializeImage(deltas);


            port.Write(encoded.ToArray(), 0, encoded.Count);

            watch.Stop();
            Cv2.ImShow("wah", recon);
            Cv2.WaitKey(1);
            Console.WriteLine($"Frame Time : {watch.ElapsedMilliseconds} :: FPS {MathF.Round((float)(11.5 * 1024 / encoded.Count), 2)} :: Bytes {encoded.Count}");
        }
    }

    private void Send()
    {

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
                uint zagged = ZigZagEncode(num);

                uint adjustedVal = zagged + 1;

                int l = (int)Math.Ceiling(Math.Log2(adjustedVal));
                int unary = 1 << (l - 1);

                int suffixBits = (int)(adjustedVal & ((1 << (l - 1)) - 1));

                postionEncoder.Write(unary, l);
                postionEncoder.Write(suffixBits, l - 1);
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
    //https://www.geeksforgeeks.org/python/elias-gamma-encoding-in-python/


    //todo fix this and make it more conscise 
    private List<List<int>> GetDeltas(List<List<Vec2i>> lines)
    {
        List<int> x = new();
        List<int> y = new();
        List<int> betweenX = new();
        List<int> betweenY = new();


        int lastStartX = -1;
        int lastStartY = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            List<Vec2i> line = lines[i];
            Vec2i start = line[0];
            line.RemoveAt(0);
            Vec2i end = line.Last();
            line.RemoveAt(line.Count - 1);

            if (lastStartX == -1)
            {
                x.Add(start.Item0);
                x.Add(start.Item0 - end.Item0);
                lastStartX = end.Item0;

                y.Add(start.Item1);
                y.Add(start.Item1 - end.Item1);
                lastStartY = end.Item1;
            }
            else
            {
                x.Add(lastStartX - start.Item0);
                x.Add(start.Item0 - end.Item0);
                lastStartX = end.Item0;

                y.Add(lastStartY - start.Item1);
                y.Add(start.Item1 - end.Item1);
                lastStartY = end.Item1;
            }


            int lastX = -1;
            int lastY = -1;
            for (int j = 0; j < line.Count; j++)
            {
                if (lastX == -1)
                {
                    betweenX.Add(line[0].Item0);
                    lastX = line[0].Item0;
                    betweenY.Add(line[0].Item1);
                    lastY = line[0].Item1;
                }
                else
                {
                    betweenX.Add(lastX - line[j].Item0);
                    lastX = line[j].Item0;
                    betweenY.Add(lastY - line[j].Item1);
                    lastY = line[j].Item1;
                }
            }
        }


        return [x, y, betweenX, betweenY];
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
            if (line.Count < 2)
                continue;

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

    private List<List<Vec2i>> DecimateLines(List<List<Vec2i>> lines, float epsilon)
    {

        List<List<Vec2i>> finalLines = new();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Count == 2)
            {
                finalLines.Add(lines[i]);
                continue;
            }
            else if (lines[i].Count <= 1)
            {
                continue;
            }

            List<Vec2i> decmiatedLine = DouglasPeucker(lines[i], epsilon);
            if (decmiatedLine.Count >= 2)
            {
                finalLines.Add(decmiatedLine);
                continue;
            }
        }
        return finalLines;

    }
    // https://en.wikipedia.org/wiki/Ramer%E2%80%93Douglas%E2%80%93Peucker_algorithm there is pseudo code i copied
    private List<Vec2i> DouglasPeucker(List<Vec2i> points, float epsilon)
    {
        float dmax = 0;
        int index = 0;
        int end = points.Count - 1;
        for (int i = 1; i < end - 1; i++)
        {
            float d = PerpendicularDistance(points[i], points[0], points[end]);
            if (d > dmax)
            {
                index = i;
                dmax = d;
            }
        }

        List<Vec2i> realPoints = new();

        if (dmax > epsilon)
        {
            // recurse
            List<Vec2i> recResults1 = DouglasPeucker(points.GetRange(1, index), epsilon);
            List<Vec2i> recResults2 = DouglasPeucker(points.GetRange(index, end - index + 1), epsilon);

            realPoints = recResults1.GetRange(0, recResults1.Count - 1);
            realPoints.AddRange(recResults2);

        }
        else
        {
            realPoints.Add(points[0]);
            realPoints.Add(points[end]);
        }
        return realPoints;

    }

    private float PerpendicularDistance(Vec2i point, Vec2i start, Vec2i end)
    {
        float[] line = GetGeneralForm(start, end);
        float a = line[0];
        float b = line[1];
        float c = line[2];
        float distance = MathF.Abs(a * point.Item0 + b * point.Item1 + c) / MathF.Sqrt(MathF.Pow(a, 2) + MathF.Pow(b, 2));
        return distance;
    }

    private float[] GetGeneralForm(Vec2i start, Vec2i end)
    {
        float a = 0;
        float b = 0;
        float c = 0;

        float topSlope = end.Item1 - start.Item1;
        float bottomSlope = end.Item0 - start.Item0;
        if (topSlope == 0 || bottomSlope == 0)
        {
            b = 1;
            c = -start.Item1;
        }
        else
        {
            a = topSlope / bottomSlope;
            b = start.Item0 - end.Item0;
            c = -a * start.Item0 + start.Item1;
        }
        return [a, b, c];
    }

    private Mat ResizeImage(Mat image, Mat outMat, Vector2 targetRes)
    {
        OpenCvSharp.Size size = new(targetRes.X, targetRes.Y);
        Cv2.Resize(image, outMat, size, 0, 0, InterpolationFlags.Area);
        return outMat;
    }

}