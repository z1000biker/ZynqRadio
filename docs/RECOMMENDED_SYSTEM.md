# Recommended system

## Host

- Windows 10 or Windows 11, x64
- .NET 8 Desktop Runtime, or a self-contained release build
- Modern dual-core CPU or better; four or more logical cores recommended for simultaneous RX/TX and diagnostics
- Stable USB or Ethernet/IP path to the SDR; avoid heavily loaded hubs or network links

## SDR

- Zynq-7000 class board with an AD9363 or AD9364-compatible IIO device model
- Pluto-compatible IIO context such as `ip:pluto.local`
- Hardware/firmware exposing `ad9361-phy`, `cf-ad9361-lpc`, and the corresponding TX DMA device

## Software

- WSJT-X 3.x (3.0.2 used during the documented validation)
- Analog Devices libiio runtime for Windows (`iio.dll` / `libiio.dll`)
- VB-Audio Virtual Cable for RX audio routing
- Voicemeeter Standard or another equivalent Windows virtual-audio route for a separate TX path

## Proven v1.0 full-duplex baseline

| Parameter | Value |
|---|---:|
| IIO sample rate | 2,100,000 S/s |
| RX transport | I-only low-bandwidth mode |
| RX LO offset | 10,000 Hz |
| RX buffer | 32,768 complex-time samples |
| TX buffer | 131,072 complex samples |
| TX Q polarity | +1 |
| TX digital level | 0.10 |
| Example TX hardware gain | -30 dB |
| Example RX gain during TX | 20 dB |

These gain values are not universal calibration values. Adjust them for your board, RF path and test setup.

## RF protection

Full duplex does not remove the need for RF isolation. Use suitable filters, attenuation, duplexing/switching hardware and power levels so the TX signal cannot damage or severely overload the RX input.
