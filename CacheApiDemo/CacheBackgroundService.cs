using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CacheApiDemo.Services
{
    public class CacheBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;

        public CacheBackgroundService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();
                    cacheService.ClearCache();
                    cacheService.LoadInitialCache();
                }
                // Get the current time
                var currentTime = DateTime.Now;

                // Calculate the time until the next 8 AM
                var nextExecutionTime = currentTime.Date.AddDays(1).AddHours(8);

                // Delay the task until the next 8 AM
                await Task.Delay(nextExecutionTime - currentTime, stoppingToken);
            }
        }
    }
}
