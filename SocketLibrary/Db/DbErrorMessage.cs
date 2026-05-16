using Library.Model;

namespace Library.Db;

public class DbErrorMessage : IInnerServerMessage
{
    public string Reason { get; init; } = "DB 처리 실패";
}
