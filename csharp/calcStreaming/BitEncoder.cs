

// lowkey i didnt know how to do the spread over multiple bits thing so gemeni wrote this :)
class BitEncoder
{
    int buffer = 0;
    int bitCount = 0;
    List<byte> outputBytes = new();

    public void Write(int data, int len)
    {

        int mask = (1 << len) - 1;
        int maskedData = data & mask;

        buffer = buffer | (maskedData << bitCount);
        bitCount += len;

        while (bitCount >= 8)
        {
            outputBytes.Add((byte)(buffer & 0xFF));
            buffer = buffer >> 8;
            bitCount -= 8;
        }
    }
    public void Flush()
    {
        if (bitCount > 8)
        {
            outputBytes.Add((byte)(buffer & 0xFF));
            buffer = 0;
            bitCount = 0;
        }
    }

    public List<byte> GetData()
    {
        return outputBytes;
    }
}