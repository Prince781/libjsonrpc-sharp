using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpJson
{
    /// <summary>
    /// A response is sent from Server to Client after a request is made.
    /// </summary>
    public class Response : RpcObject
    {
        /// <summary>
        /// The version of JSON-RPC this object supports.
        /// </summary>
        [JsonRequired]
        public string Jsonrpc { get; set; } = "2.0";

        /// <summary>
        /// The value of this member is determined by the method invoked on the Server.
        /// </summary>
        public object Result { get; set; }

        /// <summary>
        /// An error if it occurred.
        /// </summary>
        public Error Error { get; set; }

        /// <summary>
        /// If there was an error in detecting the id in the Request object (e.g. Parse error/Invalid Request), it MUST be <code>null</code>.
        /// Otherwise this will match the ID of the request object.
        /// </summary>
        public ulong? Id { get; set; }

        /// <summary>
        /// Check whether this response conforms to the JSON-RPC 2.0 spec.
        /// </summary>
        /// <exception cref="RpcException">If this object is invalid.</exception>
        public void CheckValidity()
        {
            if (Jsonrpc != "2.0")
                throw new RpcException($"Field 'jsonrpc' must be exactly '2.0', instead of {Jsonrpc}");

            if (Result != null)
            {
                if (Error != null)
                    throw new RpcException("Cannot have field 'error' be non-null when 'result' is non-null.");

                return;
            }

            // we had an error
            Error.CheckValidity();

            // check if 'id' is null if error is ParseError or InvalidRequest
            var code = (ErrorCode)Error.Code;
            if (code == ErrorCode.InvalidRequest || code == ErrorCode.ParseError)
            {
                if (Id != null)
                    throw new RpcException($"Cannot have field 'id' be non-null when error code is either {ErrorCode.InvalidRequest} or {ErrorCode.ParseError}");
            }
        }
    }
}
