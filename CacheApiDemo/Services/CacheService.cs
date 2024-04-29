using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace CacheApiDemo.Services
{
    public class CacheService : ICacheService
    {
        private readonly IMemoryCache _memoryCache;
        private CancellationTokenSource _resetCacheToken = new CancellationTokenSource();


        public CacheService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
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

        public void LoadInitialCache()
        {
           
         
            AddOrUpdateCache("123", "001", "color", "blue");
            AddOrUpdateCache("123", "002", "size", "medium");
        }



    }
}
