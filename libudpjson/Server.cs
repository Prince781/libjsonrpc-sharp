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

        public long PacketsReceived { get; private set; }

        /// <summary>
        /// The timeout, in milliseconds.
        /// </summary>
        public int Timeout { get; set; }

        /// <summary>
        /// Whether the background thread is currently running.
        /// </summary>
        public bool IsProcessing { get; private set; }

        private bool m_stopRunning;
        private DateTime m_startProcessingTime;

        private Thread m_runThread;

        /// <summary>
        /// List of execution contexts. The first item is the first execution context.
        /// </summary>
        private List<ExecutionContext> m_contexts;

        /// <summary>
        /// The current execution context.
        /// </summary>
        internal ExecutionContext Context {
            get { return m_contexts.Count > 0 ? m_contexts[m_contexts.Count - 1] : null; }
        }

        /// <summary>
        /// Data passed to any methods invoked.
        /// </summary>
        public object Data { get; set; }

        public Server(int port, int timeout, IList<Tuple<string, Type>> methods = null, object data = null)
        {
            UdpPort = port;
            Timeout = timeout;
            Data = data;

            m_contexts = new List<ExecutionContext>();

            if (methods != null)
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
        /// Starts the server. This is not thread-safe.
        /// </summary>
        public void Start()
        {
            m_stopRunning = false;

            m_udp = new UdpClient(UdpPort);
            m_udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, Timeout);
            m_endPoint = new IPEndPoint(IPAddress.Any, 0);

            m_startProcessingTime = DateTime.Now;
            PacketsReceived = 0;

            m_runThread = new Thread(Run);
            m_runThread.IsBackground = true;
            m_runThread.Name = "UdpJsonServer";
            m_runThread.Priority = ThreadPriority.Highest;
            m_runThread.Start();
        }

        /// <summary>
        /// Stops the server. This is not thread-safe.
        /// </summary>
        public void Stop()
        {
            m_stopRunning = true;

            if (m_runThread != null)
            {
                m_runThread.Join();

                m_udp?.Close();
                m_udp = null;

                m_endPoint = null;
                m_lastResponseEP = null;

                m_runThread = null;

                IsProcessing = false;
            }
        }

        private void Run()
        {
            byte[] packet;

            try
            {
                while (!m_stopRunning)
                {
                    IsProcessing = true;

                    if ((packet = GetPacket()) == null)
                        continue;

                    // process packet as request
                    ProcessRequest(packet, m_endPoint);
                }
            } catch (Exception ex)
            {
                Trace.WriteLine($"Received exception in {Thread.CurrentThread.Name} thread -> {ex}");
            }
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
                } else if (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // Windows sends a "Connection Reset" message if we try to receive
                    // packets, and the last packet we sent was not received by the remote.
                    // see https://support.microsoft.com/en-us/help/263823/winsock-recvfrom-now-returns-wsaeconnreset-instead-of-blocking-or-timi
                    // see https://stackoverflow.com/questions/7201862/an-existing-connection-was-forcibly-closed-by-the-remote-host
                    if (m_lastResponseEP != null)
                    {
                        Trace.WriteLine($"Config server @ {m_lastResponseEP} may not be running.");
                    } else
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

        private void ProcessRequest(byte[] packet, IPEndPoint sender)
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
                    SendResponse(sender, new Response
                    {
                        Error = new Error
                        {
                            Code = (int)ErrorCode.ParseError,
                            Message = "Parse error",
                            Data = ex
                        }
                    });
                } else if (ex is JsonSerializationException)
                {
                    SendResponse(sender, new Response
                    {
                        Error = new Error
                        {
                            Code = (int)ErrorCode.InvalidRequest,
                            Message = "Invalid request",
                            Data = ex
                        }
                    });
                } else
                {
                    SendResponse(sender, new Response
                    {
                        Error = new Error
                        {
                            Code = (int)ErrorCode.InternalError,
                            Message = "Some other error occurred",
                            Data = ex
                        }
                    });
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

                SendResponse(sender, new Response
                {
                    Error = new Error
                    {
                        Code = (int) ErrorCode.InvalidRequest,
                        Message = "Invalid request",
                        Data = ex
                    }
                });

                return;
            }

            // step 2: look up the method
            Type methodType = null;

            try
            {
                methodType = Context?.availableMethods[request.Method];
            } catch (KeyNotFoundException)
            {
                SendResponse(sender, new Response
                {
                    Id = request.Id,
                    Error = new Error
                    {
                        Code = (int)ErrorCode.MethodNotFound,
                        Message = "Method not found",
                        Data = request.Method
                    }
                });
                return;
            }

            if (methodType == null)
            {
                SendResponse(sender, new Response
                {
                    Id = request.Id,
                    Error = new Error
                    {
                        Code = (int)ErrorCode.MethodNotFound,
                        Message = "Method not found",
                        Data = request.Method
                    }
                });
                return;
            }

            // step 3: generate the params
            Method method;

            try
            {
                method = (Method) request.ParamsAsObject(methodType);
            } catch (Exception ex)
            {
                SendResponse(sender, new Response
                {
                    Id = request.Id,
                    Error = new Error
                    {
                        Code = (int)ErrorCode.InternalError,
                        Message = $"Internal error while converting params",
                        Data = ex
                    }
                });

                return;
            }

            try
            {
                // step 4: invoke the method
                object result = method.Invoke(this, Data);

                // step 5a: send a response (if no exception was thrown)
                SendResponse(sender, new Response
                {
                    Id = request.Id,
                    Result = result
                });
            } catch (Exception ex)
            {
                // step 5b: send an error response (otherwise)
                SendResponse(sender, new Response
                {
                    Id = request.Id,
                    Error = new Error
                    {
                        Code = (int)ErrorCode.InternalError,
                        Message = $"Internal error while invoking '{request.Method}'",
                        Data = ex
                    }
                });

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
        void PushContext(ExecutionContext ctx)
        {
            m_contexts.Add(ctx);
        }

        void PopContext()
        {
            if (m_contexts.Count == 0)
                throw new InvalidOperationException("'m_contexts' is empty");

            m_contexts.RemoveAt(m_contexts.Count - 1);
        }
        #endregion

        void SendResponse(IPEndPoint toEndpoint, Response response)
        {
            // see https://stackoverflow.com/questions/34070459/newtonsoft-jsonserializer-lower-case-properties-and-dictionary
            byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            }));

            m_lastResponseEP = toEndpoint;
            m_udp.SendAsync(data, data.Length, toEndpoint);
            Trace.WriteLine($"Sending response to {toEndpoint}");
        }
    }
}
