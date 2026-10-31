using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ShihabShieldVpn.Core;

namespace ShihabShieldVpn.Services
{
    public sealed class ShihabBrowserCompanionBridgeCallbacks
    {
        public Func<Dictionary<string, object>> GetStatusSnapshot { get; set; }
        public Func<Dictionary<string, object>> GetCatalogSnapshot { get; set; }
        public Action<string, string> RequestConnect { get; set; }
        public Action RequestDisconnect { get; set; }
        public Action RequestRotateIp { get; set; }
    }

    /// <summary>
    /// Loopback-only (127.0.0.1:38195) companion bridge allowing the Shihab Shield VPN
    /// Chrome / Brave Manifest V3 extension to inspect status and trigger country connect,
    /// disconnect, and Dynamic IP Rotation without requiring Administrator HttpListener ACLs.
    /// Rejects any web-page Origin (only chrome-extension:// or moz-extension:// allowed).
    /// </summary>
    public sealed class ShihabBrowserCompanionService : IDisposable
    {
        public const int DefaultCompanionPort = 38195;

        private readonly int _listenPort;
        private readonly ShihabBrowserCompanionBridgeCallbacks _callbacks;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(16);
        private TcpListener _listener;
        private volatile bool _running;

        public int ActivePort { get; private set; }

        public bool IsRunning
        {
            get { return _running; }
        }

        public ShihabBrowserCompanionService(ShihabBrowserCompanionBridgeCallbacks callbacks, int port = DefaultCompanionPort)
        {
            _callbacks = callbacks;
            _listenPort = port;
        }

        public bool TryStartShihabCompanion()
        {
            if (_running)
            {
                return true;
            }

            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _listenPort);
                _listener.Start();
                IPEndPoint ep = (IPEndPoint)_listener.LocalEndpoint;
                ActivePort = ep.Port;
                _running = true;
                Task.Run(new Action(AcceptLoop));
                return true;
            }
            catch
            {
                _running = false;
                return false;
            }
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

                if (!_gate.Wait(1000))
                {
                    try { client.Close(); } catch { }
                    continue;
                }

