using GameServer.Battle.Data;
using GameServer.Battle.Sim;
using Library.Logger;
using System.Collections.Concurrent;

namespace GameServer.Rooms;

// 방 생성/조회/제거. 초대 코드 → Room 매핑만 담당하고, 방 내부 상태 변경은 전부 Room의 스레드에서 한다.
public sealed class RoomManager
{
    // 헷갈리는 글자(0/O, 1/I)를 뺀 초대 코드 문자 집합
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 6;

    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();

    public static RoomManager Instance { get; private set; } = null!;

    private readonly ConcurrentDictionary<string, Room> _rooms = new();
    private readonly RoomThreadManager _threads;
    private readonly GameDataStore _data;
    private ulong _nextRoomId;

    public GameDataStore Data => _data;
    public int RoomCount => _rooms.Count;

    public RoomManager(GameDataStore data)
    {
        _data = data;
        _threads = new RoomThreadManager((int)(BattleWorld.TickSeconds * 1000f));
        Instance = this;
    }

    public void Start() => _threads.Start();

    public async Task StopAsync()
    {
        await _threads.StopAsync();
        _rooms.Clear();
    }

    public Room Create()
    {
        while (true)
        {
            string code = GenerateCode();
            var room = new Room(this, _data, Interlocked.Increment(ref _nextRoomId), code);
            if (!_rooms.TryAdd(code, room))
                continue;
            _threads.Add(room);
            _logger.Info(() => $"방 생성: {code} (#{room.RoomId})");
            return room;
        }
    }

    public Room? Find(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return _rooms.TryGetValue(code.Trim().ToUpperInvariant(), out var room) ? room : null;
    }

    internal void Remove(Room room)
    {
        if (_rooms.TryRemove(room.Code, out _))
        {
            _threads.Remove(room);
            _logger.Info(() => $"방 제거: {room.Code} (#{room.RoomId})");
        }
    }

    private static string GenerateCode()
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
            chars[i] = CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)];
        return new string(chars);
    }
}
