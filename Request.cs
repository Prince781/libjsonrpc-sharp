using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace JsonRpc
{
    /// <summary>
    /// An untyped request object.
    /// </summary>
    [JsonConverter(typeof(RequestJsonConverter))]
    public class Request 
    {
        /// <summary>
        /// A String specifying the version of the JSON-RPC protocol.
        /// </summary>
        public string Jsonrpc { get; set; } = "2.0";

        /// <summary>
        /// A String containing the name of the method to be invoked.
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        /// The JSON format of the parameters.
        /// A Structured value that holds the parameter values to be used during the invocation of the method.
        /// </summary>
        internal string ParamsJson { get; set; }

        /// <summary>
        /// An identifier established by the Client.
        /// </summary>
        public ulong? Id { get; set; }

        /// <summary>
        /// Checks if this object conforms to the JSON-RPC 2.0 spec.
        /// </summary>
        /// <exception cref="RpcException">If this object is invalid.</exception>
        public void CheckValidity()
        {
            if (Jsonrpc != "2.0")
                throw new RpcException($"Field '{nameof(Jsonrpc)}' must be exactly '2.0' instead of {Jsonrpc}.");
        }
    }

    /// <summary>
    /// A JSON-RPC call is represented by sending a Request object to a Server.
    /// </summary>
    public class Request<T> : Request
    {
        private bool _haveDeserialized = false;
        private T _deserialized;

        /// <summary>
        /// A Structured value that holds the parameter values to be used
        /// during the invocation of the method.
        /// This parameter is lazy; deserialization does not occur until it
        /// is accessed.
        /// </summary>
        public T Params
        {
            get
            {
                if (_haveDeserialized || ParamsJson == null)
                    return _deserialized;
                _deserialized = JsonConvert.DeserializeObject<T>(ParamsJson);
                _haveDeserialized = true;
                return _deserialized;
            }
            set
            {
                _haveDeserialized = true;
                _deserialized = value;
            }
        }
    }
    
    public class RequestJsonConverter : JsonConverter
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (!(value is Request req)) return;
            
            var o = new JObject
            {
                new JProperty("jsonrpc", req.Jsonrpc),
                new JProperty("method", req.Method)
            };
            
            if (req.Id != null)
                o.Add("id", (ulong) req.Id);
            if (value is Request<object> typedRequest)
                o.Add("params", JToken.FromObject(typedRequest.Params)); // serialization occurs here
            else
                o.Add("params", JToken.Parse(req.ParamsJson));
            
            o.WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            if (reader.TokenType != JsonToken.StartObject)
                return null;

            var o = JObject.Load(reader);
            string jsonrpc = o["jsonrpc"]?.Value<string>();
            string method = o["method"]?.Value<string>();
            string paramsJson = o["params"]?.ToString();
            ulong? id = o["id"]?.Value<ulong>();

            return new Request
            {
                Jsonrpc = jsonrpc,
                Method = method,
                ParamsJson = paramsJson,
                Id = id
            };
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(Request).GetTypeInfo().IsAssignableFrom(objectType);
        }
    }
}
