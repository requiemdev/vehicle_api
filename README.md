# Vehicle Charging Demo

Full stack app to simulate the charging controls of an electric vehicle.

- Native HTML/CSS/JS for dashboard
- .NET 10 Backend

The dashboard lets the user select a demo user and one of their vehicles, see the latest battery and charging state, start or stop charging, and set a charging schedule.

## Architecture

```text
Browser dashboard
  -> ASP.NET Core API + static files
  -> Azure SQL Database
  -> Azure Functions
  -> Azure IoT Hub
  -> C# vehicle simulator

SQL Server stores users, vehicles, and maps ownership.
Device twins hold the latest vehicle state and desired schedule.
```

The browser calls the API with `X-User-Id`. The API checks that the user owns the vehicle, then calls the Functions with server-side Function keys. IoT Hub credentials and Function keys are never sent to the browser.

> `X-User-Id` is deliberately unsecure, just to demonstrate basic authentication.

## Components

### Dashboard and ASP.NET Core API

`API/Backend_API/Backend_API` serves the vanilla HTML/CSS/JavaScript dashboard and exposes the public API.

- `GET /users` lists demo users.
- `GET /vehicles` lists vehicles owned by the selected user.
- `GET /vehicles/{deviceId}/state` returns the latest device state from the IoT hub.
- `PUT /vehicles/{deviceId}/charging` enables or disables a device's charging.
- `PATCH /vehicles/{deviceId}/charging-schedule` updates the charging schedule for a device.

The dashboard polls device state every 15 seconds.

### SQL Server

The API database contains `Users` and `Vehicles`:

| Table | Columns |
| --- | --- |
| `Users` | `Id` (primary key), `DisplayName` |
| `Vehicles` | `DeviceId` (primary key), `DisplayName`, `OwnerId` (foreign key to `Users.Id`) |

A vehicle has one owner, and every protected request confirms that the requesting user owns the route's `deviceId` before the API calls Azure Functions.



### Azure Functions

`Functions/TelemetryProcessor` contains four Functions:

| Function | Trigger | Responsibility |
| --- | --- | --- |
| `ProcessTelemetry` | IoT Hub Event Hub trigger | Validates and logs the telemetry from the simulated device. |
| `GetDeviceState` | `GET /devices/{deviceId}/state` | Reads the device twin and sends it to the dashboard. |
| `SetCharging` | `POST /devices/{deviceId}/charging` | Invokes the simulated device's `setCharging` direct method to begin or stop charging. |
| `UpdateDesiredProperties` | `PATCH /devices/{deviceId}/desired-properties` | Updates desired schedule properties in the device twin. |

### Azure IoT Hub and device twin

- **Desired twin properties** hold client requested configuration: schedule enabled, start time, and a schedule revision.
- **Reported twin properties** hold the latest battery level, charging state, configuration revision, and `applied` or `rejected` status of the device.

Schedule writes use the twin ETag and retry conflicts, similar to optimistic concurrency control.

### Vehicle simulator

`vehicle/Vehicle-Sim/Vehicle-Sim` connects to IoT Hub with MQTT. Every 15 seconds it:

1. Sends battery and charging telemetry.
2. Updates reported twin state.
3. Changes its simulated battery percentage.

It also receives direct charging commands and desired-twin schedule changes.

## Control flows

### Read state

```text
Dashboard -> API  -> GetDeviceState -> IoT Hub twin -> Dashboard
```

### Start or stop charging

```text
Dashboard -> API -> SetCharging Function -> IoT Hub direct method -> simulator
```


### Set a schedule

```text
Dashboard -> API -> UpdateDesiredProperties
          -> IoT Hub desired twin -> simulator applies configuration
          -> simulator reports applied/rejected revision -> Dashboard poll
```