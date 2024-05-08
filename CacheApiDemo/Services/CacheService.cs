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
        public CacheService(IMemoryCache memoryCache, IOptions<AwsConfiguration> awsConfig, IAmazonSimpleNotificationService snsClient, IAmazonSQS sqsClient)
        {
            _memoryCache = memoryCache;
            _awsConfig = awsConfig.Value;
            _snsClient = snsClient;
            _sqsClient = sqsClient;

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
                var response = await _snsClient.SubscribeAsync(request);
                Console.WriteLine("Subscribed to SNS topic successfully.");
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
                    Console.WriteLine("Received message: " + message.Body);
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


        //it generates a cache key based on the key components, sets cache entry options(e.g., expiration time), and adds or updates the cache entry.
        public void AddOrUpdateCache(string accountCode, string subAccountCode, string attributeCode, string attributeValue)
        {

            // Generate a unique cache key based on the provided key components.
            var cacheKey = GetCacheKey(accountCode, subAccountCode, attributeCode);

            // Set cache entry options (e.g., expiration time).
            var cacheEntryOptions = new MemoryCacheEntryOptions
           ()
                //           AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)*/ // Set your desired expiration time

                .SetPriority(CacheItemPriority.High)
                .SetSlidingExpiration(TimeSpan.FromMinutes(30))
                .AddExpirationToken(new CancellationChangeToken(_resetCacheToken.Token));

            _memoryCache.Set(cacheKey, attributeValue, cacheEntryOptions);




        }
        //Attempts to retrieve a value from the cache using the specified key components.
        public bool TryGetFromCache(string accountCode, string subAccountCode, string attributeCode, out string attributeValue)
        {
            var cacheKey = GetCacheKey(accountCode, subAccountCode, attributeCode);
            return _memoryCache.TryGetValue(cacheKey, out attributeValue);
        }


        // Create a unique cache key by concatenating the key components.
        private string GetCacheKey(string accountCode, string subAccountCode, string attributeCode)
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
                AttributeValue = "blue"
            };
            var cacheEntryJson = JsonConvert.SerializeObject(cacheEntry);

            await PublishMessageToSnsTopic(cacheEntryJson);
            PollSqsQueueAsync();

            AddOrUpdateCache(cacheEntry.AccountCode, cacheEntry.SubAccountCode, cacheEntry.AttributeCode, cacheEntry.AttributeValue);
            //AddOrUpdateCache("123", "002", "size", "medium");
        }

    }
}
