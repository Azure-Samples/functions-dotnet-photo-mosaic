using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MosaicMaker;
using System.IO;

namespace Runner
{
    class Program
    {
        static void Main(string[] args)
        {
            MosaicBuilder.RootFolder = @"C:\Users\donnam\Downloads\temp";
            MosaicBuilder.DownloadFolder = Path.Combine(MosaicBuilder.RootFolder, "DownloadedImages");
            MosaicBuilder.ScaledFolder = Path.Combine(MosaicBuilder.RootFolder, "ScaledImages");

            //MosaicBuilder.TileHeight = 100;
            //MosaicBuilder.TileWidth = 100;
            //MosaicBuilder.ScaleMultiplier = 1;

            Directory.CreateDirectory(MosaicBuilder.DownloadFolder);
            Directory.CreateDirectory(MosaicBuilder.ScaledFolder);

            //MosaicBuilder.CropAndScaleTileImages();

            MosaicBuilder.CreateMosaic(
                args[0],
                Directory.GetFiles(MosaicBuilder.DownloadFolder).ToList());
        }
    }
}
