using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpJson
{
    /// <summary>
    /// Thrown when there is an internal exception dealing with JSON-RPC objects.
    /// </summary>
    public class RpcException : Exception
    {
        /// <summary>
        /// Creates a new RpcException.
        /// </summary>
        /// <param name="message"></param>
        public RpcException(string message) : base(message) { }

        /// <summary>
        /// Creates a new RpcException.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="innerException"></param>
        public RpcException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
