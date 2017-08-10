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
        private bool m_pleaseShutdown;
        private Thread m_backgroundThread;

        /// <summary>
        /// The port where this client is listening for responses.
        /// </summary>
        public int Port { get; }

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
        public Client(IPEndPoint remote, int port, int timeout)
        {
            m_remoteEP = remote;
            Port = port;
            Timeout = timeout;

            m_udp = new UdpClient(Port);

            m_udp.Client.SetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, Timeout);
        }

        public void Start()
        {
            m_pleaseShutdown = false;

            m_responses = new ConcurrentDictionary<ulong, Response>();

            m_remoteEP = new IPEndPoint(IPAddress.Any, 0);
            m_backgroundThread = new Thread(Run);
            m_backgroundThread.IsBackground = true;
            m_backgroundThread.Name = "UdpJsonClient";

            m_backgroundThread.Start();
        }

        public void Stop()
        {
            m_pleaseShutdown = true;

            if (m_backgroundThread != null)
            {
                m_backgroundThread.Join();

                m_udp?.Close();
                m_udp = null;

                m_backgroundThread = null;

                IsProcessing = false;
            }
        }

        private void Run()
        {
            IsProcessing = true;

            while (!m_pleaseShutdown)
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
                        // We should not get a result without a response ID unless 
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
        private Response WaitForResponse(Request request)
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
                Thread.Sleep(200);
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
        public Task<Response> CallAsync(string method, IDictionary<string, object> @params = null)
        {
            var request = new Request
            {
                Id = NextUID,
                Method = method,
                Params = @params
            };

            byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));

            return m_udp.SendAsync(data, data.Length).ContinueWith(task => WaitForResponse(request));
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
            return m_udp.SendAsync(data, data.Length);
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
