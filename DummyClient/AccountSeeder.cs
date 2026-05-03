using Dapper;
using Library.Security;
using MySqlConnector;

namespace DummyClient;

public static class AccountSeeder
{
    public static async Task SeedAsync(string connectionString, string password, int accountCount)
    {
        Console.WriteLine($"[Seeder] {accountCount}개 계정 생성 시작...");

        var clientHash = PasswordHashHelper.ComputeClientHash(password);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS accounts (
                account_id    BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
                user_id       VARCHAR(50)  NOT NULL UNIQUE,
                password_hash VARCHAR(88)  NOT NULL,
                salt          VARCHAR(88)  NOT NULL,
                status        TINYINT UNSIGNED NOT NULL DEFAULT 0,
                created_at    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                last_login_at DATETIME NULL,
                INDEX idx_user_id (user_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        Console.WriteLine("[Seeder] 테이블 준비 완료");

        int inserted = 0;
        for (int i = 1; i <= accountCount; i++)
        {
            var userId = $"user_{i:D5}";
            var (hashBase64, saltBase64) = PasswordHashHelper.GenerateStoredHash(clientHash);

            var affected = await connection.ExecuteAsync(
                "INSERT IGNORE INTO accounts (user_id, password_hash, salt) VALUES (@UserId, @PasswordHash, @Salt)",
                new { UserId = userId, PasswordHash = hashBase64, Salt = saltBase64 });

            inserted += affected;

            if (i % 1000 == 0)
                Console.WriteLine($"[Seeder] {i}/{accountCount}");
        }

        Console.WriteLine($"[Seeder] 완료: {inserted}개 신규 삽입 (중복 제외)");
    }
}
