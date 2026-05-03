using Dapper;
using Library.Db;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Library.AdminApi;

namespace Server.AdminApi.Controllers;

[ApiController]
[Route("api/scores")]
public class ScoresController : ControllerBase
{
    private readonly DbConfig _dbConfig;

    public ScoresController(DbConfig dbConfig)
    {
        _dbConfig = dbConfig;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ScoreDto>>> ByAccount(
        [FromQuery] ulong accountId, [FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);
        await using var conn = new MySqlConnection(_dbConfig.MySqlConnectionString);
        await conn.OpenAsync();

        var rows = await conn.QueryAsync<ScoreDto>(
            "SELECT score_id AS ScoreId, account_id AS AccountId, score AS Score, " +
            "kill_count AS KillCount, survive_seconds AS SurviveSeconds, played_at AS PlayedAt " +
            "FROM scores WHERE account_id = @AccountId ORDER BY played_at DESC LIMIT @Limit",
            new { AccountId = accountId, Limit = limit });

        return Ok(rows);
    }

    [HttpGet("top")]
    public async Task<ActionResult<IEnumerable<ScoreDto>>> Top([FromQuery] int limit = 10)
    {
        limit = Math.Clamp(limit, 1, 100);
        await using var conn = new MySqlConnection(_dbConfig.MySqlConnectionString);
        await conn.OpenAsync();

        var rows = await conn.QueryAsync<ScoreDto>(
            "SELECT score_id AS ScoreId, account_id AS AccountId, score AS Score, " +
            "kill_count AS KillCount, survive_seconds AS SurviveSeconds, played_at AS PlayedAt " +
            "FROM scores ORDER BY score DESC LIMIT @Limit",
            new { Limit = limit });

        return Ok(rows);
    }
}
