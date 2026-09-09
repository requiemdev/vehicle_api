using System.Text.Json;
using Microsoft.Azure.Devices.Shared;
using TelemetryProcessor;

namespace TelemetryProcessor.Tests;

public sealed class TelemetryProcessorTest
{

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Parse_invalid_battery_throws_json_exception(int value)
    {
        var payload = $$"""
        {
          "messageId": "1",
          "deviceId": "sim-car-001",
          "timestampUtc": "2026-09-05T02:15:00Z",
          "batteryPercentage": {{value}},
          "isCharging": true,
          "scheduleRevisionApplied": 0
        }
        """;

        Assert.Throws<JsonException>(() => Telemetry.Parse(payload));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(100)]
    public void Parse_valid_battery_edge_returns_telemetry(int value)
    {
        var payload = $$"""
        {
          "messageId": "1",
          "deviceId": "sim-car-001",
          "timestampUtc": "2026-09-05T02:15:00Z",
          "batteryPercentage": {{value}},
          "isCharging": true,
          "scheduleRevisionApplied": 0
        }
        """;

        var result = Telemetry.Parse(payload);

        Assert.Equal("1", result.MessageId);
        Assert.Equal("sim-car-001", result.DeviceId);
        Assert.Equal(value, result.BatteryPercentage);
        Assert.True(result.IsCharging);
    }



    // Parametrised tests, each invalid payload (generalised)
    [Theory]
    [InlineData("not json")] //string
    [InlineData("{}")] // empty object
    [InlineData("""{"messageId":"","deviceId":"car","timestampUtc":"2026-09-05T02:15:00Z","batteryPercentage":50}""")] // no message id
    [InlineData("""{"messageId":"1","deviceId":"","timestampUtc":"2026-09-05T02:15:00Z","batteryPercentage":50}""")] //  no device id
    [InlineData("""{"messageId":"1","deviceId":"car","timestampUtc":"2026-09-05T02:15:00Z","batteryPercentage":-1}""")] // invalid battery % (lower)
    [InlineData("""{"messageId":"1","deviceId":"car","timestampUtc":"2026-09-05T02:15:00Z","batteryPercentage":101}""")] // invalid battery % (upper)
    public void Parse_invalid_payload_throws_json_exception(string payload)
    {
        Assert.Throws<JsonException>(() => Telemetry.Parse(payload));
    }


    /// Test the JSON was parsed with correct values
    [Fact]
    public void Parse_valid_payload_returns_telemetry()
    {
        var result = Telemetry.Parse("""
            {
              "messageId": "1",
              "deviceId": "sim-car-001",
              "timestampUtc": "2026-09-05T02:15:00Z",
              "batteryPercentage": 67,
              "isCharging": true,
              "scheduleRevisionApplied": 0
            }
            """);

        Assert.Equal("1", result.MessageId);
        Assert.Equal("sim-car-001", result.DeviceId);
        Assert.Equal(67, result.BatteryPercentage);
        Assert.True(result.IsCharging);
    }



    

}

public sealed class GetDeviceStateTest
{
    [Fact]
    public void Project_returns_valid_state()
    {
        var twin = Twin();

        var result = GetDeviceState.Project("sim-car-001", twin);

        Assert.Equal("sim-car-001", result.DeviceId);
        Assert.Equal(67, result.BatteryPercentage);
        Assert.True(result.IsCharging);
        Assert.Equal(DateTimeOffset.Parse("2026-09-09T02:15:00Z"), result.TelemetryTimestampUtc);
        Assert.True(result.ScheduleEnabled);
        Assert.Equal(DateTimeOffset.Parse("2026-09-09T08:00:00Z"), result.ChargingStartUtc);
        Assert.Equal("applied", result.ScheduleStatus);
        Assert.Null(result.ScheduleError);
    }

    [Theory]
    [InlineData(4, 4, "applied", "applied")]
    [InlineData(4, 3, "rejected", "pending")]
    [InlineData(4, 4, "rejected", "rejected")]
    public void Project_derives_revision_correlated_schedule_status(
        int desiredRevision,
        int configRevision,
        string configStatus,
        string expected)
    {
        var twin = Twin(desiredRevision, configRevision, configStatus);
        twin.Properties.Reported["configError"] = "bad schedule";

        var result = GetDeviceState.Project("car", twin);

        Assert.Equal(expected, result.ScheduleStatus);
        Assert.Equal(expected == "rejected" ? "bad schedule" : null, result.ScheduleError);
    }

    private static Twin Twin(
        int desiredRevision = 3,
        int configRevision = 3,
        string configStatus = "applied")
    {
        var twin = new Twin();
        twin.Properties.Desired["scheduleRevision"] = desiredRevision;
        twin.Properties.Desired["chargingScheduleEnabled"] = true;
        twin.Properties.Desired["chargingStartUtc"] = "2026-09-09T08:00:00Z";
        twin.Properties.Reported["configRevision"] = configRevision;
        twin.Properties.Reported["configStatus"] = configStatus;
        twin.Properties.Reported["batteryPercentage"] = 67;
        twin.Properties.Reported["isCharging"] = true;
        twin.Properties.Reported["telemetryTimestampUtc"] = "2026-09-09T02:15:00Z";

        return twin;
    }
}
