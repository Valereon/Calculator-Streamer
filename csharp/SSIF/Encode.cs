using System.Numerics;
using OpenCvSharp;
using OpenCvSharp.GdipExtensions;

class Encoder
{
    public void Encode(Vector2 targetRes, int quantLevels = 32, int cellSize = 20, int lowPercentile = 5, int highPercentile = 95)
    {
        Bitmap screenShot = Utilities.GetScreenShot();
        // Utilities.DisplayImage(screenShot);
        Mat resized = ResizeImage(screenShot, targetRes);
        Mat greyScale = new();
        Cv2.CvtColor(resized, greyScale, ColorConversionCodes.RGB2GRAY);


        MatRowAccessor<byte> what = greyScale.AsRows<byte>();
        Dictionary<int, int> count = new();
        for (int i = 0; i < what.Count; i++)
        {
            Span<byte> row = what[i];
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

        var sortedCounts = count.OrderBy(kvp => kvp.Key);
        int lowPercentileNum = Utilities.Percentile(sortedCounts.Count(), lowPercentile);
        int highPercentileNum = Utilities.Percentile(sortedCounts.Count(), highPercentile);
        QuantizeImage(greyScale, lowPercentileNum, highPercentileNum, quantLevels);
        Utilities.DisplayImage(BitmapConverter.ToBitmap(greyScale));

    }

    private Mat ResizeImage(Bitmap image, Vector2 targetRes)
    {
        Mat source = BitmapConverter.ToMat(image);
        Mat dest = new();
        OpenCvSharp.Size size = new(targetRes.X, targetRes.Y);
        Cv2.Resize(source, dest, size, 0, 0, InterpolationFlags.Linear);
        return dest;
    }

    private void QuantizeImage(Mat image, int lowEnd, int highEnd, int levels)
    {
        float range = (float)Math.Max(highEnd - lowEnd, 1.0);
        float factor = 255f / (levels - 1);
        Console.WriteLine($"{factor}");

        MatRowAccessor<byte> col = image.AsRows<byte>();
        for (int i = 0; i < col.Count; i++)
        {
            Span<byte> row = col[i];
            for (int j = 0; j < row.Length; j++)
            {
                byte curPixel = row[j];
                // curPixel = (byte)Math.Clamp((curPixel - lowEnd) * (255.0 / range), 0, 255);
                curPixel = (byte)(Math.Round(curPixel / factor) * factor);
                row[j] = curPixel;
            }
        }
    }
    // lower = np.percentile(image, 5)
    // upper = np.percentile(image, 95)
    // range_val = upper - lower
    // range_val = max(upper - lower, 1.0)

    // remapped = np.clip((image - lower) * (255.0 / range_val), 0, 255)

    // factor = 255.0 / (levels - 1)
    // quantized = np.round(remapped / factor)
    // quantized = quantized.astype(np.uint8)
    // return quantized
}