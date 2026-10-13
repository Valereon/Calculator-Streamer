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
        // int frames = 0;

        VideoCapture cap = new VideoCapture(0, VideoCaptureAPIs.DSHOW);
        cap.Set(VideoCaptureProperties.FrameWidth, 1920);
        cap.Set(VideoCaptureProperties.FrameHeight, 1080);

        SerialPort port = new SerialPort("COM5", 921600, Parity.None, 8, StopBits.One);
        port.Handshake = Handshake.None;
        // port.DtrEnable = true;
        // port.RtsEnable = true;
        // port.ReadTimeout = 500;
        // port.WriteTimeout = 500;
        port.Open();


        while (true)
        {


            watch.Reset();
            watch.Start();

            Mat screenShot = new();
            cap.Read(screenShot);

            Mat edges = PrepImage(screenShot, targetRes);

            List<List<Vec2i>> connected = CustomLineTrace(edges);
            if (connected.Count == 0)
                continue;

            List<List<Vec2i>> twoPoint = MakeTwoPointLines(connected);


            // 1 is half resolution and 2 is quarter (based on >> shift so powers of 2)

            ScaleRenderResolution(twoPoint, 1);



            List<List<Vec2i>> sortedLines = SortLines(twoPoint, 10);

            List<List<int>> residuals = LinearPredictor(sortedLines);
            List<byte> encoded = SerializeImage(residuals);
            encoded.Insert(0, 255);


            for (int i = 0; i < sortedLines.Count; i++)
            {
                for (int j = 0; j < sortedLines[i].Count; j++)
                {
                    Vec2i point = sortedLines[i][j];
                    point.Item0 <<= 1;
                    point.Item1 <<= 1;
                    sortedLines[i][j] = point;
                }
            }

            Mat recon = DrawLines(sortedLines, 320, 240);
            // for(int i = 0; i < encoded.Count; i++)
            // {

            // }
            port.BaseStream.Write(encoded.ToArray(), 0, encoded.Count);
            port.BaseStream.Flush();
            watch.Stop();

            Cv2.ImShow("wah", recon);
            Cv2.WaitKey(1);
            Console.WriteLine($"Frame Time : {watch.ElapsedMilliseconds} :: FPS {MathF.Round((float)(11.5 * 1024 / encoded.Count), 2)} :: Bytes {encoded.Count}");
            watch.Reset();
            watch.Start();
            while (port.BytesToRead == 0)
                continue;

            // Console.WriteLine(port.BytesToRead);
            byte[] result = new byte[3];
            port.Read(result, 0, 3);
            watch.Stop();
            Console.WriteLine(watch.ElapsedMilliseconds);
            if (result[0] != 200 || result[1] != 220 || result[2] != 250)
            {
                Console.WriteLine("FUCK");
            }
            port.DiscardInBuffer();
        }
    }


    private List<byte> SerializeImage(List<List<int>> lines)
    {
        BitEncoder positionEncoder = new();
        byte first = (byte)(lines[0].Count & 0xFF);
        byte last = (byte)(lines[0].Count >> 8);
        positionEncoder.Write(first, 8);
        positionEncoder.Write(last, 8);

        Dictionary<int, int> keyValuePairs = new();
        for (int i = 0; i < lines.Count; i++)
        {
            // int code = (1 << (len - 1)) - 2;
            for (int j = 0; j < lines[i].Count; j++)
            {
                int num = lines[i][j];
                uint zagged = ZigZagEncode(num) + 1; // bc of elias needs >=1


                int l = (int)Math.Floor(Math.Log2(zagged));
                int remainingBits = (int)(zagged & ((1 << l) - 1));

                positionEncoder.Write(0, l);
                positionEncoder.Write(1, 1);
                positionEncoder.Write(remainingBits, l);

            }
        }


        positionEncoder.Flush();
        List<byte> positionEncoded = positionEncoder.GetData();
        positionEncoded.Insert(0, (byte)(positionEncoded.Count & 0xFF)); // last byte 
        positionEncoded.Insert(1, (byte)(positionEncoded.Count >> 8)); // first byte of 16 btis
        return positionEncoded;
    }

    private void ScaleRenderResolution(List<List<Vec2i>> points, int factor)
    {
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = 0; j < points[i].Count; j++)
            {
                Vec2i point = points[i][j];
                int parityX = point.Item0 & 1;
                int parityY = point.Item1 & 1;
                point.Item0 >>= factor;
                point.Item1 >>= factor;
                point.Item0 |= parityX;
                point.Item1 |= parityY;
                points[i][j] = point;
            }
        }
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

    private List<List<Vec2i>> MakeTwoPointLines(List<List<Vec2i>> lines)
    {
        List<List<Vec2i>> result = new();

        foreach (List<Vec2i> line in lines)
        {
            if (line.Count >= 2)
            {
                Vec2i start = line[0];
                Vec2i end = line.Last();


                if (start.Item0 != end.Item0 || start.Item1 != end.Item1)
                {
                    result.Add([start, end]);
                }
            }
        }

        return result;
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
                LineTypes.Link4
            );
        }

        return output;
    }

    private List<List<int>> LinearPredictor(List<List<Vec2i>> lines)
    {
        int predicitedDx = 0;
        int predicitedDy = 0;
        List<int> predictionsX = new();
        List<int> predictionsY = new();
        List<int> actualDxs = new();
        List<int> actualDys = new();
        Vec2i endLastLine = new(0, 0);
        foreach (List<Vec2i> line in lines)
        {

            int predictedX = endLastLine.Item0 + predicitedDx;
            int predictedY = endLastLine.Item1 + predicitedDy;

            int errorX = line[0].Item0 - predictedX;
            int errorY = line[0].Item1 - predictedY;

            predictionsX.Add(errorX);
            predictionsY.Add(errorY);

            int actualDx = line[1].Item0 - line[0].Item0;
            int actualDy = line[1].Item1 - line[0].Item1;

            actualDxs.Add(actualDx);
            actualDys.Add(actualDy);

            predicitedDx = actualDx;
            predicitedDy = actualDy;
            endLastLine = line[1];
        }
        return [predictionsX, predictionsY, actualDxs, actualDys];
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
    private int LineDeltaCost(List<Vec2i> a, List<Vec2i> b)
    {
        Vec2i startA = a[0];
        Vec2i endA = a.Last();

        Vec2i startB = b[0];
        Vec2i endB = b.Last();
        // manhattan distance :)
        return Math.Abs(endA.Item0 - startB.Item0) + Math.Abs(endA.Item1 - startB.Item1);
    }
    private uint ZigZagEncode(int value)
    {
        //https://lemire.me/blog/2022/11/25/making-all-your-integers-positive-with-zigzag-encoding/
        return (uint)((value << 1) ^ (value >> 31));
    }

    private bool IsPointValid(int row, int col, int rowCount, int colCount)
    {
        return row >= 0 &&
               row < rowCount &&
               col >= 0 &&
               col < colCount;
    }
}
