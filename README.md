# Mau SysInfo

A Windows system information tool in the spirit of CPU-Z, built with C# / .NET 10 + WPF (Fluent theme).
Part of the Mau Tools family, next to Mau Uninstaller. Official downloads: https://downloads.kinkuroshideout.com

Runs as the normal user and installs no driver. *Run as administrator* adds drive temperature, wear and
power-on hours.

## Pages

**CPU**
- Name (from CPUID), vendor, codename, microarchitecture and process node (lookup table for Intel Sandy Bridge →
  Panther Lake and AMD Zen → Zen 5; unknown chips just leave these lines out rather than guess)
- Family / model / stepping, raw CPUID signature, loaded microcode revision, socket
- Instruction sets decoded from CPUID (SSE…AVX-512, AVX-VNNI, AMX, SHA, VT-x / AMD-V, …)
- Cores, threads, SMT, sockets, NUMA nodes; hybrid CPUs show the performance / efficiency core split
- Caches per level with size, associativity, line size and how many threads share each one
- **Live**: average effective clock and usage, plus a tile per thread with its own clock and load
  (P / E badge on hybrid CPUs). Clocks use the `% Processor Performance` counter × base clock, the same
  method as Task Manager.

**Mainboard** — board maker, model, revision, serial; BIOS/UEFI vendor, version and date; UEFI vs legacy,
Secure Boot, SMBIOS version; computer maker / model / family / chassis type.

**Memory** — type (DDR3/4/5, LPDDR), installed vs usable, speed (MT/s and DRAM clock), slots used, channels
(estimated from slot labels), maximum supported, live usage and commit. Flags memory running at the plain
JEDEC speed (XMP / EXPO probably off). One card per module: size, form factor, maker (decoded from JEDEC IDs
or the part number when firmware says "Unknown"), part and serial number, rated and running speed, voltage,
ranks, ECC.

**Graphics** — each adapter with its real dedicated memory (from the driver's registry entry; WMI caps it at
4 GB), driver version (with NVIDIA's own version number), driver date and PCI IDs. Active displays with mode
and refresh rate, and monitors from their EDID (name, size, year).

**Storage** — every physical disk: model, SSD/HDD/NVMe, interface, capacity (box size and Windows size),
firmware, serial, health, partition style, and the volumes on it with free space. As administrator also
temperature, wear (% of rated life), power-on hours, power cycles and read/write errors.

**Network** — connected adapters with type, link speed, MAC, IPv4/IPv6, gateway, DNS and DHCP; disconnected
ones listed below. Windows' internal filter/miniport pseudo-adapters are hidden.

**System** — Windows edition (correctly says Windows 11, which the registry doesn't), version, build, install
date, live uptime, computer name, workgroup/domain, TPM version, VBS, memory integrity, Credential Guard,
battery (laptops).

**Benchmark** — 5 s single-thread and 5 s all-thread run of a fixed floating-point + integer workload, and
the multi-thread scaling ratio. Scores only compare with other Mau SysInfo results. *Stress test* loads
every thread until stopped; watch the CPU page meanwhile.

**Reports** — *Save report…* (Ctrl+S) writes every page to a .txt; *Copy report* (Ctrl+Shift+C) puts it on
the clipboard for forum posts and support tickets. Right-click any line to copy its value.

**Status line and updates** (bottom of the window). The left side says when the hardware was read. The right side
shows the update status: at startup the app reads `https://downloads.kinkuroshideout.com/mau-sysinfo/update.json`
and shows *Version X · up to date*, *Version Y is available* (hover for what's new) or *Couldn't check for
updates* (hover for why, *Try again*). Right-click it for *Check for updates now* and *Check when Mau SysInfo
starts* (on by default, saved in `%LocalAppData%\MauSysInfo\settings.json`). The request sends only the app name
and version. *Update now* (installed copies) then:
1. downloads the installer into `%TEMP%\MauSysInfo-Update\`, refusing anything that isn't HTTPS from the
   official site, an `.exe`, and an exact match for the SHA-256 in `update.json`;
2. once releases are code-signed, also requires the same signer as the running copy;
3. runs it silently (`/SILENT /RELAUNCH=1 /CLOSEAPPLICATIONS`) for the same install type as the current copy:
   `/ALLUSERS` for Program Files (one UAC prompt) or `/CURRENTUSER` for a per-user install (no prompt). The app
   closes, Setup replaces it, and starts it again.

A portable or development copy offers *Download* instead, which opens the site.

## Build

```bash
dotnet build
```

```bash
dotnet test
```

The test run includes a smoke test that runs every collector on your machine and writes the full report to
`%TEMP%\MauSysInfo-smoke-report.txt`.

Single self-contained exe (no .NET install needed), in `publish\`:

```bash
dotnet publish src/MauSysInfo -p:PublishProfile=SingleFile
```

Installer (`publish\MauSysInfo-<version>-Setup.exe`, needs Inno Setup 6: `winget install JRSoftware.InnoSetup`):

```powershell
.\build-installer.ps1
```

The version comes from `<Version>` in `src\MauSysInfo\MauSysInfo.csproj`.

**Releasing an update:** bump `<Version>` in the `.csproj`, run `.\build-installer.ps1 -Notes "What's new"`,
then upload `publish\MauSysInfo-<version>-Setup.exe` and `publish\update.json` to
`https://downloads.kinkuroshideout.com/mau-sysinfo/`, the installer first and `update.json` last (installed
copies offer the update as soon as `update.json` changes). If the folder on the site changes, update both
`UpdateService.ReleaseFolder` and `$releaseFolder` in the script.

The installer's wizard pictures in `installer\wizard\` are made from `installer\art\mascot.png`. After changing
the artwork, regenerate them:

```powershell
.\installer\make-wizard-images.ps1 -Source path\to\new-mascot.png
```

## Icon

`src\MauSysInfo\Assets\app.ico` (16–256 px) is the exe, window, sidebar, About and setup icon. Replace the file
and build; it's an embedded resource, so a normal incremental build picks up the new one.

## Layout

- `Services\*Collector.cs` — one per page; each returns cards of label/value lines (`PageData`)
- `Services\Cpuid.cs`, `CpuTopology.cs`, `CpuNames.cs` — CPUID decoding, cores/caches from
  `GetLogicalProcessorInformationEx`, codename table (all pure and unit-tested)
- `Services\LiveMonitor.cs` — once-a-second clocks, usage, memory and uptime
- `Services\Benchmark.cs`, `ReportWriter.cs`
- `Controls\MasonryPanel.cs` — card layout that fills the shortest column first

## Not (yet) covered

CPU temperature, voltages, fan speeds, bus clock and memory timings need a kernel driver to read hardware
registers / the SMBus; this app deliberately doesn't install one.

## License

MIT — see [LICENSE](LICENSE). The mascot artwork (`installer\art\`, `installer\wizard\`, `Assets\app.ico`) is
included under the same terms.
