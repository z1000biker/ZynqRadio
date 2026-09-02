# Contributing

Contributions are welcome, especially around AD936x rate planning, FIR support, transport efficiency, additional digital modes, device compatibility and DSP validation.

Please open an issue before large architectural changes. Keep pull requests focused and include reproducible test conditions for RF/DSP changes.

## Build

```powershell
dotnet restore .\src\ZynqRadio\ZynqRadio.csproj
dotnet build .\src\ZynqRadio\ZynqRadio.csproj -c Release
```

## RF testing

Use appropriate attenuation, filtering, RF isolation and legal amateur-radio frequencies. Never rely on software alone to protect a receiver input from a transmitter.
