using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using ImageResizer;
using FarmersCognitiveServices.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Configuration;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace FarmersCognitiveServices.Controllers
{
    public class HomeController : Controller
    {
        private bool HasMatchingMetadata(CloudBlockBlob blob, string term)
        {
            //Retrieves the respective blob's metadata
            foreach (var data in blob.Metadata)
            {
                if (data.Key.StartsWith("Tag") && data.Value.Equals(term, StringComparison.InvariantCultureIgnoreCase))
                    return true;
            }

            return false;
        }
        public ActionResult Index(string id)
        {
            // Delivers a list of blob URIs and captions
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageConnectionString"]);
            CloudBlobClient clientBlob = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer containerBlob = clientBlob.GetContainerReference("images");
            List<BlobInfo> blobsList = new List<BlobInfo>();

            foreach (IListBlobItem item in containerBlob.ListBlobs())
            {
                var blob = item as CloudBlockBlob;

                if (blob != null)
                {
                    blob.FetchAttributes(); // Get the respective blob's metadata

                    if (String.IsNullOrEmpty(id) || HasMatchingMetadata(blob, id))
                    {
                        var caption = blob.Metadata.ContainsKey("Caption") ? blob.Metadata["Caption"] : blob.Name;

                        blobsList.Add(new BlobInfo()
                        {
                            ImageUri = blob.Uri.ToString(),
                            ThumbnailUri = blob.Uri.ToString().Replace("/images/", "/thumbnails/"),
                            Caption = caption
                        });
                    }
                }
            }

            ViewBag.Blobs = blobsList.ToArray();
            ViewBag.Search = id; // Enables search box to keep its state 
            return View();
        }


        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }


        [HttpPost]
        public async Task<ActionResult> Upload(HttpPostedFileBase image)
        {
            if (image != null && image.ContentLength > 0)
            {
                // Determines if selected file is an image
                if (!image.ContentType.StartsWith("image"))
                {
                    TempData["Message"] = "Only image files may be uploaded";
                }
                else
                {
                    try
                    {
                        // Stores the original image in the respective (images) container
                        CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageConnectionString"]);
                        CloudBlobClient clientBlob = storageAccount.CreateCloudBlobClient();
                        CloudBlobContainer containerBlob = clientBlob.GetContainerReference("images");
                        CloudBlockBlob photoBlock = containerBlob.GetBlockBlobReference(Path.GetFileName(image.FileName));
                        await photoBlock.UploadFromStreamAsync(image.InputStream);

                        // Creates a thumbnail and stores it in the respective container
                        using (var outputStream = new MemoryStream())
                        {
                            image.InputStream.Seek(0L, SeekOrigin.Begin);
                            var settings = new ResizeSettings { MaxWidth = 192 };
                            ImageBuilder.Current.Build(image.InputStream, outputStream, settings);
                            outputStream.Seek(0L, SeekOrigin.Begin);
                            containerBlob = clientBlob.GetContainerReference("thumbnails");
                            CloudBlockBlob thumbnail = containerBlob.GetBlockBlobReference(Path.GetFileName(image.FileName));
                            await thumbnail.UploadFromStreamAsync(outputStream);

                            //Passes the URL of the image that the user uploaded to the blob, to the Vision API which generate a description for the image
                            ComputerVisionClient vision = new ComputerVisionClient(
                                new ApiKeyServiceClientCredentials(ConfigurationManager.AppSettings["SubscriptionKey"]),
                                new System.Net.Http.DelegatingHandler[] { });
                            vision.Endpoint = ConfigurationManager.AppSettings["VisionEndpoint"];

                            VisualFeatureTypes[] features = new VisualFeatureTypes[] { VisualFeatureTypes.Description };
                            var result = await vision.AnalyzeImageAsync(photoBlock.Uri.ToString(), features);

                            //Stores the description in the blob metadata for the respective image
                            photoBlock.Metadata.Add("Caption", result.Description.Captions[0].Text);

                            for (int i = 0; i < result.Description.Tags.Count; i++)
                            {
                                string key = String.Format("Tag{0}", i);
                                photoBlock.Metadata.Add(key, result.Description.Tags[i]);
                            }

                            await photoBlock.SetMetadataAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        // If an error occurs
                        TempData["Message"] = ex.Message;
                    }
                }
            }

            return RedirectToAction("Index");

        }


        [HttpPost]
        public ActionResult Search(string term)
        {
            return RedirectToAction("Index", new { id = term });
        }


    }
}