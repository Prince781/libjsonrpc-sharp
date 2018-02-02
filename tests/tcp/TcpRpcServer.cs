using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using JsonRpc;

namespace tests.tcp
{
    class TcpRpcServer
    {
        private Server _server;
        private TcpListener _listener;
        
        int Port { get; }

        public TcpRpcServer(int port)
        {
            Port = port;
        }

        private void StartListening()
        {
            _server = new TestRpcServer();

            _listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 12345);
            _listener.Start();
            
            try
            {
                while (true)
                {
                    Console.WriteLine("listening for TCP connections...");
                    var tcpClient = _listener.AcceptTcpClient();
                    Console.WriteLine($"received connection from {tcpClient.Client.RemoteEndPoint}");
                    _server.AcceptStream(tcpClient.GetStream());
                }
            }
            catch (SocketException)
            {

            }
            
            Console.WriteLine("SERVER: Shutting down.");           
        }

        public void Start()
        {
            new Thread(StartListening).Start();
        }
    }   
}