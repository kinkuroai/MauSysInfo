# Mau SysInfo — gaps to work on

Written 2026-11-16 after v0.1.0. Ordered by priority inside each section. Measured on the dev PC
(Ryzen 9 5900X, 24 threads): main window in ~0.1 s, idle CPU about 2 % of one core (the once-a-second live
updates), 245 MB of RAM in use.

## 1. Correctness: likely wrong on some PCs

1. **Per-thread clocks on Intel hybrid CPUs (12th gen and later).** Every thread's clock is
   `% Processor Performance` × one base clock (the P-core base from WMI). The percentage is probably relative
   to each core's *own* base clock, so E-core clocks would come out wrong. Fix: also read the per-instance
   `Processor Frequency` counter and multiply by that. Needs testing on an Intel hybrid machine.
2. **Admin-only drive details never tested.** Temperature, wear, power-on hours and errors
   (`MSFT_StorageReliabilityCounter`) were written but never run elevated. Click *Run as administrator* and
   check the Storage page.
3. **Virtual machine vs Hyper-V host.** "Virtualization: In use (a hypervisor is running)" shows both on a
   PC with Hyper-V/WSL and *inside* a VM. Read the hypervisor vendor (CPUID 0x40000000: "Microsoft Hv",
   "VMwareVMware", "KVMKVMKVM"…) and the computer model ("Virtual Machine") to say which.
4. **Codename table accuracy.** Unsure about a few entries: Arrow Lake-H `0xC5`, Arrow Lake-U `0xB5`,
   Raptor Lake `0xBF`, Strix Halo / Krackan. Missing: Nova Lake, Zen 6, Intel's new family numbers (18/19).
   Check them against a reliable list.
5. **Two identical GPUs** would both get the first card's memory size (the lookup matches by device ID).
   Match by the driver's registry key instead.
6. **Thread numbering on >64-thread PCs** (processor groups): tiles are numbered `group × 64 + number`, so
   a 2 × 48-thread server shows CPU 0–47 then CPU 64–111.
7. **Non-English Windows.** Live clocks/usage use English performance counter names. Untested on a
   non-English Windows; the counters may not be found there (clocks would then just show "—").
8. **ARM64 (Snapdragon) Windows.** No CPUID there; the CPU page falls back to WMI. Never run on ARM.

## 2. Missing features: high value

1. **GPU live data.** GPU usage and video memory in use from the `GPU Engine` / `GPU Adapter Memory`
   counters (any vendor, no admin). For NVIDIA also temperature, clocks, fan and power through `nvml.dll`,
   which ships with the driver (no extra driver needed).
2. **PCIe link speed and width** for the graphics card and NVMe drives, as CPU-Z shows
   ("PCIe x16 4.0 @ x16 4.0"). The device properties `CurrentLinkSpeed`/`Width` and `MaxLinkSpeed`/`Width`
   are readable without admin. Catches a card running at x8 or a drive in a slow slot.
3. **Chipset name** on the Mainboard page (CPU-Z has it). Identify it from the PCI IDs of the chipset
   devices with a small lookup table.
4. **Privacy option for reports.** Reports include serial numbers, MAC and IP addresses, and the computer and
   user name. Add "Hide personal details" (on by default for *Copy report*), since the point is posting it
   in forums.
5. **Memory details from raw SMBIOS** (`GetSystemFirmwareTable('RSMB')`, no admin): more reliable maker IDs,
   ranks, and memory type when WMI says "Unknown". Also warn when the modules don't match (different part
   numbers or sizes).
6. **Wi-Fi details:** network name, signal strength, band/channel, link rate (Native Wi-Fi API). On
   Windows 11 24H2+ the network name needs location permission; show why it's missing if so.
7. **Battery health on laptops:** design vs current full-charge capacity (wear %), cycle count
   (`BatteryStaticData` / `BatteryFullChargedCapacity` in root\wmi).
8. **Monitor ↔ output mapping:** monitors and active displays are listed separately; link each monitor to its
   output and show HDR on/off, actual colour depth and scaling (DPI %).
9. **Temperatures without a driver:** an ACPI thermal zone (`Thermal Zone Information` counter) is available
   on many PCs but is often the motherboard, not the CPU. Only add it if clearly labelled. Real CPU
   temperature, voltages, fans and memory timings need a kernel driver; we decided not to ship one.

## 3. Polish

1. **Light theme not checked.** Everything was looked at in dark mode only.
2. **Remember window size, position and last page** between runs.
3. **Memory page card order** zigzags (DIMM 0 on the right, DIMM 1 on the left) because the layout fills the
   shortest column. Keep reading order left-to-right, top-to-bottom.
4. **Benchmark:** keep a history of runs to compare against, let the stress test stop on its own after
   a chosen time, show clocks/usage on the Benchmark page while it runs.
5. **Sidebar logo** is small (28 px) for the detailed mascot; try 36–40 px.
6. **Memory use:** 245 MB is a lot for an info tool. Check how much is WPF itself and whether the cards could be
   created only when their page is first opened.
7. **Idle CPU** (2 % of one core): pause live updates while the window is minimized or on a page with
   no live values.
8. **HTML report** alongside .txt, and a "Copy page" button per page.
9. **Error log:** when a page fails it shows a message but nothing is logged; add a log file for bug reports.
10. **Keyboard and screen reader check** (Tab order, the thread tiles, AutomationProperties on cards).

## 4. Testing and distribution

1. **Test on other hardware:** an Intel hybrid desktop, a laptop (battery, Wi-Fi, iGPU + dGPU), a VM, a
   PC with several drives/GPUs. Save each smoke report (`dotnet test` writes
   `%TEMP%\MauSysInfo-smoke-report.txt`) to compare.
2. **Click through the installer:** per-user install option, upgrade over an existing install, uninstall,
   the "close the app first" prompt.
3. **Code signing:** the exe and setup are unsigned, so Windows SmartScreen will warn people who download
   them. Same for Mau Uninstaller. (The updater already checks the signer once releases are signed.)
4. ~~**Update check**~~ — done 2026-11-17 (status line + in-app updates). Still to do: upload the first
   `update.json` to `/mau-sysinfo/` on the site, and run one real update end to end (install 0.1.0, publish a
   0.1.1, click *Update now*).
