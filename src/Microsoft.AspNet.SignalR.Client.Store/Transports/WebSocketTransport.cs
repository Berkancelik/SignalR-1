﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Client.Http;
using Windows.Networking.Sockets;
using Microsoft.AspNet.SignalR.Client.Infrastructure;
using Windows.Storage.Streams;

namespace Microsoft.AspNet.SignalR.Client.Transports
{
    public class WebSocketTransport : ClientTransportBase
    {
        private IConnection _connection;
        private MessageWebSocket _webSocket;
        private TransportInitializationHandler _initializationHandler;

        public WebSocketTransport()
            : this(new DefaultHttpClient())
        {            
        }

        public WebSocketTransport(IHttpClient httpClient)
            : base(httpClient, "webSockets")
        {
        }

        public override bool SupportsKeepAlive
        {
            get { return true; }
        }

        public override Task Start(IConnection connection, string connectionData, CancellationToken disconnectToken)
        {
            if (connection == null)
            {
                throw new ArgumentNullException("connection");    
            }

            _connection = connection;

            _initializationHandler = new TransportInitializationHandler(HttpClient, connection, connectionData, Name, disconnectToken, TransportHelper);
            _initializationHandler.OnFailure += DisposeSocket;

            return Start(connection, connectionData, _initializationHandler);
        }

        // testing - allows injecting custom initializationHandler
        internal Task Start(IConnection connection, string connectionData, TransportInitializationHandler initializationHandler)
        {
            Task.Run(async () =>
            {
                try
                {
                    await StartWebSocket(connection, UrlBuilder.BuildConnect(connection, Name, connectionData));
                }
                catch (TaskCanceledException)
                {
                    initializationHandler.Fail();
                }
                catch (Exception ex)
                {
                    initializationHandler.Fail(ex);
                }
            });

            return initializationHandler.Task;
        }

        private async Task StartWebSocket(IConnection connection, string url)
        {
            var uri = UrlBuilder.ConvertToWebSocketUri(url);
            connection.Trace(TraceLevels.Events, "WS Connecting to: {0}", uri);

            if (_webSocket == null)
            {
                var webSocket = new MessageWebSocket();
                webSocket.Control.MessageType = SocketMessageType.Utf8;
                webSocket.Closed += WebsocketClosed;
                webSocket.MessageReceived += MessageReceived;
                connection.PrepareRequest(new WebSocketRequest(webSocket));
                await OpenWebSocket(webSocket, uri);
                _webSocket = webSocket;
            }
         }

        // testing/mocking
        protected virtual async Task OpenWebSocket(IWebSocket webSocket, Uri uri)
        {
            await webSocket.ConnectAsync(uri);
        }      

        private void MessageReceived(MessageWebSocket source, MessageWebSocketMessageReceivedEventArgs eventArgs)
        {
            MessageReceived(new MessageReceivedEventArgsWrapper(eventArgs), _connection, TransportHelper, _initializationHandler);
        }

        // internal for testing, passing dependencies to allow mocking
        internal void MessageReceived(IWebSocketResponse webSocketResponse, IConnection connection, 
            TransportHelper transportHelper, TransportInitializationHandler initializationHandler)
        {
            var response = ReadMessage(webSocketResponse);

            connection.Trace(TraceLevels.Messages, "WS: OnMessage({0})", response);

            bool shouldReconnect;
            bool disconnected;
            transportHelper.ProcessResponse(_connection, response, out shouldReconnect, out disconnected, 
                initializationHandler.InitReceived);
        }


        private static string ReadMessage(IWebSocketResponse webSocketResponse)
        {
            var reader = webSocketResponse.GetDataReader();
            using ((IDisposable)reader)
            {
                reader.UnicodeEncoding = UnicodeEncoding.Utf8;
                return reader.ReadString(reader.UnconsumedBufferLength);
            }
        }

        public override Task Send(IConnection connection, string data, string connectionData)
        {
            if (connection == null)
            {
                throw new ArgumentNullException("connection");
            }

            var webSocket = _webSocket;

            if (webSocket == null)
            {
                throw new InvalidOperationException(Resources.GetResourceString("Error_WebSocketUninitialized"));
            }

            return Send(webSocket, data);
        }

        // internal for testing
        internal static async Task Send(IWebSocket webSocket, string data)
        {
            using (var messageWriter = new DataWriter(webSocket.OutputStream))
            {
                messageWriter.WriteString(data);
                await messageWriter.StoreAsync();
                messageWriter.DetachStream();
            }
        }
        
        private static void WebsocketClosed(IWebSocket webSocket, WebSocketClosedEventArgs eventArgs)
        {
            throw new NotImplementedException();
        }

        public override void LostConnection(IConnection connection)
        {
            throw new NotImplementedException();
        }

        private void DisposeSocket()
        {
            var webSocket = Interlocked.Exchange(ref _webSocket, null);
            if (webSocket != null)
            {
                webSocket.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeSocket();                
            }

            base.Dispose(disposing);
        }
    }
}