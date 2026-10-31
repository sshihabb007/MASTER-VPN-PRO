using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Shihab Shield VPN Setup")]
[assembly: AssemblyDescription("Official Windows Setup Wizard & Uninstaller for Shihab Shield VPN (Mehedi Hasan Shihab)")]
[assembly: AssemblyCompany("Mehedi Hasan Shihab")]
[assembly: AssemblyProduct("Shihab Shield VPN")]
[assembly: AssemblyCopyright("Copyright © 2026 Mehedi Hasan Shihab (sshihabb007)")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: ComVisible(false)]

namespace ShihabShieldVpnInstaller
{
    public static class SetupProgram
    {
        public const string ProductName = "Shihab Shield VPN";
        public const string ProductVersion = "1.2.0";
        public const string PublisherName = "Mehedi Hasan Shihab";
        public const string MainExeName = "ShihabShieldVPN.exe";
        public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShihabShieldVPN";
        public const string StartupRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string StartupValueName = "ShihabShieldVPN";

        [STAThread]
        public static int Main(string[] args)
        {
            bool silent = HasFlag(args, "--silent") || HasFlag(args, "/S");
            bool uninstall = HasFlag(args, "--uninstall") ||
                             string.Equals(Path.GetFileName(Assembly.GetExecutingAssembly().Location), "Uninstall.exe", StringComparison.OrdinalIgnoreCase);
            bool noRegistry = HasFlag(args, "--no-registry");
            string customDir = GetArgValue(args, "--install-dir");

            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Shihab Shield VPN");
            string targetDir = string.IsNullOrWhiteSpace(customDir) ? defaultDir : customDir;

            if (silent)
            {
                try
                {
                    if (uninstall)
                    {
                        PerformShihabUninstall(targetDir, !noRegistry, true);
                    }
                    else
                    {
                        PerformShihabInstall(targetDir, false, false, false, !noRegistry);
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    try { Console.Error.WriteLine(ex.Message); } catch { }
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ShihabSetupForm(targetDir, uninstall));
            return 0;
        }

        public static void PerformShihabInstall(
            string installDir,
            bool desktopShortcut,
            bool startMenuShortcut,
            bool launchOnStartup,
            bool writeRegistry)
        {
            StopRunningShihabInstances();

            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
            }

            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream payloadStream = asm.GetManifestResourceStream("ShihabShieldVpnInstaller.payload.zip"))
            {
                if (payloadStream == null)
                {
                    throw new InvalidOperationException("Embedded payload.zip resource was not found.");
                }
                using (ZipArchive archive = new ZipArchive(payloadStream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        string destFile = Path.Combine(installDir, entry.FullName);
                        string destSubDir = Path.GetDirectoryName(destFile);
                        if (!string.IsNullOrEmpty(destSubDir) && !Directory.Exists(destSubDir))
                        {
                            Directory.CreateDirectory(destSubDir);
                        }
                        using (Stream inStream = entry.Open())
                        using (FileStream outStream = new FileStream(destFile, FileMode.Create, FileAccess.Write))
                        {
                            inStream.CopyTo(outStream);
                        }
                    }
                }
            }

            string uninstallerPath = Path.Combine(installDir, "Uninstall.exe");
            File.Copy(asm.Location, uninstallerPath, true);

            string appExePath = Path.Combine(installDir, MainExeName);

            if (desktopShortcut)
            {
                string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProductName + ".lnk");
                CreateWindowsShortcut(deskLnk, appExePath, installDir, "Open-Source Windows VPN Client by Mehedi Hasan Shihab");
            }

            if (startMenuShortcut)
            {
                string progMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName);
                if (!Directory.Exists(progMenu)) Directory.CreateDirectory(progMenu);
                CreateWindowsShortcut(Path.Combine(progMenu, ProductName + ".lnk"), appExePath, installDir, ProductName);
                CreateWindowsShortcut(Path.Combine(progMenu, "Uninstall " + ProductName + ".lnk"), uninstallerPath, installDir, "Uninstall " + ProductName);
            }

