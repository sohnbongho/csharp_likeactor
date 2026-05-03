using Library.Db;
using Library.Db.Cache;
using Library.Db.Sql;
using Microsoft.AspNetCore.Mvc;
using Server.AdminApi.Models;

namespace Server.AdminApi.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly SqlWorkerManager _sql;
    private readonly CacheWorkerManager _cache;

    public HealthController(SqlWorkerManager sql, CacheWorkerManager cache)
    {
        _sql = sql;
        _cache = cache;
    }

    [HttpGet]
    public async Task<ActionResult<HealthDto>> Get()
    {
        var dbOk = await _sql.CheckConnectionAsync();
        var redisOk = await _cache.CheckConnectionAsync();
        return Ok(new HealthDto
        {
            Status = (dbOk && redisOk) ? "ok" : "degraded",
            Db = dbOk ? "ok" : "down",
            Redis = redisOk ? "ok" : "down"
        });
    }
}
