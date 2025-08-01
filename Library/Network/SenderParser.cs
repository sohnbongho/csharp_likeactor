using Library.ContInfo;
using Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Library.Network;

public class SenderParser
{    
    private readonly byte[] _sendBuffer;    
    public SenderParser(int bufferSize)
    {
        _sendBuffer = new byte[bufferSize];
    }
    public List<MessageWrapper> Parse(int bytesTransferred)
    {

    }
}
