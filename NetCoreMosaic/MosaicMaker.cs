using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MosaicMaker
{
    public static class MosaicBuilder
    {
        //private static FlickrTileProvider TileProvider = new FlickrTileProvider();
        private static QuadrantMatchingTileProvider MatchingTileProvider = new QuadrantMatchingTileProvider();

        public static int TileHeight { get; set; }
        public static int TileWidth { get; set; }
        public static int DitheringRadius { get; set; }
        public static int ScaleMultiplier { get; set; }

        public static string RootFolder { get; set; }
        public static string DownloadFolder { get; set; }
        public static string ScaledFolder { get; set; }

        public static string CreateMosaic(string baseImageFile, List<string> tileImages)
        {
            MosaicBuilder.TileHeight = 20;
            MosaicBuilder.TileWidth = 20;
            MosaicBuilder.DitheringRadius = -1;
            MosaicBuilder.ScaleMultiplier = 1;

            MatchingTileProvider.SetInputImage(baseImageFile);

            MatchingTileProvider.ProcessInputImageColors(MosaicBuilder.TileWidth, MosaicBuilder.TileHeight);

            CropAndScaleTileImages();
            MatchingTileProvider.ProcessTileColors(ScaledFolder);

            return GenerateMosaic(baseImageFile, tileImages);
        }

        public static bool CropAndScaleTileImages()
        {
            try {
                var files = Directory.GetFiles(DownloadFolder);
                float aspectRatio = (float)TileWidth / TileHeight;

                foreach (var f in files) {

                    var filename = Path.GetFileName(f);

                    string targetFile = Path.Combine(ScaledFolder, filename);
                    if (!File.Exists(targetFile)) { // Scale only if the output file doesn't exist
                        ResizeAndCropImage(DownloadFolder, ScaledFolder, filename, aspectRatio);
                    }
                }
            }

            catch (Exception) {
                return false;
            }
            return true;
        }

        private static void ResizeAndCropImage(string inputFolder, string outputFolder, string inputFilename, float aspectRatio)
        {
            var inputPath = Path.Combine(inputFolder, inputFilename);

            using (var inputStream = File.OpenRead(inputPath))
            using (var skStream = new SKManagedStream(inputStream))  // decode the bitmap from the stream
            using (var bitmap = SKBitmap.Decode(skStream))
            using (var outBitmap = new SKBitmap(TileWidth * ScaleMultiplier, TileHeight * ScaleMultiplier, SKImageInfo.PlatformColorType, SKAlphaType.Premul))
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
                        SaveImage(outputPath, outImage);
                    }
                }
            }
        }

        private static void SaveImage(string fullPath, SKImage outImage)
        {
            var imageBytes = outImage.Encode(SKEncodedImageFormat.Jpeg, 80);
            using (var outStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write)) {
                imageBytes.SaveTo(outStream);
            }
        }

        private static string GenerateMosaic(string baseImageFile, List<string> tileImages)
        {
            string[,] mosaicTileGrid;

            var filename = Path.GetFileNameWithoutExtension(baseImageFile);
            var extension = Path.GetExtension(baseImageFile);

            var outputPath = Path.Combine(RootFolder, $"{filename}.output{extension}");

            //var transparency = SKColors.White.WithAlpha(32); // 127 => 50%

            using (var inputStream = File.OpenRead(baseImageFile))
            using (var skStream = new SKManagedStream(inputStream))
            using (var bitmap = SKBitmap.Decode(skStream)) {

                var paint = new SKPaint() {
                    //BlendMode = SKBlendMode.Src,
                     Color = SKColors.White.WithAlpha(200)
                };

                int baseImageWidth = bitmap.Width;
                int baseImageHeight = bitmap.Height;

                int xTileCount = baseImageWidth / MosaicBuilder.TileWidth;
                int yTileCount = baseImageHeight / MosaicBuilder.TileHeight;

                int tileCount = xTileCount * yTileCount;

                mosaicTileGrid = new string[xTileCount, yTileCount];

                int finalTileWidth = MosaicBuilder.TileWidth * MosaicBuilder.ScaleMultiplier;
                int finalTileHeight = MosaicBuilder.TileHeight * MosaicBuilder.ScaleMultiplier;
                int targetWidth = xTileCount * finalTileWidth;
                int targetHeight = yTileCount * finalTileHeight;

                var tileList = new List<(int, int)>();

                // add coordinates for the left corner of each tile
                for (int x = 0; x < xTileCount; x++) {
                    for (int y = 0; y < yTileCount; y++) {
                        tileList.Add((x, y));
                    }
                }

                // create output surface
                var surface = SKSurface.Create(targetWidth, targetHeight, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
                surface.Canvas.DrawColor(SKColors.White); // clear the canvas / fill with white
                surface.Canvas.DrawBitmap(bitmap, 0, 0, paint);


                var tilePaint = new SKPaint() {
                    BlendMode = SKBlendMode.Darken
                };

                surface.Canvas.SaveLayer(tilePaint);

                var random = new Random();

                while (tileList.Count > 0) {

                    // choose a new tile at random
                    int nextIndex = random.Next(tileList.Count);
                    var point = tileList[nextIndex];
                    tileList.RemoveAt(nextIndex);

                    // get the tile image for this point
                    var exclusionList = GetExclusionList(mosaicTileGrid, point.Item1, point.Item2);
                    var tileImageFile = MatchingTileProvider.GetImageForTile(point.Item1, point.Item2, exclusionList);
                    mosaicTileGrid[point.Item1, point.Item2] = tileImageFile;

                    //Trace.WriteLine($"({point.X},{point.Y}) = {tileImageFile}");

                    // get a bitmap for the tile image
                    using (var tileFileStream = File.OpenRead(tileImageFile))
                    using (var tileImageStream = new SKManagedStream(tileFileStream))
                    using (var tileBitmap = SKBitmap.Decode(tileImageStream)) {

                        // draw the tile on the surface at the coordinates
                        surface.Canvas.DrawBitmap(
                                tileBitmap,
                                SKRect.Create(point.Item1 * TileWidth, point.Item2 * TileHeight, finalTileWidth, finalTileHeight));
                        //tilePaint);
                    }
                }

                surface.Canvas.Restore();
                surface.Canvas.Flush();
                var ouputImage = surface.Snapshot();
                SaveImage(outputPath, ouputImage);
            }

            return outputPath;
        }

        private static List<string> GetExclusionList(string[,] mosaicTileGrid, int xIndex, int yIndex)
        {
            int xRadius = (MosaicBuilder.DitheringRadius != -1 ? MosaicBuilder.DitheringRadius : mosaicTileGrid.GetLength(0));
            int yRadius = (MosaicBuilder.DitheringRadius != -1 ? MosaicBuilder.DitheringRadius : mosaicTileGrid.GetLength(1));

            var exclusionList = new List<string>();

            for (int x = Math.Max(0, xIndex - xRadius); x < Math.Min(mosaicTileGrid.GetLength(0), xIndex + xRadius); x++) {
                for (int y = Math.Max(0, yIndex - yRadius); y < Math.Min(mosaicTileGrid.GetLength(1), yIndex + yRadius); y++) {
                    if (mosaicTileGrid[x, y] != null)
                        exclusionList.Add(mosaicTileGrid[x, y]);
                }
            }

            return exclusionList;
        }
    }
}

