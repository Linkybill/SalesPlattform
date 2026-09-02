using System.Globalization;
using System.Text.Json;
using IdentityPlatform.Shared.ApplicationSettings;
using Microsoft.Extensions.Options;
using SalesPlattform.Backend.Integrations;

namespace SalesPlattform.Backend.Services;

public sealed class SalesApplicationSettingsService(
    IApplicationSettingsStore settingsStore,
    IOptions<ApplicationSettingsOptions> settingsOptions)
{
    public const string CallConversationThresholdSecondsKey = "sales.callConversationThresholdSeconds";
    public const string CallFollowUpIntervalDaysKey = "sales.rules.callFollowUpIntervalDays";
    public const string CallEmailFollowUpIntervalDaysKey = "sales.rules.callEmailFollowUpIntervalDays";
    public const string CallEmailFollowUpAttemptsKey = "sales.rules.callEmailFollowUpAttempts";
    public const string CallLongRunnerMinAttemptsKey = "sales.rules.callLongRunnerMinAttempts";
    public const string CallLongRunnerMaxAttemptsKey = "sales.rules.callLongRunnerMaxAttempts";
    public const string CallLongRunnerIntervalDaysKey = "sales.rules.callLongRunnerIntervalDays";
    public const string CallNotReachableAfterAttemptsKey = "sales.rules.callNotReachableAfterAttempts";
    public const string DealInactiveDaysKey = "sales.rules.dealInactiveDays";
    public const string DealCockpitEscalationDaysKey = "sales.rules.dealCockpitEscalationDays";
    public const string ContractRenewalHorizonDaysKey = "sales.rules.contractRenewalHorizonDays";
    public const string ContractCriticalDaysKey = "sales.rules.contractCriticalDays";
    public const string ContactInactiveDaysKey = "sales.rules.contactInactiveDays";
    public const string OwnerChangeAfterDaysKey = "sales.rules.ownerChangeAfterDays";
    public const string OwnerChangeNoContactDaysKey = "sales.rules.ownerChangeNoContactDays";
    public const string OwnerChangeFollowUpDaysKey = "sales.rules.ownerChangeFollowUpDays";
    public const string LeadFirstResponseWorkingHoursKey = "sales.rules.leadFirstResponseWorkingHours";
    public const string LeadEscalationWorkingHoursKey = "sales.rules.leadEscalationWorkingHours";
    public const string CrossSellingMinimumCustomerValueKey = "sales.rules.crossSellingMinimumCustomerValue";
    public const string TargetPaceGapPointsKey = "sales.rules.targetPaceGapPoints";
    public const string AppointmentRescheduleCountKey = "sales.rules.appointmentRescheduleCount";
    public const string AccountCareInactiveDaysKey = "sales.rules.accountCareInactiveDays";
    public const string AccountCareMinimumRevenueKey = "sales.rules.accountCareMinimumRevenue";
    public const string LostDealReactivationAgeDaysKey = "sales.rules.lostDealReactivationAgeDays";
    public const string ServiceCaseResponseDaysKey = "sales.rules.serviceCaseResponseDays";
    public const string OfferFollowUpDaysKey = "sales.rules.offerFollowUpDays";
    public const string OrderDeliveryEscalationDaysKey = "sales.rules.orderDeliveryEscalationDays";
    public const string InvoiceOverdueGraceDaysKey = "sales.rules.invoiceOverdueGraceDays";

    private const string LegacyContactInactiveMonthsKey = "sales.rules.contactInactiveMonths";
    private const string LegacyOwnerChangeAfterMonthsKey = "sales.rules.ownerChangeAfterMonths";
    private const string LegacyOwnerChangeNoContactMonthsKey = "sales.rules.ownerChangeNoContactMonths";
    private const string LegacyAccountCareInactiveMonthsKey = "sales.rules.accountCareInactiveMonths";
    private const string LegacyLostDealReactivationAgeMonthsKey = "sales.rules.lostDealReactivationAgeMonths";

    public async Task<int> GetCallConversationThresholdSecondsAsync(
        Guid tenantId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(tenantId, userId, cancellationToken);
        var value = FindValue(settings, CallConversationThresholdSecondsKey);

        var threshold = value.HasValue ? ReadInteger(value.Value) : null;
        return threshold.HasValue
            ? CallQualification.NormalizeThreshold(threshold.Value)
            : CallQualification.DefaultConversationThresholdSeconds;
    }

    public async Task<SalesRuleConfiguration> GetRuleConfigurationAsync(
        Guid tenantId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(tenantId, userId, cancellationToken);

        var callEmailFollowUpAttempts = ReadInteger(settings, CallEmailFollowUpAttemptsKey, 5, 1, 1000);
        var callLongRunnerMinAttempts = Math.Max(
            callEmailFollowUpAttempts + 1,
            ReadInteger(settings, CallLongRunnerMinAttemptsKey, 6, 1, 1000));
        var callLongRunnerMaxAttempts = Math.Max(
            callLongRunnerMinAttempts,
            ReadInteger(settings, CallLongRunnerMaxAttemptsKey, 10, 1, 1000));
        var callNotReachableAfterAttempts = Math.Max(
            callLongRunnerMaxAttempts,
            ReadInteger(settings, CallNotReachableAfterAttemptsKey, 10, 1, 1000));

        return new SalesRuleConfiguration(
            CallFollowUpIntervalDays: ReadInteger(settings, CallFollowUpIntervalDaysKey, 14, 1, 3650),
            CallEmailFollowUpIntervalDays: ReadInteger(settings, CallEmailFollowUpIntervalDaysKey, 14, 1, 3650),
            CallEmailFollowUpAttempts: callEmailFollowUpAttempts,
            CallLongRunnerMinAttempts: callLongRunnerMinAttempts,
            CallLongRunnerMaxAttempts: callLongRunnerMaxAttempts,
            CallLongRunnerIntervalDays: ReadInteger(settings, CallLongRunnerIntervalDaysKey, 30, 1, 3650),
            CallNotReachableAfterAttempts: callNotReachableAfterAttempts,
            DealInactiveDays: ReadInteger(settings, DealInactiveDaysKey, 30, 1, 3650),
            DealCockpitEscalationDays: ReadInteger(settings, DealCockpitEscalationDaysKey, 60, 1, 3650),
            ContractRenewalHorizonDays: ReadInteger(settings, ContractRenewalHorizonDaysKey, 90, 1, 3650),
            ContractCriticalDays: ReadInteger(settings, ContractCriticalDaysKey, 30, 1, 3650),
            ContactInactiveDays: ReadDays(settings, ContactInactiveDaysKey, LegacyContactInactiveMonthsKey, 90),
            OwnerChangeAfterDays: ReadDays(settings, OwnerChangeAfterDaysKey, LegacyOwnerChangeAfterMonthsKey, 180),
            OwnerChangeNoContactDays: ReadDays(settings, OwnerChangeNoContactDaysKey, LegacyOwnerChangeNoContactMonthsKey, 90),
            OwnerChangeFollowUpDays: ReadInteger(settings, OwnerChangeFollowUpDaysKey, 7, 1, 3650),
            LeadFirstResponseWorkingHours: ReadInteger(settings, LeadFirstResponseWorkingHoursKey, 1, 1, 720),
            LeadEscalationWorkingHours: ReadInteger(settings, LeadEscalationWorkingHoursKey, 4, 1, 720),
            CrossSellingMinimumCustomerValue: ReadDecimal(settings, CrossSellingMinimumCustomerValueKey, 0m, 0m, 1_000_000_000m),
            TargetPaceGapPoints: ReadDecimal(settings, TargetPaceGapPointsKey, 15m, 0m, 100m),
            AppointmentRescheduleCount: ReadInteger(settings, AppointmentRescheduleCountKey, 3, 1, 1000),
            AccountCareInactiveDays: ReadDays(settings, AccountCareInactiveDaysKey, LegacyAccountCareInactiveMonthsKey, 90),
            AccountCareMinimumRevenue: ReadDecimal(settings, AccountCareMinimumRevenueKey, 0m, 0m, 1_000_000_000m),
            LostDealReactivationAgeDays: ReadDays(settings, LostDealReactivationAgeDaysKey, LegacyLostDealReactivationAgeMonthsKey, 90),
            ServiceCaseResponseDays: ReadInteger(settings, ServiceCaseResponseDaysKey, 2, 0, 3650),
            OfferFollowUpDays: ReadInteger(settings, OfferFollowUpDaysKey, 7, 1, 3650),
            OrderDeliveryEscalationDays: ReadInteger(settings, OrderDeliveryEscalationDaysKey, 1, 0, 3650),
            InvoiceOverdueGraceDays: ReadInteger(settings, InvoiceOverdueGraceDaysKey, 0, 0, 3650));
    }

    private async Task<IReadOnlyDictionary<string, JsonElement>> LoadSettingsAsync(
        Guid tenantId,
        string? userId,
        CancellationToken cancellationToken)
    {
        var context = new ApplicationSettingsContext(
            settingsOptions.Value.ApplicationKey,
            tenantId,
            Guid.Empty,
            string.IsNullOrWhiteSpace(userId) ? "system:sales-settings" : userId);
        var settings = await settingsStore.LoadAsync(context, cancellationToken);
        return settings
            .GroupBy(setting => setting.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static JsonElement? FindValue(
        IReadOnlyDictionary<string, JsonElement> settings,
        string key)
        => settings.TryGetValue(key, out var value) ? value : null;

    private static int ReadInteger(
        IReadOnlyDictionary<string, JsonElement> settings,
        string key,
        int fallback,
        int minimum,
        int maximum)
    {
        var value = FindValue(settings, key);
        var parsed = value.HasValue ? ReadInteger(value.Value) : null;
        return Math.Clamp(parsed ?? fallback, minimum, maximum);
    }

    private static int ReadDays(
        IReadOnlyDictionary<string, JsonElement> settings,
        string daysKey,
        string legacyMonthsKey,
        int fallbackDays)
    {
        var days = FindValue(settings, daysKey);
        var parsedDays = days.HasValue ? ReadInteger(days.Value) : null;
        if (parsedDays.HasValue)
            return Math.Clamp(parsedDays.Value, 1, 3650);

        var legacyMonths = FindValue(settings, legacyMonthsKey);
        var parsedMonths = legacyMonths.HasValue ? ReadInteger(legacyMonths.Value) : null;
        var convertedDays = parsedMonths.HasValue
            ? Math.Min(int.MaxValue, (long)Math.Max(1, parsedMonths.Value) * 30L)
            : fallbackDays;
        return Math.Clamp((int)convertedDays, 1, 3650);
    }

    private static decimal ReadDecimal(
        IReadOnlyDictionary<string, JsonElement> settings,
        string key,
        decimal fallback,
        decimal minimum,
        decimal maximum)
    {
        var value = FindValue(settings, key);
        var parsed = value.HasValue ? ReadDecimal(value.Value) : null;
        return Math.Clamp(parsed ?? fallback, minimum, maximum);
    }

    private static int? ReadInteger(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }

    private static decimal? ReadDecimal(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }
}

public sealed record SalesRuleConfiguration(
    int CallFollowUpIntervalDays,
    int CallEmailFollowUpIntervalDays,
    int CallEmailFollowUpAttempts,
    int CallLongRunnerMinAttempts,
    int CallLongRunnerMaxAttempts,
    int CallLongRunnerIntervalDays,
    int CallNotReachableAfterAttempts,
    int DealInactiveDays,
    int DealCockpitEscalationDays,
    int ContractRenewalHorizonDays,
    int ContractCriticalDays,
    int ContactInactiveDays,
    int OwnerChangeAfterDays,
    int OwnerChangeNoContactDays,
    int OwnerChangeFollowUpDays,
    int LeadFirstResponseWorkingHours,
    int LeadEscalationWorkingHours,
    decimal CrossSellingMinimumCustomerValue,
    decimal TargetPaceGapPoints,
    int AppointmentRescheduleCount,
    int AccountCareInactiveDays,
    decimal AccountCareMinimumRevenue,
    int LostDealReactivationAgeDays,
    int ServiceCaseResponseDays,
    int OfferFollowUpDays,
    int OrderDeliveryEscalationDays,
    int InvoiceOverdueGraceDays);
