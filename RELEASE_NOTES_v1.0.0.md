# ZynqRadio v1.0.0

First public engineering release.

## Highlights

- End-to-end WSJT-X CAT + audio integration.
- Direct libiio control of Zynq-7000 + AD936x SDR hardware.
- Simultaneous full-duplex RX and TX.
- Successful FT8 self-reception decode demonstrated at 1296.174 MHz in the development setup.
- Low-bandwidth I-only RX transport solves the host throughput bottleneck observed with simultaneous full I/Q RX and TX.
- Gapless FT8 TX audio buffering and tail-drain logic.
- GUI diagnostics, TX IIO timing, audio meters, log export and automatic diagnostic WAV capture.

## Recommended baseline

Use the settings documented in `docs/RECOMMENDED_SYSTEM.md`. In particular, the validated full-duplex baseline uses 2.1 MS/s, 10 kHz RX LO offset, I-only RX transport, 32,768 RX buffer and 131,072 TX buffer.

## Runtime dependency

A Windows libiio runtime is required. It is not bundled in the release package.

## Safety

Use adequate RF isolation and receiver protection. Operate only within your licence and applicable regulations.
