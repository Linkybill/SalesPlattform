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
    public const string ChangeDetectionModeKey = "crm.changeDetectionMode";
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

    public const string FirstMeetingTypesKey = "sales.reports.firstMeetingTypes";
    public const string FollowUpMeetingTypesKey = "sales.reports.followUpMeetingTypes";
    public const string OfferStageNamesKey = "sales.reports.offerStageNames";
    public const string PreparationDaysKey = "sales.reports.preparationDays";

    public async Task<SalesReportConfiguration> GetReportConfigurationAsync(
        Guid tenantId, string? userId, CancellationToken cancellationToken = default)
    {
        var settings = await LoadSettingsAsync(tenantId, userId, cancellationToken);
        return new(
            ReadNames(settings, FirstMeetingTypesKey, SalesReportConfiguration.Default.FirstMeetingTypes),
            ReadNames(settings, FollowUpMeetingTypesKey, SalesReportConfiguration.Default.FollowUpMeetingTypes),
            ReadNames(settings, OfferStageNamesKey, SalesReportConfiguration.Default.OfferStageNames),
            ReadInteger(settings, PreparationDaysKey, 5, 1, 90))
        {
            DormantMonths = ReadInteger(settings, "sales.reports.dormantMonths", 5, 1, 120),
            DisinterestStatuses = ReadNames(settings, "sales.reports.disinterestStatuses", SalesReportConfiguration.Default.DisinterestStatuses),
            ActiveCustomerStatuses = ReadNames(settings, "sales.reports.activeCustomerStatuses", SalesReportConfiguration.Default.ActiveCustomerStatuses),
            LostCustomerStatuses = ReadNames(settings, "sales.reports.lostCustomerStatuses", SalesReportConfiguration.Default.LostCustomerStatuses),
            Locations = ReadReportLocations(settings),
            PostalAreas = ReadPostalAreas(settings),
            AttainmentGreen = ReadDecimal(settings,"sales.reports.attainmentGreen",90,0,1000),
            AttainmentRed = ReadDecimal(settings,"sales.reports.attainmentRed",70,0,1000),
            WinRateGreen = ReadDecimal(settings,"sales.reports.winRateGreen",35,0,100),
            WinRateRed = ReadDecimal(settings,"sales.reports.winRateRed",20,0,100),
            CoverageGreen = ReadDecimal(settings,"sales.reports.coverageGreen",3,0,1000),
            CoverageRed = ReadDecimal(settings,"sales.reports.coverageRed",2,0,1000)
        };
    }

    private static JsonElement? ReadPostalAreas(IReadOnlyDictionary<string, JsonElement> settings)
    {
        var value=FindValue(settings,"sales.reports.postalAreas");
        if(value is not {ValueKind:JsonValueKind.String}) return null;
        try
        {
            using var document=JsonDocument.Parse(value.Value.GetString()!);
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("type",out var type) || type.GetString()!="FeatureCollection"
                || !root.TryGetProperty("features",out var features) || features.ValueKind!=JsonValueKind.Array || features.GetArrayLength()==0) return null;
            return root.Clone();
        }
        catch(JsonException) { return null; }
        catch(InvalidOperationException) { return null; }
    }

    private static IReadOnlyDictionary<string, SalesReportLocation> ReadReportLocations(IReadOnlyDictionary<string, JsonElement> settings)
    {
        var value = FindValue(settings, "sales.reports.locations");
        if (value is not { ValueKind: JsonValueKind.String }) return new Dictionary<string, SalesReportLocation>();
        try { return JsonSerializer.Deserialize<Dictionary<string, SalesReportLocation>>(value.Value.GetString()!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
        catch (JsonException) { return new Dictionary<string, SalesReportLocation>(); }
    }

    private static string[] ReadNames(IReadOnlyDictionary<string, JsonElement> settings, string key, string[] fallback)
    {
        var value = FindValue(settings, key);
        if (value is not { ValueKind: JsonValueKind.String }) return fallback;
        return value.Value.GetString()!.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

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

public sealed record SalesReportLocation(decimal? Latitude, decimal? Longitude, string? CountryCode, string? PostalCode);

public sealed record SalesReportConfiguration(
    string[] FirstMeetingTypes, string[] FollowUpMeetingTypes, string[] OfferStageNames, int PreparationDays)
{
    public JsonElement? PostalAreas { get; init; }
    public IReadOnlyDictionary<string, SalesReportLocation> Locations { get; init; } = new Dictionary<string, SalesReportLocation>();
    public int CallEmailAttempts { get; init; } = 5;
    public int CallLongMin { get; init; } = 6;
    public int CallLongMax { get; init; } = 10;
    public int CallUnreachableAfter { get; init; } = 10;
    public decimal AttainmentGreen { get; init; } = 90;
    public decimal AttainmentRed { get; init; } = 70;
    public decimal WinRateGreen { get; init; } = 35;
    public decimal WinRateRed { get; init; } = 20;
    public decimal CoverageGreen { get; init; } = 3;
    public decimal CoverageRed { get; init; } = 2;
    public int DormantMonths { get; init; } = 5;
    public int RenewalDays { get; init; } = 90;
    public string[] DisinterestStatuses { get; init; } = ["Kein Interesse", "Keine Interesse", "Ohne Interesse", "Not interested"];
    public string[] ActiveCustomerStatuses { get; init; } = ["active", "aktiv", "customer", "Kunde"];
    public string[] LostCustomerStatuses { get; init; } = ["lost", "verloren", "churned", "gekündigt"];
    public static SalesReportConfiguration Default => new(
        ["Erstgespräch", "Ersttermin", "Erstkontakt", "Kennenlernen", "First meeting", "Initial meeting"],
        ["Folgetermin", "Folgegespräch", "Follow-up", "Follow-up-Termin", "Follow-up meeting", "Follow up"],
        ["Angebot", "Angebot versendet", "Proposal", "Proposal/Price Quote", "Offer"], 5);
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
