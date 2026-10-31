using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Text;
using ShihabShieldVpn.Core;

namespace ShihabShieldVpn.Services
{
    public sealed class ShihabSecurityShieldService
    {
        public const string ShihabFirewallRulePrefix = "ShihabShieldVPN_KS_";

        private readonly ShihabConfigService _configService;

        public ShihabSecurityShieldService(ShihabConfigService configService)
        {
            _configService = configService;
        }

        public static bool IsShihabRunningElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(id);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public void RecoverOrphanedShihabKillSwitchRules(ShihabAppConfig cfg, bool isTunnelCurrentlyActive)
        {
            if (cfg != null && cfg.FirewallKillSwitchArmedOnDisk && !isTunnelCurrentlyActive)
            {
                DisarmShihabFirewallKillSwitch(cfg);
            }
        }

        public ShihabResult<bool> ArmShihabFirewallKillSwitch(ShihabAppConfig cfg, string vpnServerIp)
        {
            IPAddress parsedVpnIp;
            if (string.IsNullOrWhiteSpace(vpnServerIp) || !IPAddress.TryParse(vpnServerIp.Trim(), out parsedVpnIp))
            {
                return ShihabResult<bool>.Fail("INVALID_VPN_IP", "Cannot arm strict firewall kill-switch without a valid VPN server IP.", 0);
            }

            string allowRule = ShihabFirewallRulePrefix + "AllowVpnEndpoint";
            string loopbackRule = ShihabFirewallRulePrefix + "AllowLoopback";

            string script = string.Join(" & ", new string[]
            {
                "netsh advfirewall firewall delete rule name=\"" + allowRule + "\" >nul 2>&1",
                "netsh advfirewall firewall delete rule name=\"" + loopbackRule + "\" >nul 2>&1",
                "netsh advfirewall firewall add rule name=\"" + allowRule + "\" dir=out action=allow remoteip=" + parsedVpnIp.ToString() + " enable=yes",
                "netsh advfirewall firewall add rule name=\"" + loopbackRule + "\" dir=out action=allow remoteip=127.0.0.1/8,10.0.0.0/8,192.168.0.0/16 enable=yes"
            });

            bool ok = ExecuteNetshBatch(script, true);
            if (!ok)
            {
                return ShihabResult<bool>.Fail("FIREWALL_ELEVATION_DECLINED", "Administrator approval is required for Windows Firewall Kill-Switch rules.", 0);
            }

            if (cfg != null)
            {
                cfg.FirewallKillSwitchArmedOnDisk = true;
                if (_configService != null)
                {
                    _configService.SaveShihabConfig(cfg);
                }
            }

            return ShihabResult<bool>.Ok(true, 0);
        }

        public ShihabResult<bool> DisarmShihabFirewallKillSwitch(ShihabAppConfig cfg)
        {
            string allowRule = ShihabFirewallRulePrefix + "AllowVpnEndpoint";
            string loopbackRule = ShihabFirewallRulePrefix + "AllowLoopback";

            string script = string.Join(" & ", new string[]
            {
                "netsh advfirewall firewall delete rule name=\"" + allowRule + "\" >nul 2>&1",
                "netsh advfirewall firewall delete rule name=\"" + loopbackRule + "\" >nul 2>&1"
            });

            bool hadArmedFlag = cfg != null && cfg.FirewallKillSwitchArmedOnDisk;
            if (hadArmedFlag || IsShihabRunningElevated())
            {
                ExecuteNetshBatch(script, !IsShihabRunningElevated());
            }

            if (cfg != null && cfg.FirewallKillSwitchArmedOnDisk)
            {
                cfg.FirewallKillSwitchArmedOnDisk = false;
                if (_configService != null)
                {
                    _configService.SaveShihabConfig(cfg);
                }
            }

            return ShihabResult<bool>.Ok(true, 0);
        }

        private static bool ExecuteNetshBatch(string commandChain, bool allowRunAsPrompt)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c " + commandChain;
                if (IsShihabRunningElevated())
                {
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                }
                else if (allowRunAsPrompt)
                {
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                }
                else
                {
                    return false;
                }

                using (Process proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        return false;
                    }
                    proc.WaitForExit(12000);
                    return proc.HasExited && proc.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}

