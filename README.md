# ServerMonitor — Server Statistics Collection Service

A .NET Worker Service that collects server performance metrics (CPU, memory) at configurable intervals and publishes them to a RabbitMQ message queue using a topic exchange.

---

## Table of Contents

- [Overview](#overview)
- [Project Type](#project-type)
- [Project Structure](#project-structure)
- [How It Works](#how-it-works)
- [Prerequisites](#prerequisites)
- [NuGet Packages](#nuget-packages)
- [Configuration](#configuration)
- [RabbitMQ Setup](#rabbitmq-setup)
- [Running the Project](#running-the-project)
- [Verifying Messages](#verifying-messages)
- [Architecture & Design Decisions](#architecture--design-decisions)

---

## Overview

ServerMonitor is a background service that:

- Collects **CPU usage**, **available memory**, and **used memory** from the host machine at regular intervals
- Wraps the collected data into a `ServerStatistics` object with a timestamp
- Publishes the object as a JSON message to RabbitMQ under the topic `ServerStatistics.<ServerIdentifier>`

---

## Project Type

**.NET Worker Service** (`Microsoft.NET.Sdk.Worker`)

The project runs as a long-running background process. It does not have a UI or HTTP endpoint. It is designed to run continuously as a Windows Service or Linux systemd daemon.

---

## Project Structure

```
ServerMonitor/
├── Abstractions/
│   └── IMessagePublisher.cs        # Interface for message queue abstraction
├── Configuration/
│   └── ServerStatisticsConfig.cs   # Strongly-typed config binding
├── Models/
│   └── ServerStatistics.cs         # Data model for collected metrics
├── Services/
│   └── ServerStatisticsService.cs  # Background worker (main logic)
├── Messaging/
│   └── RabbitMqPublisher.cs        # RabbitMQ implementation of IMessagePublisher
├── Program.cs                      # Host setup and DI registration
└── appsettings.json                # Configuration file
```

---

## How It Works

```
ServerStatisticsService (BackgroundService)
        │
        │  every SamplingIntervalSeconds
        ▼
   CollectStatistics()
   - PerformanceCounter → CPU %
   - PerformanceCounter → Available Memory (MB)
   - GC.GetGCMemoryInfo() → Total Memory (MB)
        │
        ▼
   ServerStatistics object
   { MemoryUsage, AvailableMemory, CpuUsage, Timestamp }
        │
        ▼
   IMessagePublisher.PublishAsync(topic, payload)
        │
        ▼
   RabbitMqPublisher
   - Exchange: server.monitoring (topic)
   - Routing key: ServerStatistics.<ServerIdentifier>
   - Queue: server.statistics.queue
```

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [RabbitMQ 3+](https://www.rabbitmq.com/install-windows.html) running locally
- [Erlang/OTP](https://www.erlang.org/downloads) (required by RabbitMQ on Windows)
- Windows OS (required for `System.Diagnostics.PerformanceCounter`)

---

## NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| `RabbitMQ.Client` | 6.8.1 | Connect and publish to RabbitMQ |
| `System.Diagnostics.PerformanceCounter` | 8.0.0 | Collect CPU and memory metrics |

Install via CLI:

```bash
dotnet add package RabbitMQ.Client --version 6.8.1
dotnet add package System.Diagnostics.PerformanceCounter
```

The `.csproj` should look like:

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="RabbitMQ.Client" Version="6.8.1" />
    <PackageReference Include="System.Diagnostics.PerformanceCounter" Version="8.0.0" />
  </ItemGroup>
</Project>
```

---

## Configuration

Edit `appsettings.json` to configure the sampling interval and server identifier:

```json
{
  "ServerStatisticsConfig": {
    "SamplingIntervalSeconds": 60,
    "ServerIdentifier": "Server123"
  },
  "RabbitMQ": {
    "Host": "localhost"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "ServerMonitor": "Debug"
    }
  }
}
```

| Setting | Description | Default |
|---|---|---|
| `SamplingIntervalSeconds` | How often to collect and publish metrics | `60` |
| `ServerIdentifier` | Appended to the topic name: `ServerStatistics.<id>` | `Server123` |
| `RabbitMQ:Host` | Hostname of the RabbitMQ broker | `localhost` |

---

## RabbitMQ Setup

### Option 1 — Windows Installation

1. Install [Erlang/OTP](https://www.erlang.org/downloads)
2. Install [RabbitMQ](https://www.rabbitmq.com/install-windows.html)
3. Open PowerShell **as Administrator** and navigate to the sbin folder:

```powershell
cd "C:\Program Files\RabbitMQ Server\rabbitmq_server-4.x.x\sbin"
```

4. Install and start the service:

```powershell
.\rabbitmq-service.bat install
.\rabbitmq-service.bat start
```

5. Fix Erlang cookie mismatch (if `rabbitmqctl.bat status` fails):

```powershell
Copy-Item "C:\Windows\System32\config\systemprofile\.erlang.cookie" `
          -Destination "C:\Users\<YourUsername>\.erlang.cookie" -Force
.\rabbitmq-service.bat stop
.\rabbitmq-service.bat start
```

6. Enable the Management UI:

```powershell
.\rabbitmq-plugins.bat enable rabbitmq_management
```

7. Verify RabbitMQ is running:

```powershell
.\rabbitmqctl.bat status
```

### Option 2 — Docker (Recommended)

```bash
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

### Management UI

Open **http://localhost:15672** in your browser.

- Username: `guest`
- Password: `guest`

---

## Running the Project

```bash
dotnet run
```

Or press **F5** in Visual Studio.

On startup the service will:

1. Connect to RabbitMQ on port `5672`
2. Declare the `server.monitoring` topic exchange
3. Declare and bind `server.statistics.queue` with routing key `ServerStatistics.#`
4. Begin collecting and publishing metrics every `SamplingIntervalSeconds`

---

## Verifying Messages

1. Open **http://localhost:15672**
2. Go to the **Queues** tab → click `server.statistics.queue`
3. Scroll to **Get Messages** → click **Get Message(s)**

You should see a JSON payload like:

```json
{
  "MemoryUsage": 4821.35,
  "AvailableMemory": 11402.12,
  "CpuUsage": 3.47,
  "Timestamp": "2026-04-01T10:00:00Z"
}
```

---

## Architecture & Design Decisions

### `IMessagePublisher` Abstraction

The `ServerStatisticsService` only depends on `IMessagePublisher`, never on RabbitMQ directly. This means you can swap RabbitMQ for Azure Service Bus, Kafka, or an in-memory stub for unit testing with a single line change in `Program.cs`.

### `BackgroundService`

Inheriting from `BackgroundService` integrates with the .NET hosted service lifecycle. The service respects `CancellationToken` for graceful shutdown on `Ctrl+C` or system SIGTERM signals.

### CPU Counter Priming

`PerformanceCounter` always returns `0` on its very first read. The service primes the counter with a 1-second warm-up before entering the sampling loop, ensuring the first published value is accurate.

### Topic Exchange

Messages are published to a RabbitMQ **topic exchange** with routing key `ServerStatistics.Server123`. Consumers can subscribe to:

- `ServerStatistics.#` — receive stats from all servers
- `ServerStatistics.Server123` — receive stats from one specific server