            if (writeRegistry)
            {
                using (RegistryKey uk = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
                {
                    if (uk != null)
                    {
                        uk.SetValue("DisplayName", ProductName);
                        uk.SetValue("DisplayVersion", ProductVersion);
                        uk.SetValue("Publisher", PublisherName);
                        uk.SetValue("DisplayIcon", appExePath);
                        uk.SetValue("InstallLocation", installDir);
                        uk.SetValue("UninstallString", "\"" + uninstallerPath + "\" --uninstall");
                        uk.SetValue("QuietUninstallString", "\"" + uninstallerPath + "\" --uninstall --silent");
                        uk.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        uk.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }

                if (launchOnStartup)
                {
                    using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(StartupRunKeyPath, true))
                    {
                        if (rk != null)
                        {
                            object prev = rk.GetValue(StartupValueName);
                            if (prev != null)
                            {
                                File.WriteAllText(Path.Combine(installDir, "startup_run_backup.txt"), prev.ToString());
                            }
                            rk.SetValue(StartupValueName, "\"" + appExePath + "\" --minimized");
                        }
                    }
                }
            }
        }

        public static void PerformShihabUninstall(string installDir, bool cleanRegistry, bool isSilent)
        {
            StopRunningShihabInstances();

            if (cleanRegistry)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); } catch { }
                try
                {
                    using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(StartupRunKeyPath, true))
                    {
                        if (rk != null) rk.DeleteValue(StartupValueName, false);
                    }
                }
                catch { }
            }

            try
            {
                string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProductName + ".lnk");
                if (File.Exists(deskLnk)) File.Delete(deskLnk);

                string progMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName);
                if (Directory.Exists(progMenu)) Directory.Delete(progMenu, true);
            }
            catch { }

            if (Directory.Exists(installDir))
            {
                string currentExe = Assembly.GetExecutingAssembly().Location;
                foreach (string file in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(file, currentExe, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(file); } catch { }
                }

                // Deferred cleanup of Uninstall.exe itself
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/C ping 127.0.0.1 -n 2 > nul & rmdir /S /Q \"" + installDir + "\"";
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                try { Process.Start(psi); } catch { }
            }
        }

        private static void StopRunningShihabInstances()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("ShihabShieldVPN");
                for (int i = 0; i < procs.Length; i++)
                {
                    try
                    {
                        procs[i].Kill();
                        procs[i].WaitForExit(2000);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void CreateWindowsShortcut(string lnkPath, string targetExe, string workDir, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object shell = Activator.CreateInstance(shellType);
            object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            Type scType = sc.GetType();
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { targetExe });
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { targetExe + ",0" });
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetArgValue(string[] args, string key)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }
    }

    public sealed class ShihabSetupForm : Form
    {
        private readonly TextBox _dirBox;
        private readonly CheckBox _chkDesktop;
        private readonly CheckBox _chkStartMenu;
        private readonly CheckBox _chkStartup;
        private readonly CheckBox _chkLaunchNow;
        private readonly Button _actionBtn;
        private readonly Label _statusLabel;
        private readonly bool _isUninstall;

        public ShihabSetupForm(string initialDir, bool uninstallMode)
        {
            _isUninstall = uninstallMode;
            Text = uninstallMode ? "Uninstall Shihab Shield VPN" : "Shihab Shield VPN v1.0.0 — Setup Wizard";
            ClientSize = new Size(540, 390);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(19, 21, 25);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

            Label header = new Label
            {
                Text = uninstallMode ? "Remove Shihab Shield VPN" : "Install Shihab Shield VPN v1.0.0",
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 20),
                AutoSize = true
            };
            Controls.Add(header);

            Label sub = new Label
            {
                Text = "Developed by Mehedi Hasan Shihab (https://sshihabb007.github.io/)",
                Font = new Font("Segoe UI", 9.2f, FontStyle.Regular),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(26, 52),
                AutoSize = true
            };
            Controls.Add(sub);

            Label planLbl = new Label
            {
                Text = uninstallMode
                    ? "Planned action: Stop running Shihab Shield VPN instance, remove shortcuts, and delete installed files."
                    : "Planned changes (applied only after you click Install):",
                ForeColor = Color.FromArgb(170, 178, 192),
                Location = new Point(26, 86),
                Size = new Size(488, 36)
            };
            Controls.Add(planLbl);

            _dirBox = new TextBox
            {
                Text = initialDir,
                Location = new Point(26, 126),
                Size = new Size(488, 28),
                BackColor = Color.FromArgb(29, 32, 38),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = uninstallMode
            };
            Controls.Add(_dirBox);

            _chkDesktop = new CheckBox { Text = "Create Desktop shortcut", Checked = true, Location = new Point(26, 168), AutoSize = true, Visible = !uninstallMode };
            _chkStartMenu = new CheckBox { Text = "Create Start Menu shortcuts & Windows Uninstall entry", Checked = true, Location = new Point(26, 196), AutoSize = true, Visible = !uninstallMode };
            _chkStartup = new CheckBox { Text = "Start minimized in System Tray with Windows", Checked = false, Location = new Point(26, 224), AutoSize = true, Visible = !uninstallMode };
            _chkLaunchNow = new CheckBox { Text = "Launch Shihab Shield VPN after installation completes", Checked = true, Location = new Point(26, 252), AutoSize = true, Visible = !uninstallMode };

            Controls.Add(_chkDesktop);
            Controls.Add(_chkStartMenu);
            Controls.Add(_chkStartup);
            Controls.Add(_chkLaunchNow);

            _statusLabel = new Label
            {
                Text = "Ready.",
                ForeColor = Color.FromArgb(156, 163, 175),
                Location = new Point(26, 300),
                Size = new Size(488, 24)
            };
            Controls.Add(_statusLabel);

            _actionBtn = new Button
            {
                Text = uninstallMode ? "Uninstall" : "Install",
                Location = new Point(394, 334),
                Size = new Size(120, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = uninstallMode ? Color.FromArgb(185, 28, 28) : Color.FromArgb(29, 161, 242),
                ForeColor = Color.White
            };
            _actionBtn.FlatAppearance.BorderSize = 0;
            _actionBtn.Click += OnActionClicked;
            Controls.Add(_actionBtn);

            Button cancelBtn = new Button
            {
                Text = "Cancel",
                Location = new Point(280, 334),
                Size = new Size(102, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(36, 41, 50),
                ForeColor = Color.White
            };
            cancelBtn.FlatAppearance.BorderSize = 0;
            cancelBtn.Click += delegate { Close(); };
            Controls.Add(cancelBtn);
        }

        private void OnActionClicked(object sender, EventArgs e)
        {
            _actionBtn.Enabled = false;
            try
            {
                if (_isUninstall)
                {
                    _statusLabel.Text = "Uninstalling Shihab Shield VPN...";
                    Application.DoEvents();
                    SetupProgram.PerformShihabUninstall(_dirBox.Text.Trim(), true, false);
                    MessageBox.Show(this, "Shihab Shield VPN has been uninstalled cleanly.", "Uninstall Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                }
                else
                {
                    string target = _dirBox.Text.Trim();
                    _statusLabel.Text = "Extracting files & registering shortcuts...";
                    Application.DoEvents();
                    SetupProgram.PerformShihabInstall(target, _chkDesktop.Checked, _chkStartMenu.Checked, _chkStartup.Checked, true);
                    _statusLabel.Text = "Installation complete!";
                    if (_chkLaunchNow.Checked)
                    {
                        string appExe = Path.Combine(target, SetupProgram.MainExeName);
                        if (File.Exists(appExe))
                        {
                            Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true });
                        }
                    }
                    Close();
                }
            }
            catch (Exception ex)
            {
                _actionBtn.Enabled = true;
                _statusLabel.Text = "Error: " + ex.Message;
                MessageBox.Show(this, ex.Message, "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

