using Microsoft.AspNetCore.Mvc;

namespace Server.AdminApi.Controllers;

[ApiController]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private readonly ServerStats _stats;

    public StatsController(ServerStats stats)
    {
        _stats = stats;
    }

    [HttpGet]
    public ActionResult<StatsSnapshot> Get() => Ok(_stats.Snapshot());
}
