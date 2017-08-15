using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UdpJson
{
    // TODO: make this disposable
    /// <summary>
    /// Listens and responds to requests. 
    /// </summary>
    public class Server
    {
        private UdpClient m_udp;

        private IPEndPoint m_endPoint;
        private IPEndPoint m_lastResponseEP;

        /// <summary>
        /// The port to listen for UDP requests.
        /// </summary>
        public int UdpPort { get; }

        /// <summary>
        /// Number of packets received.
        /// </summary>
        public long PacketsReceived { get; private set; }

        /// <summary>
        /// The timeout, in milliseconds.
        /// </summary>
        public int Timeout { get; set; }

        /// <summary>
        /// Whether the background thread is currently running.
        /// </summary>
        public bool IsProcessing { get; private set; }

        private CancellationTokenSource m_cancelTokenSource;
        private CancellationToken m_cancelToken;
        private DateTime m_startProcessingTime;

        private Task m_backgroundTask;

        /// <summary>
        /// List of execution contexts. The first item is the first execution context.
        /// </summary>
        private List<ExecutionContext> m_contexts;

        /// <summary>
        /// The current execution context.
        /// </summary>
        public ExecutionContext Context {
            get { return m_contexts.Count > 0 ? m_contexts[m_contexts.Count - 1] : null; }
        }

        /// <summary>
        /// Data passed to any methods invoked.
        /// </summary>
        public object Data { get; set; }

        /// <summary>
        /// Invoked whenever there is an error that is sent to the client.
        /// </summary>
        public event ErrorHandler OnError;

        /// <summary>
        /// Creates a new server.
        /// </summary>
        /// <param name="port">The port to listen on for incoming requests.</param>
        /// <param name="timeout">The timeout in milliseconds.</param>
        /// <param name="methods">Each tuple is (methodName, <code>typeof(Method)</code>)</param>
        /// <param name="data">This will be passed to <see cref="Method"/>s. See <see cref="Method.Invoke(Server, object)"/></param>
        public Server(int port, int timeout, IList<Tuple<string, Type>> methods = null, object data = null)
        {
            UdpPort = port;
            Timeout = timeout;
            Data = data;

            m_contexts = new List<ExecutionContext>();

            if (methods != null && methods.Count > 0)
            {
                Tuple<string, Type> invalid;

                if ((invalid = methods.FirstOrDefault(tuple => tuple.Item1.StartsWith("rpc."))) != null)
                {
                    throw new ArgumentException($"Invalid method name '{invalid.Item1}'. Names beginning with 'rpc.' are reserved for system extensions.");
                }

                // add any "rpc.*" methods here

                m_contexts.Add(new ExecutionContext(null, methods));
            }
        }

        /// <summary>
        /// Starts the server.
        /// </summary>
        public void Start()
        {
            if (IsProcessing)
                throw new Exception($"Task {m_backgroundTask.Id} was already started and is currently processing.");

            m_cancelTokenSource = new CancellationTokenSource();
            m_cancelToken = m_cancelTokenSource.Token;

            m_udp = new UdpClient(UdpPort);
            m_udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, Timeout);
            m_endPoint = new IPEndPoint(IPAddress.Any, 0);

            m_startProcessingTime = DateTime.Now;
            PacketsReceived = 0;

            // see https://stackoverflow.com/questions/20261300/what-is-correct-way-to-combine-long-running-tasks-with-async-await-pattern
            Task<Task> task = Task.Factory.StartNew(Run, m_cancelToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            m_backgroundTask = task.Unwrap();
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        public void Stop()
        {
            if (m_cancelTokenSource == null)
                throw new Exception($"{nameof(m_cancelTokenSource)} is null.");

            if (m_cancelToken.IsCancellationRequested)
                return;

            m_cancelTokenSource.Cancel();
            m_backgroundTask.Wait();

            m_udp?.Dispose();
            m_udp = null;

            m_endPoint = null;
            m_lastResponseEP = null;

            m_backgroundTask = null;
            m_cancelToken = default(CancellationToken);
            m_cancelTokenSource = null;
        }

        private async Task Run()
        {
            byte[] packet;

            IsProcessing = true;

            try
            {
                while (!m_cancelToken.IsCancellationRequested)
                {
                    if ((packet = GetPacket()) == null)
                        continue;

                    // process packet as request
                    await ProcessRequest(packet, m_endPoint);
                }
            } catch (Exception ex)
            {
                Trace.WriteLine($"Received exception in task {Task.CurrentId} -> {ex}");
            }

            IsProcessing = false;
        }

        private byte[] GetPacket()
        {
            byte[] buffer;

            try
            {
                buffer = m_udp.Receive(ref m_endPoint);
                PacketsReceived++;
            } catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    Trace.WriteLine($"Timeout waiting for UDP input @ {DateTime.Now}");
                }
                else if (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // Windows sends a "Connection Reset" message if we try to receive
                    // packets, and the last packet we sent was not received by the remote.
                    // see https://support.microsoft.com/en-us/help/263823/winsock-recvfrom-now-returns-wsaeconnreset-instead-of-blocking-or-timi
                    // see https://stackoverflow.com/questions/7201862/an-existing-connection-was-forcibly-closed-by-the-remote-host
                    if (m_lastResponseEP != null)
                    {
                        Trace.WriteLine($"Config server @ {m_lastResponseEP} may not be running.");
                    }
                    else
                    {
                        // otherwise, we silently ignore this error
                    }
                } else
                {
                    Trace.WriteLine($"Exception {ex.SocketErrorCode} in UDP receive: {ex}");
                }
                return null;
            }


            Trace.WriteLine($"Received command packet from {m_endPoint}");

            return buffer;
        }

        private async Task ProcessRequest(byte[] packet, IPEndPoint sender)
        {
            string packetStr = Encoding.UTF8.GetString(packet);
            Request request;

            try
            {
                request = JsonConvert.DeserializeObject<Request>(packetStr);
            } catch (Exception ex)
            {
                Trace.WriteLine($"Failed to deserialize request object: {ex}");

                if (ex is JsonReaderException)
                {
                    var error = new Error
                    {
                        Code = (int)ErrorCode.ParseError,
                        Message = "Parse error",
                        Data = ex
                    };

                    await SendResponse(sender, new Response { Error = error });

                    OnError?.Invoke(error);
                } else if (ex is JsonSerializationException)
                {
                    var error = new Error
                    {
                        Code = (int)ErrorCode.InvalidRequest,
                        Message = "Invalid request",
                        Data = ex
                    };

                    await SendResponse(sender, new Response { Error = error });

                    OnError?.Invoke(error);
                } else
                {
                    var error = new Error
                    {
                        Code = (int)ErrorCode.InternalError,
                        Message = "Some other error occurred",
                        Data = ex
                    };

                    await SendResponse(sender, new Response { Error = error });

                    OnError?.Invoke(error);
                }

                return;
            }

            // otherwise, we have a Request object

            // step 1: check if it is valid
            try
            {
                request.CheckValidity();
            } catch (RpcException ex)
            {
                Trace.WriteLine($"Invalid request: {ex}");

                var error = new Error
                {
                    Code = (int)ErrorCode.InvalidRequest,
                    Message = "Invalid request",
                    Data = ex
                };

                await SendResponse(sender, new Response {
                    Id = request.Id,
                    Error = error
                });

                OnError?.Invoke(error);

                return;
            }

            // step 2: look up the method
            Type methodType = null;

            try
            {
                methodType = Context?.availableMethods[request.Method];
            } catch (KeyNotFoundException)
            {
                var error = new Error
                {
                    Code = (int)ErrorCode.MethodNotFound,
                    Message = "Method not found",
                    Data = request.Method
                };

                await SendResponse(sender, new Response
                {
                    Id = request.Id,
                    Error = error
                });

                OnError?.Invoke(error);
                return;
            }

            if (methodType == null)
            {
                var error = new Error
                {
                    Code = (int)ErrorCode.MethodNotFound,
                    Message = "Method not found",
                    Data = request.Method
                };

                await SendResponse(sender, new Response
                {
                    Id = request.Id,
                    Error = error
                });

                OnError?.Invoke(error);
                return;
            }

            // step 3: generate the params
            Method method;

            try
            {
                method = (Method) request.ParamsAsObject(methodType);
            } catch (Exception ex)
            {
                var error = new Error
                {
                    Code = (int)ErrorCode.InternalError,
                    Message = $"Internal error while converting params",
                    Data = ex
                };

                await SendResponse(sender, new Response
                {
                    Id = request.Id,
                    Error = error
                });

                OnError?.Invoke(error);
                return;
            }

            try
            {
                // step 4: invoke the method
                object result = method.Invoke(this, Data);

                // step 5a: send a response (if no exception was thrown)
                await SendResponse(sender, new Response
                {
                    Id = request.Id,
                    Result = result
                });
            } catch (Exception ex)
            {
                var error = new Error
                {
                    Code = (int)ErrorCode.InternalError,
                    Message = $"Internal error while invoking '{request.Method}'",
                    Data = ex
                };

                // step 5b: send an error response (otherwise)
                await SendResponse(sender, new Response
                {
                    Id = request.Id,
                    Error = error
                });

                OnError?.Invoke(error);
                // don't transition if method call failed
                return;
            }

            var trans = method.GetTransition();

            if (trans == null)
                return;

            // step 6: push the execution context
            if (trans.PushMethods != null)
                PushContext(new ExecutionContext(method, trans.PushMethods));

            // step 7: pop execution context
            if (trans.PopContext)
                PopContext();
        }

        #region Contexts
        /// <summary>
        /// Adds a new execution context on top of the stack of execution contexts.
        /// This should only be used by classes extending <see cref="Method"/>.
        /// </summary>
        /// <param name="ctx"></param>
        public void PushContext(ExecutionContext ctx)
        {
            m_contexts.Add(ctx);
        }

        /// <summary>
        /// Pops the top-most execution context on top of the stack of execution contexts.
        /// This should only be used by classes extending <see cref="Method"/>. Will 
        /// throw an exception if the stack is empty.
        /// </summary>
        public void PopContext()
        {
            if (m_contexts.Count == 0)
                throw new InvalidOperationException($"'{nameof(m_contexts)}' is empty");

            m_contexts.RemoveAt(m_contexts.Count - 1);
        }
        #endregion

        async Task SendResponse(IPEndPoint toEndpoint, Response response)
        {
            // see https://stackoverflow.com/questions/34070459/newtonsoft-jsonserializer-lower-case-properties-and-dictionary
            byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            }));

            m_lastResponseEP = toEndpoint;

            Trace.WriteLine($"Sending response to {toEndpoint}");
            await m_udp.SendAsync(data, data.Length, toEndpoint);
        }
    }
}
