using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace JsonRpc
{
    /// <summary>
    /// Invoked when an <see cref="Error"/> is received back
    /// from the server.
    /// </summary>
    /// <param name="error">The error.</param>
    public delegate void ErrorHandler(Error error);

    /// <summary>
    /// A client sends requests and receives responses.
    /// </summary>
    public class Client : IDisposable
    {
        /// <summary>
        /// Handles a notification from the peer.
        /// </summary>
        /// <param name="client">The client for this notification.</param>
        /// <param name="method">The name of the method, like "textDocument/open"</param>
        /// <param name="params">The parameters of the method.</param>
        public delegate void NotificationHandler(Client client, string method, object @params);

        /// <summary>
        /// Handles a notification from the peer.
        /// </summary>
        /// <param name="client">The client for this notification.</param>
        /// <param name="method">The name of the method.</param>
        /// <param name="paramsJson">The raw JSON of the parameters.</param>
        internal delegate void NotificationHandlerRaw(Client client, string method, string paramsJson);

        /// <summary>
        /// Handles a method call from the peer.
        /// </summary>
        /// <param name="client">The client for this RPC.</param>
        /// <param name="method">The name of the method, like "textDocument/open"</param>
        /// <param name="id">The ID of the request object.</param>
        /// <param name="params">The parameters of the method call.</param>
        public delegate void CallHandler(Client client, string method, ulong id, object @params);

        /// <summary>
        /// Handles a method call from the peer.
        /// </summary>
        /// <param name="client">The client for this RPC.</param>
        /// <param name="method">The name of the method, like "textDocument/open"</param>
        /// <param name="id">The ID of the request object.</param>
        /// <param name="paramsJson">The raw JSON of the params object.</param>
        internal delegate void CallHandlerRaw(Client client, string method, ulong id, string paramsJson);
        
        private ulong _uid = 0;
        private ulong NextUid => ++_uid;

        private readonly ConcurrentDictionary<ulong, Response> _responses;
        private readonly ConcurrentDictionary<ulong, Request> _requests;
        private Task _backgroundTask;
        private CancellationTokenSource _cancelSource;

        /// <summary>
        /// The timeout in milliseconds.
        /// </summary>
        public int Timeout { get; }

        /// <summary>
        /// This event will occur when the server responds with
        /// an error.
        /// </summary>
        public event ErrorHandler GotErrorResponse;

        /// <summary>
        /// Will get invoked when receiving a RPC from the peer.
        /// </summary>
        public event CallHandler HandleCall;

        /// <summary>
        /// Will get invoked when receiving a RPC from the peer.
        /// The parameters object will not be serialized.
        /// </summary>
        internal event CallHandlerRaw HandleCallRaw;

        /// <summary>
        /// Will get invoked when receiving a notification from the peer.
        /// </summary>
        public event NotificationHandler HandleNotification;

        /// <summary>
        /// Will get invoked when receiving a notification from the peer.
        /// The parameters object will not be serialized.
        /// </summary>
        internal event NotificationHandlerRaw HandleNotificationRaw;
        
        /// <summary>
        /// The stream this client communicates on.
        /// </summary>
        public Stream Stream { get; }

        private readonly JsonTextReader _jsonReader;
        private readonly JsonTextWriter _jsonWriter;
        private readonly Queue<Tuple<JsonToken, object>> _tokens;

        private bool _closed;

        /// <summary>
        /// Creates a new client.
        /// </summary>
        /// <param name="stream">The bidirectional stream to communicate on.</param>
        /// <param name="timeout">The timeout, in milliseconds.</param>
        /// <exception cref="ArgumentException">If <paramref name="stream"/> is either read-only or write-only.</exception>
        public Client(Stream stream, int timeout = 0)
        {
            if (!stream.CanRead || !stream.CanWrite)
                throw new ArgumentException($"{nameof(stream)} must be both readable and writable.");
            Stream = stream;
            _responses = new ConcurrentDictionary<ulong, Response>();
            _requests = new ConcurrentDictionary<ulong, Request>();
            _jsonReader = new JsonTextReader(new StreamReader(Stream)) { SupportMultipleContent = true };
            _jsonWriter = new JsonTextWriter(new StreamWriter(Stream));
            _tokens = new Queue<Tuple<JsonToken,object>>();
            Timeout = timeout;
        }

        /// <summary>
        /// Starts listening for incoming messages from the peer.
        /// Does nothing if already listening.
        /// </summary>
        public void StartListening()
        {
            if (_cancelSource != null || _closed)
                return;
            _cancelSource = new CancellationTokenSource();
            // see https://stackoverflow.com/questions/20261300/what-is-correct-way-to-combine-long-running-tasks-with-async-await-pattern
            var task = Task.Factory.StartNew(Listen, _cancelSource.Token, TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            _backgroundTask = task.Unwrap();
        }

        private IEnumerable<Tuple<JsonToken, object>> ReadTokens()
        {
            while (true)
            {
                if (_tokens.Count > 0)
                    yield return _tokens.Dequeue();
                else if (_jsonReader.Read())
                    yield return new Tuple<JsonToken, object>(_jsonReader.TokenType, _jsonReader.Value);
                else
                    break;
            }
        }

        private string ParseValue()
        {
            var json = "";

            foreach (var tp in ReadTokens())
            {
                var tokenType = tp.Item1;
                var val = tp.Item2;
                switch (tokenType)
                {
                    case JsonToken.String:
                        json += $"\"{val}\"";
                        break;
                    case JsonToken.Date:
                        json += $"\"{val}\"";
                        break;
                    case JsonToken.Integer:
                        json += $"{val}";
                        break;
                    case JsonToken.Float:
                        json += $"{val}";
                        break;
                    case JsonToken.Boolean:
                        json += $"{val}";
                        break;
                    case JsonToken.Null:
                        json += "null";
                        break;
                    case JsonToken.StartObject:
                        json += ParseObject();
                        break;
                    case JsonToken.StartArray:
                        json += ParseArray();
                        break;
                }
                break;
            }

            return json;
        }

        private string ParseArray()
        {
            var json = "";
            bool empty = true;

            foreach (var tp in ReadTokens())
            {
                var tokenType = tp.Item1;
                var val = tp.Item2;
                if (tokenType == JsonToken.StartArray)
                    json += '[';
                else if (tokenType == JsonToken.EndArray)
                {
                    json += ']';
                    break;
                }
                else
                {
                    if (!empty)
                        json += ',';
                    json += ParseValue();
                    empty = false;
                }
            }
            
            return json;
        }

        private string ParsePropertyList()
        {
            var json = "";
            bool lastWasProp = true;
            foreach (var tp in ReadTokens())
            {
                var tokenType = tp.Item1;
                var val = tp.Item2;

                if (tokenType == JsonToken.PropertyName)
                {
                    if (!lastWasProp)
                        json += ",";
                    json += $"\"{val}\":";
                    lastWasProp = true;
                }
                else if (tokenType == JsonToken.String)
                {
                    lastWasProp = false;
                    json += $"\"{val}\"";
                }
                else if (tokenType == JsonToken.Integer)
                {
                    lastWasProp = false;
                    json += $"{val}";
                }
                else if (tokenType == JsonToken.Float)
                {
                    lastWasProp = false;
                    json += $"{val}";
                }
                else if (tokenType == JsonToken.Null)
                {
                    lastWasProp = false;
                    json += $"{val}";
                }
                else if (tokenType == JsonToken.StartObject)
                {
                    lastWasProp = false;
                    _tokens.Enqueue(new Tuple<JsonToken, object>(tokenType, val));
                    json += ParseObject();
                }
                else if (tokenType == JsonToken.StartArray)
                {
                    lastWasProp = false;
                    _tokens.Enqueue(new Tuple<JsonToken, object>(tokenType, val));
                    json += ParseArray();
                }
                else
                {
                    _tokens.Enqueue(new Tuple<JsonToken, object>(tokenType, val));
                    break;
                }
            }

            return json;
        }

        private string ParseObject()
        {
            var json = "";
            foreach (var tp in ReadTokens())
            {
                var token = tp.Item1;

                if (token == JsonToken.StartObject)
                {
                    json += '{';
                    json += ParsePropertyList();
                }
                else if (token == JsonToken.EndObject)
                {
                    json += '}';
                    break;
                }
            }

            return json;
        }

        private async Task Listen()
        {
            while (!_cancelSource.IsCancellationRequested && !_closed)
            {
                // step 1: get the JSON
                string json = null;

                try
                {
                    // json = await _jsonReader.ReadAsStringAsync(_cancelSource.Token);
                    // TODO: read arrays for batched commands
                    json = ParseObject();
                }
                catch (EndOfStreamException)
                {
                    _closed = true;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"exception in client: {ex}");
                }

                if (json == null)
                    continue;

                // step 2: parse response
                if (ParseResponse(json, out Response resp))
                {
                    // step 3: add to list of responses
                    if (resp.Id == null)
                    {
                        if (resp.ResultJson == null)
                        {
                            // server: there was an error in detecting the ID of the
                            // request object
                            GotErrorResponse?.Invoke(resp.Error);
                        }
                        else
                        {
                            // We should not get a response result without a response ID unless 
                            // it is an error, according to JSON-RPC 2.0
                        }
                    }
                    else
                    {
                        // save it for later
                        _responses.AddOrUpdate(resp.Id.Value, resp, (key, oldValue) => resp);
                    }
                } else if (ParseRequest(json, out Request req))
                {
                    // step 3: add to list of requests
                    // TODO: respond with error if failure 
                    if (req.Id != null)
                    {
                        // we have a method call; save it for later
                        _requests.AddOrUpdate(req.Id.Value, req, (key, oldValue) => req);
                        // call handler
                        HandleCall?.Invoke(this, req.Method, req.Id.Value, JsonConvert.DeserializeObject(req.ParamsJson));
                        HandleCallRaw?.Invoke(this, req.Method, req.Id.Value, req.ParamsJson);
                    }
                    else
                    {
                        // we have a notification
                        // call handler
                        HandleNotification?.Invoke(this, req.Method, JsonConvert.DeserializeObject(req.ParamsJson));
                        HandleNotificationRaw?.Invoke(this, req.Method, req.ParamsJson);
                    }
                }
                else
                {
                    await ReplyError(resp.Id, new Error
                    {
                        Code = (int) ErrorCode.InvalidRequest,
                        Message = "The JSON object is neither a request nor a response",
                        Data = json
                    });
                }
            }
        }

        /// <summary>
        /// Closes all connections.
        /// </summary>GetGetGet
        /// <param name="ct">The optional token to cancel this close operation.</param>
        public void Close(CancellationToken? ct = null)
        {
            if (_cancelSource == null) return;
            _cancelSource.Cancel();
            _backgroundTask.Wait(ct ?? default(CancellationToken));
            _cancelSource = null;
        }

        /// <summary>
        /// Parses a response object.
        /// </summary>
        /// <param name="json">The JSON to format.</param>
        /// <param name="resp">The response object to set.</param>
        /// <returns>Whether the parsing was successful.</returns>
        private bool ParseResponse(string json, out Response resp)
        {
            try
            {
                resp = JsonConvert.DeserializeObject<Response>(json);
                resp.CheckValidity();
                return true;
            } catch (Exception ex)
            {
                if (ex is RpcException)
                    Trace.WriteLine($"Could not deserialize {json} into a {typeof(Response)}:\n{ex}");
            }

            resp = null;
            return false;
        }

        /// <summary>
        /// Parses a request object from JSON.
        /// </summary>
        /// <param name="json">the raw JSON to parse</param>
        /// <param name="req">the request object to set</param>
        /// <returns>Whether the parse was successful</returns>
        private bool ParseRequest(string json, out Request req)
        {
            try
            {
                req = JsonConvert.DeserializeObject<Request>(json);
                req.CheckValidity();
                return true;
            }
            catch (Exception ex)
            {
                if (ex is RpcException)
                    Trace.WriteLine($"Could not deserialize {json} into a {typeof(Request)}:\n{ex}");
            }

            req = null;
            return false;
        }

        /// <summary>
        /// Waits for a response to a method call. Times out after <see cref="Timeout"/> milliseconds.
        /// </summary>
        /// <returns>Null if there is a timeout or cancellation</returns>
        private async Task<Response> WaitForResponse(ulong requestId, 
            CancellationToken ct)
        {
            TimeSpan maxdiff = Timeout <= 0 ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(Timeout);
            DateTime startTime = DateTime.Now;

            // On a separate thread from the one this method
            // is running on, we are listening for a UDP response.
            while (true)
            {
                if (_closed)
                {
                    Trace.WriteLine("JSON-RPC: Stream closed.");
                    return null;
                }
                
                
                if (ct.IsCancellationRequested)
                {
                    Trace.WriteLine("JSON-RPC: WaitForResponse cancelled.");
                    return null;
                }

                if (DateTime.Now - startTime >= maxdiff)
                {
                    Trace.WriteLine($"JSON-RPC: Timeout after {maxdiff} waiting for response to Id={requestId}");
                    return null;
                }

                if (_responses.TryRemove(requestId, out Response response))
                    return response;


                // loop until we get something
                await Task.Delay(Math.Max(1, Math.Min(10, Timeout)), ct);
            }
        }

        /// <summary>
        /// Calls a remote method asynchronously.
        /// </summary>
        /// <param name="method">The name of the method.</param>
        /// <param name="params">The parameters passed to the method.</param>
        /// <param name="ct">The optional cancellation token.</param>
        /// <returns></returns>
        public async Task<Response> CallAsync<T>(string method, T @params,
            CancellationToken ct = default (CancellationToken))
        {
            if (_backgroundTask == null)
                throw new InvalidOperationException($"Client must be listening for responses");
            var request = new Request<T>
            {
                Id = NextUid,
                Method = method,
                Params = @params
            };

            string json = JsonConvert.SerializeObject(request,
                new JsonSerializerSettings {ContractResolver = new CamelCasePropertyNamesContractResolver()});
            
            await _jsonWriter.WriteRawAsync(json, ct);
            await _jsonWriter.FlushAsync(ct);
            
            return await WaitForResponse((ulong) request.Id, ct);
        }

        /// <summary>
        /// Calls a remote method without parameters.
        /// </summary>
        /// <param name="method">The name of the method.</param>
        /// <param name="ct">The optional cancellation token.</param>
        /// <returns></returns>
        public Task<Response> CallAsync(string method, 
            CancellationToken ct = default (CancellationToken))
        {
            return CallAsync<object>(method, null, ct);
        }

        /// <summary>
        /// Sends a notification with parameters. The server will not respond to a
        /// notification unless there is an error.
        /// </summary>
        /// <param name="method">The name of the method.</param>
        /// <param name="params">The parameters passed to the method.</param>
        /// <param name="ct">The optional cancellation token.</param>
        /// <returns></returns>
        public async Task NotifyAsync<T>(string method, T @params, 
            CancellationToken ct = default (CancellationToken))
        {
            var request = new Request<T>
            {
                Method = method,
                Params = @params
            };

            string json = JsonConvert.SerializeObject(request,
                new JsonSerializerSettings {ContractResolver = new CamelCasePropertyNamesContractResolver()});

            await _jsonWriter.WriteRawValueAsync(json, ct);
            await _jsonWriter.FlushAsync(ct);
        }

        /// <summary>
        /// Sends a notification without parameters. The server will not respond
        /// to a notification unless there is an error.
        /// </summary>
        /// <param name="method">The name of the method.</param>
        /// <param name="ct">The optional cancellation token.</param>
        /// <returns></returns>
        public Task NotifyAsync(string method,
            CancellationToken ct = default (CancellationToken))
        {
            return NotifyAsync<object>(method, null, ct);
        }

        /// <summary>
        /// Replies to the peer with an error.
        /// </summary>
        /// <param name="id">The ID of the request, if any.</param>
        /// <param name="error">The error object to send</param>
        /// <param name="ct">The optional cancellation token.</param>
        internal async Task ReplyError(ulong? id, Error error,
            CancellationToken ct = default(CancellationToken))
        {
            string json = JsonConvert.SerializeObject(new Response {Error = error},
                new JsonSerializerSettings {ContractResolver = new CamelCasePropertyNamesContractResolver()});
            await _jsonWriter.WriteRawAsync(json, ct);
            await _jsonWriter.FlushAsync(ct);
        }

        /// <summary>
        /// Replies to a method call issued by this client. Use
        /// this within server methods.
        /// </summary>
        /// <param name="id">The ID of the command received.</param>
        /// <param name="obj">The result of executing the command.</param>
        /// <param name="ct">The optional cancellation token.</param>
        /// <typeparam name="T">The type of the result.</typeparam>
        public async Task ReplyAsync<T>(ulong id, T obj,
            CancellationToken ct = default (CancellationToken))
        {
            var response = new Response<T>
            {
                Id = id,
                Result = obj
            };
            string json = JsonConvert.SerializeObject(response,
                new JsonSerializerSettings {ContractResolver = new CamelCasePropertyNamesContractResolver()});
            await _jsonWriter.WriteRawAsync(json, ct);
            await _jsonWriter.FlushAsync(ct);
        }

        /// <summary>
        /// Cancels the background task and disposes the stream.
        /// </summary>
        public void Dispose()
        {
            _cancelSource.Cancel();
            _backgroundTask.Wait();
        }
    }
}
