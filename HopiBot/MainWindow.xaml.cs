using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Gma.System.MouseKeyHook;
using HopiBot.Game;
using HopiBot.LCU;
using HopiBot.LCU.bo;
using HopiBot.Enum;
using HopiBot.Hack;
using MessageBox = System.Windows.MessageBox;
using Timer = System.Timers.Timer;
using System.Windows.Controls;

namespace HopiBot
{
    public partial class MainWindow
    {
        private Timer _updateTimer;
        private Thread _botThread;
        private Client _client;

        private int _previousXp = 0;
        private int _xpUntilNextLevel = 0;
        private int _earnedXp = 0;
        private static readonly IntPtr HwndTop = new IntPtr(0);
        private const uint SwpNosize = 0x0001;
        private const uint SwpNomove = 0x0002;
        private const uint SwpShowwindow = 0x0040;

        public MainWindow()
        {
            InitializeComponent();
            Init();
            UpdateInfo();
        }

        private void Init()
        {
            var hook = Hook.GlobalEvents();
            hook.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.F1)
                {
                    Stop();
                }

                if (e.KeyCode == Keys.U)
                {
                    var tracker = new MiniMapTracker();
                    tracker.Track();
                }
            };

            RefreshClientData(false);
        }

        private bool RefreshClientData(bool showMessage = true)
        {
            if (!ClientApi.CheckConnection())
            {
                if (showMessage)
                {
                    MessageBox.Show("请先打开游戏客户端");
                }
                ChampCb.ItemsSource = new List<Champion>();
                ChampCb.SelectedIndex = -1;
                return false;
            }

            var champs = ClientApi.GetAllChampions();
            ChampCb.ItemsSource = champs;
            ChampCb.SelectedItem = champs.Find(c => c.Name == "麦林炮手");

            _previousXp = ClientApi.GetMyXp();
            _xpUntilNextLevel = ClientApi.GetMyXpUntilNextLevel();
            return true;
        }

        private void GetTeamInfo(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                var scores = ScoreService.GetScores();
                Dispatcher.Invoke(() =>
                {
                    var allyInfo = scores["ally"].Select(s => $"{s.Item1}: {s.Item2}").ToList();
                    var enemyInfo = scores["enemy"].Select(s => $"{s.Item1}: {s.Item2}").ToList();
                    if (allyInfo.Count == 0 || enemyInfo.Count == 0)
                    {
                        MessageBox.Show("无法获取队伍信息");
                        return;
                    }
                    // 保留一位小数
                    TbAllyInfo.Text = string.Join("\n\n", allyInfo);
                    TbEnemyInfo.Text = string.Join("\n\n", enemyInfo);
                });
            });
        }

        private void UpdateInfo()
        {
            _updateTimer = new Timer(5000);
            _updateTimer.Elapsed += (sender, e) =>
            {
                var phase = ClientApi.GetGamePhase();
                if (phase == GamePhase.ChampSelect || phase == GamePhase.EndOfGame || phase == GamePhase.PreEndOfGame)
                {
                    var currXp = ClientApi.GetMyXp();
                    if (currXp == _previousXp) return;
                    var earnedXpThisRound = currXp - _previousXp;
                    if (currXp < _previousXp)
                    {
                        earnedXpThisRound = currXp + _xpUntilNextLevel;
                    }
                    _xpUntilNextLevel = ClientApi.GetMyXpUntilNextLevel();
                    _earnedXp += earnedXpThisRound;
                    _previousXp = currXp;
                    Dispatcher.Invoke(() =>
                    {
                        LbXpEarnedLastRound.Content = earnedXpThisRound.ToString();
                        LbXpEarnedTotal.Content = _earnedXp.ToString();
                    });
                }
                // Dispatcher.Invoke(() => GameStatusBlk.Text = phase.ToChinese());
            };
            _updateTimer.Start();
        }

        public void UpdatePlayerInfo(bool isUnderAttack, bool isLowHealth, string action, bool isDead, string position, string lastHealth)
        {
            Dispatcher.Invoke(() =>
            {
                LbUnderAttack.Content = isUnderAttack;
                LbLowHealth.Content = isLowHealth;
                LbAction.Content = action;
                LbDead.Content = isDead;
                LbChampPosition.Content = position;
                LbLastHealth.Content = lastHealth;
            });
        }

        private void StartupBtn_OnClick(object sender, RoutedEventArgs e)
        {
            if (_botThread == null)
            {
                if (!PrepareAndLaunchLeagueClient()) return;
                if (!RefreshClientData()) return;

                if (ChampCb.SelectedIndex == -1)
                {
                    MessageBox.Show("请选择英雄");
                    return;
                }

                ChampCb.IsEnabled = false;
                StartupBtn.Content = "停止";
                _botThread = new Thread(() =>
                {
                    _client = new Client(this);
                    Dispatcher.Invoke(() => { _client.Champ = (Champion)ChampCb.SelectedItem; });
                    _client.Start();
                });
                _botThread.Start();
            }
            else
            {
                Stop();
            }
        }

        public void Stop()
        {
            ChampCb.IsEnabled = true;
            StartupBtn.Content = "启动";
            _client?.Shutdown();
            _botThread?.Abort();
            _botThread?.Join();
            _botThread = null;
        }


        private bool PrepareAndLaunchLeagueClient()
        {
            var weGameWindow = FindWeGameWindow();
            if (weGameWindow == IntPtr.Zero)
            {
                MessageBox.Show("未找到 WeGame 窗口（标题: WeGame，类名: CefTopWindow）");
                return false;
            }

            if (!ShowAndFocusWindow(weGameWindow))
            {
                MessageBox.Show("无法激活 WeGame 窗口");
                return false;
            }
            Thread.Sleep(1000);

            Controller.RightClick(new RatioPoint(27, 300));
            Thread.Sleep(1000);

            if (!TryRightClickStartText(weGameWindow))
            {
                MessageBox.Show("未在指定区域识别到“启动”文字");
                return false;
            }
            Thread.Sleep(1000);

            if (!WaitForLeagueClientStart(60))
            {
                MessageBox.Show("等待英雄联盟客户端启动超时");
                return false;
            }

            Thread.Sleep(3000);
            return true;
        }

        private static IntPtr FindWeGameWindow()
        {
            var processWindow = Process.GetProcessesByName("wegame")
                                  .Concat(Process.GetProcessesByName("WeGame"))
                                  .Select(p => p.MainWindowHandle)
                                  .FirstOrDefault(h => h != IntPtr.Zero);
            if (processWindow != IntPtr.Zero)
            {
                return processWindow;
            }

            var titleAndClass = FindWindow("CefTopWindow", "WeGame");
            if (titleAndClass != IntPtr.Zero)
            {
                return titleAndClass;
            }

            var titleOnly = FindWindow(null, "WeGame");
            if (titleOnly != IntPtr.Zero)
            {
                return titleOnly;
            }

            return FindWindow("CefTopWindow", null);
        }

        private bool TryRightClickStartText(IntPtr weGameWindow)
        {
            const int searchLeft = 900;
            const int searchTop = 600;
            const int searchRight = 1280;
            const int searchBottom = 720;

            var foundPoint = System.Drawing.Point.Empty;
            EnumChildWindows(weGameWindow, (child, _) =>
            {
                var text = GetWindowTextByHandle(child);
                if (string.IsNullOrWhiteSpace(text) || !text.Contains("启动"))
                {
                    return true;
                }

                if (!GetWindowRect(child, out var rect))
                {
                    return true;
                }

                var centerX = (rect.Left + rect.Right) / 2;
                var centerY = (rect.Top + rect.Bottom) / 2;
                if (centerX < searchLeft || centerX > searchRight || centerY < searchTop || centerY > searchBottom)
                {
                    return true;
                }

                foundPoint = new System.Drawing.Point(centerX, centerY);
                return false;
            }, IntPtr.Zero);

            if (foundPoint == System.Drawing.Point.Empty)
            {
                return false;
            }

            Controller.RightClick(new RatioPoint(foundPoint.X, foundPoint.Y));
            return true;
        }

        private static bool WaitForLeagueClientStart(int timeoutSeconds)
        {
            var endTime = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < endTime)
            {
                var hasClientProcess = Process.GetProcessesByName("LeagueClient").Any()
                                       || Process.GetProcessesByName("LeagueClientUx").Any();
                if (hasClientProcess || ClientApi.CheckConnection())
                {
                    return true;
                }
                Thread.Sleep(1000);
            }

            return false;
        }

        private static bool ShowAndFocusWindow(IntPtr hWnd)
        {
            ShowWindow(hWnd, 5);
            SetWindowPos(hWnd, HwndTop, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpShowwindow);
            return SetForegroundWindow(hWnd);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out HopiBot.Hack.Utils.RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        private delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

        private static string GetWindowTextByHandle(IntPtr hWnd)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }


        #region General UI Event


        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        #endregion

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            ClientApi.CreatePracticeLobby();
        }

        private void Draw(object sender, RoutedEventArgs e)
        {
            MiniMapTracker tracker = new MiniMapTracker();
            tracker.Track();
        }

        private void TestClick(object sender, RoutedEventArgs e)
        {
            if (_client != null)
            {
                System.Windows.Forms.MessageBox.Show(_client._roundLimit.ToString());
                System.Windows.Forms.MessageBox.Show(_client._roundCount.ToString());
            }
        }

        private void TextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            System.Windows.Controls.TextBox textBox = sender as System.Windows.Controls.TextBox;
            if (textBox != null && _client != null)
            {
                _client._roundLimit = int.Parse(textBox.Text);
            }
        }
    }
}
