# FPGA-native VDL2 receiver on Zynq-7000 + AD936x

This branch starts a hardware-assisted VDL Mode 2 receive path for Zynq-7000 + AD936x boards.

## Goal

Use the programmable logic for the fixed-rate DSP work and keep protocol decoding on the ARM cores.

Initial target:

- Zynq-7010 class SoC
- AD9363 / AD9364-class RF front end
- Linux on the ARM Cortex-A9
- 136.975 MHz VDL2 channel
- dumpvdl2 2.7.0 for framing and higher-layer protocol decoding

VDL2 uses D8PSK at 10,500 symbols/s (31.5 kbit/s). dumpvdl2 processes IQ at integer multiples of 105,000 samples/s. A convenient baseline for AD936x is 2.1 MS/s, which dumpvdl2 accepts with `--oversample 20`.

## Milestone 0: software baseline

Before moving DSP into the FPGA, validate the RF and decoding chain with direct libiio capture:

```text
AD936x @ 136.975 MHz
        |
     libiio
        |
  S16_LE IQ @ 2.1 MS/s
        |
     stdout
        |
dumpvdl2 --iq-file - --sample-format S16_LE --oversample 20
```

This gives a known-good reference against which the FPGA path can be compared.

## Milestone 1: FPGA decimator

The first hardware block reduces 2.1 MS/s complex IQ to 1.05 MS/s:

```text
AD936x RX
   |
AXI-Stream IQ, 2.1 MS/s
   |
FPGA low-pass + decimate-by-2
   |
AXI DMA
   |
ARM userspace
   |
dumpvdl2 --iq-file - --sample-format S16_LE --oversample 10
```

At this stage dumpvdl2 still performs channel filtering, carrier/timing recovery, D8PSK demodulation, FEC and protocol parsing. The purpose is to prove the PL-to-PS path and halve memory/DMA traffic.

## Milestone 2: channel DDC in programmable logic

Add one or more independent NCO/mixer/filter/decimator chains so a wider AD936x capture can cover several VDL2 channels simultaneously.

```text
                  +--> DDC ch0 --> DMA0
wideband AD936x --+--> DDC ch1 --> DMA1
                  +--> DDC ch2 --> DMA2
                  +--> DDC chN --> DMAN
```

Each output is centered at one VDL2 channel and delivered at 1.05 MS/s or a lower rate compatible with the software demodulator.

## Milestone 3: move demodulation into PL

Only after the baseline is measured and bit-exact test vectors exist:

- root-raised-cosine / matched filtering
- symbol timing recovery
- differential 8-PSK phase extraction
- burst/training sequence detection
- optional soft metrics

The ARM side then receives symbols or decoded physical-layer bits instead of raw IQ.

## Division of work

### Programmable logic

Good FPGA candidates:

- NCO / complex mixer
- FIR / half-band filtering
- decimation
- per-channel DDC
- level measurement / AGC assistance
- symbol-rate matched filtering
- timing/carrier loops once validated

### ARM Cortex-A9

Keep initially in software:

- AVLC framing
- Reed-Solomon/FEC handling until FPGA demod is proven
- ACARS over AVLC
- X.25 / ISO 8208
- CLNP / COTP
- CPDLC
- ADS-C
- logging, UDP, ZeroMQ, SQLite

## Validation rule

Do not call an FPGA stage working merely because it synthesizes.

Each milestone needs comparison against the software baseline using the same captured IQ. Useful checks are:

1. sample count and rate
2. amplitude and clipping
3. spectrum before/after filtering
4. dumpvdl2 frame count
5. identical decoded AVLC payloads
6. CPU usage and DMA throughput

## Board integration

The RTL in `fpga/vdl2_frontend` is intentionally AXI-Stream based. It is meant to sit after the ADI receive path in a Vivado design and before AXI DMA.

The exact AD936x interface, clocking and DMA wiring depend on the FPGA image used by the board, so the generic DSP block is kept separate from board-specific top-level constraints.
