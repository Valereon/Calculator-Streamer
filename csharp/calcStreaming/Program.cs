using System.Numerics;

namespace SSIF;

class Program
{
    static void Main(string[] args)
    {

        // this fixes if your display is at a higher dpi than 100% in windows 
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);


        Encoder encoder = new();

        Vector2 targetRes = new Vector2(80, 60);
        encoder.Encode(targetRes);
    }
}
