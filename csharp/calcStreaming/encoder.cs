using System.Numerics;
using OpenCvSharp;
using OpenCvSharp.GdipExtensions;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Net;
using HPPH;
using System.Diagnostics.Tracing;


class Encoder
{
    public void Encode(Vector2 targetRes, int frameSkip, int msToSleepFrameSkip)
    {
        Stopwatch watch = Stopwatch.StartNew();
        int frames = 0;

        string targetIp = "127.0.0.1";
        int port = 25566;
        Socket udpClient = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(targetIp), port);
        VideoCapture cap = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
        cap.Set(VideoCaptureProperties.FrameWidth, 1920);
        cap.Set(VideoCaptureProperties.FrameHeight, 1080);
        // cap.
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
            // Cv2.GaussianBlur(greyScale, greyScale, new(5, 5), 1.4);
            double otsuThresh = Cv2.Threshold(greyScale, edges, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            Cv2.Canny(greyScale, edges, otsuThresh, otsuThresh * 2);







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
                // points = points.OrderBy(key => key.Item0).ThenBy(key => key.Item1).ToList();

                lines.Add(points);
            }

            List<List<Vec2i>> decimatedLines = DecimateLines(lines, 10f);
            List<List<Vec2i>> averageTwoPoint = decimatedLines.Where(key => key.Count == 2).ToList();
            List<List<Vec2i>> sortedLines = SortLines(decimatedLines, 10);
            Mat recon = DrawLines(sortedLines, edges.Cols, edges.Rows);

            foreach (List<Vec2i> line in sortedLines)
            {
                string message = "";
                foreach (Vec2i point in line)
                {
                    string tempMess = $"{(int)point.Item0},{(int)point.Item1};";
                    if (tempMess == ",;")
                        continue;
                    message += tempMess;
                }
                message += "ENDLINE";
                if (message == "ENDLINE")
                    continue;
                byte[] data = Encoding.UTF8.GetBytes(message);
                udpClient.SendTo(data, endPoint);
            }
            byte[] endmess = Encoding.UTF8.GetBytes("END");
            udpClient.SendTo(endmess, endPoint);

            SerializeImage(sortedLines);

