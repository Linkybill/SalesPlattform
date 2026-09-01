using IdentityPlatform.Shared.Database;

namespace SalesPlattform.Backend.Data;

public sealed class SalesWorkItem : SalesEntity
{
    public required string WorkItemType { get; set; }
    public required string Status { get; set; }
    public required string Title { get; set; }
    public string? Reason { get; set; }
    public Guid? OwnerId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public decimal? PriorityScore { get; set; }
    public DateTimeOffset? PriorityCalculatedAt { get; set; }
    public string? SourceRuleCode { get; set; }
    public Guid? SourceRuleRunId { get; set; }
    public bool RequiresApproval { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
    public DateTimeOffset? SnoozedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public SalesOwner? Owner { get; set; }
    public SalesRuleRun? SourceRuleRun { get; set; }
    public ICollection<SalesWorkItemRelation> Relations { get; set; } = [];
    public ICollection<SalesWorkItemEvent> Events { get; set; } = [];
    public ICollection<SalesRuleEvaluation> RuleEvaluations { get; set; } = [];
    public ICollection<SalesNotification> Notifications { get; set; } = [];
}

public sealed class SalesWorkItemRelation : SalesEntity
{
    public Guid WorkItemId { get; set; }
    public required string TargetType { get; set; }
    public Guid TargetId { get; set; }
    public string? RelationRole { get; set; }

    public SalesWorkItem? WorkItem { get; set; }
}

public sealed class SalesWorkItemEvent : SalesEntity
{
    public Guid WorkItemId { get; set; }
    public required string EventType { get; set; }
    public string? DetailsJson { get; set; }
    public string? ActorSubject { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public SalesWorkItem? WorkItem { get; set; }
}

public sealed class SalesRuleDefinition : SalesEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public required string AutomationMode { get; set; }
    public int Version { get; set; }
    public string? ParametersJson { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<SalesRuleEvaluation> Evaluations { get; set; } = [];
}

public sealed class SalesRuleRun : SalesEntity
{
    public required string TriggerType { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int RuleSetVersion { get; set; }
    public int EvaluatedCount { get; set; }
    public int CreatedCount { get; set; }
    public string? Error { get; set; }

    public ICollection<SalesRuleEvaluation> Evaluations { get; set; } = [];
    public ICollection<SalesWorkItem> WorkItems { get; set; } = [];
}

public sealed class SalesRuleEvaluation : SalesEntity
{
    public Guid RuleRunId { get; set; }
    public Guid RuleDefinitionId { get; set; }
    public required string TargetType { get; set; }
    public Guid TargetId { get; set; }
    public required string Outcome { get; set; }
    public Guid? WorkItemId { get; set; }
    public string? ExplanationJson { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }

    public SalesRuleRun? RuleRun { get; set; }
    public SalesRuleDefinition? RuleDefinition { get; set; }
    public SalesWorkItem? WorkItem { get; set; }
}

public sealed class SalesPriorityProfile : SalesEntity
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public decimal BaseScore { get; set; }
    public decimal AgeBonusPerDay { get; set; }
    public decimal ValueBonusFactor { get; set; }
    public decimal? MaximumScore { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<SalesPriorityWeight> Weights { get; set; } = [];
}

public sealed class SalesPriorityWeight : SalesEntity
{
    public Guid PriorityProfileId { get; set; }
    public required string WorkItemType { get; set; }
    public decimal Weight { get; set; }
    public string? ConfigurationJson { get; set; }

    public SalesPriorityProfile? PriorityProfile { get; set; }
}

public sealed class SalesFiscalYear : SalesEntity
{
    public required string Name { get; set; }
    public DateOnly StartsAt { get; set; }
    public DateOnly EndsAt { get; set; }
    public required string TimeZone { get; set; }
    public bool IsClosed { get; set; }

    public ICollection<SalesTargetPeriod> TargetPeriods { get; set; } = [];
    public ICollection<SalesTarget> Targets { get; set; } = [];
}

public sealed class SalesTargetPeriod : SalesEntity
{
    public Guid FiscalYearId { get; set; }
    public required string PeriodType { get; set; }
    public int PeriodNumber { get; set; }
    public DateOnly StartsAt { get; set; }
    public DateOnly EndsAt { get; set; }
    public decimal DistributionWeight { get; set; }

    public SalesFiscalYear? FiscalYear { get; set; }
    public ICollection<SalesTarget> Targets { get; set; } = [];
}

public sealed class SalesTarget : SalesEntity
{
    public Guid FiscalYearId { get; set; }
    public Guid? TargetPeriodId { get; set; }
    public Guid OwnerId { get; set; }
    public required string TargetType { get; set; }
    public string? AppointmentType { get; set; }
    public decimal TargetValue { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public SalesFiscalYear? FiscalYear { get; set; }
    public SalesTargetPeriod? TargetPeriod { get; set; }
    public SalesOwner? Owner { get; set; }
}

public sealed class SalesWorkCalendar : SalesEntity
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string TimeZone { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<SalesWorkingHours> WorkingHours { get; set; } = [];
    public ICollection<SalesHoliday> Holidays { get; set; } = [];
}

public sealed class SalesWorkingHours : SalesEntity
{
    public Guid CalendarId { get; set; }
    public int DayOfWeek { get; set; }
    public bool IsWorkingDay { get; set; }
    public TimeSpan? StartAt { get; set; }
    public TimeSpan? EndAt { get; set; }
    public TimeSpan? BreakStartAt { get; set; }
    public TimeSpan? BreakEndAt { get; set; }

    public SalesWorkCalendar? Calendar { get; set; }
}

public sealed class SalesHoliday : SalesEntity
{
    public Guid CalendarId { get; set; }
    public DateOnly Date { get; set; }
    public required string Name { get; set; }
    public bool IsWorkingDayOverride { get; set; }

    public SalesWorkCalendar? Calendar { get; set; }
}

public sealed class SalesCommunicationTemplate : SalesEntity
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Channel { get; set; }
    public string? SubjectTemplate { get; set; }
    public required string BodyTemplate { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class SalesNotification : SalesEntity
{
    public required string RecipientSubject { get; set; }
    public Guid? WorkItemId { get; set; }
    public string? Title { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public int EscalationLevel { get; set; }
    public required string DeliveryStatus { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public SalesWorkItem? WorkItem { get; set; }
}
