# AnomalyDetectionService — Message Processing and Anomaly Detection Service

A .NET Web Application that consumes server statistics from a RabbitMQ message queue, persists the data to MongoDB, and sends real-time alerts via SignalR when anomalies or high usage is detected.

---

## Table of Contents

- [Overview](#overview)
- [Project Type](#project-type)
- [Project Structure](#project-structure)
- [How It Works](#how-it-works)
- [Alert Logic](#alert-logic)
- [Prerequisites](#prerequisites)
- [NuGet Packages](#nuget-packages)
- [Configuration](#configuration)
- [MongoDB Setup](#mongodb-setup)
- [RabbitMQ Setup](#rabbitmq-setup)
- [Running the Project](#running-the-project)
- [Verifying SignalR Alerts](#verifying-signalr-alerts)
- [Triggering Alerts Manually](#triggering-alerts-manually)
- [Architecture & Design Decisions](#architecture--design-decisions)
- [Full Solution Structure](#full-solution-structure)

---

## Overview

AnomalyDetectionService is the second component of the Server Monitoring System. It:

- Subscribes to the RabbitMQ topic `ServerStatistics.*` to receive metrics from all monitored servers
- Deserializes each message into a `ServerStatistics` object containing memory usage, available memory, CPU usage, server identifier, and timestamp
- Persists every record to a MongoDB collection for historical tracking
- Compares each incoming record against the previous one to detect sudden spikes (anomalies)
- Evaluates each record against configurable thresholds to detect sustained high usage
- Broadcasts real-time alerts to connected browser clients via SignalR

---

## Project Type

**.NET Web Application** (`Microsoft.NET.Sdk.Web`)

The project uses the Web SDK because SignalR requires the ASP.NET Core HTTP pipeline to serve the hub endpoint. The anomaly detection logic still runs as a `BackgroundService` (hosted service) within the same process.

---

## Project Structure

```
AnomalyDetectionService/
├── Abstractions/
│   ├── IMessageConsumer.cs             # Interface for message queue abstraction
│   └── IStatisticsRepository.cs        # Interface for database abstraction
├── Configuration/
│   ├── AnomalyDetectionConfig.cs       # Strongly-typed threshold config
│   └── SignalRConfig.cs                # SignalR hub URL config
├── Models/
│   └── ServerStatistics.cs             # Data model with MongoDB annotations
├── Services/
│   ├── AnomalyDetectionWorker.cs       # Background worker (main logic)
│   └── AlertService.cs                 # Sends alerts via SignalR
├── Messaging/
│   └── RabbitMqConsumer.cs             # RabbitMQ implementation of IMessageConsumer
├── Persistence/
│   └── MongoStatisticsRepository.cs    # MongoDB implementation of IStatisticsRepository
├── Hubs/
│   └── MonitoringHub.cs                # SignalR hub endpoint
├── wwwroot/
│   └── test.html                       # Browser test page for live alerts
├── Program.cs                          # Host setup, DI registration, middleware
└── appsettings.json                    # Configuration file
```

---

## How It Works

```
RabbitMQ Queue (ServerStatistics.*)
            │
            ▼
  RabbitMqConsumer.StartConsumingAsync()
  - Deserializes JSON payload into ServerStatistics
  - Extracts ServerIdentifier from routing key
            │
            ▼
  AnomalyDetectionWorker.ProcessMessageAsync()
            │
            ├── GetLatestAsync()         ← fetch previous record from MongoDB
            ├── SaveAsync()              ← persist current record to MongoDB
            └── RunChecksAsync()
                    │
                    ├── Anomaly Check    → if spike detected
                    │                   → AlertService.SendAnomalyAlertAsync()
                    │                   → SignalR broadcast "AnomalyAlert"
                    │
                    └── High Usage Check → if threshold exceeded
                                        → AlertService.SendHighUsageAlertAsync()
                                        → SignalR broadcast "HighUsageAlert"
                                                │
                                                ▼
                                    Connected browser clients
                                    (http://localhost:5000/test.html)
```

---

## Alert Logic

All thresholds are configurable in `appsettings.json`.

### Anomaly Alerts (sudden spike detection)

Compares the current sample against the previous sample for the same server.

**Memory Usage Anomaly:**
```
if (CurrentMemoryUsage > PreviousMemoryUsage * (1 + MemoryUsageAnomalyThresholdPercentage))
```
Example: previous = 4000 MB, threshold = 0.4 → alert if current > 5600 MB

**CPU Usage Anomaly:**
```
if (CurrentCpuUsage > PreviousCpuUsage * (1 + CpuUsageAnomalyThresholdPercentage))
```
Example: previous = 40%, threshold = 0.5 → alert if current > 60%

### High Usage Alerts (sustained threshold breach)

Evaluated on every sample regardless of previous values.

**Memory High Usage:**
```
if ((CurrentMemoryUsage / (CurrentMemoryUsage + CurrentAvailableMemory)) > MemoryUsageThresholdPercentage)
```
Example: used = 15000 MB, available = 500 MB → usage = 96.8% → alert if threshold = 0.8

**CPU High Usage:**
```
if (CurrentCpuUsage > CpuUsageThresholdPercentage)
```
Example: CPU = 95% → alert if threshold = 0.9

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [RabbitMQ 3+](https://www.rabbitmq.com/install-windows.html) running on port `5672`
- [MongoDB Community Server 6+](https://www.mongodb.com/try/download/community) running on port `27017`
- [Erlang/OTP](https://www.erlang.org/downloads) (required by RabbitMQ on Windows)
- **ServerMonitor** (Task 1) running and publishing messages

---

## NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| `RabbitMQ.Client` | 6.8.1 | Connect and consume from RabbitMQ |
| `MongoDB.Driver` | 2.28.0 | Connect and persist data to MongoDB |
| `Microsoft.AspNetCore.SignalR` | 1.1.0 | Real-time alert broadcasting |

Install via CLI:

```bash
dotnet add package RabbitMQ.Client --version 6.8.1
dotnet add package MongoDB.Driver --version 2.28.0
dotnet add package Microsoft.AspNetCore.SignalR
```

The `.csproj` should look like:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="RabbitMQ.Client" Version="6.8.1" />
    <PackageReference Include="MongoDB.Driver" Version="2.28.0" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR" Version="1.1.0" />
  </ItemGroup>
</Project>
```

---

## Configuration

Edit `appsettings.json` to configure thresholds, MongoDB, RabbitMQ, and SignalR:

```json
{
  "AnomalyDetectionConfig": {
    "MemoryUsageAnomalyThresholdPercentage": 0.4,
    "CpuUsageAnomalyThresholdPercentage": 0.5,
    "MemoryUsageThresholdPercentage": 0.8,
    "CpuUsageThresholdPercentage": 0.9
  },
  "SignalRConfig": {
    "SignalRUrl": "http://localhost:5000/hubs/monitoring"
  },
  "MongoDB": {
    "ConnectionString": "mongodb://localhost:27017",
    "Database": "ServerMonitor",
    "Collection": "ServerStatistics"
  },
  "RabbitMQ": {
    "Host": "localhost"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "AnomalyDetectionService": "Debug"
    }
  }
}
```

### Threshold Reference

| Setting | Description | Default |
|---|---|---|
| `MemoryUsageAnomalyThresholdPercentage` | Alert if memory jumps more than X% above previous sample | `0.4` (40%) |
| `CpuUsageAnomalyThresholdPercentage` | Alert if CPU jumps more than X% above previous sample | `0.5` (50%) |
| `MemoryUsageThresholdPercentage` | Alert if memory usage exceeds X% of total memory | `0.8` (80%) |
| `CpuUsageThresholdPercentage` | Alert if CPU usage exceeds X% | `0.9` (90%) |

---

## MongoDB Setup

### Install MongoDB Community Server

1. Download from https://www.mongodb.com/try/download/community
2. Run the MSI installer
3. Check **Install MongoDB as a Service** during setup
4. Optionally install **MongoDB Compass** (GUI tool) to view saved data

### Verify MongoDB is Running

```powershell
Get-Service -Name MongoDB
```

If it shows **Stopped**, start it:

```powershell
Start-Service -Name MongoDB
```

### View Saved Data in MongoDB Compass

Open **MongoDB Compass** and connect to:
```
mongodb://localhost:27017
```

Navigate to **ServerMonitor → ServerStatistics** to see the persisted records.

---

## RabbitMQ Setup

RabbitMQ must already be running from the ServerMonitor setup. Verify it is still running:

```powershell
cd "C:\Program Files\RabbitMQ Server\rabbitmq_server-4.x.x\sbin"
.\rabbitmqctl.bat status
```

The service listens on port `5672`. The management UI is at **http://localhost:15672** (guest/guest).

---

## Running the Project

Both **ServerMonitor** and **AnomalyDetectionService** must be running at the same time.

### Option 1 — Run from Visual Studio (Recommended)

1. Open `ServerMonitoring.sln`
2. Right-click the Solution → **Set Startup Projects**
3. Select **Multiple startup projects**
4. Set both `ServerMonitor` and `AnomalyDetectionService` to **Start**
5. Press **F5**

### Option 2 — Run from CLI

Open two separate terminals:

Terminal 1:
```bash
cd ServerMonitor
dotnet run
```

Terminal 2:
```bash
cd AnomalyDetectionService
dotnet run
```

### Expected Console Output

```
info: AnomalyDetectionWorker[0]
      AnomalyDetectionWorker started.

info: RabbitMqConsumer[0]
      Started consuming from queue 'server.statistics.queue' with pattern 'ServerStatistics.*'.

info: MongoStatisticsRepository[0]
      MongoDB repository initialized — ServerMonitor/ServerStatistics.

dbug: MongoStatisticsRepository[0]
      Saved statistics for server 'Server123'.

warn: AlertService[0]
      High Usage Alert — Server123 | MemoryUsage: 85.30% exceeds 80.00%

warn: AlertService[0]
      Anomaly Alert — Server123 | MemoryUsage: 4200.00 → 6100.00
```

---

## Verifying SignalR Alerts

Open your browser at:

```
http://localhost:5000/test.html
```

You should see:

```
✅ Connected to SignalR hub — waiting for alerts...
```

When alerts fire they appear in real time:

```
🔴 [10:05:23] High usage on Server123: MemoryUsage at 96.8%, threshold is 80.0%
              Server: Server123 | Metric: MemoryUsage

⚠️ [10:05:45] Anomaly detected on Server123: MemoryUsage jumped from 4821.35 to 9500.00
              Server: Server123 | Metric: MemoryUsage
```

---

## Triggering Alerts Manually

Instead of waiting 60 seconds for ServerMonitor to publish, you can manually publish messages from the RabbitMQ Management UI at **http://localhost:15672**.

Go to **Exchanges → server.monitoring → Publish Message**.

### Trigger a High Usage Alert

Set routing key to `ServerStatistics.Server123` and publish:

```json
{
  "MemoryUsage": 15000.00,
  "AvailableMemory": 500.00,
  "CpuUsage": 95.00,
  "Timestamp": "2026-04-01T10:00:00Z"
}
```

### Trigger an Anomaly Alert

First publish a baseline message:

```json
{
  "MemoryUsage": 4000.00,
  "AvailableMemory": 10000.00,
  "CpuUsage": 30.00,
  "Timestamp": "2026-04-01T10:00:00Z"
}
```

Then publish a spike message:

```json
{
  "MemoryUsage": 9500.00,
  "AvailableMemory": 5000.00,
  "CpuUsage": 85.00,
  "Timestamp": "2026-04-01T10:01:00Z"
}
```

The second message exceeds the 40% anomaly threshold and will fire an AnomalyAlert.

---

## Architecture & Design Decisions

### `IMessageConsumer` Abstraction

`AnomalyDetectionWorker` only depends on `IMessageConsumer`, never on RabbitMQ directly. Swapping RabbitMQ for Azure Service Bus, Kafka, or an in-memory stub for unit testing requires only a single line change in `Program.cs`.

### `IStatisticsRepository` Abstraction

The worker only depends on `IStatisticsRepository`, never on MongoDB directly. The MongoDB implementation can be swapped for SQL Server, PostgreSQL, or an in-memory store without touching the business logic.

### `BasicQos` Prefetch

The RabbitMQ consumer sets `prefetchCount: 1` so the service processes one message at a time. This prevents the service from being overwhelmed if a large backlog builds up.

### MongoDB Index

A compound index on `ServerIdentifier + Timestamp (descending)` is created at startup. This makes `GetLatestAsync()` — which fetches the most recent record per server — fast even with millions of records.

### `Sdk.Web` vs `Sdk.Worker`

The project uses `Microsoft.NET.Sdk.Web` instead of `Microsoft.NET.Sdk.Worker` because SignalR requires the ASP.NET Core HTTP pipeline to serve the `/hubs/monitoring` endpoint. The background worker still runs via `AddHostedService` within the same process.

---

## Full Solution Structure

This service is part of a two-project solution:

```
ServerMonitoring.sln
│
├── ServerMonitor/                  ← Task 1: Collects & publishes metrics
│   └── (Worker Service)
│
└── AnomalyDetectionService/        ← Task 2: Consumes, stores & alerts
    └── (Web Application)
```

Both projects communicate exclusively through RabbitMQ — they are fully decoupled and can be deployed independently on different machines.
