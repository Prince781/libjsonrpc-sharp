using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpJson
{
    public class RpcException : Exception
    {
        public RpcException(string message) : base(message) { }

        public RpcException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
