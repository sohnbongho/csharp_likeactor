using Library.Db.Broadcast;
using Microsoft.AspNetCore.Mvc;
using Library.AdminApi;

namespace Server.AdminApi.Controllers;

[ApiController]
[Route("api/notice")]
public class NoticeController : ControllerBase
{
    private readonly RedisBroadcastManager _broadcast;

    public NoticeController(RedisBroadcastManager broadcast)
    {
        _broadcast = broadcast;
    }

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] NoticeRequestDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest("message required");

        await _broadcast.PublishAsync(req.Message);
        return NoContent();
    }
}
