# ZynqRadio

**Full-duplex WSJT-X transceiver stack in C#/.NET 8 for Zynq-7000 + AD936x SDR hardware, using direct libiio and no SoapySDR.**

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue)
![Language](https://img.shields.io/badge/language-C%23-239120)
![SDR](https://img.shields.io/badge/SDR-Zynq%20%2B%20AD936x-orange)
![libiio](https://img.shields.io/badge/I%2FO-direct%20libiio-success)
![SoapySDR](https://img.shields.io/badge/SoapySDR-not%20used-lightgrey)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> **Status:** v1.0.0 baseline validated with simultaneous FT8 transmit and receive, including successful self-reception decode in a full-duplex laboratory setup.

![Successful FT8 self-decode](docs/images/ft8-self-decode.jpg)

---

## English

### What is ZynqRadio?

ZynqRadio turns a Pluto-compatible Zynq-7000 + AD936x SDR into a WSJT-X-controlled transceiver using a native Windows C#/.NET 8 application. It provides CAT, RX DSP, TX DSP, Windows audio routing, direct IIO streaming, full-duplex operation and diagnostic instrumentation in one application.

The project deliberately talks to **libiio directly**. SoapySDR is not part of the data or control path.

### Proven signal path

```text
WSJT-X
  |  Hamlib NET rigctl / CAT
  |  48 kHz audio
  v
ZynqRadio (.NET 8 / C#)
  |  RX/TX DSP
  |  direct libiio
  v
Zynq-7000 + AD936x
  |
  v
RF
```

A second WSJT-X instance was used as a self-receiver during development. The screenshot above shows successful decode of the transmitted `CQ SV1EEX KM17` frames. The 1296.174 MHz dial frequency shown in the demonstration was a development/self-test configuration, not a general band-plan recommendation.

### Why the I-only RX mode exists

The most important full-duplex engineering issue was not oscillator stability. With simultaneous full I/Q RX and I/Q TX, the host/libiio transport became the bottleneck: RX sample delivery fell below the configured rate and FT8 symbol timing was distorted.

The working solution uses an intentional **10 kHz RX LO offset** and transports only the physical **I** channel during full duplex. The wanted real-signal component is translated to baseband in DSP and the image is rejected by the receive filter. This halves RX transport bandwidth and restored stable FT8 timing and decodes in the tested setup.

### Features

- C# / .NET 8 Windows GUI
- Direct Analog Devices libiio control
- AD9363 / AD9364-class SDR support through the IIO device model
- Hamlib NET rigctl-compatible CAT server for WSJT-X
- Frequency control and CAT PTT
- Full-duplex RX and TX
- 48 kHz Windows WASAPI audio integration via NAudio
- RX LO offset and narrowband DSP
- Analytic I/Q TX generation
- Gapless FT8 TX buffering
- Low-bandwidth I-only RX transport
- Live RX/TX level and throughput diagnostics
- IIO TX deadline timing diagnostics
- Automatic diagnostic RX/TX WAV recording
- Persistent GUI settings

### Control Center

![ZynqRadio Control Center](docs/images/control-center.jpg)

### Recommended / proven configuration

| Setting | Proven baseline |
|---|---:|
| Host OS | Windows 10/11 x64 |
| Framework | .NET 8 |
| WSJT-X | 3.x; 3.0.2 used for validation |
| IIO URI | `ip:pluto.local` in the tested setup |
| IIO rate | **2,100,000 S/s** |
| RX transport | **I-only low-bandwidth mode** |
| RX LO offset | **10,000 Hz** |
| RX buffer | **32,768** |
| TX buffer | **131,072** |
| TX Q polarity | **+1** |
| TX digital level | **0.10** |

Gain values depend on the board and RF path. The development self-test used values such as -30 dB TX hardware gain and 20 dB RX gain during TX; these are **not** universal calibration settings.

See [Recommended System](docs/RECOMMENDED_SYSTEM.md) for the complete host, SDR and RF requirements.

### WSJT-X setup

The basic CAT configuration is:

```text
Rig:            Hamlib NET rigctl
Network Server: 127.0.0.1:4532
PTT Method:     CAT
```

The demonstrated Windows audio routing used:

```text
RX: ZynqRadio -> VB-CABLE -> WSJT-X input
TX: WSJT-X -> Voicemeeter Input -> B1 -> ZynqRadio
```

Full instructions: [WSJT-X Setup](docs/WSJTX_SETUP.md).

### Build from source

```powershell
git clone https://github.com/z1000biker/ZynqRadio.git
cd ZynqRadio
dotnet restore .\src\ZynqRadio\ZynqRadio.csproj
dotnet build .\src\ZynqRadio\ZynqRadio.csproj -c Release
```

The application loads `iio.dll` / `libiio.dll` dynamically. Install a Windows libiio distribution or point the `IIO_DLL` environment variable to your DLL. A PothosSDR libiio DLL location is also recognized, but **ZynqRadio does not use SoapySDR**.

### Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [WSJT-X Setup](docs/WSJTX_SETUP.md)
- [Recommended System](docs/RECOMMENDED_SYSTEM.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Third-party components](THIRD_PARTY.md)
- [Changelog](CHANGELOG.md)

### Release model

Tags matching `v*` trigger the included GitHub Actions release workflow. It builds a self-contained Windows x64 package and publishes it as a GitHub Release asset. libiio itself is intentionally not bundled; install it separately and observe its license.

### RF and regulatory notice

This software can control an RF transmitter. Use it only on frequencies and power levels permitted by your licence and local regulations. Provide appropriate RF filtering, attenuation, isolation and switching. Software full duplex is **not** a substitute for receiver protection.

---

## Ελληνικά

### Τι είναι το ZynqRadio;

Το ZynqRadio μετατρέπει ένα Pluto-compatible SDR βασισμένο σε **Zynq-7000 + AD936x** σε πομποδέκτη ελεγχόμενο από το WSJT-X μέσω μιας native εφαρμογής **C#/.NET 8 για Windows**. Ενοποιεί CAT, DSP λήψης, DSP εκπομπής, Windows audio routing, direct IIO streaming, full-duplex λειτουργία και διαγνωστικά σε μία εφαρμογή.

Η επικοινωνία με το SDR γίνεται **απευθείας μέσω libiio**. Το SoapySDR δεν συμμετέχει ούτε στο control path ούτε στο data path.

### Αποδεδειγμένη λειτουργία

Κατά την ανάπτυξη χρησιμοποιήθηκαν δύο instances του WSJT-X: το πρώτο για TX και το δεύτερο ως self-receiver. Η εικόνα στην κορυφή του README δείχνει κανονικό FT8 decode των πλαισίων `CQ SV1EEX KM17` ενώ RX και TX λειτουργούν ταυτόχρονα.

Η συχνότητα 1296.174 MHz της επίδειξης ήταν ρύθμιση development/self-test και δεν αποτελεί γενική πρόταση band plan.

### Το κρίσιμο full-duplex πρόβλημα και η λύση

Το βασικό πρόβλημα δεν ήταν η σταθερότητα του reference clock. Με ταυτόχρονο full I/Q RX και I/Q TX, το host/libiio transport δεν διατηρούσε το απαιτούμενο throughput. Η πραγματική ροή RX έπεφτε κάτω από το ρυθμισμένο sample rate και αλλοιωνόταν το symbol timing του FT8.

Η λύση της v1.0 χρησιμοποιεί **RX LO offset 10 kHz** και, σε full duplex, μεταφέρει μόνο το φυσικό **I channel**. Το επιθυμητό real-signal component μεταφέρεται στο baseband από το DSP, ενώ το image απορρίπτεται από το φίλτρο λήψης. Έτσι μειώνεται περίπου στο μισό το transport bandwidth του RX και στο δοκιμασμένο σύστημα επανήλθε σταθερό FT8 timing και κανονικό decode.

### Βασικές δυνατότητες

- Windows GUI σε C# / .NET 8
- Direct libiio, χωρίς SoapySDR
- Hamlib NET rigctl CAT για WSJT-X
- CAT tuning και PTT
- Ταυτόχρονο RX/TX
- 48 kHz WASAPI audio
- RX και TX DSP
- Gapless FT8 TX buffering
- I-only low-bandwidth RX transport
- Μετρήσεις throughput και levels
- Timing diagnostics για IIO TX buffers
- Αυτόματη καταγραφή diagnostic WAV για RX και TX
- Αποθήκευση ρυθμίσεων από το GUI

### Προτεινόμενη ρύθμιση που έχει δοκιμαστεί

| Παράμετρος | Τιμή |
|---|---:|
| Λειτουργικό | Windows 10/11 x64 |
| Framework | .NET 8 |
| WSJT-X | 3.x; validation με 3.0.2 |
| IIO sample rate | **2,100,000 S/s** |
| RX transport | **I-only low-bandwidth** |
| RX LO offset | **10,000 Hz** |
| RX buffer | **32,768** |
| TX buffer | **131,072** |
| TX Q polarity | **+1** |
| TX digital level | **0.10** |

Οι τιμές gain εξαρτώνται από το συγκεκριμένο SDR και το RF chain και δεν πρέπει να θεωρούνται calibration values.

### Ρύθμιση WSJT-X

```text
Rig:            Hamlib NET rigctl
Network Server: 127.0.0.1:4532
PTT Method:     CAT
```

Το audio routing που χρησιμοποιήθηκε στη δοκιμή ήταν:

```text
RX: ZynqRadio -> VB-CABLE -> WSJT-X input
TX: WSJT-X -> Voicemeeter Input -> B1 -> ZynqRadio
```

Αναλυτικά: [WSJT-X Setup](docs/WSJTX_SETUP.md).

### Build

```powershell
git clone https://github.com/z1000biker/ZynqRadio.git
cd ZynqRadio
dotnet restore .\src\ZynqRadio\ZynqRadio.csproj
dotnet build .\src\ZynqRadio\ZynqRadio.csproj -c Release
```

Απαιτείται Windows libiio runtime. Η εφαρμογή φορτώνει δυναμικά `iio.dll` ή `libiio.dll`. Το SoapySDR δεν απαιτείται από το ZynqRadio.

### Άδεια και RF χρήση

Το ZynqRadio διατίθεται με άδεια MIT. Οι βιβλιοθήκες και εφαρμογές τρίτων διατηρούν τις δικές τους άδειες.

Η εφαρμογή μπορεί να ελέγχει πραγματικό RF transmitter. Η χρήση πρέπει να γίνεται μόνο εντός των δικαιωμάτων της άδειας του χειριστή και της τοπικής νομοθεσίας, με κατάλληλα φίλτρα, attenuation, isolation και προστασία του RX front end.

---

## Project status

The current release is a **working engineering baseline**, not a claim of universal support for every AD936x image, USB/Ethernet transport or FPGA design. Hardware compatibility reports and measured pull requests are welcome.