                TcpClient captured = client;
                Task.Run(delegate
                {
                    try
                    {
                        HandleHttpClient(captured);
                    }
                    finally
                    {
                        try { captured.Close(); } catch { }
                        try { _gate.Release(); } catch { }
                    }
                });
            }
        }

        private void HandleHttpClient(TcpClient client)
        {
            client.ReceiveTimeout = 3000;
            client.SendTimeout = 3000;

            using (NetworkStream ns = client.GetStream())
            {
                byte[] buf = new byte[16384];
                int total = 0;
                int headerEnd = -1;

                while (total < buf.Length)
                {
                    int n = ns.Read(buf, total, buf.Length - total);
                    if (n <= 0) break;
                    total += n;
                    headerEnd = FindHeaderEnd(buf, total);
                    if (headerEnd >= 0) break;
                }

                if (headerEnd < 0)
                {
                    return;
                }

                string headerRaw = Encoding.UTF8.GetString(buf, 0, headerEnd);
                string[] lines = headerRaw.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
                {
                    return;
                }

                string[] reqParts = lines[0].Split(' ');
                if (reqParts.Length < 2)
                {
                    return;
                }

                string method = reqParts[0].Trim().ToUpperInvariant();
                string rawPath = reqParts[1].Trim();
                int qIdx = rawPath.IndexOf('?');
                string path = qIdx >= 0 ? rawPath.Substring(0, qIdx) : rawPath;

                string origin = string.Empty;
                string shihabHeader = string.Empty;
                int contentLength = 0;

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int cIdx = line.IndexOf(':');
                    if (cIdx <= 0) continue;
                    string k = line.Substring(0, cIdx).Trim();
                    string v = line.Substring(cIdx + 1).Trim();
                    if (string.Equals(k, "Origin", StringComparison.OrdinalIgnoreCase))
                    {
                        origin = v;
                    }
                    else if (string.Equals(k, "X-Shihab-Client", StringComparison.OrdinalIgnoreCase))
                    {
                        shihabHeader = v;
                    }
                    else if (string.Equals(k, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsedLen;
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedLen) && parsedLen > 0 && parsedLen <= 65536)
                        {
                            contentLength = parsedLen;
                        }
                    }
                }

                // Strict Origin enforcement: block any ordinary web page from communicating with the companion bridge
                if (!string.IsNullOrEmpty(origin) && !IsAllowedExtensionOrigin(origin))
                {
                    WriteJsonResponse(ns, 403, "Forbidden", string.Empty, "{\"ok\":false,\"error\":\"FORBIDDEN_ORIGIN\"}");
                    return;
                }

                string allowedCorsOrigin = IsAllowedExtensionOrigin(origin)
                    ? origin
                    : (string.IsNullOrEmpty(origin) ? "*" : string.Empty);

                if (method == "OPTIONS")
                {
                    WriteJsonResponse(ns, 204, "No Content", allowedCorsOrigin, string.Empty);
                    return;
                }

                int bodyStart = headerEnd + 4;
                int bodyAlready = Math.Max(0, total - bodyStart);
                byte[] bodyBytes = new byte[contentLength];
                if (contentLength > 0 && bodyAlready > 0)
                {
                    Buffer.BlockCopy(buf, bodyStart, bodyBytes, 0, Math.Min(contentLength, bodyAlready));
                }
                int offset = Math.Min(contentLength, bodyAlready);
                while (offset < contentLength)
                {
                    int r = ns.Read(bodyBytes, offset, contentLength - offset);
                    if (r <= 0) break;
                    offset += r;
                }
                string bodyText = contentLength > 0 ? Encoding.UTF8.GetString(bodyBytes, 0, offset) : string.Empty;

                JavaScriptSerializer ser = new JavaScriptSerializer();

                if (method == "GET" && string.Equals(path, "/api/shihab/status", StringComparison.OrdinalIgnoreCase))
                {
                    Dictionary<string, object> payload = _callbacks != null && _callbacks.GetStatusSnapshot != null
                        ? _callbacks.GetStatusSnapshot()
                        : new Dictionary<string, object>();
                    payload["ok"] = true;
                    payload["bridgeVersion"] = "1.3.0";
                    WriteJsonResponse(ns, 200, "OK", allowedCorsOrigin, ser.Serialize(payload));
                    return;
                }

                if (method == "GET" && string.Equals(path, "/api/shihab/countries", StringComparison.OrdinalIgnoreCase))
                {
                    Dictionary<string, object> payload = _callbacks != null && _callbacks.GetCatalogSnapshot != null
                        ? _callbacks.GetCatalogSnapshot()
                        : new Dictionary<string, object>();
                    payload["ok"] = true;
                    WriteJsonResponse(ns, 200, "OK", allowedCorsOrigin, ser.Serialize(payload));
                    return;
                }

                // All state-mutating POST endpoints require explicit X-Shihab-Client header
                if (method == "POST")
                {
                    if (!string.Equals(shihabHeader, "ShihabShieldBrowserExt", StringComparison.Ordinal))
                    {
                        WriteJsonResponse(ns, 403, "Forbidden", allowedCorsOrigin, "{\"ok\":false,\"error\":\"MISSING_SHIHAB_CLIENT_HEADER\"}");
                        return;
                    }

                    Dictionary<string, object> reqObj = null;
                    if (!string.IsNullOrWhiteSpace(bodyText))
                    {
                        try { reqObj = ser.DeserializeObject(bodyText) as Dictionary<string, object>; } catch { }
                    }

                    if (string.Equals(path, "/api/shihab/connect", StringComparison.OrdinalIgnoreCase))
                    {
                        string cc = ReadField(reqObj, "countryCode");
                        string sid = ReadField(reqObj, "serverId");
                        if (_callbacks != null && _callbacks.RequestConnect != null)
                        {
                            _callbacks.RequestConnect(cc, sid);
                        }
                        WriteJsonResponse(ns, 200, "OK", allowedCorsOrigin, "{\"ok\":true,\"action\":\"connecting\"}");
                        return;
                    }

                    if (string.Equals(path, "/api/shihab/disconnect", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_callbacks != null && _callbacks.RequestDisconnect != null)
                        {
                            _callbacks.RequestDisconnect();
                        }
                        WriteJsonResponse(ns, 200, "OK", allowedCorsOrigin, "{\"ok\":true,\"action\":\"disconnecting\"}");
                        return;
                    }

                    if (string.Equals(path, "/api/shihab/rotate-ip", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_callbacks != null && _callbacks.RequestRotateIp != null)
                        {
                            _callbacks.RequestRotateIp();
                        }
                        WriteJsonResponse(ns, 200, "OK", allowedCorsOrigin, "{\"ok\":true,\"action\":\"rotating\"}");
                        return;
                    }
                }

                WriteJsonResponse(ns, 404, "Not Found", allowedCorsOrigin, "{\"ok\":false,\"error\":\"NOT_FOUND\"}");
            }
        }

        private static bool IsAllowedExtensionOrigin(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                return false;
            }
            return origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                   origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) ||
                   origin.StartsWith("brave://", StringComparison.OrdinalIgnoreCase) ||
                   origin.StartsWith("edge://", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadField(Dictionary<string, object> d, string key)
        {
            object val;
            if (d != null && d.TryGetValue(key, out val) && val != null)
            {
                return Convert.ToString(val, CultureInfo.InvariantCulture).Trim();
            }
            return string.Empty;
        }

        private static int FindHeaderEnd(byte[] buf, int len)
        {
            for (int i = 0; i <= len - 4; i++)
            {
                if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10)
                {
                    return i;
                }
            }
            return -1;
        }

        private static void WriteJsonResponse(NetworkStream ns, int statusCode, string reason, string corsOrigin, string jsonBody)
        {
            byte[] bodyBytes = string.IsNullOrEmpty(jsonBody) ? new byte[0] : Encoding.UTF8.GetBytes(jsonBody);
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "HTTP/1.1 {0} {1}\r\n", statusCode, reason);
            sb.Append("Content-Type: application/json; charset=utf-8\r\n");
            sb.AppendFormat(CultureInfo.InvariantCulture, "Content-Length: {0}\r\n", bodyBytes.Length);
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("Connection: close\r\n");
            if (!string.IsNullOrEmpty(corsOrigin))
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "Access-Control-Allow-Origin: {0}\r\n", corsOrigin);
                sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
                sb.Append("Access-Control-Allow-Headers: Content-Type, X-Shihab-Client\r\n");
                sb.Append("Access-Control-Allow-Private-Network: true\r\n");
            }
            sb.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            ns.Write(headerBytes, 0, headerBytes.Length);
            if (bodyBytes.Length > 0)
            {
                ns.Write(bodyBytes, 0, bodyBytes.Length);
            }
            ns.Flush();
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                if (_listener != null)
                {
                    _listener.Stop();
                }
            }
            catch
            {
            }
        }
    }
}

