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

        SerialPort port = new SerialPort("COM5", 115200, Parity.None, 8, StopBits.One);
        port.Handshake = Handshake.None;
        // port.DtrEnable = true;
        // port.RtsEnable = true;
        // port.ReadTimeout = 1000;
        // port.WriteTimeout = 1000;
        port.Open();


        while (true)
        {




            Mat screenShot = new();
            cap.Read(screenShot);

            Mat edges = PrepImage(screenShot, targetRes);

            List<List<Vec2i>> connected = CustomLineTrace(edges);
            if (connected.Count == 0)
                continue;

            List<List<Vec2i>> twoPoint = MakeTwoPointLines(connected);
            List<List<Vec2i>> sortedLines = SortLines(twoPoint, 10);


            List<byte> encoded = SerializeImage(sortedLines);
            encoded.Insert(0, 255);




            Mat recon = DrawLines(sortedLines, 255, 240);
            watch.Reset();
            watch.Start();
            port.Write(encoded.ToArray(), 0, encoded.Count);
            port.BaseStream.Flush();
            watch.Stop();

            Cv2.ImShow("wah", recon);
            Cv2.WaitKey(1);
            Console.WriteLine($"Frame Time : {1000/watch.ElapsedMilliseconds} :: FPS {MathF.Round((float)(11.5 * 1024 / encoded.Count), 2)} :: Bytes {encoded.Count}");


            byte[] result = new byte[3]; // literally just a magic ACK doenst matter what it is really
            port.Read(result, 0, result.Length);

  
            Console.WriteLine(watch.ElapsedMilliseconds);


        }
    }


    private List<byte> SerializeImage(List<List<Vec2i>> lines)
    {
        BitEncoder positionEncoder = new();
        int totalLength = lines.Count*4;
        byte first = (byte)(totalLength & 0xFF);
        byte last = (byte)(totalLength >> 8);
        positionEncoder.Write(first, 8);
        positionEncoder.Write(last, 8);

        for (int i = 0; i < lines.Count; i++)
        {
            positionEncoder.Write((byte)lines[i][0].Item0, 8);
            positionEncoder.Write((byte)lines[i][0].Item1, 8);
            positionEncoder.Write((byte)lines[i][1].Item0, 8);
            positionEncoder.Write((byte)lines[i][1].Item1, 8);
        }

        positionEncoder.Flush();
        List<byte> positionEncoded = positionEncoder.GetData();
        positionEncoded.Insert(0, (byte)(positionEncoded.Count & 0xFF));
        positionEncoded.Insert(1, (byte)(positionEncoded.Count >> 8));
        return positionEncoded;
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
                        break;
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
        // manhattan distance :) //
        return Math.Abs(endA.Item0 - startB.Item0) + Math.Abs(endA.Item1 - startB.Item1);
    }
    private bool IsPointValid(int row, int col, int rowCount, int colCount)
    {
        return row >= 0 &&
               row < rowCount &&
               col >= 0 &&
               col < colCount;
    }
}
