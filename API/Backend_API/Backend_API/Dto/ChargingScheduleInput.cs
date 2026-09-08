namespace Backend_API.Dto
{
    public sealed class ChargingScheduleInput
    {
        public DateTimeOffset? StartTime { get; set; }
        public bool? ScheduleEnabled { get; set; }
    }
}
