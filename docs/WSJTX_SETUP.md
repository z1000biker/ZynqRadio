# WSJT-X setup

## Radio

Use:

- **Rig:** Hamlib NET rigctl
- **Network Server:** `127.0.0.1:4532`
- **PTT Method:** CAT
- **Mode:** None or USB according to your WSJT-X workflow
- **Split Operation:** None for the demonstrated baseline

## Audio routing used for the full-duplex demonstration

### Receive

`ZynqRadio -> VB-CABLE playback -> CABLE Output -> WSJT-X Input`

### Transmit

`WSJT-X Output -> Voicemeeter Input -> B1 -> Voicemeeter Out B1 -> ZynqRadio`

Set virtual devices to 48 kHz where possible. The tested B1 capture endpoint appeared as 32-bit IEEE float, 48 kHz, stereo; ZynqRadio downmixes the channels to mono before TX DSP.

## Self-reception test

For development, a second WSJT-X instance can monitor the RX audio while the first instance transmits. Disable TX in the second instance. Use adequate RF isolation/attenuation between TX and RX signal paths.
