using Library.Model;

namespace Server.Model.Message;

public class InnerTestMessage : IInnerServerMessage
{
    public int Id { get; set; }
}
