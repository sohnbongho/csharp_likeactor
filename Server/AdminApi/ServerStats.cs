using System.Diagnostics;
using Library.AdminApi;
using Library.Network;
using Server.Actors;

namespace Server.AdminApi;

public class ServerStats
{
    private readonly UserObjectPoolManager _userManager;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _prevCpuTime;
    private long _prevTick;

    public ServerStats(UserObjectPoolManager userManager)
    {
        _userManager = userManager;
        _prevCpuTime = _process.TotalProcessorTime;
        _prevTick = Stopwatch.GetTimestamp();
    }

    public StatsDto Snapshot()
    {
        _process.Refresh();
        var currCpuTime = _process.TotalProcessorTime;
        var currTick = Stopwatch.GetTimestamp();
        var elapsedSec = (currTick - _prevTick) / (double)Stopwatch.Frequency;
        var cpuPercent = elapsedSec > 0
            ? (currCpuTime - _prevCpuTime).TotalSeconds / (elapsedSec * Environment.ProcessorCount) * 100.0
            : 0.0;

        _prevCpuTime = currCpuTime;
        _prevTick = currTick;

        var (recv, sent) = PacketStats.Snapshot();

        return new StatsDto
        {
            ActiveSessions = _userManager.ActiveSessionCount,
            PacketsReceivedTotal = recv,
            PacketsSentTotal = sent,
            CpuPercent = Math.Round(cpuPercent, 2),
            MemoryMb = Math.Round(_process.WorkingSet64 / 1024.0 / 1024.0, 1),
            UptimeSeconds = (long)(DateTime.UtcNow - _startedAt).TotalSeconds
        };
    }
}
