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

        Vector2 targetRes = new System.Numerics.Vector2(1920, 1200);
        encoder.Encode(targetRes,32);
    }
}
