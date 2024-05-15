using CacheApiDemo.Models;
using CacheApiDemo.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CacheApiDemo.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmployeeController : ControllerBase
    {
        private readonly ICacheService _cacheService;
        public EmployeeController(ICacheService cacheService)
        {
            _cacheService = cacheService;
        }

        [HttpGet("{accountCode}/{subAccountCode}/{attributeCode}")]
        public IActionResult GetAttributeValue(string accountCode, string subAccountCode, string attributeCode)
        {
            _cacheService.TryGetFromCache(accountCode, subAccountCode, attributeCode, out var attributeValue);
            if (attributeValue != null)
            {
                return Ok(attributeValue);
            }
            else
            {
                //get record from database
                return NotFound();
            }
        }

        [HttpPost("AddOrUpdateCache")]
        public IActionResult AddOrUpdateCache([FromBody] CacheEntryModel model)
        {
            _cacheService.AddOrUpdateCache(model.AccountCode, model.SubAccountCode, model.AttributeCode, model.AttributeValue);
            return Ok();
        }
        [HttpPost("LoadInitialCache")]
        public IActionResult LoadInitialCache()
        {
            _cacheService.LoadInitialCache();
            return Ok();
        }
    }
}
