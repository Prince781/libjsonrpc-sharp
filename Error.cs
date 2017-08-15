using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpJson
{
    /// <summary>
    /// An error code for <see cref="Error"/>s.
    /// </summary>
    public enum ErrorCode
    {
        /// <summary>
        /// Invalid JSON was received by the server.
        /// An error occurred on the server while parsing the JSON text.
        /// </summary>
        ParseError      = -32700,

        /// <summary>
        /// The JSON sent is not a valid Request object.
        /// </summary>
        InvalidRequest  = -32600,

        /// <summary>
        /// The method does not exist / is not available.
        /// </summary>
        MethodNotFound  = -32601,

        /// <summary>
        /// Invalid method parameter(s).
        /// </summary>
        InvalidParams   = -32602,

        /// <summary>
        /// Internal JSON-RPC error.
        /// </summary>
        InternalError   = -32603

        // -32000 through -32099: Reserved for implementation-defined server-errors.
    }

    /// <summary>
    /// When a rpc call encounters an error.
    /// </summary>
    public class Error : RpcObject
    {
        /// <summary>
        /// Defined by JSON-RPC 2.0.
        /// </summary>
        public static int ServerErrorMin = -32000;

        /// <summary>
        /// Defined by JSON-RPC 2.0.
        /// </summary>
        public static int ServerErrorMax = -32099;

        /// <summary>
        /// A Number that indicates the error type that occurred.
        /// </summary>
        [JsonRequired]
        public int Code { get; set; }

        /// <summary>
        /// A String providing a short description of the error.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public string Message { get; set; }

        /// <summary>
        /// Any extra data sent with the error.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }

        /// <summary>
        /// Checks if this error conforms with the JSON-RPC 2.0 spec.
        /// </summary>
        public void CheckValidity()
        {
            if (!(Code >= ServerErrorMin && Code <= ServerErrorMax) || Enum.IsDefined(typeof(ErrorCode), Code))
                throw new RpcException($"Invalid error code '{Code}'. Codes must be between" +
                    $" {ServerErrorMin} and {ServerErrorMax} or one of {Enum.GetValues(typeof(ErrorCode))}");
        }
    }
}