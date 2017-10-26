using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.ProjectOxford.Vision;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Mvc;

namespace MosaicMaker
{
    public static class MosaicBuilder
    {
        #region Member variables

        private static readonly string VisionServiceApiKey = Environment.GetEnvironmentVariable("MicrosoftVisionApiKey");
        private static readonly string ImagePredictionKey = Environment.GetEnvironmentVariable("PredictionApiKey");

        public static int TileHeight { get; set; }
        public static int TileWidth { get; set; }
        public static int DitheringRadius { get; set; }
        public static int ScaleMultiplier { get; set; }

        #endregion  

        [FunctionName("RequestMosaic")]
        public static IActionResult RequestImageProcessing(
            [HttpTrigger(AuthorizationLevel.Anonymous, new string[] { "POST" })] MosaicRequest input,
            [Queue("%generate-mosaic%")] out MosaicRequest queueOutput,
            TraceWriter log)
        {
            if (string.IsNullOrEmpty(input.OutputFilename))
                input.OutputFilename = $"{Guid.NewGuid()}.jpg";

            queueOutput = input;

            // get output URL
            var storageURL = Environment.GetEnvironmentVariable("STORAGE_URL");
            var outputContainerName = Environment.GetEnvironmentVariable("output-container");
            var location = $"{storageURL}{outputContainerName}/{input.OutputFilename}";
            log.Info($"\n\nOutput location:\n{location}\n\n");

            return new AcceptedResult(location, location);
        }

        [FunctionName("Settings")]
        public static SettingsMessage Settings(
            [HttpTrigger(AuthorizationLevel.Anonymous, new string[] { "GET" })] string input,
            TraceWriter log)
        {
            string stage = (Environment.GetEnvironmentVariable("STAGE") == null) ? "LOCAL" : Environment.GetEnvironmentVariable("STAGE");
            return new SettingsMessage() {
                Stage = stage,
                SiteURL = Environment.GetEnvironmentVariable("SITEURL"),
                StorageURL = Environment.GetEnvironmentVariable("STORAGE_URL"),
                ContainerSAS = Environment.GetEnvironmentVariable("CONTAINER_SAS"),
                InputContainerName = Environment.GetEnvironmentVariable("input-container"),
                OutputContainerName = Environment.GetEnvironmentVariable("output-container")
            };
        }

        [FunctionName("CreateMosaic")]
        public static async Task CreateMosaicAsync(
            [QueueTrigger("%generate-mosaic%")] MosaicRequest mosaicRequest,
            [Blob("%tile-image-container%")] CloudBlobContainer tileContainer,
            [Blob("%output-container%/{OutputFilename}", FileAccess.Write)] Stream outputStream,
            TraceWriter log)
        {
            var imageKeyword = mosaicRequest.ImageContentString;
            var sourceImage = await DownloadFileAsync(mosaicRequest.InputImageUrl);

            // fall back to regular vision service if PredictionApiUrl is empty, 
            // or if Custom Vision does not have high confidence
            bool noCustomImageSearch = false;

            if (String.IsNullOrEmpty(mosaicRequest.ImageContentString)) { // no keyword provided, use image recognition

                string predictionUrl = Environment.GetEnvironmentVariable("PredictionApiUrl");

                if (String.IsNullOrEmpty(predictionUrl)) { // if no Custom Vision API key was provided, skip it
                    noCustomImageSearch = true;
                }
               
                try {
                    imageKeyword = await PredictImageAsync(predictionUrl, sourceImage, log);
                    noCustomImageSearch = String.IsNullOrEmpty(imageKeyword);
                }
                catch (Exception e) {
                    log.Info($"Custom image failed: {e.Message}");
                    noCustomImageSearch = true; // on exception, use regular Vision Service
                }

                if (noCustomImageSearch) {
                    log.Info("Falling back to Vision Service");

                    sourceImage.Seek(0, SeekOrigin.Begin);
                    imageKeyword = await AnalyzeImageAsync(sourceImage);
                }
            }

            log.Info($"\n\nImage analysis: {imageKeyword}\n");

            var queryDirectory = Utilities.GetStableHash(imageKeyword).ToString();
            log.Info($"Query hash: {queryDirectory}");

            var imageUrls = await DownloadImages.GetImageResultsAsync(imageKeyword, log);
            await DownloadImages.GetBingImagesAsync(
                queryDirectory, imageUrls, tileContainer, TileWidth, TileHeight);

            await GenerateMosaicFromTilesAsync(
                sourceImage, tileContainer, queryDirectory, 
                outputStream, 
                mosaicRequest.TilePixels,
                log);

            Utilities.EmitCustomTelemetry(!noCustomImageSearch, imageKeyword);
        }

        private static async Task<Stream> DownloadFileAsync(string inputImageUrl)
        {
            var client = new HttpClient();

            try {
                var bytes = await client.GetByteArrayAsync(inputImageUrl);
                return new MemoryStream(bytes);
                
            }
            catch (Exception) {
                return null;
            }
        }

