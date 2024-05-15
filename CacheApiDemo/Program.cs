using Amazon.SimpleNotificationService;
using Amazon.SQS;
using CacheApiDemo.Services;
using Amazon;
using CacheApiDemo.Models;
using Microsoft.Extensions.DependencyInjection;
using Amazon.Runtime;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache(); // Register in-memory cache
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddSingleton<CacheBackgroundService>();
builder.Services.AddHostedService<CacheBackgroundService>();
builder.Services.Configure<AwsConfiguration>(builder.Configuration.GetSection("AWS"));
var awsConfig = builder.Configuration.GetSection("AWS").Get<AwsConfiguration>();

builder.Services.AddSingleton<IAmazonSimpleNotificationService>(sp =>
    new AmazonSimpleNotificationServiceClient(
        new BasicAWSCredentials(awsConfig.AccessKey, awsConfig.SecretKey),
        new AmazonSimpleNotificationServiceConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(awsConfig.Region)
        }
        ));
builder.Services.AddSingleton<IAmazonSQS>(sp =>
    new AmazonSQSClient(
        new BasicAWSCredentials(awsConfig.AccessKey, awsConfig.SecretKey),
        new AmazonSQSConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(awsConfig.Region) }
    ));

var app = builder.Build();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
