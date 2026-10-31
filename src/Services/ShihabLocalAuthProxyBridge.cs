using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShihabShieldVpn.Services
{
    public sealed class ShihabLocalAuthProxyBridge : IDisposable
    {
        private readonly string _upstreamHost;
        private readonly int _upstreamPort;
        private readonly bool _upstreamUseTls;
        private readonly string _basicAuthHeaderLine;
        private readonly SemaphoreSlim _concurrencyGate = new SemaphoreSlim(64);
        private readonly object _clientsLock = new object();
        private readonly List<TcpClient> _activeClients = new List<TcpClient>();

        private TcpListener _listener;
        private volatile bool _running;
        private long _bytesReceived;
        private long _bytesSent;

        public int LocalListenPort { get; private set; }

        public bool IsRunning
        {
            get { return _running; }
        }

        public long TotalBytesReceived
        {
            get { return Interlocked.Read(ref _bytesReceived); }
        }

        public long TotalBytesSent
        {
            get { return Interlocked.Read(ref _bytesSent); }
        }

        public ShihabLocalAuthProxyBridge(
            string upstreamHost,
            int upstreamPort,
            bool upstreamUseTls,
            string username,
            string plainPassword)
        {
            _upstreamHost = (upstreamHost ?? string.Empty).Trim();
            _upstreamPort = upstreamPort > 0 ? upstreamPort : 8080;
            _upstreamUseTls = upstreamUseTls;

            if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(plainPassword))
            {
                string raw = (username ?? string.Empty) + ":" + (plainPassword ?? string.Empty);
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
                _basicAuthHeaderLine = "Proxy-Authorization: Basic " + b64 + "\r\n";
            }
            else
            {
                _basicAuthHeaderLine = string.Empty;
            }
        }

        public void StartShihabBridge()
        {
            if (_running)
            {
                return;
            }

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            IPEndPoint ep = (IPEndPoint)_listener.LocalEndpoint;
            LocalListenPort = ep.Port;
            _running = true;

            Task.Run(new Action(AcceptLoop));
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch
                {
                    if (client != null)
                    {
                        try { client.Close(); } catch { }
                    }
                    if (!_running) break;
                    continue;
                }

                if (!_concurrencyGate.Wait(2000))
                {
                    try { client.Close(); } catch { }
                    continue;
                }

                TrackClient(client);
                TcpClient captured = client;
                Task.Run(delegate
                {
                    try
                    {
                        HandleClientSession(captured);
                    }
                    finally
                    {
                        UntrackClient(captured);
                        try { captured.Close(); } catch { }
                        _concurrencyGate.Release();
                    }
                });
            }
        }

        private void HandleClientSession(TcpClient localClient)
        {
            localClient.NoDelay = true;
            localClient.ReceiveTimeout = 20000;
            localClient.SendTimeout = 20000;

            NetworkStream localStream = localClient.GetStream();

            // Read initial HTTP request headers up to \r\n\r\n (bounded to 16 KB)
            byte[] headerBuf = new byte[16384];
            int totalRead = 0;
            int headerEndIndex = -1;

            while (totalRead < headerBuf.Length)
            {
                int n = localStream.Read(headerBuf, totalRead, headerBuf.Length - totalRead);
                if (n <= 0)
                {
                    return;
                }
                totalRead += n;
                headerEndIndex = FindDoubleCrlf(headerBuf, totalRead);
                if (headerEndIndex >= 0)
                {
                    break;
                }
            }

            if (headerEndIndex < 0)
            {
                return;
            }

            Interlocked.Add(ref _bytesSent, totalRead);

            int headerByteLen = headerEndIndex + 4;
            int trailingBodyLen = totalRead - headerByteLen;

            string rawHeaders = Encoding.ASCII.GetString(headerBuf, 0, headerByteLen);
            string outboundHeaders = InjectAuthHeaderIfNeeded(rawHeaders);
            byte[] outboundHeaderBytes = Encoding.ASCII.GetBytes(outboundHeaders);

            TcpClient upstreamTcp = new TcpClient();
            try
            {
                IAsyncResult ar = upstreamTcp.BeginConnect(_upstreamHost, _upstreamPort, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(8000, false) || !upstreamTcp.Connected)
                {
                    return;
                }
                upstreamTcp.EndConnect(ar);
                upstreamTcp.NoDelay = true;

                Stream upstreamStream = upstreamTcp.GetStream();
                SslStream ssl = null;
                try
                {
                    if (_upstreamUseTls)
                    {
                        ssl = new SslStream(upstreamStream, false);
                        ssl.AuthenticateAsClient(_upstreamHost);
                        upstreamStream = ssl;
                    }

                    upstreamStream.Write(outboundHeaderBytes, 0, outboundHeaderBytes.Length);
                    if (trailingBodyLen > 0)
                    {
                        upstreamStream.Write(headerBuf, headerByteLen, trailingBodyLen);
                    }
                    upstreamStream.Flush();

                    localClient.ReceiveTimeout = 0;
                    localClient.SendTimeout = 0;

                    Task upToLocal = Task.Run(delegate
                    {
                        PumpStreamBytes(upstreamStream, localStream, true);
                        try { localClient.Client.Shutdown(SocketShutdown.Send); } catch { }
                    });

                    Task localToUp = Task.Run(delegate
                    {
                        PumpStreamBytes(localStream, upstreamStream, false);
                        try { upstreamTcp.Client.Shutdown(SocketShutdown.Send); } catch { }
                    });

                    Task.WaitAll(new Task[] { upToLocal, localToUp });
                }
                finally
                {
                    if (ssl != null)
                    {
                        try { ssl.Dispose(); } catch { }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                try { upstreamTcp.Close(); } catch { }
            }
        }

        private string InjectAuthHeaderIfNeeded(string rawHeaders)
        {
            if (string.IsNullOrEmpty(_basicAuthHeaderLine) ||
                rawHeaders.IndexOf("\r\nProxy-Authorization:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return rawHeaders;
            }

            int firstCrlf = rawHeaders.IndexOf("\r\n", StringComparison.Ordinal);
            if (firstCrlf <= 0)
            {
                return rawHeaders;
            }

            return rawHeaders.Substring(0, firstCrlf + 2) +
                   _basicAuthHeaderLine +
                   rawHeaders.Substring(firstCrlf + 2);
        }

        private void PumpStreamBytes(Stream src, Stream dst, bool countAsReceived)
        {
            byte[] buf = new byte[16384];
            try
            {
                while (_running)
                {
                    int n = src.Read(buf, 0, buf.Length);
                    if (n <= 0)
                    {
                        break;
                    }
                    dst.Write(buf, 0, n);
                    if (countAsReceived)
                    {
                        Interlocked.Add(ref _bytesReceived, n);
                    }
                    else
                    {
                        Interlocked.Add(ref _bytesSent, n);
                    }
                }
            }
            catch
            {
            }
        }

        private static int FindDoubleCrlf(byte[] buf, int length)
        {
            for (int i = 0; i <= length - 4; i++)
            {
                if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10)
                {
                    return i;
                }
            }
            return -1;
        }

        private void TrackClient(TcpClient c)
        {
            lock (_clientsLock)
            {
                _activeClients.Add(c);
            }
        }

        private void UntrackClient(TcpClient c)
        {
            lock (_clientsLock)
            {
                _activeClients.Remove(c);
            }
        }

        public void StopShihabBridge()
        {
            _running = false;
            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                _listener = null;
            }

            List<TcpClient> snapshot;
            lock (_clientsLock)
            {
                snapshot = new List<TcpClient>(_activeClients);
                _activeClients.Clear();
            }

            for (int i = 0; i < snapshot.Count; i++)
            {
                try { snapshot[i].Close(); } catch { }
            }
        }

        public void Dispose()
        {
            StopShihabBridge();
            try { _concurrencyGate.Dispose(); } catch { }
        }
    }
}

