using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BingImageDownloader
{
    public class ResizeImages
    {
        //[FunctionName("CropAndResizeImages")]
        //public static void CropAndResizeImages(
        //    [QueueTrigger("image-resize-queue")] string imageDirectory, TraceWriter log)
        //{
        //    var files = Directory.GetFiles(MosaicBuilder.DownloadFolder);
        //    float aspectRatio = (float)MosaicBuilder.TileWidth / MosaicBuilder.TileHeight;

        //    foreach (var f in files) {

        //        var filename = Path.GetFileName(f);
        //        string targetFile = Path.Combine(MosaicBuilder.ScaledFolder, filename);

        //        if (!File.Exists(targetFile)) { // Scale only if the output file doesn't exist
        //            ResizeAndCropImage(MosaicBuilder.DownloadFolder, MosaicBuilder.ScaledFolder, filename, aspectRatio);
        //        }
        //    }
        //}

        private static void ResizeAndCropImage(string inputFolder, string outputFolder, string inputFilename, float aspectRatio)
        {
            var inputPath = Path.Combine(inputFolder, inputFilename);

            using (var inputStream = File.OpenRead(inputPath))
            using (var skStream = new SKManagedStream(inputStream))  // decode the bitmap from the stream
            using (var bitmap = SKBitmap.Decode(skStream))
            using (var outBitmap = new SKBitmap(MosaicBuilder.TileWidth * MosaicBuilder.ScaleMultiplier, MosaicBuilder.TileHeight * MosaicBuilder.ScaleMultiplier, SKImageInfo.PlatformColorType, SKAlphaType.Premul))
            using (var canvas = new SKCanvas(outBitmap)) {

                canvas.DrawColor(SKColors.White); // clear the canvas / fill with white

                int newTileWidth = bitmap.Width;
                int newTileHeight = bitmap.Height;
                int xpos = 0, ypos = 0;

                // crop based on aspect ratio
                double imageAspectRatio = (float)bitmap.Width / bitmap.Height;
                if (imageAspectRatio != aspectRatio) {
                    if (imageAspectRatio > aspectRatio) {
                        xpos = (bitmap.Width - bitmap.Height) / 2;
                        newTileWidth = (int)(newTileHeight * aspectRatio);
                    }
                    else {
                        ypos = (bitmap.Height - bitmap.Width) / 2;
                        newTileHeight = (int)(newTileWidth / aspectRatio);
                    }
                }

                using (var croppedBitmap = new SKBitmap(newTileWidth, newTileHeight)) {
                    bitmap.ExtractSubset(croppedBitmap, SKRectI.Create(xpos, ypos, newTileWidth, newTileHeight));
                    croppedBitmap.Resize(outBitmap, SKBitmapResizeMethod.Lanczos3);

                    var outputPath = Path.Combine(outputFolder, inputFilename);

                    using (var outImage = SKImage.FromBitmap(outBitmap)) {
                        MosaicBuilder.SaveImage(outputPath, outImage);
                    }
                }
            }
        }
    }
}
