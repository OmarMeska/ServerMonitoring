# Server Monitoring System

A distributed, event-driven server health monitoring system built on .NET 10. It continuously collects server performance metrics, stores them historically, and sends real-time alerts when anomalies or dangerous usage levels are detected.

---

## Table of Contents

- [System Overview](#system-overview)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Project Structure](#project-structure)
- [Task 1 — ServerMonitor](#task-1--servermonitor)
- [Task 2 — AnomalyDetectionService](#task-2--anomalydetectionservice)
- [Running Both Services Together](#running-both-services-together)
- [Verifying the Full Flow](#verifying-the-full-flow)
- [Scaling](#scaling)

---

## System Overview

The system is composed of two independently deployable .NET services that communicate exclusively through a RabbitMQ message broker:

- **ServerMonitor** runs on each machine being monitored. It reads CPU and memory metrics from the OS and publishes them to RabbitMQ every configurable interval.
- **AnomalyDetectionService** runs on a central monitoring server. It consumes those messages, persists them to MongoDB, and sends real-time alerts via SignalR when thresholds are exceeded.

| | ServerMonitor | AnomalyDetectionService |
|---|---|---|
| Role | Data Producer | Data Consumer & Analyzer |
| Project Type | Worker Service | Web Application |
| Deployed On | Each monitored server | Central monitoring server |
| Outputs To | RabbitMQ exchange | MongoDB + SignalR clients |

---

## Architecture

```
┌─────────────────────┐         ┌─────────────────┐         ┌──────────────────────────┐
│   ServerMonitor     │         │    RabbitMQ      │         │  AnomalyDetectionService │
│  (each server)      │         │    (broker)      │         │   (central server)       │
│                     │         │                  │         │                          │
│  PerformanceCounter │──────▶  │ server.monitoring│──────▶  │  RabbitMqConsumer        │
│  collects metrics   │  topic  │ exchange         │  topic  │  deserializes message    │
│  every 60 seconds   │  msg    │                  │  msg    │         │                │
│                     │         │  server.stats    │         │         ▼                │
│  Publishes to:      │         │  .queue          │         │  SaveAsync()             │
│  ServerStatistics   │         │                  │         │  → MongoDB               │
│  .<ServerId>        │         └─────────────────┘         │         │                │
└─────────────────────┘                                      │         ▼                │
                                                             │  RunChecksAsync()        │
                                                             │  → Anomaly Alert?        │
                                                             │  → High Usage Alert?     │
                                                             │         │                │
                                                             │         ▼                │
                                                             │  SignalR Hub             │
                                                             │  /hubs/monitoring        │
                                                             └──────────────────────────┘
                                                                        │
                                                                        ▼
                                                             ┌──────────────────────────┐
                                                             │   Browser Dashboard      │
                                                             │   Real-time alerts       │
                                                             └──────────────────────────┘
```

---

## Prerequisites

Make sure all of the following are installed and running before starting:

| Requirement | Version | Purpose | Download |
|---|---|---|---|
| .NET SDK | 10.0+ | Build and run both projects | https://dotnet.microsoft.com/download |
| RabbitMQ | 3+ | Message broker between services | https://www.rabbitmq.com/install-windows.html |
| Erlang/OTP | Any recent | Required by RabbitMQ on Windows | https://www.erlang.org/downloads |
| MongoDB | 6+ | Persistent storage for metrics | https://www.mongodb.com/try/download/community |


---

## Project Structure

```
ServerMonitoring/
│
├── ServerMonitor/                          ← Task 1: Collects & publishes metrics
│   ├── Abstractions/
│   │   └── IMessagePublisher.cs            # Message queue abstraction
│   ├── Configuration/
│   │   └── ServerStatisticsConfig.cs       # Strongly-typed config binding
│   ├── Models/
│   │   └── ServerStatistics.cs             # Metrics data model
│   ├── Services/
│   │   └── ServerStatisticsService.cs      # Background worker (main logic)
│   ├── Messaging/
│   │   └── RabbitMqPublisher.cs            # RabbitMQ implementation
│   ├── Program.cs
│   ├── appsettings.json
│   └── ServerMonitor.csproj
│
├── AnomalyDetectionService/                ← Task 2: Consumes, stores & alerts
│   ├── Abstractions/
│   │   ├── IMessageConsumer.cs             # Message queue abstraction
│   │   └── IStatisticsRepository.cs        # Database abstraction
│   ├── Configuration/
│   │   ├── AnomalyDetectionConfig.cs       # Threshold config
│   │   └── SignalRConfig.cs                # SignalR hub URL config
│   ├── Models/
│   │   └── ServerStatistics.cs             # Metrics model with MongoDB annotations
│   ├── Services/
│   │   ├── AnomalyDetectionWorker.cs       # Background worker (main logic)
│   │   └── AlertService.cs                 # Sends SignalR alerts
│   ├── Messaging/
│   │   └── RabbitMqConsumer.cs             # RabbitMQ implementation
│   ├── Persistence/
│   │   └── MongoStatisticsRepository.cs    # MongoDB implementation
│   ├── Hubs/
│   │   └── MonitoringHub.cs                # SignalR hub endpoint
│   ├── wwwroot/
│   │   └── test.html                       # Browser test page for live alerts
│   ├── Program.cs
│   ├── appsettings.json
│   └── AnomalyDetectionService.csproj
│
└── README.md                              
```

---

## Task 1 — ServerMonitor

### Purpose

A .NET Worker Service that runs on each machine being monitored. It collects CPU usage, available memory, and used memory from the OS at a configurable interval and publishes the data as a JSON message to RabbitMQ.

### How It Works

```
Every SamplingIntervalSeconds:
    PerformanceCounter → CPU %, Available Memory (MB)
    GC.GetGCMemoryInfo() → Total Memory (MB)
    Used Memory = Total - Available
          │
          ▼
    ServerStatistics { MemoryUsage, AvailableMemory, CpuUsage, Timestamp }
          │
          ▼
    IMessagePublisher.PublishAsync("ServerStatistics.Server123", stats)
          │
          ▼
    RabbitMQ → server.monitoring exchange → server.statistics.queue
```

### NuGet Packages

```bash
dotnet add package RabbitMQ.Client --version 6.8.1
dotnet add package System.Diagnostics.PerformanceCounter
```

### Configuration — `ServerMonitor/appsettings.json`

```json
{
  "ServerStatisticsConfig": {
    "SamplingIntervalSeconds": 60,
    "ServerIdentifier": "Server123"
  },
  "RabbitMQ": {
    "Host": "localhost"
  }
}
```

| Setting | Description | Default |
|---|---|---|
| `SamplingIntervalSeconds` | How often metrics are collected and published | `60` |
| `ServerIdentifier` | Unique name for this server, appended to the topic | `Server123` |
| `RabbitMQ:Host` | Hostname or IP of the RabbitMQ broker | `localhost` |


---

## Task 2 — AnomalyDetectionService

### Purpose

A .NET Web Application that consumes server metrics from RabbitMQ, persists every record to MongoDB, and sends real-time alerts via SignalR when anomalies or high usage is detected.

### How It Works

```
RabbitMQ message received
        │
        ▼
Deserialize JSON → ServerStatistics object
        │
        ├── GetLatestAsync()  → fetch previous record from MongoDB
        ├── SaveAsync()       → persist current record to MongoDB
        └── RunChecksAsync()
                │
                ├── Memory Anomaly?  ──▶ SignalR "AnomalyAlert"
                ├── CPU Anomaly?     ──▶ SignalR "AnomalyAlert"
                ├── Memory High?     ──▶ SignalR "HighUsageAlert"
                └── CPU High?        ──▶ SignalR "HighUsageAlert"
```

### Alert Logic

All thresholds are configurable in `appsettings.json`. No code changes are needed to adjust sensitivity.

**Anomaly Alerts** — detect sudden spikes by comparing to the previous sample:

```
Memory Anomaly: CurrentMemoryUsage > PreviousMemoryUsage * (1 + MemoryUsageAnomalyThresholdPercentage)
CPU Anomaly:    CurrentCpuUsage    > PreviousCpuUsage    * (1 + CpuUsageAnomalyThresholdPercentage)
```

**High Usage Alerts** — detect sustained dangerous levels on every sample:

```
Memory High: (CurrentMemoryUsage / (CurrentMemoryUsage + CurrentAvailableMemory)) > MemoryUsageThresholdPercentage
CPU High:    CurrentCpuUsage > CpuUsageThresholdPercentage
```

### NuGet Packages

```bash
dotnet add package RabbitMQ.Client --version 6.8.1
dotnet add package MongoDB.Driver --version 2.28.0
dotnet add package Microsoft.AspNetCore.SignalR
```

### Configuration — `AnomalyDetectionService/appsettings.json`

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
  }
}
```

| Setting | Description | Default |
|---|---|---|
| `MemoryUsageAnomalyThresholdPercentage` | Alert if memory jumps more than X% above previous sample | `0.4` (40%) |
| `CpuUsageAnomalyThresholdPercentage` | Alert if CPU jumps more than X% above previous sample | `0.5` (50%) |
| `MemoryUsageThresholdPercentage` | Alert if memory usage exceeds X% of total memory | `0.8` (80%) |
| `CpuUsageThresholdPercentage` | Alert if CPU usage exceeds X% | `0.9` (90%) |

---

## Running Both Services Together

Both services must be running simultaneously along with RabbitMQ and MongoDB.

### Option 1 — Visual Studio (Recommended)

1. Open `ServerMonitoring.sln`
2. Right-click the Solution → **Set Startup Projects**
3. Select **Multiple startup projects**
4. Set both `ServerMonitor` and `AnomalyDetectionService` to **Start**
5. Press **F5**

### Option 2 — Two Terminal Windows

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

### Expected Output

**ServerMonitor terminal:**
```
info: ServerStatisticsService[0]
      ServerStatisticsService started. Sampling every 60s for server 'Server123'.
info: ServerStatisticsService[0]
      Published message to topic 'ServerStatistics.Server123'.
```

**AnomalyDetectionService terminal:**
```
info: AnomalyDetectionWorker[0]
      AnomalyDetectionWorker started.
info: RabbitMqConsumer[0]
      Started consuming from queue 'server.statistics.queue' with pattern 'ServerStatistics.*'.
info: MongoStatisticsRepository[0]
      MongoDB repository initialized — ServerMonitor/ServerStatistics.
dbug: MongoStatisticsRepository[0]
      Saved statistics for server 'Server123'.
```

---

## Verifying the Full Flow

### 1. RabbitMQ Management UI — http://localhost:15672

Login with `guest` / `guest`

- **Exchanges tab** → `server.monitoring` exchange should be visible with type `topic`
- **Queues tab** → `server.statistics.queue` should show incoming message rates

### 2. MongoDB — MongoDB Compass

Connect to `mongodb://localhost:27017` and navigate to **ServerMonitor → ServerStatistics**.

You should see a new document inserted every 60 seconds:

```json
{
  "_id": "661f1a2b3c4d5e6f7a8b9c0d",
  "ServerIdentifier": "Server123",
  "MemoryUsage": 4821.35,
  "AvailableMemory": 11402.12,
  "CpuUsage": 3.47,
  "Timestamp": "2026-04-01T10:00:00Z"
}
```

### 3. SignalR Alerts — http://localhost:5000/test.html

Open the test page in your browser. You should see:

```
✅ Connected to SignalR hub — waiting for alerts...
```

### 4. Trigger Alerts Manually

Instead of waiting 60 seconds, publish a test message directly from the RabbitMQ Management UI:

Go to **Exchanges → server.monitoring → Publish Message**

Set routing key to `ServerStatistics.Server123` and publish:

```json
{
  "MemoryUsage": 15000.00,
  "AvailableMemory": 500.00,
  "CpuUsage": 95.00,
  "Timestamp": "2026-04-01T10:00:00Z"
}
```

The browser should immediately show:

```
🔴 High usage on Server123: MemoryUsage at 96.8%, threshold is 80.0%
⚠️ Anomaly detected on Server123: MemoryUsage jumped from 4821.35 to 15000.00
```

---

## Scaling

### Monitoring More Servers

Deploy ServerMonitor on each additional machine with a unique `ServerIdentifier`:

```json
{ "ServerStatisticsConfig": { "ServerIdentifier": "WebServer01" } }
{ "ServerStatisticsConfig": { "ServerIdentifier": "DatabaseServer" } }
{ "ServerStatisticsConfig": { "ServerIdentifier": "ApiGateway" } }
```

No changes to AnomalyDetectionService or RabbitMQ are needed. The `ServerStatistics.*` wildcard binding automatically picks up all new servers.

### Scaling AnomalyDetectionService

Run multiple instances pointing at the same RabbitMQ broker. RabbitMQ distributes messages across all instances automatically using round-robin.

### Fault Tolerance

| Component Goes Down | Effect | Recovery |
|---|---|---|
| ServerMonitor | No new messages published | Restarts and resumes publishing automatically |
| AnomalyDetectionService | Messages queue up in RabbitMQ (durable — no data loss) | Restarts and processes all queued messages in order |
| RabbitMQ | Both services log errors and retry | Both reconnect automatically when RabbitMQ restarts |
| MongoDB | AnomalyDetectionService fails to start | Start MongoDB, restart the service |
