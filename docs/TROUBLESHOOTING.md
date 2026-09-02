# Troubleshooting

## CAT does not connect

Confirm WSJT-X is configured for Hamlib NET rigctl at `127.0.0.1:4532`, and verify no other process is already using the port.

## RX waterfall is alive but no decode occurs during full duplex

Enable **Low-bandwidth RX transport (I only)** and use the proven 2.1 MS/s baseline. During development, full I/Q RX plus simultaneous TX saturated the host transport sufficiently to compress RX sample timing and destroy FT8 symbol timing.

## `filter_fir_en` / low sample-rate failure

Some AD936x firmware builds do not expose the FIR-enable attribute expected by generic low-rate code. The proven v1.0 baseline deliberately avoids the experimental 1.05 MS/s path and uses 2.1 MS/s.

## TX is visible but jagged

Check Diagnostics for TX FIFO starvation, IIO deadline misses and RX sample rate. The diagnostic WAV files in `%LOCALAPPDATA%\ZynqRadio\logs` can separate Windows-audio problems from RF/IIO transport problems.

## `iio.dll` cannot be found

Install a Windows libiio distribution or set the `IIO_DLL` environment variable to the exact DLL path. ZynqRadio also checks common application and PothosSDR locations.
