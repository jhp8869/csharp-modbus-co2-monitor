# Senseair K30 CO₂ Monitor

Senseair K30 CO₂ 센서를 RS-232 / Modbus RTU 프로토콜로 읽어  
실시간 농도와 센서 상태를 표시하는 **C# WPF 데스크탑 애플리케이션**입니다.

\---

## 스크린샷

!\[Senseair K30 CO₂ Monitor](SenseairK30App/docs/screenshot.png)

\---

## 개발 환경

|항목|버전|
|-|-|
|언어|C# (.NET 8 / WPF)|
|IDE|Visual Studio 2022|
|통신|RS-232 (System.IO.Ports)|
|프로토콜|Modbus RTU (FC 04 – Read Input Registers)|
|대상 센서|Senseair K30 (UART 인터페이스 모델)|

\---

## 프로젝트 구조

```
SenseairK30App/
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml          # UI 레이아웃 (WPF)
├── MainWindow.xaml.cs       # UI 이벤트 및 표시 로직
└── SenseairK30.cs           # 센서 드라이버 (Modbus RTU 통신)
```

\---

## 통신 스펙

|항목|값|
|-|-|
|Baud Rate|9,600 bps|
|Data Bits|8|
|Parity|None|
|Stop Bits|1|
|프로토콜|Modbus RTU|
|Function Code|0x04 (Read Input Registers)|
|Slave Address|0xFE (Any Sensor)|
|폴링 주기|2초|

### 요청 프레임 예시

```
TX: FE 04 00 00 00 04 E5 C6
     │   │  └──────┘  └────── CRC16 (Low byte first)
     │   │  startAddr=0x0000, quantity=4
     │   └─ FC 0x04: Read Input Registers
     └─ SlaveAddr: 0xFE
```

### 읽는 레지스터

|레지스터|이름|설명|
|-|-|-|
|IR1 (0x0000)|MeterStatus|센서 상태 비트|
|IR4 (0x0003)|Space CO2|CO₂ 농도 (ppm)|

\---

## 주요 기능

* **실시간 CO₂ 농도 표시** — 2초 주기 폴링
* **농도 레벨별 색상 표시**

|농도|색상|의미|
|-|-|-|
|< 800 ppm|🟢 초록|양호|
|800 \~ 999 ppm|🟠 주황|주의|
|≥ 1000 ppm|🔴 빨강|위험|

* **센서 상태 비트 디코딩** (IR1 MeterStatus)

|비트|의미|
|-|-|
|0x0001|Fatal error|
|0x0002|Offset regulation error|
|0x0004|Algorithm error|
|0x0008|Output error|
|0x0010|Self-diagnostics error|
|0x0020|Out of range|
|0x0040|Memory error|

* **CRC16 Modbus**
* **포트 목록 자동 감지 및 새로고침**
* **수신 로그** 최대 200줄 표시

\---

## 실행 방법

### 요구 사항

* Windows 10 이상
* .NET 8 Runtime (또는 Visual Studio 2022)
* RS-232 포트 또는 USB-Serial 변환기

### 빌드 및 실행

```bash
# 1. 프로젝트 클론
git clone https://github.com/<your-id>/SenseairK30App.git

# 2. Visual Studio 2022로 .sln 파일 열기

# 3. 빌드 (Ctrl+Shift+B)

# 4. 실행 (F5)
```

### 센서 연결 순서

1. Senseair K30을 RS-232 케이블(또는 USB-Serial)로 PC에 연결
2. 앱 실행 후 포트 목록에서 해당 COM 포트 선택
3. **연결** 버튼 클릭
4. 2초마다 CO₂ 농도 및 센서 상태 자동 갱신

\---

## 센서 없이 테스트하려면

현재 버전은 실제 센서가 필요합니다.  
Mock 모드(시뮬레이션)는 추후 추가 예정입니다.

\---

## 참고 문서

* [Senseair K30 UART Interface Description](https://senseair.com)  
→ §2.1 Serial settings, §3 Table3 IR registers, §4.2 Slave address, §5.4 FC04

\---

## 개발 배경

C++/Qt로 구현한 경험을 바탕으로, 동일한 Modbus RTU 통신 구조를 **C# WPF** 환경에서 재구현한 포트폴리오 프로젝트입니다.

