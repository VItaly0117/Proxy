# Universal High-Performance SOCKS5 Proxy Server

A lightweight, cross-platform, high-performance SOCKS5 proxy server built natively in .NET 8. 

Designed for raw speed and minimal resource consumption, this proxy engine stands out by providing high throughput and low latency. Whether you're running it locally or deploying it on Linux/Cloud environments (like e2-micro instances), it serves as a robust and general-purpose SOCKS5 daemon perfectly suited for systemd integration.

## Core Features

- **Full SOCKS5 Protocol Support:** Fully compliant with RFC 1928, accommodating a wide range of connection types.
- **Mandatory Authentication:** Enforces secure username/password authentication in compliance with RFC 1929.
- **High-Performance Asynchronous I/O:** Leverages modern `.NET 8` `System.Net.Sockets` and `System.Threading.Channels` for seamless, non-blocking asynchronous traffic relay logic.
- **Minimal Memory Footprint:** Highly optimized memory allocation using `ArrayPool<byte>` ensures the daemon runs flawlessly on entry-level cloud VMs, such as Google Cloud `e2-micro` instances.
- **Headless Daemon Design:** Built from the ground up for unattended operations, making it an ideal candidate for background services and Linux `systemd` integration.

## Compatibility

This proxy server is not limited to Telegram; it is a general-purpose engine compatible with any standard SOCKS5 client, including:

- **Web Browsers:** Google Chrome, Mozilla Firefox (via extensions like FoxyProxy or native system proxy settings).
- **Messengers:** Telegram, Discord, and other chat applications with proxy support.
- **CLI Tools:** Network utilities like `curl`, `wget`, `git`, and `ssh`.
- **Streaming Apps:** Spotify and other streaming media clients.

## Benchmarking

This engine is built for scale. Real-world benchmarking on **GCP Frankfurt nodes** demonstrated exceptional performance, reliably sustaining:

- **800+ Mbps** bandwidth
- **Sub-20ms** added latency

## Quick Start

### 1. Configuration (`AppSettings.json`)

To configure the proxy server, create an `AppSettings.json` file in the launch directory. The structure maps directly to the proxy's core configuration model:

```json
{
  "LocalPort": 8080,
  "ActiveDcName": "DC2",
  "SocksUsername": "admin",
  "SocksPassword": "TgProxy2026!",
  "Datacenters": [
    { "Name": "DC1", "Ip": "149.154.175.50", "Port": 443 },
    { "Name": "DC2", "Ip": "149.154.167.51", "Port": 443 },
    { "Name": "DC3", "Ip": "149.154.175.100", "Port": 443 },
    { "Name": "DC4", "Ip": "149.154.167.91", "Port": 443 },
    { "Name": "DC5", "Ip": "91.108.56.110", "Port": 443 }
  ],
  "UseCloudflare": false
}
```

### 2. Deployment (Ubuntu 24.04 `systemd` Guide)

We recommend deploying the server via `systemd` for automatic startups and crash restarts.

1. **Publish the .NET 8 App:**
   ```bash
   dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
   ```

2. **Copy the binary & configuration:**
   Move the generated binary and `AppSettings.json` to `/opt/socks5-proxy/`.

3. **Create the service file:**
   Create a new systemd file `/etc/systemd/system/socks5-proxy.service`:

   ```ini
   [Unit]
   Description=Universal High-Performance SOCKS5 Proxy Server
   After=network.target

   [Service]
   Type=simple
   User=root
   WorkingDirectory=/opt/socks5-proxy
   ExecStart=/opt/socks5-proxy/TelegramProxy.Daemon
   Restart=always
   RestartSec=5
   Environment=ASPNETCORE_ENVIRONMENT=Production

   [Install]
   WantedBy=multi-user.target
   ```

4. **Enable and start the service:**
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable socks5-proxy
   sudo systemctl start socks5-proxy
   sudo systemctl status socks5-proxy
   ```

### 3. Firewall Configuration

**Important:** Do not forget to open the configured `LocalPort` (default is `8080`) on your firewall.
For UFW (Ubuntu Default):
```bash
sudo ufw allow 8080/tcp
```
If deploying on GCP/AWS/Azure, ensure your VPC network instances also have the inbound TCP port `8080` enabled.