            watch.Stop();
            Cv2.ImShow("wah", recon);
            Cv2.ImWrite("what.png", recon);
            Cv2.WaitKey(1);
            Console.WriteLine($"Frame Time : {watch.ElapsedMilliseconds} :: Lines {(int)connected.Average(inner => inner.Count)} :: decmiated lines {decimatedLines.Count} :: avg 2 line {averageTwoPoint.Count} :: {sortedLines.Count}" );
        }
    }
    private List<byte> SerializeImage(List<List<Vec2i>> lines)
    {
        BitEncoder postionEncoder = new();
        BitEncoder lengthEncoder = new();
        // sort the lines by count and then rle the counts but also sort them by delta distance in terms of their groups so sort the 2 point lines so the deltas are the smallest between them to improve it

        // ACTUALLY sort the lines by delta maximize the thing is the lenght encoder thing with the codes only takes about 100-200 bytes per frame and is the wrong thing to optimize
        // if you order them to maximize delta you are optmizing for the thing that has a much bigger cieling to be shrunk so 
        // order by shortest deltas and then get the length code bitstream before that :)

        for (int i = 0; i < lines.Count; i++)
        {
            int len = lines[i].Count;
            int code = (1 << (len - 1)) - 2;
            lengthEncoder.Write(code, len - 1);
            for (int j = 0; j < lines[i].Count; j++)
            {
                byte x = (byte)lines[i][j].Item0;
                byte y = (byte)lines[i][j].Item1;
                postionEncoder.Write(x, 8);
                postionEncoder.Write(y, 8);
            }
        }
        postionEncoder.Flush();
        lengthEncoder.Flush();
        List<byte> lengthEncoded = lengthEncoder.GetData();
        List<byte> positionEncoded = postionEncoder.GetData();

        short lengthOfCodes = (short)lengthEncoded.Count;
        List<byte> byteLengthCodes = BitConverter.GetBytes(lengthOfCodes).ToList();
        lengthEncoded.InsertRange(0, byteLengthCodes);
        lengthEncoded.AddRange(positionEncoded);
        // Console.WriteLine(lengthOfCodes);
        // Console.WriteLine(positionEncoded.Count);

        Console.WriteLine((11.5 * 1024) / (lengthOfCodes + positionEncoded.Count));
        return lengthEncoded;
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
                if(visited[i])
                    continue;
                int cost = LineDeltaCost(current, lines[i]);
                if(cost < bestCost)
                {
                    index = i;
                    bestCost = cost;
                    bestCandidate = lines[i];   
                    foundNext = true;
                }
            }
            if(!foundNext)
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
    private float Distance(Vec2i start, Vec2i end)
    {
        return MathF.Sqrt((float)(MathF.Pow(end.Item0 - start.Item0, 2) + Math.Pow(end.Item1 - start.Item1, 2)));
    }
    // basically a greedy search that given the 4 connectedness 
    private List<List<int>> CustomLineTrace(Mat src)
    {


        int[][] directions =
         {
            [0,1],
            [0,-1],
            [1,0],
            [-1,0]
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
                while (true)
                {
                    int node = innerR * colsCount + innerC;
                    int row = node / colsCount;
                    int col = node % colsCount;

                    bool isFound = false;
                    foreach (int[] direction in directions)
                    {
                        int nextRow = row + direction[0];
                        int nextCol = col + direction[1];

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
                            innerR += direction[0];
                            innerC += direction[1];
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
    private List<List<int>> TraceLines(Mat src)
    {
        int[][] directions =
         {
            [0,1],
            [0,-1],
            [1,0],
            [-1,0],
            // [1,1],
            // [1,-1],
            // [-1,1],
            // [-1,-1]
        };
        int rowCount = src.Rows;
        int colsCount = src.Cols;
        bool[] visited = new bool[rowCount * colsCount];

        List<List<int>> listOfregions = new();

        MatRowAccessor<byte> imageRows = src.AsRows<byte>();
        Queue<int> positions = new();
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
                positions.Enqueue(startIndex);

                while (positions.Count > 0)
                {
                    int node = positions.Dequeue();
                    group.Add(node);


                    int row = node / colsCount;
                    int col = node % colsCount;


                    foreach (int[] direction in directions)
                    {
                        int nextRow = row + direction[0];
                        int nextCol = col + direction[1];

                        if (!IsPointValid(nextRow, nextCol, rowCount, colsCount))
                            continue;

                        int index = nextRow * colsCount + nextCol;

                        if (visited[index])
                            continue;


                        int nextValue = imageRows[nextRow][nextCol];

                        if (nextValue > 0)
                        {

                            visited[index] = true;
                            positions.Enqueue(index);
                        }
                    }
                }
                listOfregions.Add(group);
            }
        }
        return listOfregions;
    }
    private float QuantizeImageNoRemap(Mat image, int levels)
    {
        float factor = 255f / (levels - 1);
        byte[] lut = new byte[256];
        for (int l = 0; l < 256; l++)
        {
            lut[l] = (byte)(l / factor);
        }

        MatRowAccessor<byte> col = image.AsRows<byte>();
        int colLen = col.Count;

        Parallel.For(0, colLen, i =>
        {
            Span<byte> row = image.AsRows<byte>()[i]; // re-created per call, not captured
            for (int j = 0; j < row.Length; j++)
            {
                row[j] = lut[row[j]];
            }
        });

        return factor;
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
            else if(lines[i].Count <= 1)
            {
                continue;
            }

            List<Vec2i> decmiatedLine = DouglasPeucker(lines[i], epsilon);
            finalLines.Add(decmiatedLine);
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


    private Mat ResizeImage(Bitmap image, Mat outMat, Vector2 targetRes)
    {
        Mat source = BitmapConverter.ToMat(image);
        OpenCvSharp.Size size = new(targetRes.X, targetRes.Y);
        Cv2.Resize(source, outMat, size, 0, 0, InterpolationFlags.Area);
        return outMat;
    }


    private Mat ResizeImage(Mat image, Mat outMat, Vector2 targetRes)
    {
        OpenCvSharp.Size size = new(targetRes.X, targetRes.Y);
        Cv2.Resize(image, outMat, size, 0, 0, InterpolationFlags.Area);
        return outMat;
    }

}