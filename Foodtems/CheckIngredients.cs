using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Foodtems
{
    public static class CheckIngredients
    {
        [FunctionName("CheckIngredients")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            // Read restricted ingredients from request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var request = JsonConvert.DeserializeObject<RequestData>(requestBody);

            // Retrieve the JSON from Azure Blob Storage
            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            string containerName = Environment.GetEnvironmentVariable("ContainerName");
            string blobName = Environment.GetEnvironmentVariable("BlobName");

            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            BlobClient blobClient = containerClient.GetBlobClient(blobName);

            BlobDownloadInfo blobDownloadInfo = await blobClient.DownloadAsync();
            string jsonContent;

            using (StreamReader reader = new StreamReader(blobDownloadInfo.Content))
            {
                jsonContent = await reader.ReadToEndAsync();
            }

            JObject json = JObject.Parse(jsonContent);

            // Flag items based on restricted ingredients
            var items = json["items"]["item"];
            var flaggedItems = new List<dynamic>();

            foreach (var item in items)
            {
                var allIngredients = GetAllIngredients(item);

                bool isRestricted = CheckForRestrictedIngredients(item, request.RestrictedIngredients);
                string flag = isRestricted ? "red" : "green";
                flaggedItems.Add(new { id = item["id"], type = item["type"], flag, ingredients = allIngredients });
            }

            return new OkObjectResult(flaggedItems);
        }

        private static bool CheckForRestrictedIngredients(JToken item, List<string> restrictedIngredients)
        {
            var batters = item["batters"]["batter"].Select(b => (string)b["type"]).ToList();
            var toppings = item["topping"].Select(t => (string)t["type"]).ToList();
            var fillings = item["fillings"]?["filling"].Select(f => (string)f["name"]).ToList() ?? new List<string>();

            var allIngredients = batters.Concat(toppings).Concat(fillings);

            // Check if all restricted ingredients are present
            return !restrictedIngredients.Except(allIngredients).Any();
        }

        private static List<string> GetAllIngredients(JToken item)
        {
            var batters = item["batters"]["batter"].Select(b => (string)b["type"]).ToList();
            var toppings = item["topping"].Select(t => (string)t["type"]).ToList();
            var fillings = item["fillings"]?["filling"].Select(f => (string)f["name"]).ToList() ?? new List<string>();

            return batters.Concat(toppings).Concat(fillings).ToList();
        }
        private class RequestData
        {
            public List<string> RestrictedIngredients { get; set; }
        }
    }
}
