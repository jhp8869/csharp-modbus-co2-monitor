using System;
using System.IO.Ports;
using System.Windows;
using System.Windows.Media;

namespace SenseairK30App
{
    public partial class MainWindow : Window
    {
        private SenseairK30 _sensor;
        private bool        _isConnected = false;

        public MainWindow()
        {
            InitializeComponent();
            RefreshPorts();
        }

        // ── 포트 목록 새로고침 ────────────────────────────────────────────
        private void RefreshPorts()
        {
            CmbPort.Items.Clear();
            foreach (var p in SerialPort.GetPortNames())
                CmbPort.Items.Add(p);
            if (CmbPort.Items.Count > 0)
                CmbPort.SelectedIndex = 0;
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
            => RefreshPorts();

        // ── 연결 / 해제 ───────────────────────────────────────────────────
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
                Disconnect();
            else
            {
                if (CmbPort.SelectedItem == null)
                {
                    MessageBox.Show("포트를 선택하세요.", "알림",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Connect(CmbPort.SelectedItem.ToString());
            }
        }

        private void Connect(string portName)
        {
            try
            {
                _sensor = new SenseairK30(SenseairK30.ADDR_ANY_SENSOR);
                _sensor.DataReceived  += OnDataReceived;
                _sensor.ErrorOccurred += OnError;
                _sensor.Open(portName);

                _isConnected             = true;
                BtnConnect.Content       = "해제";
                BtnConnect.Background    = new SolidColorBrush(Colors.OrangeRed);
                TxtConnectionStatus.Text = $"연결됨: {portName}  |  9600 / 8 / N / 1";
                AddLog($"[연결] {portName} 오픈 성공");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"연결 실패:\n{ex.Message}", "오류",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Disconnect()
        {
            _sensor?.Dispose();
            _sensor      = null;
            _isConnected = false;

            BtnConnect.Content       = "연결";
            BtnConnect.Background    = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            TxtConnectionStatus.Text = "연결 안됨";
            TxtCO2.Text              = "---";
            TxtCO2.Foreground        = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
            TxtStatus.Text           = "---";
            TxtStatusDetail.Text     = "";
            AddLog("[해제] 포트 닫힘");
        }

        // ── 데이터 수신 이벤트 ────────────────────────────────────────────
        private void OnDataReceived(object sender, Co2ReadEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                TxtCO2.Text = e.Ppm.ToString();

                // CO₂ 레벨에 따른 색상 (PSP0110 알람 기준 참고)
                if (e.Ppm < 800)
                    TxtCO2.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                else if (e.Ppm < 1000)
                    TxtCO2.Foreground = new SolidColorBrush(Colors.Orange);
                else
                    TxtCO2.Foreground = new SolidColorBrush(Colors.Red);

                // 센서 상태 (§3 Table3 IR1 MeterStatus)
                if (e.Status == 0)
                {
                    TxtStatus.Text       = "✓ OK";
                    TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                    TxtStatusDetail.Text = "";
                }
                else
                {
                    TxtStatus.Text       = $"⚠ 0x{e.Status:X4}";
                    TxtStatus.Foreground = new SolidColorBrush(Colors.OrangeRed);
                    TxtStatusDetail.Text = DecodeStatus(e.Status);
                }

                AddLog($"[{DateTime.Now:HH:mm:ss}]  CO₂: {e.Ppm,5} ppm  |  Status: 0x{e.Status:X4}");
            });
        }

        private void OnError(object sender, string msg)
        {
            Dispatcher.Invoke(() => AddLog($"[오류] {msg}"));
        }

        // ── 상태 비트 디코딩 (§3 Table3 IR1) ────────────────────────────
        private string DecodeStatus(ushort status)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((status & 0x0001) != 0) parts.Add("Fatal error");
            if ((status & 0x0002) != 0) parts.Add("Offset regulation error");
            if ((status & 0x0004) != 0) parts.Add("Algorithm error");
            if ((status & 0x0008) != 0) parts.Add("Output error");
            if ((status & 0x0010) != 0) parts.Add("Self-diagnostics error");
            if ((status & 0x0020) != 0) parts.Add("Out of range");
            if ((status & 0x0040) != 0) parts.Add("Memory error");
            return string.Join(", ", parts);
        }

        // ── 로그 추가 (최대 200줄) ────────────────────────────────────────
        private void AddLog(string msg)
        {
            LstLog.Items.Add(msg);
            if (LstLog.Items.Count > 200)
                LstLog.Items.RemoveAt(0);
            LstLog.ScrollIntoView(LstLog.Items[LstLog.Items.Count - 1]);
        }

        private void Window_Closing(object sender,
                                    System.ComponentModel.CancelEventArgs e)
            => _sensor?.Dispose();
    }
}
