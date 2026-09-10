namespace Backend_API.Dto
{
    // Input DTO for charing schedule input
    public sealed class ChargingScheduleInput
    {
        public DateTimeOffset? StartTime { get; set; }
        public bool? ScheduleEnabled { get; set; }
    }
}
