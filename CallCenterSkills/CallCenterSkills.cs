using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using CallCenterFunctions.Common;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using CallCenterSkills.Models;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Linq;

namespace CallCenterSkills
{
    public static class CallCenterSkills
    {
        private const string TranscriptionLocationHeaderKey = "Location";
        private const string EventTypeHeaderName = "X-MicrosoftSpeechServices-Event";
        private const string SignatureHeaderName = "X-MicrosoftSpeechServices-Signature";
        [FunctionName("SubmitTranscription")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log, ExecutionContext executionContext)
        {
            log.LogInformation("Submit S2T Skill: C# HTTP trigger function processed a request.");
            log.LogInformation($"REQUEST: {new StreamReader(req.Body).ReadToEnd()}");
            req.Body.Position = 0;
            try
            {
                string sasToken;
                string skillName = executionContext.FunctionName;
                IEnumerable<WebApiRequestRecord> requestRecords = WebApiSkillHelpers.GetRequestRecords(req);
                if (requestRecords == null)
                {
                    return new BadRequestObjectResult($"{skillName} - Invalid request record array.");
                }
                string storageConnectionString = Environment.GetEnvironmentVariable("StorageConnection");

                CloudStorageAccount storageAccount;
                if (CloudStorageAccount.TryParse(storageConnectionString, out storageAccount))
                {
                    CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
                    CloudBlobContainer cloudBlobContainer = cloudBlobClient.GetContainerReference("transcribed");
                    await cloudBlobContainer.CreateIfNotExistsAsync();
                    sasToken = GetContainerSasUri(cloudBlobContainer);
                }
                else
                {
                    // Otherwise, let the user know that they need to define the environment variable.
                    throw new Exception("Cannot access storage account");
                }


                WebApiSkillResponse response = await WebApiSkillHelpers.ProcessRequestRecordsAsync(skillName, requestRecords,
                    async (inRecord, outRecord) =>
                    {
                        Uri jobId;
                        var recUrl = inRecord.Data["recUrl"] as string;
                        var recSasToken = inRecord.Data["recSasToken"] as string;

                        Transcription tc = new Transcription();

                        tc.recordingsUrls = new string[] { recUrl + recSasToken };
                        tc.models = null;
                        tc.locale = "en-US";
                        tc.name = "foobars";
                        tc.description = "bar";
                        tc.properties = new Properties();
                        tc.properties.AddDiarization = "True";
                        tc.properties.AddSentiment = "True";
                        tc.properties.AddWordLevelTimestamps = "True";
                        tc.properties.ProfanityFilterMode = "Masked";
                        tc.properties.PunctuationMode = "DictatedAndAutomatic";
                        tc.properties.TranscriptionResultsContainerUrl = sasToken;


                        var client = new HttpClient();
                        client.Timeout = TimeSpan.FromMinutes(25);
                        client.BaseAddress = new UriBuilder(Uri.UriSchemeHttps, "southcentralus.cris.ai", 443).Uri;
                        string path = "api/speechtotext/v3.0-beta1/Transcriptions";
                        client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", Environment.GetEnvironmentVariable("Ocp-Apim-Subscription-Key"));
                        string res = Newtonsoft.Json.JsonConvert.SerializeObject(tc);
                        StringContent sc = new StringContent(res);
                        sc.Headers.ContentType = JsonMediaTypeFormatter.DefaultMediaType;

                        using (var resp = await client.PostAsync(path, sc))
                        {
                            if (!resp.IsSuccessStatusCode)
                            {
                                throw new HttpRequestException("Failed to create  S2T transcription job");
                            }

                            IEnumerable<string> headerValues;
                            if (resp.Headers.TryGetValues(TranscriptionLocationHeaderKey, out headerValues))
                            {
                                if (headerValues.Any())
                                {
                                    jobId = new Uri(headerValues.First());
                                    outRecord.Data["jobId"] = jobId;
                                }
                            }
                        }




                        return outRecord;
                    });
                log.LogInformation(JsonConvert.SerializeObject(response));
                return new OkObjectResult(response);
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError(ex.StackTrace);

            }
            return null;

        }
        private static string GetContainerSasUri(CloudBlobContainer container)
        {

            string sasContainerToken;
            SharedAccessBlobPolicy adHocPolicy = new SharedAccessBlobPolicy()
            {
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24),
                Permissions = SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Create
            };
            sasContainerToken = container.GetSharedAccessSignature(adHocPolicy, null);
            return container.Uri + sasContainerToken;
        }

        [FunctionName("SortAndSummarize")]
        public static IActionResult RunProjection(
       [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
       ILogger log, ExecutionContext executionContext)
        {
            log.LogInformation("Projection: C# HTTP trigger function processed a request.");
            string skillName = executionContext.FunctionName;
            log.LogInformation($"REQUEST: {new StreamReader(req.Body).ReadToEnd()}");
            req.Body.Position = 0;
            IEnumerable<WebApiRequestRecord> requestRecords = WebApiSkillHelpers.GetRequestRecords(req);
            if (requestRecords == null)
            {
                return new BadRequestObjectResult($"{skillName} - Invalid request record array.");
            }
            WebApiSkillResponse response = WebApiSkillHelpers.ProcessRequestRecords(skillName, requestRecords,
            (inRecord, outRecord) =>
            {
                Conversation[] turns = JsonConvert.DeserializeObject<Conversation[]>(inRecord.Data["conversation"].ToString());
                List<Conversation> output = turns.ToList<Conversation>();
                if (output == null || output.All(x => x == null))
                {
                    outRecord.Data["result"] = null;
                    outRecord.Data["summary"] = null;
                    return outRecord;
                }
                var sortedList = output.OrderBy(foo => foo.offset).ToList();
                ConversationSummary summary = new ConversationSummary();
                summary.Turns = turns.Length;
                if (sortedList.Where(c => c.speaker == "1").FirstOrDefault() == null)
                {
                    summary.AverageSentiment = 0;
                    summary.LowestSentiment = 0;
                    summary.HighestSentiment = 0;
                    summary.MaxChange = new Tuple<int, float>(0, 0.0f);
                }
                else
                {
                    
                    summary.AverageSentiment = sortedList.Where(c => c.speaker == "1").Select(a => a.sentiment).Average();
                    summary.LowestSentiment = sortedList.Where(c => c.speaker == "1").Select(a => a.sentiment).Min();
                    summary.HighestSentiment = sortedList.Where(c => c.speaker == "1").Select(a => a.sentiment).Max();
                    //summary.MaxChange = ConversationSummary.MaxDiff(sortedList.Where(c => c.speaker == "1").Select(a => a.sentiment).ToList());
                    summary.Moment = ConversationSummary.GetKeyMoment(sortedList);
                    
                    
                }
                outRecord.Data["result"] = sortedList;
                outRecord.Data["summary"] = summary;
                return outRecord;
            });
            log.LogInformation(JsonConvert.SerializeObject(response));
            return new OkObjectResult(response);
        }

    }
}
