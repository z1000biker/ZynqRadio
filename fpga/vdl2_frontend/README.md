# VDL2 FPGA front-end

This directory contains the first programmable-logic stage for the VDL2 receiver.

## Current block

`axis_vdl2_frontend.sv` accepts signed 16-bit I/Q samples over AXI-Stream and performs a decimate-by-2 low-pass stage.

Intended first operating point:

- input: 2.1 MS/s complex IQ
- output: 1.05 MS/s complex IQ
- RF center: 136.975 MHz for the single-channel baseline
- output format on the PS side: interleaved S16_LE I,Q
- dumpvdl2: `--oversample 10`

The HDL is a first integration block, not yet a claim of RF validation. It must be tested against captured IQ before moving more of the VDL2 demodulator into PL.

## AXI-Stream sample format

`s_axis_tdata[31:16]` = signed Q

`s_axis_tdata[15:0]` = signed I

The output uses the same packing.

## Next hardware steps

1. integrate with ADI RX AXI stream
2. connect output to AXI DMA
3. capture a known VDL2 burst through both software-only and FPGA paths
4. compare decoded frames
5. add NCO/DDC for multiple channels
