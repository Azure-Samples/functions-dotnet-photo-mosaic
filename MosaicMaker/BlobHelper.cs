using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace MosaicMaker
{
    internal static class StorageBlobClientExtensions
    {
        public static async Task<IEnumerable<IListBlobItem>> ListBlobsAsync(this CloudBlobDirectory directory, bool useFlatBlobListing, CancellationToken cancellationToken)
        {
            if (directory == null)
            {
                throw new ArgumentNullException("directory");
            }

            List<IListBlobItem> allResults = new List<IListBlobItem>();
            BlobContinuationToken continuationToken = null;
            BlobResultSegment result;

            do
            {
                result = 
                    await directory.ListBlobsSegmentedAsync(useFlatBlobListing, BlobListingDetails.Metadata, maxResults: null, currentToken: continuationToken, options: null, operationContext: null, cancellationToken: cancellationToken);

                if (result != null)
                {
                    IEnumerable<IListBlobItem> currentResults = result.Results;
                    if (currentResults != null)
                    {
                        allResults.AddRange(currentResults);
                    }

                    continuationToken = result.ContinuationToken;
                }
            }
            while (result != null && continuationToken != null);

            return allResults;
        }
    }
}