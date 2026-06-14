using System.Collections.Concurrent;
using System.Drawing;

namespace Zadatak36
{
    public class Binarizator
    {
        //ovaj path je takav jer je trenutni folder kad debagujem u bin/debug/net8.0 folderu
        //po potrebi moze da se promeni ili da se premesti folder
        private static string path = "..\\..\\..\\Slike\\";
        public static async Task<byte[]> Binarizuj(string naziv, byte prag)
        {
            Bitmap image;
            try
            {
                image = new Bitmap(path + naziv);
                int x, y;
                Color pixelColor;
                float average;
                for (x = 0; x < image.Width; x++)
                {
                    for (y = 0; y < image.Height; y++)
                    {
                        pixelColor = image.GetPixel(x, y);
                        average = (pixelColor.R + pixelColor.G + pixelColor.B) / 3;
                        if (average >= prag) image.SetPixel(x, y, Color.White);
                        else image.SetPixel(x, y, Color.Black);
                    }
                }
                MemoryStream stream = new MemoryStream();
                using (stream)
                {
                    image.Save(stream, image.RawFormat);
                    return stream.ToArray();
                }
            }
            catch (ArgumentException e)
            {
                Console.WriteLine("Greska pri binarizaciji: " + e);
                return new byte[0];
            }
        }
    }
}