        public class MosaicRequest
        {
            public string InputImageUrl { get; set; }
            public string OutputFilename { get; set; }
            public string ImageContentString { get; set; }  // if null or empty, use image recognition on the input image
            public int TilePixels { get; set; } // override default value in app settings
        }

        #region Helpers
        private static async Task<string> AnalyzeImageAsync(Stream image)
        {
            var client = new VisionServiceClient(VisionServiceApiKey);
            var result = await client.AnalyzeImageAsync(image, new VisualFeature[] { VisualFeature.Description });

            return result.Description.Captions.FirstOrDefault().Text;
        }

        static byte[] GetImageAsByteArray(Stream imageStream)
        {
            BinaryReader binaryReader = new BinaryReader(imageStream);
            return binaryReader.ReadBytes((int)imageStream.Length);
        }

        static async Task<string> PredictImageAsync(string predictionApiUrl, Stream imageStream, TraceWriter log)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Prediction-Key", ImagePredictionKey);          

            HttpResponseMessage response;
            byte[] byteData = GetImageAsByteArray(imageStream);
            imageStream.Seek(0, SeekOrigin.Begin);

            using (var content = new ByteArrayContent(byteData)) {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response = await client.PostAsync(predictionApiUrl, content);

                var resultString = await response.Content.ReadAsStringAsync();
                var resultObject = JObject.Parse(resultString);

                var prediction = resultObject["Predictions"].First;
                var probability = prediction["Probability"].ToObject<double>();
                var tag = prediction["Tag"].ToString();

                log.Info($"Tag: {tag}, Probability {probability}");

                return probability >= 0.1 ? tag : ""; 
            }
        }

        public static async Task GenerateMosaicFromTilesAsync(
            Stream sourceImage, 
            CloudBlobContainer tileContainer, string tileDirectory, 
            Stream outputStream,
            int tilePixels,
            TraceWriter log)
        {
            using (var tileProvider = new QuadrantMatchingTileProvider()) {
                MosaicBuilder.TileHeight = int.Parse(Environment.GetEnvironmentVariable("MosaicTileWidth"));
                MosaicBuilder.TileWidth = int.Parse(Environment.GetEnvironmentVariable("MosaicTileHeight"));

                // override default tile width and height if specified
                if (tilePixels != 0) {
                    MosaicBuilder.TileWidth = MosaicBuilder.TileHeight = tilePixels;
                }

                MosaicBuilder.DitheringRadius = -1;
                MosaicBuilder.ScaleMultiplier = 1;
                List<byte[]> tileImages = await GetTileImagesAsync(tileContainer, tileDirectory, log);

                tileProvider.SetSourceStream(sourceImage);
                tileProvider.ProcessInputImageColors(MosaicBuilder.TileWidth, MosaicBuilder.TileHeight);
                tileProvider.ProcessTileColors(tileImages);

                log.Info("Generating mosaic...");
                var start = DateTime.Now;

                GenerateMosaic(tileProvider, sourceImage, tileImages, outputStream);

                log.Info($"Time to generate mosaic: {(DateTime.Now - start).TotalMilliseconds}");
            }
        }

        private static async Task<List<byte[]>> GetTileImagesAsync(CloudBlobContainer tileContainer, string tileDirectory, TraceWriter log)
        {
            var cacheDir = Path.Combine(Path.GetTempPath(), "MosaicCache", tileDirectory);
            Directory.CreateDirectory(cacheDir);
            var files = new DirectoryInfo(cacheDir).GetFiles();
            if (files.Length >= 50) {
                return files.Select(x=> File.ReadAllBytes(x.FullName)).ToList();
            }

            log.Info("Downloading tiles images from storage");
            var start = DateTime.Now;

            var directory = tileContainer.GetDirectoryReference("");
            var blobs = await directory.ListBlobsAsync(true, CancellationToken.None);

            var tasks = blobs.OfType<CloudBlockBlob>().Select(blob => 
                blob.DownloadToFileAsync(
                    Path.Combine(cacheDir, Guid.NewGuid().ToString()), FileMode.Create)
            );

            await Task.WhenAll(tasks);
            files = new DirectoryInfo(cacheDir).GetFiles();
            var result = files.Select(x => File.ReadAllBytes(x.FullName)).ToList();
            
            log.Info($"Total time to fetch tiles {(DateTime.Now - start).TotalMilliseconds}");

            return result;
        }

