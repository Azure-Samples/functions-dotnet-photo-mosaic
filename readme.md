---
services: functions
platforms: dotnet
author: lindydonna
---

# Azure Functions Photo Mosaic Generator

Use Azure Functions and [Microsoft Cognitive Services Custom Vision Service](https://azure.microsoft.com/en-us/services/cognitive-services/custom-vision-service/) to generate a photo mosaic from an input image.

For example, you can train your model with Orlando landmarks, such as the Orlando Eye. Custom Vision will recognize an image of the Orlando Eye, and the function will create a photo mosaic composed of Bing image search results for "Orlando Eye." See example below.

![Orlando Eye Mosaic](images/orlando-eye-both.jpg)

## Branches

- Use the `master` branch if you're on Windows
- Use the `core` branch if you're on a Mac.

## Prerequisites

1. Visual Studio, either:
   - Visual Studio 2017 Update 3 with the Azure workload installed (Windows)
   - Visual Studio Code with the [C# extension](https://code.visualstudio.com/docs/languages/csharp) (Mac/Linux)

1. If running on a Mac/Linux, [.NET Core 2.0](https://www.microsoft.com/net/core#macos)

1. If running on a Mac/Linx, install [azure\-functions\-core\-tools](https://www.npmjs.com/package/azure-functions-core-tools) from npm

1. Azure Storage Account

1. [Azure CLI 2.0](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli?view=azure-cli-latest)

## 1. Create API keys

1. Create a Bing Search API key:

    - In the Azure portal, click **+ New** and search for **Bing Search APIs**. 
    - Enter the required information in the Create blade. You may use the lowest service tier of **S1** for this module.

1. (Optional) Create a Computer Vision API key. The function will fall back to the regular Computer Vision API if there isn't a match with images that have been trained in the Custom Vision Service. If you plan to test only with images that will match custom vision, you can skip this step.

    To create a Computer Vision API key:

    - In the Azure portal, click **+ New** and search for **Bing Search APIs**.
    - Enter the required information in the Create blade. You may use the free tier **F0** for this module.

## 2. Set up Custom Vision Service project

1. Go to https://www.customvision.ai/

1. Sign in with a Microsoft account

1. Create a new project and select the image type, such as "Landmarks"
   
1. Add images and tag them. You can use a Chrome extension such as [Bulk Image Downloader](http://www.talkapps.org/bulk-image-downloader) to download Google or Bing images of landmarks.

1. Once you have added several landmarks, click the **Train** button on the upper right. Make sure you have at least 2 tags and 5 images for each tag. 

1. (Optional) Test image recognition using the **Test** tab.

1. Click on the **Performance** tab. If you have more than one iteration, choose the latest iteration and click **Make default**.


## 3. Configure the photo mosaic project


### Run the install script 

There's a Python setup script [setup.py](setup.py) that will set up your storage account keys. It uses the Azure CLI 2.0 to automate the storage account setup. Run the following commands:

```
az login
python setup.py storage-account resource-group
```

If you get python errors, make sure you've installed Python 3 and run the command `python3` instead (see [Installing Python 3 on Mac OS X](http://docs.python-guide.org/en/latest/starting/install3/osx/)).

This will modify the file [local.settings.json](MosaicMaker/local.settings.json).

Alternatively, you can run the script from the Azure Cloud Shell in the Azure Portal. Just run `python` and paste the script. The script prints out settings values that you can use to manually modify `local.settings.json`. 

### Edit local.settings.json

1. If using Visual Studio, open **MosaicMaker.sln**. On a Mac, open the **photo-mosaic** folder in VS Code. 

1. Open the file **MosaicMaker/local.settings.json** 

1. In the [Custom Vision portal](https://www.customvision.ai/), get the URL for your prediction service. Select **Prediction URL** and copy the second URL in the dialog box, under the section "**If you have an image file**". It will have the form `https://southcentralus.api.cognitive.microsoft.com/customvision/v1.0/Prediction/<guid>/image`. Paste this value for the key `PredictionApiUrl` in **local.settings.json**.

1. In the Custom Vision portal, select the settings gear in the upper right. Copy the value of **Prediction Key** for the key `PredictionApiKey` in **local.settings.json**.

    ![Prediction API key](images/custom-vision-keys.png)

1. In the Azure portal, select your Bing Search APIs instance. Select the **Keys** menu item and copy the value of **KEY 1**. Paste the value for the key `SearchAPIKey`in **local.settings.json**.

1. (Optional) Photo mosaic will fall back to the regular vision service if there is not a match with custom vision. Paste your key for your Cognitive Services Vision Service as the value for `MicrosoftVisionApiKey` in **local.settings.json**.

### Summary of App Settings 

| Key                  | Description |
|-----                 | ------|
| AzureWebJobsStorage  | Storage account connection string. |
| SearchAPIKey         | Key for [Bing Search API](https://azure.microsoft.com/en-us/services/cognitive-services/bing-web-search-api/). |
| MicrosoftVisionApiKey | Key for [Computer Vision Service](https://azure.microsoft.com/en-us/services/cognitive-services/computer-vision/). |
| PredictionApiUrl     | Endpoint for [Cognitive Services Custom Vision Service](https://azure.microsoft.com/en-us/services/cognitive-services/custom-vision-service/). It should end with "image". |
| PredictionApiKey     | Prediction key for [Cognitive Services Custom Vision Service](https://azure.microsoft.com/en-us/services/cognitive-services/custom-vision-service/). |
| generate-mosaic      | Name of Storage queue for to trigger mosaic generation. Default value is "generate-mosaic". |
| input-container      | Name of Storage container for input images. Default value is "input-images". |
| output-container     | Name of Storage container for output images. Default value is "mosaic-output". |
| tile-image-container | Name of Storage container for tile images. Default value is "tile-images". |
| SITEURL              | Set to `http://localhost:7072` locally. Not required on Azure. |
| STORAGE_URL          | URL of storage account, in the form `https://accountname.blob.core.windows.net/` |
| CONTAINER_SAS        | SAS token for uploading to input-container. Include the "?" prefix. |
| APPINSIGHTS_INSTRUMENTATIONKEY | (optional) Application Insights instrumentation key. | 
| MosaicTileWidth      | Default width of each mosaic tile. |
| MosaicTileHeight     | Default height of each mosaic tile. |

If you want to set these values in Azure, you can set them in **local.settings.json** and use the Azure Functions Core Tools to publish to Azure.

```
func azure functionapp publish function-app-name --publish-app-settings
```

## 4. Load Tile Images

When the function app creates a mosaic, it needs source images to compose the mosaic.  The **tile-image-container** referred to in App Settings (defaulted to **tile-images**) is the container that the function will look for images to use.  Running the **setup.py** script above generated the containers for you, but you'll need to load images that container.  Using the portal, [Azure Storage Explorer](https://azure.microsoft.com/en-us/features/storage-explorer/), or any other method of uploading blobs, upload images into the **tile-image-container** inside of your storage account.  You can reuse the landmark images you downloaded earlier if desired.

## 5. Run the project

1. Compile and run:

    - If using Visual Studio, just press F5 to compile and run **PhotoMosaic.sln**.

    - If using VS Code on a Mac, the build task will run `dotnet build`. Then, navigate to the output folder and run the Functions core tools:

        ```
        cd photo-mosaic/MosaicMaker/bin/Debug/netstandard2.0/osx
        func host start
        ```

    You should see output similar to the following:

    ```
    Http Functions:

            RequestMosaic: http://localhost:7072/api/RequestMosaic

            Settings: http://localhost:7072/api/Settings

    [10/4/2017 10:24:20 PM] Host lock lease acquired by instance ID '000000000000000000000000C9A597BE'.
    [10/4/2017 10:24:20 PM] Found the following functions:
    [10/4/2017 10:24:20 PM] MosaicMaker.MosaicBuilder.RequestImageProcessing
    [10/4/2017 10:24:20 PM] MosaicMaker.MosaicBuilder.Settings
    [10/4/2017 10:24:20 PM] MosaicMaker.MosaicBuilder.CreateMosaicAsync
    [10/4/2017 10:24:20 PM]
    [10/4/2017 10:24:20 PM] Job host started
    Debugger listening on [::]:5858
    ```

2. To test that the host is up and running, navigate to [http://localhost:7072/api/Settings](http://localhost:7072/api/Settings).

## Run

- To run using the provided SPA, open in a command prompt and navigate to the `Client` directory.

    - Run `npm install`
    - Run `npm start`. This will launch a webpage at `http://127.0.0.1:8080/`. 

- To run manually, send an HTTP request using Postman or CURL:

    `POST http://localhost:7072/api/RequestMosaic`

    Body: 
    ```json
    {
    "InputImageUrl": "http://url.of.your.image",
    "ImageContentString": "optional keyword for mosaic tiles",
    "TilePixels": 20 
    }
    ```
