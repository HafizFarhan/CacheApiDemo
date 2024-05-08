namespace CacheApiDemo.Models
{
    public class AwsConfiguration
    {

        public string Region { get; set; }
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }
        public string SnsTopicArn { get; set; }
        public string QueueArn { get; set; }
        public string QueueUrl { get; set; }

    }

   
}
