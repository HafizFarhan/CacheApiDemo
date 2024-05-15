using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using CacheApiDemo.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
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
            // Create a PublishRequest object with the specified topic ARN and message
            var request = new PublishRequest
            {
                TopicArn = _awsConfig.SnsTopicArn,
                Message = message
            };
            try
            {
                // Log the start of the publishing event.
                Console.WriteLine("Publisher Event Start");
                // Publish the message to the SNS topic.
                var response = _snsClient.PublishAsync(request).Result;
                // Log the successful publishing of the message with the MessageId.
                Console.WriteLine($"Message published to SNS topic. MessageId: {response.MessageId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to publish message to SNS topic. Error: {ex.Message}");
            }
        }
        public async Task SubscribeToSnsTopicAsync()
        {
            // Check if the AWS configuration or SNS topic ARN is not properly loaded
            if (_awsConfig == null || _awsConfig.SnsTopicArn == null)
            {
                // Log a message indicating the configuration issue.
                Console.WriteLine("AWS configuration is not properly loaded.");
                return; // Exit the function early if configuration is invalid.
            }
            // Create a SubscribeRequest object with the specified topic ARN, protocol, and endpoint.
            var request = new SubscribeRequest
            {
                TopicArn = _awsConfig.SnsTopicArn,
                Protocol = "sqs",
                Endpoint = _awsConfig.QueueArn
            };
            try
            {
                // Log the start of the subscription process
                Console.WriteLine("Subscribing to SNS topic");
                // Send the subscription request asynchronously and await the response.
                var response = await _snsClient.SubscribeAsync(request);
                // Log the successful subscription response.
                Console.WriteLine("Subscribed to SNS Response : "+response);
            }
            catch (Exception ex)
            {
                // Log any exception that occurs during the subscription process.
                Console.WriteLine("Failed to subscribe to SNS topic. Error: " + ex.Message);
            }
        }
        public async Task PollSqsQueueAsync()
        {
            // Create a ReceiveMessageRequest object to configure the polling settings.
            var request = new ReceiveMessageRequest
            {
                QueueUrl = _awsConfig.QueueUrl,  //Set the SQS queue URL from configuration.
                MaxNumberOfMessages = 10, //  Maximum number of messages to retrieve per poll.
                WaitTimeSeconds = 20 // Enable long polling to reduce empty responses.
            };
            // HashSet to keep track of processed message IDs to avoid processing duplicates.
            var processedMessageIds = new HashSet<string>();
            // Infinite loop to continuously poll the SQS queue.
            while (true)
            {
                // Asynchronously receive messages from the SQS queue.
                var response = await _sqsClient.ReceiveMessageAsync(request);
                // Check if any messages were received.
                if (response.Messages.Any())
                {
                    // Iterate over each received message
                    foreach (var message in response.Messages)
                    {
                        // Check if message has already been processed
                        if (processedMessageIds.Contains(message.MessageId))
                        {
                            continue; // Skip already processed message
                        }
                        // Log the received message body.
                        Console.WriteLine("Message receviced: " + message.Body);
                        try
                        {
                            // Deserialize the message body into a JObject.
                            var messageObject = JsonConvert.DeserializeObject<JObject>(message.Body);
                            // Extract the actual message content from the "Message" property.
                            var messageBody = messageObject["Message"].ToString();
                            // Deserialize the message content into a CacheEntryModel object.
                            var cacheEntry = JsonConvert.DeserializeObject<CacheEntryModel>(messageBody);
                            // Check if the deserialization was successful.
                            if (cacheEntry != null)
                            {
                                // Update the cache based on the received message
                                AddOrUpdateCache(cacheEntry.AccountCode, cacheEntry.SubAccountCode, cacheEntry.AttributeCode, cacheEntry.AttributeValue);
                                processedMessageIds.Add(message.MessageId);
                            }
                            else
                            {
                                // Log a message if deserialization failed.
                                Console.WriteLine("Failed to deserialize message.");
                            }
                        }
                        catch (JsonException je)
                        {
                             // Log any JSON deserialization errors.
                            Console.WriteLine("Error deserializing message: " + je.Message);
                        }
                    }
                }        
            }
        }
        public Dictionary<string, object> GetAllCachedEntries()
        {
            // Create a new dictionary to hold all cached entries.
            var cachedEntries = new Dictionary<string, object>();
            // Iterate over each cache key in the _cacheKeys collection.
            foreach (var cacheKey in _cacheKeys)
            {
                // Try to get the cached value for the current key.
                if (_memoryCache.TryGetValue(cacheKey, out object value))
                {
                    // If the value is found, add it to the cachedEntries dictionary.
                    cachedEntries.Add(cacheKey, value);
                }
            }
            // Return the dictionary containing all cached entries.
            return cachedEntries;
        }
        //it generates a cache key based on the key components, sets cache entry options(e.g., expiration time), and adds or updates the cache entry.
        public void AddOrUpdateCache(string accountCode, string subAccountCode, string attributeCode, string attributeValue)
        {
            try
            {
                // Check if any of the input parameters are null or empty
                if (string.IsNullOrEmpty(accountCode) || string.IsNullOrEmpty(subAccountCode) || string.IsNullOrEmpty(attributeCode) || string.IsNullOrEmpty(attributeValue))
                {
                    Console.WriteLine("Invalid input parameters.");
                    return;  // Exit the function if validation fails
                }
                // Generate the cache key using the provided input parameters
                var cacheKey = GetCacheKey(accountCode, subAccountCode, attributeCode);
                // Check if cache key generation failed
                if (string.IsNullOrEmpty(cacheKey))
                {
                    Console.WriteLine("Failed to generate cache key.");
                    return;  // Exit the function if cache key generation fails
                }
                // Log cache before update
                Console.WriteLine("Cache Before Update:");
                LogAllCachedEntries();
                // Add the cache key to the _cacheKeys collection
                _cacheKeys.Add(cacheKey);

                // Set cache entry options (e.g., priority, sliding expiration time, and expiration token)
                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetPriority(CacheItemPriority.High)
                    .SetSlidingExpiration(TimeSpan.FromMinutes(30))
                    .AddExpirationToken(new CancellationChangeToken(_resetCacheToken.Token));
                // Update the cache with the new value and cache entry options
                _memoryCache.Set(cacheKey, attributeValue, cacheEntryOptions);

                // Log cache after update
                Console.WriteLine("Cache After Update:");
               LogAllCachedEntries();
            }
            catch (Exception ex)
            {
                // Log any exceptions that occur during the cache update process
                Console.WriteLine($"Error in AddOrUpdateCache: {ex.Message}");
            }
        }
        //Attempts to retrieve a value from the cache using the specified key components.
        public bool TryGetFromCache(string accountCode, string subAccountCode, string attributeCode, out string attributeValue)
        {
            // Generate the cache key using the provided input parameters
            var cacheKey = GetCacheKey(accountCode, subAccountCode, attributeCode);
            // Attempt to retrieve the value from the cache using the generated cache key
            return _memoryCache.TryGetValue(cacheKey, out attributeValue);
        }
        private void LogAllCachedEntries()
        {
            // Retrieve all cached entries from the cache
            var cachedEntries = GetAllCachedEntries();
            // Iterate through each cached entry and log its key and value
            foreach (var entry in cachedEntries)
            {
                Console.WriteLine($"Key: {entry.Key}, Value: {entry.Value}");
            }
        }
        // Create a unique cache key by concatenating the key components.
        public string GetCacheKey(string accountCode, string subAccountCode, string attributeCode)
        {
            // Generate a cache key by concatenating the input parameters with underscores
            return $"{accountCode}_{subAccountCode}_{attributeCode}";
        }
        public void ClearCache()
        {
            // Check if the cancellation token has not been requested
            if (!_resetCacheToken.IsCancellationRequested)
            {
                // Request cancellation to trigger cache invalidation
                _resetCacheToken.Cancel();
                // Dispose the current cancellation token to release resources
                _resetCacheToken.Dispose();
                // Create a new cancellation token for future cache invalidation requests
                _resetCacheToken = new CancellationTokenSource();
            }
        }
        public async Task LoadInitialCache()
        {
            // Subscribe to the SNS topic to receive cache update notifications
            await SubscribeToSnsTopicAsync();
            await PollSqsQueueAsync();            
        }
    }
}