# Who Is On My WiFi Reborn

<div align="center">

![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white)
![Language](https://img.shields.io/badge/Language-C%23%20%2F%20.NET%208-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green.svg?style=for-the-badge)
[![WhatsApp](https://img.shields.io/badge/WhatsApp-Contact%20letmese-25D366?style=for-the-badge&logo=whatsapp&logoColor=white)](https://wa.me/923171616345?text=Hi%20letmese%2C%20I%20am%20interested%20in%20WhoIsOnMyWiFi%20Reborn.)

### *A Modern C# / .NET 8 Rebuild of Who Is On My WiFi with Automated Subnet Sweeps, Device Intruder Alerts, and Windows Firewall Blocking*

</div>

---

## 📖 Overview

**Who Is On My WiFi Reborn** is a modern, high-performance ground-up rebuild of the classic network surveillance tool **Who Is On My WiFi 4.0.1**. Engineered with modern **.NET 8**, asynchronous C# architecture, and Fluent styling, it actively monitors your local wireless and wired networks, identifies every connected hardware device, and alerts you the moment an unauthorized intruder joins.

---

## ✨ Features & Architecture

| Feature | Details |
| :--- | :--- |
| **ARP / Ping Device Scanning** | Parallel ping sweep coupled with direct `GetIpNetTable` Windows API calls for zero-latency, locale-independent device discovery. |
| **NetBIOS & Reverse-DNS** | Automated UDP 137 Node Status queries and asynchronous DNS hostname resolution. |
| **MAC Vendor (Company) OUI** | Instant hardware manufacturer identification using comprehensive IEEE OUI databases. |
| **Intruder Defense & Firewall** | One-click blocking via elevated Windows Defender Firewall rules (inbound + outbound traffic isolation). |
| **Multi-Port Threat Scanner** | Fast TCP port triage (HTTP 80, HTTPS 443, Telnet 23, SSH 22, SMB 445, etc.) to detect exposed services. |
| **Live Telemetry & Signal** | Real-time bandwidth tracking, Wi-Fi 6 standard detection, signal percentage, and latency monitoring. |
| **Smart Device Classification** | Automatic categorization of devices (Laptops, Phones, Smart TVs, IoT hardware, Routers, Printers). |
| **Data Migration & Backups** | Full backward-compatibility importer for legacy `.dl2`, `manu.db`, and `.wif` database files. |

---

## 🚀 Requirements & Building

### Prerequisites
- Windows 10 or 11 (64-bit)
- **.NET 8 SDK** (for compiling from source) or **.NET 8 Desktop Runtime**

### Build Instructions
```bash
git clone https://github.com/letmese/WhoIsOnMyWiFi_Reborn.git
cd WhoIsOnMyWiFi_Reborn

# Build in Release mode
dotnet build -c Release
```

The output executable will be placed in `bin/Release/net8.0-windows/`.

To generate a completely standalone, self-contained single directory that runs on any 64-bit Windows machine without requiring .NET pre-installed:
```bash
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

---

## 📦 Pre-Built Downloads

To download the ready-to-run package (no developer tools required), check the releases:
* **[GitHub Releases](https://github.com/letmese/WhoIsOnMyWiFi_Reborn/releases)**

---

## 👨‍💻 Developer & Inquiries

Developed and maintained by **letmese**.

* **GitHub Profile:** [@letmese](https://github.com/letmese)
* **Location:** Lahore, Pakistan (Available for projects & consulting worldwide)
* **WhatsApp Direct:** [**+92 317 1616345**](https://wa.me/923171616345?text=Hi%20letmese%2C%20I%20am%20interested%20in%20WhoIsOnMyWiFi%20Reborn.)

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
