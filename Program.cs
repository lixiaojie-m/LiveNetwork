using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net.NetworkInformation;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LiveNetwork
{
    public class Program : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private System.Windows.Forms.Timer updateTimer;
        private PerformanceCounter networkDownCounter;
        private PerformanceCounter networkUpCounter;
        private Label downloadSpeedLabel;
        private Label uploadSpeedLabel;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Program());
        }

        public Program()
        {
            InitializeComponent();
            InitializeNetworkCounters();
            StartMonitoring();
        }

        private void InitializeComponent()
        {
            // 创建系统托盘图标
            trayIcon = new NotifyIcon()
            {
                Icon = GetApplicationIcon(),
                Visible = true
            };

            // 创建右键菜单
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("退出", null, OnExit);
            trayIcon.ContextMenuStrip = trayMenu;

            // 设置窗体属性
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Size = new Size(120, 40);
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Width - this.Width - 10,
                                    Screen.PrimaryScreen.WorkingArea.Height - this.Height);

            // 创建标签控件
            downloadSpeedLabel = new Label
            {
                AutoSize = true,
                Location = new Point(5, 20),
                Text = "↓ 0 B/s",
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(240, 240, 240)
            };

            uploadSpeedLabel = new Label
            {
                AutoSize = true,
                Location = new Point(5, 3),
                Text = "↑ 0 B/s",
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(240, 240, 240)
            };

            this.Controls.Add(downloadSpeedLabel);
            this.Controls.Add(uploadSpeedLabel);

            this.MouseDown += Form_MouseDown;
            this.MouseMove += Form_MouseMove;

            // 设置窗体背景色和透明度
            this.BackColor = Color.FromArgb(32, 32, 32);
            this.Opacity = 0.95;
        }

        private Point lastPoint;

        private void Form_MouseDown(object sender, MouseEventArgs e)
        {
            lastPoint = new Point(e.X, e.Y);
        }

        private void Form_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.Left += e.X - lastPoint.X;
                this.Top += e.Y - lastPoint.Y;
            }
        }

        private Icon GetApplicationIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(iconPath))
                {
                    return new Icon(iconPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载自定义图标失败: {ex.Message}");
                // 记录异常以避免编译器警告
            }
            return SystemIcons.Application;
        }

        private void InitializeNetworkCounters()
        {
            try
            {
                string instanceName = GetNetworkInterfaceInstanceName();
                if (networkDownCounter != null)
                {
                    networkDownCounter.Close();
                    networkDownCounter.Dispose();
                }
                if (networkUpCounter != null)
                {
                    networkUpCounter.Close();
                    networkUpCounter.Dispose();
                }

                networkDownCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", instanceName, true);
                networkUpCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instanceName, true);
                
                // 初始化时先读取一次值，避免首次读取返回0
                Thread.Sleep(1000); // 等待1秒确保计数器准备就绪
                networkDownCounter.NextValue();
                networkUpCounter.NextValue();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化网络监控失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        private string GetNetworkInterfaceInstanceName()
        {
            try
            {
                // 获取所有网络接口
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                NetworkInterface activeInterface = null;
                
                // 查找活动的网络接口
                foreach (NetworkInterface ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                         ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    {
                        activeInterface = ni;
                        break;
                    }
                }
                
                if (activeInterface == null)
                {
                    throw new Exception("未找到活动的网络接口");
                }
                
                // 获取性能计数器分类中的实例名称
                PerformanceCounterCategory category = new PerformanceCounterCategory("Network Interface");
                string[] instanceNames = category.GetInstanceNames();
                
                // 查找匹配的实例名称
                string activeNicDescription = activeInterface.Description;
                foreach (string instance in instanceNames)
                {
                    // 使用部分匹配，因为性能计数器中的实例名可能与网卡描述不完全一致
                    if (instance.IndexOf(activeNicDescription, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        activeNicDescription.IndexOf(instance, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return instance;
                    }
                }
                
                // 如果没有找到精确匹配，尝试使用第一个可用的网络接口实例
                if (instanceNames.Length > 0)
                {
                    return instanceNames[0];
                }
                
                throw new Exception("未找到匹配的网络接口性能计数器实例");
            }
            catch (Exception ex)
            {
                throw new Exception($"获取网络接口实例名称失败: {ex.Message}");
            }
        }

        private void StartMonitoring()
        {
            try
            {
                InitializeNetworkCounters();
                updateTimer = new System.Windows.Forms.Timer();
                updateTimer.Interval = 1000; // 每秒更新一次
                updateTimer.Tick += UpdateNetworkSpeed;
                updateTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动网络监控失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateNetworkSpeed(object sender, EventArgs e)
        {
            try
            {
                // 检查网络接口是否可用
                if (!IsNetworkInterfaceAvailable())
                {
                    trayIcon.Text = "网络接口不可用";
                    downloadSpeedLabel.Text = "↓ 不可用";
                    uploadSpeedLabel.Text = "↑ 不可用";
                    Thread.Sleep(1000); // 等待1秒后重试
                    InitializeNetworkCounters();
                    return;
                }

                if (networkDownCounter == null || networkUpCounter == null)
                {
                    InitializeNetworkCounters();
                    return;
                }

                float downloadSpeed = Math.Max(0, networkDownCounter.NextValue());
                float uploadSpeed = Math.Max(0, networkUpCounter.NextValue());

                string downloadText = FormatSpeed(downloadSpeed);
                string uploadText = FormatSpeed(uploadSpeed);

                downloadSpeedLabel.Text = $"↓ {downloadText}";
                uploadSpeedLabel.Text = $"↑ {uploadText}";

                string tooltipText = $"↓ {downloadText}\n↑ {uploadText}";
                if (tooltipText.Length > 63)
                {
                    tooltipText = tooltipText.Substring(0, 60) + "...";
                }
                trayIcon.Text = tooltipText;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新网络速度失败: {ex.Message}");
                // 尝试重新初始化网络计数器
                try
                {
                    InitializeNetworkCounters();
                }
                catch
                {
                    // 如果重新初始化失败，显示错误状态
                    downloadSpeedLabel.Text = "↓ 错误";
                    uploadSpeedLabel.Text = "↑ 错误";
                    trayIcon.Text = "网络监控错误";
                }
            }
        }

        private bool IsNetworkInterfaceAvailable()
        {
            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                return interfaces.Any(ni =>
                    ni.OperationalStatus == OperationalStatus.Up &&
                    (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                     ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"检查网络接口状态失败: {ex.Message}");
                return false;
            }
        }

        private string FormatSpeed(float bytesPerSec)
        {
            string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
            int unitIndex = 0;
            double speed = bytesPerSec;

            while (speed >= 1024 && unitIndex < units.Length - 1)
            {
                speed /= 1024;
                unitIndex++;
            }

            return $"{speed:F1} {units[unitIndex]}";
        }

        private void OnExit(object sender, EventArgs e)
        {
            try
            {
                // 停止定时器
                if (updateTimer != null)
                {
                    updateTimer.Stop();
                    updateTimer.Dispose();
                }

                // 释放性能计数器
                if (networkDownCounter != null)
                {
                    networkDownCounter.Close();
                    networkDownCounter.Dispose();
                }
                if (networkUpCounter != null)
                {
                    networkUpCounter.Close();
                    networkUpCounter.Dispose();
                }

                // 释放托盘图标
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }

                // 释放托盘菜单
                if (trayMenu != null)
                {
                    trayMenu.Dispose();
                }

                // 退出应用程序
                Application.Exit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"退出程序时发生错误: {ex.Message}");
                Application.Exit();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                OnExit(this, EventArgs.Empty);
            }
            base.Dispose(disposing);
        }
    }
}