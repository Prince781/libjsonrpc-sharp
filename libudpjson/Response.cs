using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpJson
{
    public class Response : RpcObject
    {
        [JsonRequired]
        public string Jsonrpc { get; set; } = "2.0";

        /// <summary>
        /// The value of this member is determined by the method invoked on the Server.
        /// </summary>
        [JsonProperty(Required = Required.DisallowNull)]
        public object Result { get; set; }

        /// <summary>
        /// An error if it occurred.
        /// </summary>
        [JsonProperty(Required = Required.DisallowNull)]
        public Error Error { get; set; }

        /// <summary>
        /// If there was an error in detecting the id in the Request object (e.g. Parse error/Invalid Request), it MUST be Null.
        /// </summary>
        public ulong? Id { get; set; }

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
