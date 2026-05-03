using Microsoft.AspNetCore.Mvc;
using Server.Actors;
using Library.AdminApi;

namespace Server.AdminApi.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController : ControllerBase
{
    private readonly UserObjectPoolManager _userManager;

    public SessionsController(UserObjectPoolManager userManager)
    {
        _userManager = userManager;
    }

    [HttpGet]
    public ActionResult<IEnumerable<SessionDto>> List()
    {
        var list = new List<SessionDto>();
        foreach (var s in _userManager.EnumerateSessions())
        {
            list.Add(new SessionDto
            {
                SessionId = s.SessionId,
                UserId = s.AccountData?.UserId,
                WorldId = s.WorldId,
                IsAuthenticated = s.IsAuthenticated
            });
        }
        return Ok(list);
    }

    [HttpPost("{sessionId}/disconnect")]
    public IActionResult Disconnect(ulong sessionId)
    {
        var ok = _userManager.TryDisconnect(sessionId);
        return ok ? NoContent() : NotFound();
    }
}
