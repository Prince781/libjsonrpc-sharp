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
    /// An untyped response object that holds
    /// the result as a string prior to deserialization.
    /// </summary>
    [JsonConverter(typeof(ResponseJsonConverter))]
    public class Response 
    {
        /// <summary>
        /// The version of JSON-RPC this object supports.
        /// </summary>
        public string Jsonrpc { get; set; } = "2.0";
        
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
        /// The result object, in JSON form.
        /// </summary>
        internal string ResultJson { get; set; }

        /// <summary>
        /// Check whether this response conforms to the JSON-RPC 2.0 spec.
        /// </summary>
        /// <exception cref="RpcException">If this object is invalid.</exception>
        public void CheckValidity()
        {
            if (Jsonrpc != "2.0")
                throw new RpcException($"Field '{nameof(Jsonrpc).ToLower()}' must be exactly '2.0', instead of {Jsonrpc}");

            if (ResultJson != null)
            {
                if (Error != null)
                    throw new RpcException($"Cannot have field '{nameof(Error).ToLower()}' be non-null when \'result\' is non-null.");

                return;
            }

            // we had an error
            Error.CheckValidity();

            // check if 'id' is null if error is ParseError or InvalidRequest
            var code = (ErrorCode)Error.Code;
            if (code == ErrorCode.InvalidRequest || code == ErrorCode.ParseError)
            {
                if (Id != null)
                    throw new RpcException($"Cannot have field '{nameof(Id).ToLower()}' be non-null" +
                        $" when error code is either {ErrorCode.InvalidRequest} or {ErrorCode.ParseError}");
            }
        }
        
        internal Response() {}

        /// <summary>
        /// Converts to a typed response object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Response<T> AsTyped<T>()
        {
            return new Response<T>
            {
                Jsonrpc = Jsonrpc,
                Id = Id,
                Error = Error,
                ResultJson = ResultJson
            };
        }
    }

    /// <summary>
    /// A response is sent from Server to Client after a request is made.
    /// </summary>
    public class Response<T> : Response
    {
        /// <summary>
        /// Whether we've deserialized the raw result.
        /// </summary>
        private bool _haveDeserialized = false;

        /// <summary>
        /// This is the deserialized object.
        /// </summary>
        private T _deserialized;

        /// <summary>
        /// The value of this member is determined by the method invoked on
        /// the Server. This parameter is lazy: deserialization occurs only when
        /// this parameter is accessed on the first time.
        /// </summary>
        public T Result
        {
            get
            {
                if (_haveDeserialized || ResultJson == null)
                    return _deserialized;
                _deserialized = JsonConvert.DeserializeObject<T>(ResultJson);
                _haveDeserialized = true;

                return _deserialized;
            }
            set
            {
                _haveDeserialized = true;
                _deserialized = value;
                ResultJson = JsonConvert.SerializeObject(value);
            }
        }

        internal Response() {}
    }

    public class ResponseJsonConverter : JsonConverter
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (!(value is Response resp)) return;
            
            var o = new JObject
            {
                new JProperty("jsonrpc", resp.Jsonrpc),
                new JProperty("error", resp.Error)
            };
            
            if (resp.Id != null)
                o.Add("id", (ulong) resp.Id);
            if (value is Response<object> typedResponse)
                o.Add("result", typedResponse.Result == null ? null : JToken.FromObject(typedResponse.Result));    // serialization occurs here
            else
                o.Add("result", resp.ResultJson == null ? null : JToken.Parse(resp.ResultJson));
            
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
            string resultJson = o["result"]?.ToString();
            var error = o["error"]?.ToObject<Error>();
            ulong? id = o["id"]?.Value<ulong>();
            
            return new Response
            {
                Jsonrpc = jsonrpc,
                Error = error,
                Id = id,
                ResultJson = resultJson
            };
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(Response).GetTypeInfo().IsAssignableFrom(objectType);
        }
    }
}
