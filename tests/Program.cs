﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using JsonRpc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
                Console.WriteLine($"SERVER: Received notification {method}({@params})");
            };           
        }
        
        private async void Typed1(Server server, Client client, string method, ulong? id, int i)
        {
            Console.WriteLine($"SERVER: Received call {method}({i})");
            await client.ReplyAsync((ulong) id, 3);
        }

        private async void Hello(Server server, Client client, string method, ulong? id, object @params)
        {
            Console.WriteLine($"SERVER: Received call {method}({@params})");
            await client.ReplyAsync((ulong) id, "response from server");
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

        private static TestType? _test;

        private static ushort _port = 12345;

        private static void PrintHelp()
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
            return AsyncMain(args).Result;
        }
        
        private static async Task<int> AsyncMain(string[] args)
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
                                _test = TestType.Tcp;
                                break;
                            case "udp":
                                _test = TestType.Udp;
                                break;
                            case "pipe":
                                _test = TestType.Pipe;
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

                        _port = port;

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

            switch (_test)
            {
                case TestType.Udp:
                    break;
                case null:
                    Console.Error.WriteLine("Test was not specified.");
                    PrintHelp();
                    return 1;
                case TestType.Tcp:
                {
                    var s = new TcpRpcServer(_port);
                    s.Start();
                
                    var tcpClient = new TcpClient("127.0.0.1", _port);
                    // tcpClient.Client.Send(Encoding.ASCII.GetBytes("\"{}\""));
                    // var stream = tcpClient.GetStream();
                    // var data = Encoding.ASCII.GetBytes("\"{}\"");
                    // stream.Write(data, 0, data.Length);
                    var client = new Client(tcpClient.GetStream(), 10000);
                    client.StartListening();
                    await client.NotifyAsync("notfound", "hi");
                    await client.CallAsync("test.hello", new { custom = "hello" });
                    await client.CallAsync("test.typed1", 3);
                
                    // tcpClient.Close();
                    break;
                }
                case TestType.Pipe:
                {
                    /*
                var testServer = new TestRpcServer();
                testServer.AcceptStream(new IOStream(Console.OpenStandardInput(), Console.OpenStandardOutput()));
                */

                    while (true)
                    {
                        JsonTextReader reader = null;
                    
                        try
                        {
                            string json = JObject.Load(reader = new JsonTextReader(Console.In)).ToString();
                            Console.WriteLine($"You entered this JSON: {json}");
                        }
                        catch (Exception ex)
                        {
                            try
                            {
                                int ch = Console.In.Read();
                                if (ch == -1)
                                    break;
                            }
                            catch (Exception exc)
                            {
                                // ignored
                                Console.Error.WriteLine($"Failed to read after: {exc}");
                            }

                            Console.Error.WriteLine(ex);
                        }
                    
                        if (reader != null)
                            Console.WriteLine($"token type: {reader.TokenType}");
                    }

                    break;
                }
                default:
                    Console.WriteLine("unsupported");
                    break;
            }

            return 0;
        }
    }
}