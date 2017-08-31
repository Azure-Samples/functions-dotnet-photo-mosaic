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

namespace BingImageDownloader
{
    public static class DownloadImages
    {
        private const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";
        private const string SearchApiKeyName = "SearchAPIKey";
        private const string BingSearchUri = "https://api.cognitive.microsoft.com/bing/v5.0/search";

        public static async Task<List<string>> GetImageResultsAsync(string query)
        {
            var result = new List<string>();

            try {
                var httpClient = new HttpClient();

                var builder = new UriBuilder(BingSearchUri);
                var queryParams = HttpUtility.ParseQueryString(string.Empty);
                queryParams["q"] = query.ToString();
                queryParams["responseFilter"] = "Images";

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

                    var images = resultObject["images"]["value"];

                    foreach (var imageInfo in images) {
                        result.Add(imageInfo["contentUrl"].ToString());
                    }
                }

            }
            catch (Exception e) {

            }

            return result;
        }

        private static async Task DownloadImagesAsync(string queryId, List<string> imageUrls, CloudBlobContainer outputContainer)
        {
            var httpClient = new HttpClient();

            foreach (var url in imageUrls) {
                try {
                    var responseStream = await httpClient.GetStreamAsync(url);

                    var dir = outputContainer.GetDirectoryReference(queryId);
                    var blob = dir.GetBlockBlobReference(Guid.NewGuid().ToString() + ".jpg");
                    await blob.UploadFromStreamAsync(responseStream);
                }
                catch (Exception) {
                    continue;
                }
            }
        }

        [FunctionName("DownloadImages")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestMessage req,
            [Blob("bing-images")] CloudBlobContainer outputContainer,
            TraceWriter log)
        {
            // parse query parameter
            string query = req.GetQueryNameValuePairs()
                .FirstOrDefault(q => string.Compare(q.Key, "query", true) == 0)
                .Value;

            if (query != null) {
                var imageUrls = await GetImageResultsAsync(query);
                await DownloadImagesAsync(query.GetHashCode().ToString(), imageUrls, outputContainer);

                return req.CreateResponse(HttpStatusCode.OK, "Done");
            }

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
    }
}
