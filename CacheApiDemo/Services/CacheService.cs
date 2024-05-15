using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SimpleNotificationService.Util;
using Amazon.SQS;
using Amazon.SQS.Model;
using CacheApiDemo.Models;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;
using CacheApiDemo.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace CacheApiDemo.Services
{
    public class CacheService : ICacheService
    {
        private readonly IMemoryCache _memoryCache;

        private CancellationTokenSource _resetCacheToken = new CancellationTokenSource();
        private readonly AwsConfiguration _awsConfig;
        private readonly IAmazonSimpleNotificationService _snsClient;
        private readonly IAmazonSQS _sqsClient;
        //string queueUrl = "https://sqs.eu-north-1.amazonaws.com/103353526655/Update";
        private readonly HashSet<string> _cacheKeys;
        public CacheService(IMemoryCache memoryCache, IOptions<AwsConfiguration> awsConfig, IAmazonSimpleNotificationService snsClient, IAmazonSQS sqsClient)
        {
            _memoryCache = memoryCache;
            _awsConfig = awsConfig.Value;
            _snsClient = snsClient;
            _sqsClient = sqsClient;
            _cacheKeys = new HashSet<string>();

        }
        public async Task PublishMessageToSnsTopic(string message)
        {
            var request = new PublishRequest
            {

                TopicArn = _awsConfig.SnsTopicArn,
                Message = message
            };

            try
            {
                Console.WriteLine("Publisher Event Start");
                var response = _snsClient.PublishAsync(request).Result;
                Console.WriteLine($"Message published to SNS topic. MessageId: {response.MessageId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to publish message to SNS topic. Error: {ex.Message}");
            }
        }
        public async Task SubscribeToSnsTopicAsync()
        {

            Console.WriteLine();
            Console.WriteLine();

            if (_awsConfig == null || _awsConfig.SnsTopicArn == null)
            {
                Console.WriteLine("AWS configuration is not properly loaded.");
            }

            var request = new SubscribeRequest
            {
                TopicArn = _awsConfig.SnsTopicArn,
                Protocol = "sqs",
                Endpoint = _awsConfig.QueueArn
            };

            try
            {
                Console.WriteLine("Subscribing to SNS topic");
                var response = await _snsClient.SubscribeAsync(request);
                Console.WriteLine("Subscribed to SNS Response : "+response);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to subscribe to SNS topic. Error: " + ex.Message);
            }

        }
        public async Task PollSqsQueueAsync()
        {
            var request = new ReceiveMessageRequest
            {
                QueueUrl = _awsConfig.QueueUrl,
                MaxNumberOfMessages = 10, // adjust as needed
                WaitTimeSeconds = 20 // enable long polling
            };

            while (true)
            {
                var response = await _sqsClient.ReceiveMessageAsync(request);

                foreach (var message in response.Messages)
                {
                    Console.WriteLine("Message receviced: " + message.Body);
                    try
                    {
                        var messageObject = JsonConvert.DeserializeObject<JObject>(message.Body);
                        var messageBody = messageObject["Message"].ToString();
                        var cacheEntry = JsonConvert.DeserializeObject<CacheEntryModel>(messageBody);
                        if (cacheEntry != null)
                        {
                            // Update the cache based on the received message
                            AddOrUpdateCache(cacheEntry.AccountCode, cacheEntry.SubAccountCode, cacheEntry.AttributeCode, cacheEntry.AttributeValue);
                        }
                        else
                        {
                            Console.WriteLine("Failed to deserialize message.");
                        }
                    }
                    catch (JsonException je)
                    {
                        Console.WriteLine("Error deserializing message: " + je.Message);
                    }
                    // Delete the message from the queue
                    var deleteRequest = new DeleteMessageRequest
                    {
                        QueueUrl = _awsConfig.QueueUrl,
                        ReceiptHandle = message.ReceiptHandle
                    };
                    await _sqsClient.DeleteMessageAsync(deleteRequest);
                }
            }
        }
        public Dictionary<string, object> GetAllCachedEntries()
        {
            var cachedEntries = new Dictionary<string, object>();

            foreach (var cacheKey in _cacheKeys)
            {
                if (_memoryCache.TryGetValue(cacheKey, out object value))
                {
                    cachedEntries.Add(cacheKey, value);

                }
            }

            return cachedEntries;
        }

        //it generates a cache key based on the key components, sets cache entry options(e.g., expiration time), and adds or updates the cache entry.
        public void AddOrUpdateCache(string accountCode, string subAccountCode, string attributeCode, string attributeValue)
        {
            try
            {
                if (string.IsNullOrEmpty(accountCode) || string.IsNullOrEmpty(subAccountCode) || string.IsNullOrEmpty(attributeCode) || string.IsNullOrEmpty(attributeValue))
                {
                    Console.WriteLine("Invalid input parameters.");
                    return;
                }

                var cacheKey = GetCacheKey(accountCode, subAccountCode, attributeCode);
                _cacheKeys.Add(cacheKey);
                if (string.IsNullOrEmpty(cacheKey))
                {
                    Console.WriteLine("Failed to generate cache key.");
                    return;
                }

                // Log cache before update
                Console.WriteLine("Cache Before Update:");
                LogAllCachedEntries();

                // Set cache entry options (e.g., expiration time).
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetPriority(CacheItemPriority.High)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(30))
                    .AddExpirationToken(new CancellationChangeToken(_resetCacheToken.Token));

                _memoryCache.Set(cacheKey, attributeValue, cacheEntryOptions);

                // Log cache after update
                Console.WriteLine("Cache After Update:");
                LogAllCachedEntries();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddOrUpdateCache: {ex.Message}");
            }
        }
        //Attempts to retrieve a value from the cache using the specified key components.
        public bool TryGetFromCache(string accountCode, string subAccountCode, string attributeCode, out string attributeValue)
        {
            var cacheKey = GetCacheKey(accountCode, subAccountCode, attributeCode);
            return _memoryCache.TryGetValue(cacheKey, out attributeValue);
        }
        private void LogAllCachedEntries()
        {
            var cachedEntries = GetAllCachedEntries();
            foreach (var entry in cachedEntries)
            {
                Console.WriteLine($"Key: {entry.Key}, Value: {entry.Value}");
            }
        }

        // Create a unique cache key by concatenating the key components.
        public string GetCacheKey(string accountCode, string subAccountCode, string attributeCode)
        {
            return $"{accountCode}_{subAccountCode}_{attributeCode}";
        }
        public void ClearCache()
        {
            if (!_resetCacheToken.IsCancellationRequested)
            {
                _resetCacheToken.Cancel();
                _resetCacheToken.Dispose();
                _resetCacheToken = new CancellationTokenSource();
            }

        }

        public async Task LoadInitialCache()
        {
            await SubscribeToSnsTopicAsync();


            var cacheEntry = new CacheEntryModel
            {
                AccountCode = "123",
                SubAccountCode = "001",
                AttributeCode = "color",
                AttributeValue = "red"
            };
            var cacheEntryJson = JsonConvert.SerializeObject(cacheEntry);

            await PublishMessageToSnsTopic(cacheEntryJson);
            PollSqsQueueAsync();

            //AddOrUpdateCache(cacheEntry.AccountCode, cacheEntry.SubAccountCode, cacheEntry.AttributeCode, cacheEntry.AttributeValue);
            //AddOrUpdateCache("123", "002", "size", "medium");
        }

    }
}