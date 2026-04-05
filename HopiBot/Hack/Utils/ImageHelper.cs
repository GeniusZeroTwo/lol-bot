using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using OpenCvSharp;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace HopiBot.Hack.Utils
{
    public static class ImageHelper
    {
        public static Point LocatePattern(Bitmap large, Bitmap pattern, double threshold = 0.3)
        {
            Mat largeImage = OpenCvSharp.Extensions.BitmapConverter.ToMat(large);
            Mat template = OpenCvSharp.Extensions.BitmapConverter.ToMat(pattern);

            // 将图像转换为灰度图像
            Mat largeImageGray = new Mat();
            Mat templateGray = new Mat();
            Cv2.CvtColor(largeImage, largeImageGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(template, templateGray, ColorConversionCodes.BGR2GRAY);

            Mat result = new Mat();
            Cv2.MatchTemplate(largeImageGray, templateGray, result, TemplateMatchModes.CCorrNormed);
            Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out _, out OpenCvSharp.Point matchLoc);


            Console.WriteLine($"MinVal: {minVal}, MaxVal: {maxVal}, MatchLoc: {matchLoc}");
            if (maxVal < threshold)
            {
                return Point.Empty;
            }

            return new Point(matchLoc.X, matchLoc.Y);
        }

        public static Point LocatePatternWithBlur(Bitmap large, Bitmap pattern, double threshold = 0.5, int blurKernel = 7)
        {
            var largeImage = OpenCvSharp.Extensions.BitmapConverter.ToMat(large);
            var template = OpenCvSharp.Extensions.BitmapConverter.ToMat(pattern);

            var largeGray = new Mat();
            var templateGray = new Mat();
            Cv2.CvtColor(largeImage, largeGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(template, templateGray, ColorConversionCodes.BGR2GRAY);

            var kernelSize = blurKernel % 2 == 0 ? blurKernel + 1 : blurKernel;
            kernelSize = Math.Max(3, kernelSize);
            Cv2.GaussianBlur(largeGray, largeGray, new OpenCvSharp.Size(kernelSize, kernelSize), 0);
            Cv2.GaussianBlur(templateGray, templateGray, new OpenCvSharp.Size(kernelSize, kernelSize), 0);

            var result = new Mat();
            Cv2.MatchTemplate(largeGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var matchLoc);

            Console.WriteLine($"BlurMatch MaxVal: {maxVal}, MatchLoc: {matchLoc}");
            if (maxVal < threshold)
            {
                return Point.Empty;
            }

            return new Point(matchLoc.X, matchLoc.Y);
        }

        public static Bitmap ResizeBitmap(Bitmap originalBitmap, Size size)
        {
            // 创建新的 Bitmap 对象
            Bitmap resizedBitmap = new Bitmap(size.Width, size.Height);

            // 使用 Graphics 对象将原始图像绘制到新的 Bitmap 上并调整大小
            using (Graphics graphics = Graphics.FromImage(resizedBitmap))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(originalBitmap, 0, 0, size.Width, size.Height);
            }

            return resizedBitmap;
        }

        public static Bitmap CropBitmap(Bitmap source, Point startPoint, Size cropSize)
        {
            // 创建一个新的Bitmap来存放截取的图像
            Bitmap croppedBitmap = new Bitmap(cropSize.Width, cropSize.Height);

            // 使用Graphics绘制截取的部分到新的Bitmap上
            using (Graphics g = Graphics.FromImage(croppedBitmap))
            {
                Rectangle sourceRect = new Rectangle(startPoint, cropSize);
                Rectangle destRect = new Rectangle(Point.Empty, cropSize);
                g.DrawImage(source, destRect, sourceRect, GraphicsUnit.Pixel);
            }

            return croppedBitmap;
        }

        public static Bitmap CropToCircle(Bitmap srcImage)
        {
            int diameter = Math.Min(srcImage.Width, srcImage.Height);
            Bitmap circularTemplate = new Bitmap(diameter, diameter);

            using (Graphics g = Graphics.FromImage(circularTemplate))
            {
                g.Clear(Color.Transparent);

                GraphicsPath path = new GraphicsPath();
                path.AddEllipse(0, 0, diameter, diameter);
                g.SetClip(path);
                g.DrawImage(srcImage, 0, 0, diameter, diameter);
            }

            return circularTemplate;
        }
    }
}