        public static void SaveImage(string fullPath, SKImage outImage)
        {
            var imageBytes = outImage.Encode(SKEncodedImageFormat.Jpeg, 80);
            using (var outStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write)) {
                imageBytes.SaveTo(outStream);
            }
        }

        private static void GenerateMosaic(QuadrantMatchingTileProvider tileProvider, Stream inputStream, List<byte[]> tileImages, Stream outputStream)
        {
            SKBitmap[,] mosaicTileGrid;

            inputStream.Seek(0, SeekOrigin.Begin);

            using (var skStream = new SKManagedStream(inputStream))
            using (var bitmap = SKBitmap.Decode(skStream)) {

                // use transparency for the source image overlay
                var srcImagePaint = new SKPaint() { Color = SKColors.White.WithAlpha(200) };

                int xTileCount = bitmap.Width / MosaicBuilder.TileWidth;
                int yTileCount = bitmap.Height / MosaicBuilder.TileHeight;

                int tileCount = xTileCount * yTileCount;

                mosaicTileGrid = new SKBitmap[xTileCount, yTileCount];

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
                surface.Canvas.DrawBitmap(bitmap, 0, 0, srcImagePaint);

                // using the Darken blend mode causes colors from the source image to come through
                var tilePaint = new SKPaint() { BlendMode = SKBlendMode.Darken };
                surface.Canvas.SaveLayer(tilePaint); // save layer so blend mode is applied

                var random = new Random();

                while (tileList.Count > 0) {

                    // choose a new tile at random
                    int nextIndex = random.Next(tileList.Count);
                    var tileInfo = tileList[nextIndex];
                    tileList.RemoveAt(nextIndex);

                    // get the tile image for this point
                    //var exclusionList = GetExclusionList(mosaicTileGrid, tileInfo.Item1, tileInfo.Item2);
                    var tileBitmap = tileProvider.GetImageForTile(tileInfo.Item1, tileInfo.Item2);
                    mosaicTileGrid[tileInfo.Item1, tileInfo.Item2] = tileBitmap;

                    // draw the tile on the surface at the coordinates
                    SKRect tileRect = SKRect.Create(tileInfo.Item1 * TileWidth, tileInfo.Item2 * TileHeight, finalTileWidth, finalTileHeight);
                    surface.Canvas.DrawBitmap(tileBitmap, tileRect);
                }

                surface.Canvas.Restore(); // merge layers
                surface.Canvas.Flush();

                var imageBytes = surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, 80);
                imageBytes.SaveTo(outputStream);
            }
        }

        private static List<string> GetExclusionList(string[,] mosaicTileGrid, int xIndex, int yIndex)
        {
            int xRadius = (MosaicBuilder.DitheringRadius != -1 ? MosaicBuilder.DitheringRadius : mosaicTileGrid.GetLength(0));
            int yRadius = (MosaicBuilder.DitheringRadius != -1 ? MosaicBuilder.DitheringRadius : mosaicTileGrid.GetLength(1));

            var exclusionList = new List<string>();

            // TODO: add this back. Currently requires too many input tile images

            //for (int x = Math.Max(0, xIndex - xRadius); x < Math.Min(mosaicTileGrid.GetLength(0), xIndex + xRadius); x++) {
            //    for (int y = Math.Max(0, yIndex - yRadius); y < Math.Min(mosaicTileGrid.GetLength(1), yIndex + yRadius); y++) {
            //        if (mosaicTileGrid[x, y] != null)
            //            exclusionList.Add(mosaicTileGrid[x, y]);
            //    }
            //}

            return exclusionList;
        }

        public class SettingsMessage
        {
            public string Stage { get; set; }
            public string SiteURL { get; set; }
            public string StorageURL { get; set; }
            public string ContainerSAS { get; set; }
            public string InputContainerName { get; set; }
            public string OutputContainerName { get; set; }
        }

        #endregion 
    }

    public class QuadrantMatchingTileProvider : IDisposable
    {
        internal static int quadrantDivisionCount = 1;
        private Stream inputStream;
        private SKColor[,][,] inputImageRGBGrid;
        private readonly List<(SKBitmap, SKColor[,])> tileImageRGBGridList = new List<(SKBitmap, SKColor[,])>();
        private Random random = new Random();

        public void SetSourceStream(Stream inputStream)
        {
            this.inputStream = inputStream;
            inputStream.Seek(0, SeekOrigin.Begin);
        }

        // Preprocess the quadrants of the input image
        public void ProcessInputImageColors(int tileWidth, int tileHeight)
        {
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
        public void ProcessTileColors(List<byte[]> tileImages)
        {
            foreach (var bytes in tileImages) {

                var bitmap = SKBitmap.Decode(bytes);

                var rect = SKRectI.Create(0, 0, bitmap.Width, bitmap.Height);
                tileImageRGBGridList.Add((bitmap, GetAverageColorGrid(bitmap, rect)));
            }
        }

        // Returns the best match image per tile area
        public SKBitmap GetImageForTile(int xIndex, int yIndex)
        {
            var tileDistances = new List<(double, SKBitmap)>();

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
                //.Where(x => !excludedImageFiles.Contains(x.Item2)) // remove items from excluded list
                .OrderBy(item => item.Item1); // sort by best match

            var rand = random.Next(5);

            return rand < 4 ? sorted.First().Item2 : sorted.ElementAt(1).Item2;
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
        
        public void Dispose()
        {
            foreach (var tileImage in tileImageRGBGridList) {
                tileImage.Item1.Dispose();
            }
        }
    }
}


