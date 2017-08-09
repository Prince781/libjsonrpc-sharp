﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpJson
{
    /// <summary>
    /// A rpc call is represented by sending a Request object to a Server.
    /// </summary>
    public class Request : RpcObject
    {
        /// <summary>
        /// A String specifying the version of the JSON-RPC protocol.
        /// </summary>
        [JsonRequired]
        public string Jsonrpc { get; set; } = "2.0";

        /// <summary>
        /// A String containing the name of the method to be invoked.
        /// </summary>
        [JsonProperty(Required = Required.Always)]
        public string Method { get; set; }

        /// <summary>
        /// A Structured value that holds the parameter values to be used during the invocation of the method.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public IDictionary<string, object> Params { get; set; }

        /// <summary>
        /// An identifier established by the Client.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ulong? Id { get; set; }

        public object ParamsAsObject(Type type)
        {
            return JsonConvert.DeserializeObject(JsonConvert.SerializeObject(Params), type);
        }

        public void CheckValidity()
        {
            if (Jsonrpc != "2.0")
                throw new RpcException($"Field 'jsonrpc' must be exactly '2.0' instead of {Jsonrpc}.");
        }
    }
}