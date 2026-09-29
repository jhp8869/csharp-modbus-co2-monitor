using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace SenseairK30App
{
    public class Co2ReadEventArgs : EventArgs
    {
        public int Ppm { get; }
        public ushort Status { get; }
        public Co2ReadEventArgs(int ppm, ushort status) { Ppm = ppm; Status = status; }
    }

    public class SenseairK30 : IDisposable
    {
        // §4.2: 0xFE = "Any sensor" (단일 센서 환경 전용)
        public const byte ADDR_ANY_SENSOR = 0xFE;

        // §3 Table3: IR4 = Space CO2, register address = 3 (= IR# - 1)
        private const ushort REG_STATUS     = 0x0000; // IR1 MeterStatus
        private const ushort REG_CO2        = 0x0003; // IR4 Space CO2
        // §5.4: Read Input Registers
        private const byte   FC_READ_INPUT  = 0x04;
        // §5 Bus timing: 최대 180ms
        private const int    RESPONSE_TIMEOUT_MS = 200;

        private SerialPort              _port;
        private CancellationTokenSource _cts;
        private readonly byte           _slaveAddr;

        public event EventHandler<Co2ReadEventArgs> DataReceived;
        public event EventHandler<string>           ErrorOccurred;

        public bool IsOpen => _port?.IsOpen == true;

        public SenseairK30(byte slaveAddr = ADDR_ANY_SENSOR)
        {
            _slaveAddr = slaveAddr;
        }

        // ── 포트 열기 ────────────────────────────────────────────────────
        // §2.1, §2.2: 9600 / 8 / None / 1
        public void Open(string portName)
        {
            _port = new SerialPort(portName)
            {
                BaudRate     = 9600,
                DataBits     = 8,
                Parity       = Parity.None,   // §2.1 Table2: 센서 기본값 No parity
                StopBits     = StopBits.One,
                ReadTimeout  = RESPONSE_TIMEOUT_MS,
                WriteTimeout = 500
            };
            _port.Open();

            _cts = new CancellationTokenSource();
            Task.Run(() => PollLoop(_cts.Token));
        }

        public void Close()
        {
            _cts?.Cancel();
            _port?.Close();
        }

        // ── 폴링 루프 (2초마다 — §1.1 권장) ─────────────────────────────
        private async Task PollLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    ReadStatusAndCO2();
                }
                catch (Exception ex)
                {
                    OnError(ex.Message);
                }
                try { await Task.Delay(2000, ct); } catch { }
            }
        }

        // ── Status + CO2 동시 읽기 (Appendix A §16 방식) ─────────────────
        // IR1~IR4 한번에: startAddr=0x0000, quantity=4
        // TX: FE 04 00 00 00 04 E5 C6
        private void ReadStatusAndCO2()
        {
            byte[] request = BuildReadRequest(REG_STATUS, 4);
            _port.DiscardInBuffer();
            _port.Write(request, 0, request.Length);

            // 응답: addr(1)+fc(1)+bytecount(1)+data(8)+crc(2) = 13바이트
            byte[] response = ReadBytes(13);
            if (response == null) { OnError("응답 타임아웃"); return; }

            if (!ParseInputRegResponse(response, FC_READ_INPUT, out List<ushort> values))
            {
                OnError("파싱 실패 또는 CRC 오류");
                return;
            }

            ushort status = values[0]; // IR1 MeterStatus
            ushort co2    = values[3]; // IR4 Space CO2 (ppm)

            DataReceived?.Invoke(this, new Co2ReadEventArgs((int)co2, status));
        }

        // ── 지정 바이트 수 수신 ───────────────────────────────────────────
        private byte[] ReadBytes(int count)
        {
            byte[]   buf      = new byte[count];
            int      received = 0;
            DateTime deadline = DateTime.Now.AddMilliseconds(RESPONSE_TIMEOUT_MS);

            while (received < count && DateTime.Now < deadline)
            {
                if (_port.BytesToRead > 0)
                    received += _port.Read(buf, received, count - received);
                else
                    Thread.Sleep(5);
            }
            return received == count ? buf : null;
        }

        // ── Modbus Read Input Register 요청 프레임 생성 ───────────────────
        private byte[] BuildReadRequest(ushort startAddr, ushort quantity)
        {
            byte[] pdu = new byte[]
            {
                _slaveAddr,
                FC_READ_INPUT,
                (byte)(startAddr >> 8),
                (byte)(startAddr & 0xFF),
                (byte)(quantity  >> 8),
                (byte)(quantity  & 0xFF)
            };

            ushort crc = Crc16(pdu);
            // 문서 전체 예제: CRC low byte first
            return new byte[]
            {
                pdu[0], pdu[1], pdu[2], pdu[3], pdu[4], pdu[5],
                (byte)(crc & 0xFF), (byte)(crc >> 8)
            };
        }

        // ── 응답 파싱 ─────────────────────────────────────────────────────
        private bool ParseInputRegResponse(byte[] frame, byte expectedFC,
                                           out List<ushort> values)
        {
            values = new List<ushort>();
            if (frame.Length < 5) return false;

            byte addr      = frame[0];
            byte fc        = frame[1];
            byte byteCount = frame[2];

            // Exception response 확인
            if (fc == (byte)(expectedFC | 0x80))
            {
                OnError($"Exception response code: 0x{frame[2]:X2}");
                return false;
            }
            if (fc != expectedFC || addr != _slaveAddr) return false;

            int totalLen = 1 + 1 + 1 + byteCount + 2;
            if (frame.Length < totalLen) return false;

            // CRC 검증
            byte[] dataForCrc = new byte[totalLen - 2];
            Array.Copy(frame, dataForCrc, totalLen - 2);
            ushort calcCrc  = Crc16(dataForCrc);
            ushort frameCrc = (ushort)(frame[totalLen - 2] | (frame[totalLen - 1] << 8));

            if (calcCrc != frameCrc)
            {
                OnError($"CRC 불일치: calc=0x{calcCrc:X4} recv=0x{frameCrc:X4}");
                return false;
            }

            // 레지스터 값 추출 (big-endian)
            for (int i = 0; i < byteCount; i += 2)
                values.Add((ushort)((frame[3 + i] << 8) | frame[3 + i + 1]));

            return true;
        }

        // ── CRC16 Modbus ──────────────────────────────────────────────────
        private static ushort Crc16(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0
                        ? (ushort)((crc >> 1) ^ 0xA001)
                        : (ushort)(crc >> 1);
            }
            return crc;
        }

        private void OnError(string msg) =>
            ErrorOccurred?.Invoke(this, msg);

        public void Dispose() => Close();
    }
}
