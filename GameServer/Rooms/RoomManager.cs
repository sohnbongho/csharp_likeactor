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

    private readonly ConcurrentDictionary<string, Room> _rooms = new();      // 초대 코드 → 방
    private readonly ConcurrentDictionary<string, Room> _byWorld = new();    // 월드 id → 방
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

    public Room Create() => Create(worldId: "");

    private Room Create(string worldId)
    {
        while (true)
        {
            string code = GenerateCode();
            var room = new Room(this, _data, Interlocked.Increment(ref _nextRoomId), code, worldId);
            if (!_rooms.TryAdd(code, room))
                continue;
            _threads.Add(room);
            _logger.Info(() => $"방 생성: {code} (#{room.RoomId}){(worldId != "" ? $" 월드={worldId}" : "")}");
            return room;
        }
    }

    // 월드(맵 위치)의 방을 찾거나 만든다. 같은 월드로 들어오는 플레이어는 같은 방에 모인다.
    // 방이 막 닫히는 중이면(마지막 멤버 퇴장) 새 방으로 바꿔 끼운다.
    public Room GetOrCreateForWorld(string worldId)
    {
        while (true)
        {
            var room = _byWorld.GetOrAdd(worldId, id => Create(id));
            if (!room.IsClosed) return room;
            _byWorld.TryRemove(new KeyValuePair<string, Room>(worldId, room));
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
            if (room.WorldId != "")
                _byWorld.TryRemove(new KeyValuePair<string, Room>(room.WorldId, room));
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
