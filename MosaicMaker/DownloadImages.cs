using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Web;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Diagnostics;

namespace MosaicMaker
{
    public static class DownloadImages
    {
        private const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";
        private const string SearchApiKeyName = "SearchAPIKey";
        private const string BingSearchUri = "https://api.cognitive.microsoft.com/bing/v7.0/images/search";

        public static async Task<List<string>> GetImageResultsAsync(string query, TraceWriter log)
        {
            var result = new List<string>();

            try {
                var httpClient = new HttpClient();

                var builder = new UriBuilder(BingSearchUri);
                var queryParams = HttpUtility.ParseQueryString(string.Empty);
                queryParams["q"] = query.ToString();
                queryParams["count"] = "100";

                builder.Query = queryParams.ToString();

                var request = new HttpRequestMessage() {
                    RequestUri = builder.Uri,
                    Method = HttpMethod.Get
                };

                var apiKey = Environment.GetEnvironmentVariable(SearchApiKeyName);
                request.Headers.Add(SubscriptionKeyHeader, apiKey);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows Phone 8.0; Trident/6.0; IEMobile/10.0; ARM; Touch; NOKIA; Lumia 822)");

                var response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode) {
                    var resultString = await response.Content.ReadAsStringAsync();
                    var resultObject = JObject.Parse(resultString);

                    if (resultObject == null) {
                        log.Info("ERROR: No results from image search");
                    }

                    var images = resultObject["value"];

                    foreach (var imageInfo in images) {
                        result.Add(imageInfo["thumbnailUrl"].ToString());
                    }
                }
            }
            catch (Exception e) {
                log.Info($"Exception during image search: {e.Message}");
            }

            return result;
        }

        public static async Task GetBingImagesAsync(
            string queryId, List<string> imageUrls, 
            CloudBlobContainer outputContainer,
            int tileWidth, int tileHeight)
        {
            var httpClient = new HttpClient();
            var dir = outputContainer.GetDirectoryReference(queryId);

            var cachedTileCount = dir.ListBlobs(true).Count();

            if (cachedTileCount >= 100) {
                Trace.WriteLine($"Skipping tile download, have {cachedTileCount} images cached");
                return;
            }

            foreach (var url in imageUrls) {
                try {
                    var resizedUrl = $"{url}&w={tileWidth}&h={tileHeight}&c=7";
                    var queryString = HttpUtility.ParseQueryString(new Uri(url).Query);
                    var imageId = queryString["id"] + ".jpg";

                    var blob = dir.GetBlockBlobReference(imageId);

                    if (!await blob.ExistsAsync()) {
                        using (var responseStream = await httpClient.GetStreamAsync(resizedUrl)) {
                            Trace.WriteLine($"Downloading blob: {imageId}");
                            await blob.UploadFromStreamAsync(responseStream);
                        }
                    }
                    else {
                        Trace.WriteLine($"Skipping blob download: {imageId}");
                    }
                }
                catch (Exception e) {
                    Trace.WriteLine($"Exception downloading blob: {e.Message}");
                    continue;
                }
            }
        }
    }
}
