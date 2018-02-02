using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using JsonRpc;
using tests.tcp;

namespace tests
{
    class TestRpcServer : Server
    {
        public TestRpcServer()
        {
            AddHandler("test.hello", Hello);
            AddHandler<int>("test.typed1", Typed1);
            HandleNotification += (client, method, @params) =>
            {
                Console.WriteLine($"SERVER: Received notification ${method}(${@params})");
            };           
        }
        
        private void Typed1(Server server, Client client, string method, ulong? id, int i)
        {
            Console.WriteLine($"Client called test.typed1 with param {i}");
        }

        private void Hello(Server server, Client client, string method, ulong? id, object @params)
        {
            Console.WriteLine($"Client called test.hello with params {@params}");
        }              
    }
    
    static class Program
    {
        enum TestType
        {
            Pipe,
            Tcp,
            Udp
        }

        static TestType? Test;

        private static ushort Port = 12345;
        
        static void PrintHelp()
        {
            Console.WriteLine(@"options:
--help - show help
--test={tcp,udp,pipe} - run a test
--port=<int> - for udp and tcp tests. Default port is 12345
");
        }

        class IOStream : Stream
        {
            public Stream InputStream { get; }
            public Stream OutputStream { get; }
            
            public override bool CanRead => true;

            public override bool CanSeek => InputStream.CanSeek;

            public override bool CanWrite => true;
            public override long Length => InputStream.Length;

            public override long Position
            {
                get => InputStream.Position;
                set => InputStream.Position = value;
            }
            
            public IOStream(Stream input, Stream output)
            {
                InputStream = input;
                OutputStream = output;
            }

            public override void Flush()
            {
                InputStream.Flush();
                OutputStream.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return InputStream.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return InputStream.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                InputStream.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                InputStream.Write(buffer, offset, count);
            }
        }
        
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            for (int i = 0; i < args.Length; ++i)
            {
                if (args[i] == "--help")
                {
                    PrintHelp();
                    return 0;
                }
                    
                if (args[i].StartsWith("--test"))
                {
                    string after = args[i].Substring("--test".Length);
                        
                    if (i + 1 < args.Length || after != "")
                    {
                        string next = after != "" ? after.Substring(1) : args[i + 1];
                        switch (next)
                        {
                            case "tcp":
                                Test = TestType.Tcp;
                                break;
                            case "udp":
                                Test = TestType.Udp;
                                break;
                            case "pipe":
                                Test = TestType.Pipe;
                                break;
                            default:
                                Console.Error.WriteLine($"Unknown test type '{next}'.");
                                PrintHelp();
                                return 1;
                        }

                        if (after == "")
                            i++;
                        continue;
                    }                       

                    Console.Error.WriteLine("No test specified.");
                    PrintHelp();
                    return 1;
                }

                if (args[i].StartsWith("--port"))
                {
                    string after = args[i].Substring("--port".Length);

                    if (i + 1 < args.Length || after != "")
                    {
                        string next = after != "" ? after.Substring(1) : args[i + 1];

                        if (!ushort.TryParse(next, out ushort port))
                        {
                            Console.Error.WriteLine($"{next} is not an unsigned short.");
                            PrintHelp();
                            return 1;
                        }

                        Port = port;

                        if (after == "")
                            i++;
                        continue;
                    }
                        
                    Console.Error.WriteLine("No port specified.");
                    PrintHelp();
                    return 1;
                }

                if (args[i] == "--help")
                {
                    PrintHelp();
                    return 0;
                }
                    
                Console.Error.WriteLine($"Unsupported option '{args[i]}'");
                return 1;
            }

            if (Test == null)
            {
                Console.Error.WriteLine("Test was not specified.");
                PrintHelp();
                return 1;
            }

            if (Test == TestType.Tcp)
            {
                var s = new TcpRpcServer(Port);
                s.Start();
                
                var tcpClient = new TcpClient("127.0.0.1", Port);
                var client = new Client(tcpClient.GetStream());
                client.NotifyAsync("notfound");
                client.CallAsync("test.hello", new { custom = "hello" });
                client.CallAsync("test.typed1", 3);
                
                tcpClient.Close();
            }
            else if (Test == TestType.Pipe)
            {
                var testServer = new TestRpcServer();
                testServer.AcceptStream(new IOStream(Console.OpenStandardInput(), Console.OpenStandardOutput()));
            }
            else
            {
                Console.WriteLine("unsupported");
            }

            return 0;
        }
    }
}