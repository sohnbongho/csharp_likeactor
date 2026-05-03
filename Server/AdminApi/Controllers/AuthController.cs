using Dapper;
using Library.Db;
using Library.Security;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Server.AdminApi.Models;

namespace Server.AdminApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly DbConfig _dbConfig;
    private readonly AdminApiConfig _adminConfig;
    private readonly SessionKeyStore _store;

    public AuthController(DbConfig dbConfig, AdminApiConfig adminConfig, SessionKeyStore store)
    {
        _dbConfig = dbConfig;
        _adminConfig = adminConfig;
        _store = store;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto req)
    {
        if (string.IsNullOrWhiteSpace(req.UserId) || string.IsNullOrEmpty(req.Password))
            return BadRequest("userId and password required");

        if (!_adminConfig.Admins.Contains(req.UserId))
            return Unauthorized("not an admin account");

        await using var conn = new MySqlConnection(_dbConfig.MySqlConnectionString);
        await conn.OpenAsync();

        var row = await conn.QueryFirstOrDefaultAsync<(string PasswordHash, string Salt, byte Status)>(
            "SELECT password_hash AS PasswordHash, salt AS Salt, status AS Status FROM accounts WHERE user_id = @UserId",
            new { req.UserId });

        if (row == default)
            return Unauthorized("invalid credentials");

        var clientHash = PasswordHashHelper.ComputeClientHash(req.Password);
        if (!PasswordHashHelper.Verify(clientHash, row.PasswordHash, row.Salt))
            return Unauthorized("invalid credentials");

        var key = await _store.IssueAsync(req.UserId);
        return Ok(new LoginResponseDto { SessionKey = key, ExpiresInMinutes = _adminConfig.SessionKeyTtlMinutes });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (Request.Headers.TryGetValue(SessionKeyMiddleware.HeaderName, out var keys) && keys.Count > 0)
            await _store.RevokeAsync(keys[0]!);
        return NoContent();
    }
}
