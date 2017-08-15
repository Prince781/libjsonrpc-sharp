using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading;
using System.Net;
using System.Diagnostics;
using Newtonsoft.Json;

namespace UdpJson
{
    /// <summary>
    /// Invoked when an <see cref="Error"/> is received back
    /// from the server.
    /// </summary>
    /// <param name="error">The error.</param>
    public delegate void ErrorHandler(Error error);

    // TODO: make this disposable
    /// <summary>
    /// A client sends requests and receives responses.
    /// </summary>
    public class Client
    {
        private UdpClient m_udp;
        private IPEndPoint m_remoteEP;

        /// <summary>
        /// The remote server.
        /// </summary>
        public string Remote { get { return m_remoteEP.ToString(); } }

        private ulong uid = 0;

        private ulong NextUID { get { return ++uid; } }

        private ConcurrentDictionary<ulong, Response> m_responses;
        private Task m_backgroundTask;
        private CancellationTokenSource m_cancelTokenSource;
        private CancellationToken m_cancelToken;

        /// <summary>
        /// The port where this client is listening for responses.
        /// </summary>
        public int Port { get; }

        private Uri m_remoteUri;

        /// <summary>
        /// The receive timeout in milliseconds.
        /// </summary>
        public int Timeout { get; }

        /// <summary>
        /// Whether the background thread is currently running.
        /// </summary>
        public bool IsProcessing { get; private set; }

        /// <summary>
        /// This event will occur when the server responds with
        /// an error.
        /// </summary>
        public event ErrorHandler GotErrorResponse;

        /// <summary>
        /// Creates a new client.
        /// </summary>
        /// <param name="remote">The remote server to send commands to.</param>
        /// <param name="port">The port to send commands from.</param>
        /// <param name="timeout">The timeout in milliseconds.</param>
        public Client(Uri remote, int port, int timeout)
        {
            m_remoteUri = remote;
            Port = port;
            Timeout = timeout;

            m_udp = new UdpClient(Port);

            m_udp.Client.SetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, Timeout);
        }

        /// <summary>
        /// Starts the background task where the client will process further responses.
        /// </summary>
        public void Start()
        {
            if (IsProcessing)
                throw new Exception($"Task {m_backgroundTask.Id} was already started and is currently processing.");

            m_cancelTokenSource = new CancellationTokenSource();
            m_cancelToken = m_cancelTokenSource.Token;

            m_responses = new ConcurrentDictionary<ulong, Response>();
            m_remoteEP = new IPEndPoint(IPAddress.Parse(m_remoteUri.Host), m_remoteUri.Port);

            m_backgroundTask = Task.Factory.StartNew(Run, m_cancelToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// Stops the Client from processing responses.
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

            m_remoteEP = null;

            m_backgroundTask = null;
            m_cancelToken = default(CancellationToken);
            m_cancelTokenSource = null;

            IsProcessing = false;
        }

        private void Run()
        {
            IsProcessing = true;

            while (!m_cancelToken.IsCancellationRequested)
            {
                byte[] data;
                Response resp;

                // step 1: wait synchronously for a response
                if ((data = GetPacket()) == null)
                    continue;

                // step 2: parse response
                if ((resp = ParseResponse(data)) == null)
                    continue;

                // step 3: add to list of responses
                if (resp.Id == null)
                {
                    if (resp.Result == null)
                    {
                        // there was an error
                        GotErrorResponse?.Invoke(resp.Error);
                    } else
                    {
                        // We should not get a response result without a response ID unless 
                        // it is an error, according to JSON-RPC 2.0
                    }
                }
                else
                {
                    m_responses.AddOrUpdate(resp.Id.Value, resp, (key, oldValue) => resp);
                }
            }

            IsProcessing = false;
        }

        private byte[] GetPacket()
        {
            byte[] data = null;

            try
            {
                data = m_udp.Receive(ref m_remoteEP);
            } catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    // ignore timeouts
                }
                else
                {
                    Trace.WriteLine($"Encountered exception while attempting to receive data: {ex}");
                }
            }

            return data;
        }

        private Response ParseResponse(byte[] data)
        {
            Response resp = null;
            string text = Encoding.UTF8.GetString(data);

            try
            {
                resp = JsonConvert.DeserializeObject<Response>(text);
            } catch (Exception ex)
            {
                Trace.WriteLine($"Could not deserialize {text} into a {typeof(Response)}:\n{ex}");
            }

            return resp;
        }

        /// <summary>
        /// Waits for a response to a method call. Times out after 20 seconds.
        /// </summary>
        /// <returns>null if there is a timeout</returns>
        private async Task<Response> WaitForResponse(Request request)
        {
            TimeSpan maxdiff = TimeSpan.FromSeconds(20);
            DateTime startTime = DateTime.Now;

            // On a separate thread from the one this method
            // is running on, we are listening for a UDP response.
            while (!m_responses.ContainsKey(request.Id.Value))
            {
                if ((DateTime.Now - startTime) >= maxdiff)
                {
                    Trace.WriteLine($"Timeout after {maxdiff} waiting for response to '{request.Method}()' (Id={request.Id})");
                    return null;
                }

                // loop until we get something
                // wait for response listener background thread to get something
                await Task.Delay(200);
            }

            // we've gotten a response

            Response response;
            m_responses.TryRemove(request.Id.Value, out response);
            return response;
        }

        /// <summary>
        /// Calls a remote method asynchronously.
        /// </summary>
        /// <param name="method">The name of the method.</param>
        /// <param name="params">The parameters passed to the method.</param>
        /// <returns></returns>
        public async Task<Response> CallAsync(string method, IDictionary<string, object> @params = null)
        {
            var request = new Request
            {
                Id = NextUID,
                Method = method,
                Params = @params
            };

            byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));

            await m_udp.SendAsync(data, data.Length, m_remoteEP);
            return await WaitForResponse(request);
        }

        /// <summary>
        /// Calls a remote method asynchronously.
        /// </summary>
        /// <param name="method">The name of the method.</param>
        /// <param name="params">The object to serialize to a dictionary. This cannot be a primitive type.</param>
        /// <returns></returns>
        public Task<Response> CallAsync(string method, object @params)
        {
            string serialized = JsonConvert.SerializeObject(@params);
            var deserialized = JsonConvert.DeserializeObject<IDictionary<string, object>>(serialized);

            return CallAsync(method, deserialized);
        }

        /// <summary>
        /// Sends a notification. The server will not respond to a notification unless there
        /// is an error.
        /// </summary>
        /// <param name="method">The name of the method.</param>
        /// <param name="params">The parameters passed to the method.</param>
        /// <returns></returns>
        public Task<int> NotifyAsync(string method, IDictionary<string, object> @params = null)
        {
            var request = new Request
            {
                Method = method,
                Params = @params
            };

            byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));
            return m_udp.SendAsync(data, data.Length, m_remoteEP);
        }

        /// <summary>
        /// Sends a notification. The server will not respond to a notification unless there
        /// is an error.
        /// </summary>
        /// <param name="method">The name of the method.</param>
        /// <param name="params">The object to serialize to a dictionary. This cannot be a primitive type.</param>
        /// <returns></returns>
        public Task<int> NotifyAsync(string method, object @params)
        {
            string serialized = JsonConvert.SerializeObject(@params);
            var deserialized = JsonConvert.DeserializeObject<IDictionary<string, object>>(serialized);

            return NotifyAsync(method, deserialized);
        }
    }
}
