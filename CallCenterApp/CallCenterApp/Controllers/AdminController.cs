using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace CallCenterApp.Controllers
{
    public class AdminController : Controller
    {
        private IConfiguration configuration;
        private readonly IHostingEnvironment _env;

        private static ISearchServiceClient _searchClient;
        private static HttpClient _httpClient = new HttpClient();
        public AdminController(IConfiguration iconfig, IHostingEnvironment env)
        {
            configuration = iconfig;
            _env = env;
        }
        public IActionResult Index(string code)
        {

            string appInsightsKey = configuration.GetValue<string>("AppInsights_InstrumentationKey");
            if (code != appInsightsKey)
                ViewData["status"] = "Unauthorized";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Init()
        {


            string prefixName = configuration.GetValue<string>("prefixName");
            string storageConnection = configuration.GetValue<string>("storageConnectionString");
            string searchServiceName = prefixName + "search";
            string apiKey = configuration.GetValue<string>("searchMgmtKey");

            CloudStorageAccount storageAccount;
            if (CloudStorageAccount.TryParse(storageConnection, out storageAccount))
            {
                CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer cloudBlobContainer = cloudBlobClient.GetContainerReference("audio");
                await cloudBlobContainer.CreateIfNotExistsAsync();
                cloudBlobContainer = cloudBlobClient.GetContainerReference("transcribed");
                await cloudBlobContainer.CreateIfNotExistsAsync();
            }
            else
            {
                // Otherwise, let the user know that they need to define the environment variable.
                throw new Exception("Cannot access storage account");
            }



            _searchClient = new SearchServiceClient(searchServiceName, new SearchCredentials(apiKey));
            _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
            string _searchServiceEndpoint = String.Format("https://{0}.{1}", searchServiceName, _searchClient.SearchDnsSuffix);

            bool result = RunAsync(storageConnection, _searchClient, _searchServiceEndpoint, apiKey).GetAwaiter().GetResult();

            return View();
        }
        public async Task<bool> RunAsync(string storageConnection, ISearchServiceClient _searchClient, string _searchServiceEndpoint, string apiKey)
        {
            bool result;

            result = await CreateDataSourceAsync(storageConnection, _searchServiceEndpoint).ConfigureAwait(true);
            if (!result)
                return result;
            result = await CreateSkillSet(storageConnection, _searchClient, _searchServiceEndpoint, apiKey);
            if (!result)
                return result;

            result = await CreateIndex(_searchServiceEndpoint, apiKey);
            if (!result)
                return result;
            result = await CreateIndexer(storageConnection, _searchServiceEndpoint, apiKey);
            if (!result)
                return result;
            return result;
        }
        private async Task<bool> CreateDataSourceAsync(string storageConnection, string _searchServiceEndpoint)
        {
            Console.WriteLine("Creating Data Source...");
            try
            {
                int counter = 0;
                string[] containerNames = new string[] { "audio", "transcribed" };
                foreach (string datasource in Helper.DataSourceNames)
                {
                    var file = System.IO.Path.Combine(_env.ContentRootPath, "Assets", $"{datasource}.json");
                    using (StreamReader r = new StreamReader(file))
                    {
                        string json = r.ReadToEnd();
                        json = json.Replace("[DatasourceName]", datasource);
                        json = json.Replace("[Container]", containerNames[counter]);
                        json = json.Replace("[STORAGECONNECTIONSTRING]", storageConnection);


                        string uri = String.Format("{0}/datasources/{1}?api-version={2}", _searchServiceEndpoint, datasource, Helper.apiVersion);
                        HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
                        HttpResponseMessage response = _httpClient.PutAsync(uri, content).Result;

                        if (!response.IsSuccessStatusCode)
                        {
                            return false;
                        }
                        counter++;
                    }
                }

            }
            catch (Exception ex)
            {
                return false;
            }
            return true;
        }
        private async Task<bool> CreateSkillSet(string storageConnection, ISearchServiceClient _searchClient, string _searchServiceEndpoint, string apiKey)
        {
            try
            {
                string FUNCTIONHOST = $"{configuration.GetValue<string>("prefixName")}-skillfns";
                //string FUNCTIONHOST = "callcenterskills20191213054744";
                string FUNCTIONKEY = configuration.GetValue<string>("functionKey");
                string COGNITIVESERVICESKEY = configuration.GetValue<string>("allCogSvc");



                foreach (string skillset in Helper.SkillSetNames)
                {
                    var file = System.IO.Path.Combine(_env.ContentRootPath, "Assets", $"{skillset}.json");
                    using (StreamReader r = new StreamReader(file))
                    {
                        string json = r.ReadToEnd();
                        json = json.Replace("[FUNCTIONHOST]", FUNCTIONHOST);
                        json = json.Replace("[FUNCTIONKEY]", FUNCTIONKEY);
                        json = json.Replace("[COGNITIVESERVICESKEY]", COGNITIVESERVICESKEY);
                        json = json.Replace("[STORGECONNECTIONSTRING]", storageConnection);

                        string uri = String.Format("{0}/skillsets/{1}?api-version={2}", _searchServiceEndpoint, skillset, Helper.apiVersion);
                        HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
                        HttpResponseMessage response = _httpClient.PutAsync(uri, content).Result;

                        if (!response.IsSuccessStatusCode)
                        {
                            return false;
                        }
                    }


                }
            }
            catch (Exception ex)
            {

                return false;
            }
            return true;
        }

        private async Task<bool> CreateIndex(string _searchServiceEndpoint, string apiKey)
        {
            try
            {
                var file = System.IO.Path.Combine(_env.ContentRootPath, "Assets", $"{Helper.IndexName}.json");
                using (StreamReader r = new StreamReader(file))
                {
                    string json = r.ReadToEnd();

                    json = json.Replace("[IndexName]", Helper.IndexName);
                    string uri = String.Format("{0}/indexes/{1}?api-version={2}", _searchServiceEndpoint, Helper.IndexName, Helper.apiVersion);
                    HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
                    HttpResponseMessage response = _httpClient.PutAsync(uri, content).Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {

                return false;
            }
            return true;
        }
        private async Task<bool> CreateIndexer(string storageConnection, string _searchServiceEndpoint, string apiKey)
        {
            try
            {

                int counter = 0;
                foreach (string indexer in Helper.IndexerNames)
                {
                    var file = System.IO.Path.Combine(_env.ContentRootPath, "Assets", $"{indexer}.json");
                    using (StreamReader r = new StreamReader(file))
                    {
                        string json = r.ReadToEnd();
                        json = json.Replace("[IndexerName]", indexer);
                        json = json.Replace("[DatasourceName]", Helper.DataSourceNames[counter]);
                        json = json.Replace("[IndexName]", Helper.IndexName);
                        json = json.Replace("[SkillsetName]", Helper.SkillSetNames[counter]);
                        json = json.Replace("[STORAGECONNECTIONSTRING]", storageConnection);
                        string uri = String.Format("{0}/indexers/{1}?api-version={2}", _searchServiceEndpoint, indexer, Helper.apiVersion);
                        HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
                        HttpResponseMessage response = _httpClient.PutAsync(uri, content).Result;

                        if (!response.IsSuccessStatusCode)
                        {
                            return false;
                        }
                        counter++;
                    }
                }

            }
            catch (Exception ex)
            {

                return false;
            }
            return true;
        }
    }
}