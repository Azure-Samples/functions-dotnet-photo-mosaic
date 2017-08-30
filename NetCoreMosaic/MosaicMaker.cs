using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MosaicMaker
{
    public static class MosaicBuilder
    {
#if false
        private static FlickrTileProvider TileProvider = new FlickrTileProvider();
        private static QuadrantMatchingTileProvider MatchingTileProvider = new QuadrantMatchingTileProvider();
#endif

        public static int TileHeight { get; set; }
        public static int TileWidth { get; set; }
        public static int DitheringRadius { get; set; }

        public static string RootFolder { get; set; }
        public static string DownloadFolder { get; set; }
        public static string ScaledFolder { get; set; }

#if false
        public static async Task<string> CreateMosaicAsync(string baseImageFile, List<string> tileImages, CancellationToken cancel = default(CancellationToken))
        {
            MosaicBuilder.TileHeight = 16;
            MosaicBuilder.TileWidth = 16;
            MosaicBuilder.DitheringRadius = -1;

            MosaicBuilder.MatchingTileProvider.SetProgressCallBack(progressCallback);
            MosaicBuilder.MatchingTileProvider.SetInputImage(baseImageFile);

            await MosaicBuilder.MatchingTileProvider.PreprocessInputImage(MosaicBuilder.TileWidth, MosaicBuilder.TileHeight, cancel);

            await CropAndScaleTileImages(cancel);
            await MosaicBuilder.MatchingTileProvider.PreProcessTileImages(ScaledFolder, cancel);

            return await GenerateMosaic(baseImageFile, tileImages, cancel);
        }
#endif

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
            var fullPath = Path.Combine(inputFolder, inputFilename);

            using (var inputStream = File.OpenRead(fullPath))
            using (var skStream = new SKManagedStream(inputStream))  // decode the bitmap from the stream
            using (var bitmap = SKBitmap.Decode(skStream))
            using (var outBitmap = new SKBitmap(TileWidth, TileHeight, SKImageInfo.PlatformColorType, SKAlphaType.Premul))
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

                    using (var outImage = SKImage.FromBitmap(outBitmap)) {

                        // save to file
                        var imageBytes = outImage.Encode(SKEncodedImageFormat.Jpeg, 80);

                        var filename = Path.Combine(outputFolder, inputFilename);
                        using (var outStream = new FileStream(filename, FileMode.Create, FileAccess.Write)) {
                            imageBytes.SaveTo(outStream);
                        }
                    }
                }
            }
        }
    }
}

#if false
            private static async Task<string> GenerateMosaic(string baseImageFile, List<string> tileImages, CancellationToken cancel)
            {
                IStorageFile[,] mosaicTileGrid;

                using (var baseStream = await baseImageFile.OpenAsync(FileAccessMode.Read)) {
                    var baseDecoder = await BitmapDecoder.CreateAsync(baseStream);
                    var baseImageSize = new BitmapBounds { Width = baseDecoder.PixelWidth, Height = baseDecoder.PixelHeight };

                    int baseImageWidth = (int)baseImageSize.Width, baseImageHeight = (int)baseImageSize.Height;
                    int xTileCount = baseImageWidth / MosaicBuilder.TileWidth, yTileCount = baseImageHeight / MosaicBuilder.TileHeight;
                    int tileCount = xTileCount * yTileCount, currentTileCount = 0;
                    mosaicTileGrid = new IStorageFile[xTileCount, yTileCount];
                    var random = new Random();

                    var targetWidth = (uint)(xTileCount * MosaicBuilder.TileWidth);
                    var targetHeight = (uint)(yTileCount * MosaicBuilder.TileHeight);
                    var targetPixels = new byte[targetWidth * targetHeight * 4];

                    var outputFile = await RootFolder.CreateFileAsync("target.jpg", CreationCollisionOption.ReplaceExisting);
                    using (var outputStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite)) {
                        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outputStream);

                        var tileList = new List<Point>();
                        for (int x = 0; x < xTileCount; x++)
                            for (int y = 0; y < yTileCount; y++)
                                tileList.Add(new Point(x, y));

                        await Task.Run(async () => {
                            while (tileList.Count > 0) {
                                try {
                                    int nextIndex = random.Next(tileList.Count);
                                    var point = tileList[nextIndex];
                                    tileList.RemoveAt(nextIndex);
                                    var exclusionList = GetExclusionList(mosaicTileGrid, (int)point.X, (int)point.Y);
                                    var tileImageFile = MosaicBuilder.MatchingTileProvider.GetImageForTile((int)point.X, (int)point.Y, exclusionList);
                                    mosaicTileGrid[(int)point.X, (int)point.Y] = tileImageFile;

                                    byte[] tilePixels;
                                    using (var tileStream = await tileImageFile.OpenAsync(FileAccessMode.Read)) {
                                        var tileDecoder = await BitmapDecoder.CreateAsync(tileStream);
                                        var pixelProvider = await tileDecoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
                                        tilePixels = pixelProvider.DetachPixelData();
                                    }

                                    for (int y = 0; y < MosaicBuilder.TileHeight; y++) {
                                        // Copy entire row of tile image:
                                        int sourceIndex = y * MosaicBuilder.TileWidth * 4;
                                        int destIndex = (int)(((point.Y * MosaicBuilder.TileHeight + y) * targetWidth) + (point.X * MosaicBuilder.TileWidth)) * 4;
                                        Array.Copy(tilePixels, sourceIndex, targetPixels, destIndex, MosaicBuilder.TileWidth * 4);
                                    }

                                    currentTileCount++;
                                }
                                catch (Exception) { }

                                cancel.ThrowIfCancellationRequested();
                            }
                        });

                        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, targetWidth, targetHeight, 96, 96, targetPixels);
                        await encoder.FlushAsync();
                    }

                    return outputFile;
                }
            }

            private static List<string> GetExclusionList(string[,] mosaicTileGrid, int xIndex, int yIndex)
            {
                int xRadius = (MosaicBuilder.DitheringRadius != -1 ? MosaicBuilder.DitheringRadius : mosaicTileGrid.GetLength(0));
                int yRadius = (MosaicBuilder.DitheringRadius != -1 ? MosaicBuilder.DitheringRadius : mosaicTileGrid.GetLength(1));
                var exclusionList = new List<IStorageFile>();
                for (int x = Math.Max(0, xIndex - xRadius); x < Math.Min(mosaicTileGrid.GetLength(0), xIndex + xRadius); x++)
                    for (int y = Math.Max(0, yIndex - yRadius); y < Math.Min(mosaicTileGrid.GetLength(1), yIndex + yRadius); y++)
                        if (mosaicTileGrid[x, y] != null)
                            exclusionList.Add(mosaicTileGrid[x, y]);
                return exclusionList;
            }

        private static Action<int, string> progressCallback;
        public static void SetProgressCallBack(Action<int, string> progressCallback)
        {
            MosaicBuilder.progressCallback = progressCallback;

            TileProvider.SetProgressCallBack(progressCallback);
            MatchingTileProvider.SetProgressCallBack(progressCallback);
        }
    }

