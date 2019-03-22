using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace JsonRpc
{
    // TODO: make this disposable
    /// <summary>
    /// Listens and responds to requests. 
    /// </summary>
    public class Server : IDisposable
    {
        /// <summary>
        /// Invoked when the server is disposed. Used by clients to shut down.
        /// </summary>
        internal event Action Disposed;

        /// <summary>
        /// Handles a particular event (notification or method call). If <paramref name="id"/>
        /// is null, then the event is a notification.
        /// </summary>
        /// <param name="server">The server that received this message (this).</param>
        /// <param name="client">The client that sent this message.</param>
        /// <param name="method">The name of the method invoked.</param>
        /// <param name="id">The ID of the method, or null if a notification.</param>
        /// <param name="params">The optional parmeters sent by the client.</param>
        public delegate void Handler(Server server, Client client, string method, ulong? id, object @params);
        
        /// <summary>
        /// A typed handler that requires parameters of a certain type. <see cref="Handler"/>
        /// </summary>
        /// <param name="server">The server that received this message (this).</param>
        /// <param name="client">The client that sent this message.</param>
        /// <param name="method">The name of the method invoked.</param>
        /// <param name="id">The ID of the method, or null if a notification.</param>
        /// <param name="params">The optional parameters sent by the client.</param>
        /// <typeparam name="T">The type of the parameters.</typeparam>
        public delegate void TypedHandler<in T>(Server server, Client client, string method, ulong? id, T @params);

        /// <summary>
        /// Holds all handlers for server events, indexed by method.
        /// </summary>
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<ulong, HandlerInfo>> _handlers;

        /// <summary>
        /// Stores all handlers, indexed by their ID.
        /// </summary>
        private ConcurrentDictionary<ulong, HandlerInfo> _handlerInfos;

        private ulong _handlerUid = 0;
        private ulong NextHandlerUid => ++_handlerUid;
        
        private class HandlerInfo
        {
            /// <summary>
            /// The ID of the handler.
            /// </summary>
            public ulong uid { get; }

            /// <summary>
            /// The method that the handler is for.
            /// </summary>
            public string method { get; }

            /// <summary>
            /// The handler itself.
            /// </summary>
            public Handler handler { get; }

            public HandlerInfo(ulong uid, string method, Handler handler)
            {
                this.uid = uid;
                this.method = method;
                this.handler = handler;
            }
        };
        
        /// <summary>
        /// Holds information about a typed handler.
        /// </summary>
        private class TypedHandlerInfo : HandlerInfo
        {
            public Type type { get; }

            public TypedHandlerInfo(ulong uid, string method, Handler handler, Type type) 
                : base(uid, method, handler)
            {
                this.type = type;
            }
        };
         
        /// <summary>
        /// Handles a method call from the client.
        /// </summary>
        public event Client.CallHandler HandleCall;

        /// <summary>
        /// Handles a notification from the client.
        /// </summary>
        public event Client.NotificationHandler HandleNotification;

        /// <summary>
        /// Creates a new server.
        /// </summary>
        public Server()
        {
            _handlers = new ConcurrentDictionary<string, ConcurrentDictionary<ulong, HandlerInfo>>();
            _handlerInfos = new ConcurrentDictionary<ulong, HandlerInfo>();
        }

        /// <summary>
        /// Will connect to the client on <paramref name="stream"/> and start
        /// listening for connections.
        /// </summary>
        /// <param name="stream">the stream to communicate on</param>
        /// <exception cref="ArgumentException">If <paramref name="stream"/> is either read-only or write-only.</exception>
        public void AcceptStream(Stream stream)
        {
            if (!stream.CanRead || !stream.CanWrite)
                throw new ArgumentException($"{nameof(stream)} must be readable and writable");
            var client = new Client(stream);
            
            client.HandleNotificationRaw += ClientOnHandleNotificationRaw;
            client.HandleNotificationRaw += async (_, method, json) =>
            {
                try
                {
                    if (!_handlers.ContainsKey(method))
                    {
                        await _.ReplyError(null, new Error
                        {
                            Code = (int) ErrorCode.MethodNotFound
                        });
                        return;                       
                    }
                    foreach (var info in _handlers[method].Values)
                    {
                        if (info is TypedHandlerInfo tinfo)
                        {
                            try
                            {
                                tinfo.handler(this, _, method, null, JsonConvert.DeserializeObject(json, tinfo.type));
                            }
                            catch (JsonSerializationException ex)
                            {
                                await _.ReplyError(null, new Error
                                {
                                    Code = (int) ErrorCode.InvalidParams,
                                    Message = $"Failed to deserialize parameters to the required type {tinfo.type}",
                                    Data = ex.Message
                                });
                            }
                        }
                        else
                        {
                            info.handler(this, _, method, null, JsonConvert.DeserializeObject(json));
                        }
                    }
                }
                catch (Exception ex)
                {
                    // we want to avoid throwing any uncaught exceptions in this async void method:
                    // http://www.jaylee.org/post/2012/07/08/c-sharp-async-tips-and-tricks-part-2-async-void.aspx
                    Trace.WriteLine($"JSON-RPC: received exception in HandleNotificationRaw for client: \n{ex}");                   
                    try
                    {
                        await _.ReplyError(null, new Error
                        {
                            Code = (int) ErrorCode.InternalError,
                            Message = ex.Message
                        });
                    } catch (Exception) { /* ignore */ }
                }
            };
            
            client.HandleCallRaw += ClientOnHandleCallRaw;
            client.HandleCallRaw += async (_, method, id, json) =>
            {
                try
                {
                    if (!_handlers.ContainsKey(method))
                    {
                        await _.ReplyError(id, new Error
                        {
                            Code = (int) ErrorCode.MethodNotFound
                        });
                        return;
                    }
                    foreach (var info in _handlers[method].Values)
                    {
                        if (info is TypedHandlerInfo tinfo)
                        {
                            try
                            {
                                tinfo.handler(this, _, method, id, JsonConvert.DeserializeObject(json, tinfo.type));
                            }
                            catch (JsonSerializationException ex)
                            {
                                await _.ReplyError(id, new Error
                                {
                                    Code = (int) ErrorCode.InvalidParams,
                                    Message = $"Failed to deserialize parameters to the required type {tinfo.type}",
                                    Data = ex.Message
                                });
                            }
                        }
                        else
                        {
                            info.handler(this, _, method, id, JsonConvert.DeserializeObject(json));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"JSON-RPC: received exception in HandleCallRaw for client: \n{ex}");
                    try
                    {
                        await _.ReplyError(id, new Error
                        {
                            Code = (int) ErrorCode.InternalError,
                            Message = ex.Message
                        });
                    } catch (Exception) { /* ignore */ }
                }
            };
            
            Disposed += client.Dispose;
            client.StartListening();
        }

        private void ClientOnHandleNotificationRaw(Client client, string method, string paramsJson)
        {
            HandleNotification?.Invoke(client, method, JsonConvert.SerializeObject(paramsJson));
        }

        private void ClientOnHandleCallRaw(Client client, string method, ulong id, string paramsJson)
        {
            HandleCall?.Invoke(client, method, id, JsonConvert.SerializeObject(paramsJson));
        }

        /// <summary>
        /// Closes all connections to all clients.
        /// </summary>
        public void Dispose()
        {
            Disposed?.Invoke();
        }

        private void AddHandlerInfo(HandlerInfo hinfo)
        {
            var dict = new ConcurrentDictionary<ulong, HandlerInfo>();
            dict.AddOrUpdate(hinfo.uid, hinfo, (arg1, info) => hinfo);
            _handlers.AddOrUpdate(hinfo.method, dict, (methodName, infos) =>
            {
                infos.AddOrUpdate(hinfo.uid, hinfo, (arg1, info) => hinfo);
                return infos;
            });
            _handlerInfos.AddOrUpdate(hinfo.uid, hinfo, (sameUid, info) => hinfo);           
        }

        /// <summary>
        /// Adds a handler for any message received from a peer.
        /// </summary>
        /// <param name="method">The name of the method</param>
        /// <param name="handler">The handler</param>
        /// <returns>A unique ID for the handler <seealso cref="RemoveHandler"/></returns>
        public ulong AddHandler(string method, Handler handler)
        {
            ulong uid = NextHandlerUid;
            AddHandlerInfo(new HandlerInfo(uid, method, handler));
            return uid;
        }

        /// <summary>
        /// Adds a handler that expects a parameter of a certain type.
        /// </summary>
        /// <param name="method">The name of the method</param>
        /// <param name="handler">The handler</param>
        /// <typeparam name="T">The type of the parameter</typeparam>
        /// <returns>A unique ID for the handler <seealso cref="RemoveHandler"/></returns>
        public ulong AddHandler<T>(string method, TypedHandler<T> handler)
        {
            ulong uid = NextHandlerUid;
            AddHandlerInfo(new TypedHandlerInfo(uid, method, 
                (server, client, methodName, id, @params) =>
                    handler(server, client, methodName, id, (T) @params), typeof(T)));
            return uid;
        }

        /// <summary>
        /// Removes a handler with the specified ID.
        /// </summary>
        /// <param name="uid">The ID of the handler.</param>
        /// <returns>whether the operation was successful</returns>
        public bool RemoveHandler(ulong uid)
        {
            return _handlerInfos.TryRemove(uid, out var hinfo)
                   && _handlers.TryGetValue(hinfo.method, out var set)
                   && set.TryRemove(hinfo.uid, out var _);
        }
    }
}
