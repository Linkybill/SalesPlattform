namespace SalesPlattform.Backend.Data;

public sealed class TaskGoalProfile : SalesEntity
{
    public required string Name { get; set; }
    public int DailyCapacityMinutes { get; set; }
    public Guid Revision { get; set; }
    public string[] Industries { get; set; } = [];
    public ICollection<TaskGoalProfileItem> Items { get; set; } = [];
}

public sealed class SalesTaskGoalPlan : SalesEntity
{
    public Guid? MembershipId { get; set; }
    public DateOnly? StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public Guid Revision { get; set; }
    public required string Name { get; set; }
    public required string Configuration { get; set; }
    public Guid[] Audience { get; set; } = [];
}

public sealed class SalesTaskAssignment : SalesEntity
{
    public Guid MembershipId { get; set; }
    public Guid PlanId { get; set; }
    public Guid WorkItemId { get; set; }
    public required string TargetType { get; set; }
    public Guid TargetId { get; set; }
    public required string TaskType { get; set; }
    public required string Title { get; set; }
    public string? Industry { get; set; }
    public DateOnly AssignedOn { get; set; }
    public DateTimeOffset? CrmCompletedAt { get; set; }
}

public sealed class TaskGoalProfileItem : SalesEntity
{
    public Guid ProfileId { get; set; }
    public required string TaskType { get; set; }
    public int SharePercent { get; set; }
    public int MinutesPerTask { get; set; }
    public int DailyTarget { get; set; }
    public int Priority { get; set; }
    public TaskGoalProfile? Profile { get; set; }
}