public class QuadrantMatchingTileProvider
{
    internal static int quadrantDivisionCount = 1;
    private string inputFile;
    private SKColor[,][,] inputImageRGBGrid;
    private List<(string, SKColor[,])> tileImageRGBGridList;

    public void SetInputImage(string inputFile)
    {
        this.inputFile = inputFile;
    }

    // Preprocess the quadrants of the input image
    public void ProcessInputImageColors(int tileWidth, int tileHeight)
    {
        using (var inputStream = File.OpenRead(inputFile))
        using (var skStream = new SKManagedStream(inputStream))
        using (var bitmap = SKBitmap.Decode(skStream)) {

            int xTileCount = bitmap.Width / tileWidth;
            int yTileCount = bitmap.Height / tileHeight;

            int tileDivisionWidth = tileWidth / quadrantDivisionCount;
            int tileDivisionHeight = tileHeight / quadrantDivisionCount;

            int quadrantsCompleted = 0;
            int quadrantsTotal = xTileCount * yTileCount * quadrantDivisionCount * quadrantDivisionCount;
            inputImageRGBGrid = new SKColor[xTileCount, yTileCount][,];

            //Divide the input image into separate tile sections and calculate the average pixel value for each one
            for (int yTileIndex = 0; yTileIndex < yTileCount; yTileIndex++) {
                for (int xTileIndex = 0; xTileIndex < xTileCount; xTileIndex++) {
                    var rect = SKRectI.Create(xTileIndex * tileWidth, yTileIndex * tileHeight, tileWidth, tileHeight);
                    inputImageRGBGrid[xTileIndex, yTileIndex] = GetAverageColorGrid(bitmap, rect);
                    quadrantsCompleted += (quadrantDivisionCount * quadrantDivisionCount);
                }
            }
        }
    }

