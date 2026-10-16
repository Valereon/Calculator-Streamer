using System.Numerics;

namespace SSIF;

class ProgramRunner
{
    static void Main(string[] args)
    {

        Encoder encoder = new();

        Vector2 targetRes = new Vector2(255, 240);
        encoder.Encode(targetRes);
    }
}
