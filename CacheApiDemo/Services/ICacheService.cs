using System.Buffers.Text;

namespace CacheApiDemo.Services
{
    public interface ICacheService
    {
       // Adds or updates a cache entry based on the provided key-value pairs.
        void AddOrUpdateCache(string accountCode,
            string subAccountCode,
            string attributeCode,
            string attributeValue);
        void ClearCache();
        void LoadInitialCache();

        //  Attempts to retrieve a value from the cache using the specified key components
        bool TryGetFromCache(string accountCode, string subAccountCode, string attributeCode, out string attributeValue);
    }
}