    // Convert tile images to average color
    public void ProcessTileColors(string sourceImageFolder)
    {
        tileImageRGBGridList = new List<(string, SKColor[,])>();

        foreach (var file in Directory.GetFiles(sourceImageFolder)) {

            using (var inputStream = File.OpenRead(file))
            using (var skStream = new SKManagedStream(inputStream))
            using (var bitmap = SKBitmap.Decode(skStream)) {

                var rect = SKRectI.Create(0, 0, bitmap.Width, bitmap.Height);
                tileImageRGBGridList.Add((file, GetAverageColorGrid(bitmap, rect)));
            }
        }
    }

    /// Returns the best match image per tile area
    public string GetImageForTile(int xIndex, int yIndex, List<string> excludedImageFiles)
    {
        var tileDistances = new List<(double, string)>();

        foreach (var tileGrid in tileImageRGBGridList) {
            double distance = 0;

            for (int x = 0; x < quadrantDivisionCount; x++)
                for (int y = 0; y < quadrantDivisionCount; y++) {
                    distance +=
                        Math.Sqrt(
                            Math.Abs(Math.Pow(tileGrid.Item2[x, y].Red, 2) - Math.Pow(inputImageRGBGrid[xIndex, yIndex][x, y].Red, 2)) +
                            Math.Abs(Math.Pow(tileGrid.Item2[x, y].Green, 2) - Math.Pow(inputImageRGBGrid[xIndex, yIndex][x, y].Green, 2)) +
                            Math.Abs(Math.Pow(tileGrid.Item2[x, y].Blue, 2) - Math.Pow(inputImageRGBGrid[xIndex, yIndex][x, y].Blue, 2)));
                }

            tileDistances.Add((distance, tileGrid.Item1));
        }

        var sorted = tileDistances
            // .Where(x => !excludedImageFiles.Contains(x.Item2)) // remove items from excluded list
            .OrderBy(item => item.Item1); // sort by best match

        return sorted.First().Item2;
    }

    // Converts a portion of the base image to an average RGB color
    private SKColor[,] GetAverageColorGrid(SKBitmap bitmap, SKRectI bounds)
    {
        var rgbGrid = new SKColor[quadrantDivisionCount, quadrantDivisionCount];
        int xDivisionSize = bounds.Width / quadrantDivisionCount;
        int yDivisionSize = bounds.Height / quadrantDivisionCount;

        for (int yDivisionIndex = 0; yDivisionIndex < quadrantDivisionCount; yDivisionIndex++) {
            for (int xDivisionIndex = 0; xDivisionIndex < quadrantDivisionCount; xDivisionIndex++) {

                int pixelCount = 0;
                int totalR = 0, totalG = 0, totalB = 0;

                for (int y = yDivisionIndex * yDivisionSize; y < (yDivisionIndex + 1) * yDivisionSize; y++) {
                    for (int x = xDivisionIndex * xDivisionSize; x < (xDivisionIndex + 1) * xDivisionSize; x++) {

                        var pixel = bitmap.GetPixel(x + bounds.Left, y + bounds.Top);

                        totalR += pixel.Red;
                        totalG += pixel.Green;
                        totalB += pixel.Blue;
                        pixelCount++;
                    }
                }

                var finalR = (byte)(totalR / pixelCount);
                var finalG = (byte)(totalG / pixelCount);
                var finalB = (byte)(totalB / pixelCount);

                rgbGrid[xDivisionIndex, yDivisionIndex] = new SKColor(finalR, finalG, finalB);
            }
        }

        return rgbGrid;
    }

}


