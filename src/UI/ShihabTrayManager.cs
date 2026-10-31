using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ShihabShieldVpn.UI
{
    public sealed class ShihabTrayManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _toggleConnectItem;
        private bool _disposed;

        public event EventHandler OpenDashboardRequested;
        public event EventHandler ToggleConnectRequested;
        public event EventHandler SpeedTestRequested;
        public event EventHandler AboutDeveloperRequested;
        public event EventHandler ExitAppRequested;

        public ShihabTrayManager()
        {
            _menu = new ContextMenuStrip();
            _menu.Renderer = new ToolStripProfessionalRenderer(new ShihabDarkMenuColorTable());
            _menu.BackColor = Color.FromArgb(24, 27, 32);
            _menu.ForeColor = Color.FromArgb(236, 240, 245);
            _menu.ShowImageMargin = false;
            _menu.Font = new Font("Segoe UI", 9.2f, FontStyle.Regular);

            _statusItem = new ToolStripMenuItem("MASTER VPN PRO — Disconnected");
            _statusItem.Enabled = false;

            _toggleConnectItem = new ToolStripMenuItem("⚡  Quick Connect (Auto)");
            _toggleConnectItem.Click += delegate { Raise(ToggleConnectRequested); };

            ToolStripMenuItem openItem = new ToolStripMenuItem("🛡  Open MASTER VPN PRO Dashboard");
            openItem.Click += delegate { Raise(OpenDashboardRequested); };

            ToolStripMenuItem speedItem = new ToolStripMenuItem("🚀  Run Speed Benchmark");
            speedItem.Click += delegate { Raise(SpeedTestRequested); };

            ToolStripMenuItem aboutItem = new ToolStripMenuItem("ℹ  About Developer (Mehedi Hasan Shihab)");
            aboutItem.Click += delegate { Raise(AboutDeveloperRequested); };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("✕  Disconnect & Exit");
            exitItem.Click += delegate { Raise(ExitAppRequested); };

            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_toggleConnectItem);
            _menu.Items.Add(speedItem);
            _menu.Items.Add(openItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(aboutItem);
            _menu.Items.Add(exitItem);

            Icon exeIcon = null;
            try
            {
                exeIcon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
            }

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = exeIcon ?? SystemIcons.Shield;
            _notifyIcon.Text = "MASTER VPN PRO (Disconnected)";
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += delegate { Raise(OpenDashboardRequested); };
        }

        public void UpdateShihabTrayState(bool isConnected, string locationTitle, string ipAddress)
        {
            if (_disposed) return;
            if (isConnected)
            {
                string label = string.Format("Connected: {0} ({1})", locationTitle ?? "VPN", ipAddress ?? "");
                _statusItem.Text = label;
                _toggleConnectItem.Text = "■  Disconnect VPN Tunnel";
                string tip = "MASTER VPN PRO: " + (locationTitle ?? "Connected");
                _notifyIcon.Text = tip.Length > 62 ? tip.Substring(0, 62) : tip;
            }
            else
            {
                _statusItem.Text = "MASTER VPN PRO — Disconnected";
                _toggleConnectItem.Text = "⚡  Quick Connect (" + (locationTitle ?? "Auto") + ")";
                _notifyIcon.Text = "MASTER VPN PRO — Ready";
            }
        }

        public void ShowShihabNotification(string title, string body)
        {
            if (_disposed) return;
            try
            {
                _notifyIcon.ShowBalloonTip(2500, title, body, ToolTipIcon.Info);
            }
            catch
            {
            }
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _menu.Dispose();
                }
                catch
                {
                }
            }
        }

        private sealed class ShihabDarkMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(24, 27, 32); } }
            public override Color MenuBorder { get { return Color.FromArgb(46, 52, 62); } }
            public override Color MenuItemBorder { get { return Color.FromArgb(29, 161, 242); } }
            public override Color MenuItemSelected { get { return Color.FromArgb(35, 42, 54); } }
            public override Color SeparatorDark { get { return Color.FromArgb(44, 49, 58); } }
            public override Color SeparatorLight { get { return Color.FromArgb(44, 49, 58); } }
        }
    }
}

