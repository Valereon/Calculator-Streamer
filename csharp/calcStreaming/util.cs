public static class Utilities
{
    // if copying a full 2560x1440 screen it takes from 30-45ms but going down to 1920x1080 takes only 20-30ms
    public static Bitmap GetScreenShot()
    {
        Bitmap bmpScreenCapture = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
        Graphics g = Graphics.FromImage(bmpScreenCapture);
        
        g.CopyFromScreen(Screen.PrimaryScreen.Bounds.X,
                            Screen.PrimaryScreen.Bounds.Y,
                            0, 0,
                            bmpScreenCapture.Size,
                            CopyPixelOperation.SourceCopy);

        return bmpScreenCapture;
    }

    public static void DisplayImage(Bitmap image)
    {

        Form form = new Form();
        form.Text = "Image Viewer";
        PictureBox pictureBox = new PictureBox();
        pictureBox.SizeMode = PictureBoxSizeMode.AutoSize;
        pictureBox.Image = image;
        pictureBox.Dock = DockStyle.Fill;
        form.Controls.Add(pictureBox);
        Application.Run(form);
    }
}