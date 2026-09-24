using System.Numerics;
using OpenCvSharp;
using OpenCvSharp.GdipExtensions;

class Encoder
{
    public void Encode(Vector2 targetRes, int quantLevels = 32)
    {
        // Thread.Sleep(3000);
        // Bitmap screenShot = Utilities.GetScreenShot();
        Bitmap screenShot = Cv2.ImRead("nature.png").ToBitmap();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // Utilities.DisplayImage(screenShot);
        Mat resized = ResizeImage(screenShot, targetRes);
        Mat greyScale = new();
        Cv2.CvtColor(resized, greyScale, ColorConversionCodes.BGRA2GRAY);


        float factor = QuantizeImageNoRemap(greyScale, quantLevels);

        List<List<int>> thing = BFSConnect(greyScale);


        // RemapImage(greyScale, factor);

        Mat rgb = new();
        Cv2.CvtColor(greyScale, rgb, ColorConversionCodes.GRAY2BGR);

        // Utilities.DisplayImage(BitmapConverter.ToBitmap(greyScale));
        Mat originalRgb = new();
        Cv2.CvtColor(resized, originalRgb, ColorConversionCodes.BGRA2BGR);

        MatRowAccessor<byte> greyRows = greyScale.AsRows<byte>();
        MatRowAccessor<Vec3b> imageRows = rgb.AsRows<Vec3b>();
        MatRowAccessor<Vec3b> ogImageRows = originalRgb.AsRows<Vec3b>();
        List<List<int>> newGroups = new();

        for (int i = 0; i < thing.Count - 1; i += 2)
        {
            int row = thing[i][0] / rgb.Cols;
            int col = thing[i][0] % rgb.Cols;
            int rowNext = thing[i + 1][0] / rgb.Cols;
            int colNext = thing[i + 1][0] % rgb.Cols;
            byte lum = greyRows[row][col];
            byte lumNext = greyRows[rowNext][colNext];
            if (thing[i].Count < 15 && thing[i + 1].Count < 15 && Math.Abs(lum - lumNext) <= 5)
            {
                newGroups.Add(thing[i].Concat(thing[i + 1]).ToList());
            }
            else
            {
                newGroups.Add(thing[i]);
                newGroups.Add(thing[i + 1]);
            }

        }

        foreach (List<int> groups in newGroups)
        {
            List<Vec3b> colorsInRegion = new();
            if (groups.Count > newGroups.Average(key => key.Count))
            {
                groups.Sort();

                int startRow = groups[0] / rgb.Cols;
                int startCol = groups[0] % rgb.Cols;
                Vec3b startColor = ogImageRows[startRow][startCol];

                // int endRow = groups[groups.Count - 1] / rgb.Cols;
                // int endCol = groups[groups.Count - 1] % rgb.Cols;
                // Vec3b endColor = ogImageRows[endRow][endCol];

                List<Vec3b> colors = [startColor];
                for(int i = 0; i < groups.Count; i++)
                {
                    int row = groups[i] / rgb.Cols;
                    int col = groups[i] % rgb.Cols;
                    Vec3b currentColor = ogImageRows[row][col];
                    Vec3b lastColor = colors.TakeLast(1).ToArray()[0];

                    float dist = (float)Math.Sqrt(Math.Pow(lastColor.Item0 - currentColor.Item0,2) + Math.Pow(lastColor.Item1 - currentColor.Item1,2) + Math.Pow(lastColor.Item2 - currentColor.Item2,2));
                    // Console.WriteLine(dist);

                    colors.Add(currentColor);

                }


                for (int j = 0; j < groups.Count; j++)
                {
                    // Global progress from 0.0 to 1.0
                    float t = (float)j / (groups.Count - 1);

                    // Get the interpolated color from your custom N-color palette
                    Vec3b finalColor = EvaluateGradient(colors, t);

                    int row = groups[j] / rgb.Cols;
                    int col = groups[j] % rgb.Cols;
                    imageRows[row][col] = finalColor;
                }
            }
            else
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    int row = groups[i] / rgb.Cols;
                    int col = groups[i] % rgb.Cols;
                    colorsInRegion.Add(ogImageRows[row][col]);
                }
                Vec3b color = new((byte)colorsInRegion.Average(key => key.Item0), (byte)colorsInRegion.Average(key => key.Item1), (byte)colorsInRegion.Average(key => key.Item2));
                for (int i = 0; i < groups.Count; i++)
                {
                    int row = groups[i] / rgb.Cols;
                    int col = groups[i] % rgb.Cols;
                    imageRows[row][col] = color;
                }
            }




        }

        watch.Stop();
        Dictionary<string, int> combos = new();

        foreach (List<int> group in newGroups)
        {
            var groupList = group.ToList(); // Materialize once

            int c = (int)Math.Round(groupList.Average(key => (double)(key % rgb.Cols)));
            int r = (int)Math.Round(groupList.Average(key => (double)(key / rgb.Cols)));

            List<Vec2i> cords = new();
            foreach (int cord in groupList)
            {
                int x = (cord % rgb.Cols) - c;
                int y = (cord / rgb.Cols) - r;
                cords.Add(new Vec2i(x, y));
            }


            var sortedCords = cords.OrderBy(v => v.Item0).ThenBy(v => v.Item1).ToList();


            string shapeKey = string.Join(";", sortedCords.Select(v => $"{v.Item0},{v.Item1}"));

            if (combos.ContainsKey(shapeKey))
                combos[shapeKey] += 1;
            else
                combos[shapeKey] = 1;
        }

        Console.WriteLine($"Total Groups : {newGroups.Count}");
        Console.WriteLine($"Average Area : {newGroups.Average(key => key.Count)}");
        Console.WriteLine($"Groups with more than 100 area : {combos.Values.Where(key => key > 100).ToArray().Length}");
        Console.WriteLine($"1 pixel area : {newGroups.Where(key => key.Count == 1).ToArray().Length}");
        Console.WriteLine($"Groups with <= 10 : {newGroups.Where(key => key.Count <= 10 && key.Count != 1).ToArray().Length}");
        Console.WriteLine($"Shapes with more than 10 apperances : {combos.Values.Count(key => key >= 10)}");
        Console.WriteLine($"total shapes : {combos.Keys.Count}");
        Console.WriteLine($"top 15 shape appearance frequency : {string.Join(", ", combos.Values.OrderByDescending(key => key).Take(15))}");
        Console.WriteLine(watch.ElapsedMilliseconds);

        Console.WriteLine(Cv2.PSNR(resized, rgb));
        RemapImage(greyScale, factor);
        Utilities.DisplayImage(BitmapConverter.ToBitmap(rgb));
        // Utilities.DisplayImage(BitmapConverter.ToBitmap(greyScale));



        Cv2.ImWrite("compare.jpg", resized);
        Cv2.ImWrite("myformat.png", rgb);
    }
    //gemeni 
    Vec3b EvaluateGradient(List<Vec3b> colorStops, float t)
    {
        if (colorStops.Count == 0) return new Vec3b(0, 0, 0);
        if (colorStops.Count == 1 || t <= 0.0f) return colorStops[0];
        if (t >= 1.0f) return colorStops[colorStops.Count - 1];

        // Scale t to the number of segments (e.g., 3 colors = 2 segments)
        float scaled = t * (colorStops.Count - 1);
        int index = (int)scaled;          // Which color we are starting from
        float localT = scaled - index;    // How far we are between color[index] and color[index + 1]

        Vec3b c1 = colorStops[index];
        Vec3b c2 = colorStops[index + 1];

        byte r = (byte)(c1.Item0 + (c2.Item0 - c1.Item0) * localT);
        byte g = (byte)(c1.Item1 + (c2.Item1 - c1.Item1) * localT);
        byte b = (byte)(c1.Item2 + (c2.Item2 - c1.Item2) * localT);

        return new Vec3b(r, g, b);
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

                int startLum = imageRows[r][c];
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

                        int index = nextRow * colsCount + nextCol;

                        if (!IsPointValid(nextRow, nextCol, rowCount, colsCount))
                            continue;

                        if (visited[index])
                            continue;


                        int nextValue = imageRows[nextRow][nextCol];

                        if (startLum == nextValue)
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
        // Span<byte> row = rows[i];








    }

    private bool IsPointValid(int row, int col, int rowCount, int colCount)
    {
        return row >= 0 &&
               row < rowCount &&
               col >= 0 &&
               col < colCount;
    }

    private Mat ResizeImage(Bitmap image, Vector2 targetRes)
    {
        Mat source = BitmapConverter.ToMat(image);
        Mat dest = new();
        OpenCvSharp.Size size = new(targetRes.X, targetRes.Y);
        Cv2.Resize(source, dest, size, 0, 0, InterpolationFlags.Area);
        return dest;
    }

    private void QuantizeImageWithRemap(Mat image, int lowEnd, int highEnd, int levels)
    {
        float range = (float)Math.Max(highEnd - lowEnd, 1.0);
        float factor = 255f / (levels - 1);

        MatRowAccessor<byte> col = image.AsRows<byte>();
        for (int i = 0; i < col.Count; i++)
        {
            Span<byte> row = col[i];
            for (int j = 0; j < row.Length; j++)
            {
                byte curPixel = row[j];
                curPixel = (byte)Math.Clamp((curPixel - lowEnd) * (255.0 / range), 0, 255);
                curPixel = (byte)(Math.Round(curPixel / factor) * factor);
                row[j] = curPixel;
            }
        }
    }
    // this means that once quanted to N levels it will not remap it and will instead be 0-N instead of 0-255
    private float QuantizeImageNoRemap(Mat image, int levels)
    {
        float factor = 255f / (levels - 1);

        MatRowAccessor<byte> col = image.AsRows<byte>();
        for (int i = 0; i < col.Count; i++)
        {
            Span<byte> row = col[i];
            for (int j = 0; j < row.Length; j++)
            {
                byte curPixel = row[j];
                curPixel = (byte)Math.Round(curPixel / factor);
                row[j] = curPixel;
            }
        }
        return factor;
    }


    // takes a quantized image and remaps it back to the full 0-255 scale by the factor
    private void RemapImage(Mat image, float factor)
    {
        MatRowAccessor<byte> col = image.AsRows<byte>();
        for (int i = 0; i < col.Count; i++)
        {
            Span<byte> row = col[i];
            for (int j = 0; j < row.Length; j++)
            {
                byte curPixel = row[j];
                curPixel = (byte)Math.Round(curPixel * factor);
                row[j] = curPixel;
            }
        }
    }


    private int[] AnalyzeImage(Mat image, int lowPercentile, int highPercentile)
    {
        MatRowAccessor<byte> col = image.AsRows<byte>();
        Dictionary<int, int> count = new();
        for (int i = 0; i < col.Count; i++)
        {
            Span<byte> row = col[i];
            for (int j = 0; j < row.Length; j++)
            {
                int curNum = row[j];
                if (!count.ContainsKey(curNum))
                {
                    count.Add(curNum, 0);
                }
                count[curNum] += 1;
            }
        }

        int[] sortedCounts = count.Keys.OrderBy(kvp => kvp).ToArray();
        int lowPercentileNum = Utilities.Percentile(sortedCounts.Length, lowPercentile);
        int highPercentileNum = Utilities.Percentile(sortedCounts.Length, highPercentile);

        return [
                sortedCounts[Math.Clamp(lowPercentileNum, 0, sortedCounts.Length - 1)],
                sortedCounts[Math.Clamp(highPercentileNum, 0, sortedCounts.Length - 1)]
        ];

    }

}