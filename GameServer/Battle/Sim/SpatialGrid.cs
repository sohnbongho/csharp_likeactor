using System.Numerics;

namespace GameServer.Battle.Sim;

// 유니티 EnemyManager의 해시 그리드를 그대로 옮긴 것.
// 틱마다 Build로 다시 만들고, 겹침 밀어내기(ComputePush)와 무기용 범위 질의(OverlapCircle)에 쓴다.
public sealed class SpatialGrid
{
    private const int BucketCount = 4096; // 2의 거듭제곱 (비트 마스크용)

    private readonly float _cellSize;
    private readonly int[] _head = new int[BucketCount];
    private readonly int[] _visited = new int[9];
    private int[] _next = new int[512];
    private Vector2[] _positions = new Vector2[512];
    private float[] _nearestDist = new float[16];
    private List<EnemyEntity> _enemies = new();
    private bool _built;
    private bool _dirty = true;
    private int _queryStamp;
    private float _maxEnemyRadius;

    public SpatialGrid(float cellSize)
    {
        _cellSize = cellSize;
        Array.Fill(_head, -1);
    }

    public Vector2 CachedPosition(int index) => _positions[index];

    public void MarkDirty() => _dirty = true;

    public void NoteRadius(float radius)
    {
        if (radius > _maxEnemyRadius) _maxEnemyRadius = radius;
    }

    public void Build(List<EnemyEntity> enemies)
    {
        _enemies = enemies;
        int n = enemies.Count;
        EnsureCapacity(n);
        Array.Fill(_head, -1);

        float invCell = 1f / _cellSize;
        for (int i = 0; i < n; i++)
        {
            Vector2 p = enemies[i].Pos;
            _positions[i] = p;
            int b = Bucket((int)MathF.Floor(p.X * invCell), (int)MathF.Floor(p.Y * invCell));
            _next[i] = _head[b];
            _head[b] = i;
        }
        _built = true;
        _dirty = false;
    }

    // self 주변의 겹친 적들이 self를 밀어내는 벡터 합
    public Vector2 ComputePush(int self, Vector2 p, float r)
    {
        float invCell = 1f / _cellSize;
        int cx = (int)MathF.Floor(p.X * invCell);
        int cy = (int)MathF.Floor(p.Y * invCell);

        Vector2 push = Vector2.Zero;
        int visitedCount = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int b = Bucket(cx + dx, cy + dy);

                bool seen = false;
                for (int v = 0; v < visitedCount; v++)
                {
                    if (_visited[v] == b) { seen = true; break; }
                }
                if (seen) continue;
                _visited[visitedCount++] = b;

                for (int j = _head[b]; j >= 0; j = _next[j])
                {
                    if (j == self) continue;

                    Vector2 d = p - _positions[j];
                    float minDist = r + _enemies[j].Radius;
                    float dSqr = d.LengthSquared();
                    if (dSqr >= minDist * minDist) continue;

                    if (dSqr < 1e-6f)
                    {
                        // 완전히 같은 위치: 인덱스 기반의 결정적 방향으로 분리
                        float a = (self * 2.399963f) + (j * 0.618f);
                        push += new Vector2(MathF.Cos(a), MathF.Sin(a));
                    }
                    else
                    {
                        float dist = MathF.Sqrt(dSqr);
                        push += d / dist * ((minDist - dist) / minDist);
                    }
                }
            }
        }
        return push;
    }

    // 원 안에 (적 반지름 포함) 걸치는 살아있는 적을 results에 추가한다. results는 비우지 않는다.
    public void OverlapCircle(Vector2 center, float radius, List<EnemyEntity> results)
    {
        if (!_built || _dirty)
        {
            OverlapLinear(center, radius, results);
            return;
        }

        int stamp = ++_queryStamp;
        float invCell = 1f / _cellSize;
        float pad = MathF.Max(_maxEnemyRadius, 0.01f);
        int x0 = (int)MathF.Floor((center.X - radius - pad) * invCell);
        int x1 = (int)MathF.Floor((center.X + radius + pad) * invCell);
        int y0 = (int)MathF.Floor((center.Y - radius - pad) * invCell);
        int y1 = (int)MathF.Floor((center.Y + radius + pad) * invCell);
        int count = _enemies.Count;

        for (int cy = y0; cy <= y1; cy++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                for (int j = _head[Bucket(cx, cy)]; j >= 0; j = _next[j])
                {
                    if (j >= count) continue;
                    EnemyEntity e = _enemies[j];
                    if (e.QueryStamp == stamp || !e.Alive) continue;

                    float reach = radius + e.Radius;
                    if ((e.Pos - center).LengthSquared() > reach * reach) continue;

                    e.QueryStamp = stamp;
                    results.Add(e);
                }
            }
        }
    }

    private void OverlapLinear(Vector2 center, float radius, List<EnemyEntity> results)
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            EnemyEntity e = _enemies[i];
            if (!e.Alive) continue;

            float reach = radius + e.Radius;
            if ((e.Pos - center).LengthSquared() <= reach * reach) results.Add(e);
        }
    }

    // range 안에서 from에 가까운 순으로 최대 results.Length 마리를 채우고 개수를 반환
    public int FindNearest(Vector2 from, float range, EnemyEntity?[] results)
    {
        int max = results.Length;
        if (max == 0) return 0;
        if (_nearestDist.Length < max) _nearestDist = new float[max];

        float rangeSqr = range * range;
        int found = 0;

        for (int i = 0; i < _enemies.Count; i++)
        {
            EnemyEntity e = _enemies[i];
            if (!e.Alive) continue;

            float d = (e.Pos - from).LengthSquared();
            if (d > rangeSqr) continue;
            if (found == max && d >= _nearestDist[max - 1]) continue;

            int pos = found < max ? found : max - 1;
            while (pos > 0 && _nearestDist[pos - 1] > d)
            {
                _nearestDist[pos] = _nearestDist[pos - 1];
                results[pos] = results[pos - 1];
                pos--;
            }
            _nearestDist[pos] = d;
            results[pos] = e;
            if (found < max) found++;
        }
        return found;
    }

    private static int Bucket(int x, int y)
    {
        unchecked
        {
            return (x * 73856093 ^ y * 19349663) & (BucketCount - 1);
        }
    }

    private void EnsureCapacity(int n)
    {
        if (_next.Length >= n) return;
        int size = _next.Length;
        while (size < n) size *= 2;
        Array.Resize(ref _next, size);
        Array.Resize(ref _positions, size);
    }
}