#endif

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


    public class QuadrantMatchingTileProvider
    {
#region Private members
        internal static int quadrantDivisionCount = 1;
        private Action<int, string> progressCallback;
        private IStorageFile inputFile;
        private AveragePixel[,][,] inputImageRGBGrid;
        private List<Tuple<IStorageFile, AveragePixel[,]>> tileImageRGBGridList;
#endregion

        /// <summary>
        /// Sets the input image
        /// </summary>
        /// <param name="inputImage"></param>
        public void SetInputImage(string inputFile)
        {
            this.inputFile = inputFile;
        }

        /// <summary>
        /// Preprocess the quadrants of the input image
        /// </summary>
        /// <param name="tileWidth"></param>
        /// <param name="tileHeight"></param>
        /// <returns></returns>
        public async Task<bool> PreprocessInputImage(int tileWidth, int tileHeight, CancellationToken cancel)
        {
            try {
                await Task.Run(async () => {
                    using (var stream = await inputFile.OpenAsync(FileAccessMode.Read)) {
                        var decoder = await BitmapDecoder.CreateAsync(stream);
                        var pixelProvider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
                        var imageSize = new Size(decoder.PixelWidth, decoder.PixelHeight);
                        byte[] inputImage = pixelProvider.DetachPixelData();

                        int xTileCount = (int)imageSize.Width / tileWidth, yTileCount = (int)imageSize.Height / tileHeight;
                        int tileDivisionWidth = (int)tileWidth / quadrantDivisionCount, tileDivisionHeight = (int)tileHeight / quadrantDivisionCount;
                        int quadrantsCompleted = 0, quadrantsTotal = xTileCount * yTileCount * quadrantDivisionCount * quadrantDivisionCount;
                        inputImageRGBGrid = new AveragePixel[xTileCount, yTileCount][,];

                        //Divide the input image into separate tile sections and calculate the average pixel value for each one
                        for (int yTileIndex = 0; yTileIndex < yTileCount; yTileIndex++) {
                            for (int xTileIndex = 0; xTileIndex < xTileCount; xTileIndex++) {
                                inputImageRGBGrid[xTileIndex, yTileIndex] = GetAverageColorGrid(inputImage, imageSize, new Rect(xTileIndex * tileWidth, yTileIndex * tileHeight, tileWidth, tileHeight));
                                cancel.ThrowIfCancellationRequested();
                                quadrantsCompleted += (quadrantDivisionCount * quadrantDivisionCount);
                            }
                        }
                    }
                }, cancel);
            }
            catch (Exception) {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Preprocess the tile images
        /// </summary>
        /// <param name="sourceImagePath"></param>
        /// <returns></returns>
        public async Task<bool> PreProcessTileImages(string sourceImageFolder, CancellationToken cancel)
        {
            try {
                tileImageRGBGridList = new List<Tuple<string, AveragePixel[,]>>();
                var files = await Directory.GetFiles(sourceImageFolder);

                var tasks = new List<Task>();
                await Task.Run(async () => {
                    foreach (var f in files) {
                        try {
                            IStorageFile file = f;

                            Size imageSize;
                            byte[] tileImage;
                            using (var stream = await file.OpenAsync(FileAccessMode.Read)) {
                                var decoder = await BitmapDecoder.CreateAsync(stream);
                                var pixelProvider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
                                imageSize = new Size(decoder.PixelWidth, decoder.PixelHeight);
                                tileImage = pixelProvider.DetachPixelData();
                            }

                            tileImageRGBGridList.Add(new Tuple<IStorageFile, AveragePixel[,]>(file, GetAverageColorGrid(tileImage, imageSize, new Rect(0, 0, imageSize.Width, imageSize.Height))));
                        }
                        catch (Exception) { }

                        filesCompleted++;
                        cancel.ThrowIfCancellationRequested();
                    }
                }, cancel);
            }
            catch (Exception) {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Returns the best match image per tile area
        /// </summary>
        /// <param name="tile"></param>
        /// <param name="excludedImageFiles"></param>
        /// <returns></returns>
        public IStorageFile GetImageForTile(int xIndex, int yIndex, List<IStorageFile> excludedImageFiles)
        {
            var tileDistances = new List<Tuple<double, IStorageFile>>();
            foreach (var tileGrid in tileImageRGBGridList) {
                double distance = 0;
                for (int x = 0; x < quadrantDivisionCount; x++)
                    for (int y = 0; y < quadrantDivisionCount; y++) {
                        distance += Math.Sqrt(Math.Abs(Math.Pow(tileGrid.Item2[x, y].R, 2) - Math.Pow(inputImageRGBGrid[xIndex, yIndex][x, y].R, 2)) +
                            Math.Abs(Math.Pow(tileGrid.Item2[x, y].G, 2) - Math.Pow(inputImageRGBGrid[xIndex, yIndex][x, y].G, 2)) +
                            Math.Abs(Math.Pow(tileGrid.Item2[x, y].B, 2) - Math.Pow(inputImageRGBGrid[xIndex, yIndex][x, y].B, 2)));
                    }
                tileDistances.Add(new Tuple<double, IStorageFile>(distance, tileGrid.Item1));
            }

            tileDistances.Sort(new FunctionComparer<Tuple<double, IStorageFile>>((t1, t2) => { return t1.Item1.CompareTo(t2.Item1); }));
            for (int i = 0; i < tileDistances.Count; i++)
                if (!excludedImageFiles.Contains(tileDistances[i].Item2))
                    return tileDistances[i].Item2;
            return tileDistances[0].Item2;
        }

        /// <summary>
        /// Converts a portion of the base image to an average RGB color
        /// </summary>
        /// <param name="imagePixels"></param>
        /// <param name="bounds"></param>
        /// <returns></returns>
        private AveragePixel[,] GetAverageColorGrid(byte[] image, Size imageSize, Rect bounds)
        {
            var rgbGrid = new AveragePixel[quadrantDivisionCount, quadrantDivisionCount];
            int xDivisionSize = (int)bounds.Width / quadrantDivisionCount, yDivisionSize = (int)bounds.Height / quadrantDivisionCount;
            for (int yDivisionIndex = 0; yDivisionIndex < quadrantDivisionCount; yDivisionIndex++)
                for (int xDivisionIndex = 0; xDivisionIndex < quadrantDivisionCount; xDivisionIndex++) {
                    double pixelCount = 0, totalR = 0, totalG = 0, totalB = 0;
                    for (int y = yDivisionIndex * yDivisionSize; y < (yDivisionIndex + 1) * yDivisionSize; y++) {
                        for (int x = xDivisionIndex * xDivisionSize; x < (xDivisionIndex + 1) * xDivisionSize; x++) {
                            var pixelIndex = (((y + (int)bounds.Y) * (int)imageSize.Width) + (x + (int)bounds.X)) * 4;
                            // Assume RGBA8:
                            totalR += image[pixelIndex];
                            totalG += image[pixelIndex + 1];
                            totalB += image[pixelIndex + 2];
                            pixelCount++;
                        }
                    }
                    rgbGrid[xDivisionIndex, yDivisionIndex] = new AveragePixel() { R = totalR / pixelCount, G = totalG / pixelCount, B = totalB / pixelCount };
                }
            return rgbGrid;
        }

        /// <summary>
        /// Sets the call back for updating progress
        /// </summary>
        /// <param name="progressCallback"></param>
        public void SetProgressCallBack(Action<int, string> progressCallback)
        {
            this.progressCallback = progressCallback;
        }

        /// <summary>
        /// Class for managing Pixel Averages
        /// </summary>
        private class AveragePixel
        {
            public double R { get; set; }
            public double G { get; set; }
            public double B { get; set; }
        }

        private class FunctionComparer<T> : Comparer<T>
        {
            readonly Func<T, T, int> comparison;

            public FunctionComparer(Func<T, T, int> comparison)
            {
                this.comparison = comparison;
            }

            public override int Compare(T x, T y)
            {
                return comparison(x, y);
            }
        }
    }
}

#endif