#if false
public class FlickrTileProvider
{
#region Private members
    private const int maxImageCount = 100;
    private const string tagFilter = "";
    private Action<int, string> progressCallback;
    private const string apiKey = "6dba7971b2abf352b9dcd48a2e5a5921";
#endregion

    private const string searchQueryString = "http://flickr.com/services/rest/?api_key={0}&method=flickr.photos.search&tags={1}&tag_mode={2}&sort=date-posted-asc&{3}per_page=100&page={4}";

    public static string GetFlickrUri(string tags)
    {
        return String.Format(searchQueryString, apiKey, tags, tagFilter, "", 0);
    }

    public static async Task<List<string>> FetchImagesAsync(IList<PhotoInfo> imageList, CancellationToken cancel = default(CancellationToken))
    {
        // Download matching images that are not yet cached:
        int totalImageCount = imageList.Count;
        int currentImageCount = 0;
        string imageUrl = "http://farm{0}.static.flickr.com/{1}/{2}_{3}.jpg";
        string localFileFormat = "{0}_{1}_{2}_{3}.jpg";
        var folder = MosaicBuilder.DownloadFolder;
        var tasks = new List<Task>();
        var files = new List<string>();

        await Task.Run(async () => {
            for (int i = 0; i < imageList.Count; i++) {
                int imageID = i;

                var localFileName = string.Format(localFileFormat, imageList[imageID].Farm, imageList[imageID].Server, imageList[imageID].ID, imageList[imageID].Secret);

                try {
                    var localFile = Path.GetFullPath(Path.Combine(folder, localFileName));
                    files.Add(localFile);

                    continue;   // Skip downloading file if it already exists
                }
                catch (Exception) { }

                // For demo purposes, do not download new images:
                //continue;

                try {
                    var uri = new Uri(string.Format(imageUrl, imageList[imageID].Farm, imageList[imageID].Server, imageList[imageID].ID, imageList[imageID].Secret));
                    var client = new HttpClient() { MaxResponseContentBufferSize = Int32.MaxValue };
                    var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, uri), cancel);
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();

                    var localFile = await folder.CreateFileAsync(localFileName);
                    using (var fileStream = await localFile.OpenAsync(FileAccessMode.ReadWrite)) {
                        var outputStream = fileStream.GetOutputStreamAt(0);
                        var writer = new DataWriter(outputStream);
                        writer.WriteBytes(imageBytes);
                        await writer.StoreAsync();
                        await outputStream.FlushAsync();
                    }

                    files.Add(localFile);
                }
                catch (OperationCanceledException) {
                    throw;
                }
                catch (Exception) { }

                if (i % 10 == 0)
                    GC.Collect();

                currentImageCount++;
                cancel.ThrowIfCancellationRequested();
            }
        });

        return files;
    }

    public static IList<PhotoInfo> ParsePhotosFromXML(string xml)
    {
        var root = XElement.Parse(xml);
        var photos = (from photo in root.Element("photos").Elements("photo")
                      select new PhotoInfo {
                          ID = (string)photo.Attribute("id"),
                          Secret = (string)photo.Attribute("secret"),
                          Server = (string)photo.Attribute("server"),
                          Farm = (string)photo.Attribute("farm")
                      }).Take(100);
        return photos.ToList();
    }

    /// <summary>
    /// Sets the progress callback
    /// </summary>
    /// <param name="progressCallback"></param>
    public void SetProgressCallBack(Action<int, string> progressCallback)
    {
        this.progressCallback = progressCallback;
    }

    /// <summary>
    /// Object for containing image info
    /// </summary>
}

public class PhotoInfo
{
    public string ID { get; set; }
    public string Secret { get; set; }
    public string Server { get; set; }
    public string Farm { get; set; }
}
#endif

