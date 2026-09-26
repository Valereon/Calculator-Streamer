using System.Numerics;
using OpenCvSharp;
using OpenCvSharp.GdipExtensions;
using System.IO.Ports;
using System.Diagnostics;


class Encoder
{
    public void Encode(Vector2 targetRes, int frameSkip, int msToSleepFrameSkip)
    {
        Stopwatch watch = Stopwatch.StartNew();
        int frames = 0;
        // FrameSource frameSource = Cv2.CreateFrameSource_Camera(0);
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
            // Bitmap screenShot = Utilities.GetScreenShot(); //todo replace with dxgi
            Mat screenShot = new();
            cap.Read(screenShot);
            Console.WriteLine(screenShot.Size());
            Mat greyScale = new();
            ResizeImage(screenShot, greyScale, targetRes);
            Cv2.CvtColor(screenShot, greyScale, ColorConversionCodes.BGRA2GRAY);
            
            
            Mat edges = new Mat();
            Cv2.GaussianBlur(greyScale, greyScale, new(5,5), 1.4);
            double otsuThresh = Cv2.Threshold(greyScale, edges, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            Cv2.Canny(greyScale, edges, otsuThresh * .5f, otsuThresh);
            // List<List<int>> connected = BFSConnect(greyScale);



            // Cv2.ImWrite("test.jpg", edges);
            watch.Stop();
            Cv2.ImShow("wah", edges);
            Cv2.WaitKey(1);
            // Cv2.DestroyAllWindows();
            Console.WriteLine($"Frame Time : {watch.ElapsedMilliseconds}");
        }
        // // Cv2.ImWrite("myformat.png", rgb);
    }
    private List<List<int>> BFSConnect(Mat image)
    {
        int[][] directions =
        {
            [0,1],
            [0,-1],
            [1,0],
            [-1,0]
        };
        int merges = 0;
        int rowCount = image.Rows;
        int colsCount = image.Cols;
        bool[] visited = new bool[rowCount * colsCount];

        List<List<int>> listOfregions = new();

        MatRowAccessor<byte> imageRows = image.AsRows<byte>();
        Queue<int> positions = new();
        for (int r = 0; r < rowCount; r++)
        {
            for (int c = 0; c < colsCount; c++)
            {
                int startIndex = r * colsCount + c;
                if (visited[startIndex] == true)
                    continue;

                visited[startIndex] = true;

                List<int> group = new();
                positions.Enqueue(startIndex);

                while (positions.Count > 0)
                {
                    int node = positions.Dequeue();
                    group.Add(node);


                    int row = node / colsCount;
                    int col = node % colsCount;

                    // Parallel.For();
                    foreach (int[] direction in directions)
                    {
                        int nextRow = row + direction[0];
                        int nextCol = col + direction[1];

                        int index = nextRow * colsCount + nextCol;

                        if (!IsPointValid(nextRow, nextCol, rowCount, colsCount))
                            continue;

                        if (visited[index])
                            continue;


                        int nextValue = imageRows[nextRow][nextCol];

                        if (nextValue == 1)
                        {

                            visited[index] = true;
                            positions.Enqueue(index);
                            merges += 1;
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