using System.Text.Json;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoCrmRecordMapper : ICrmRecordMapper
{
    private static readonly IReadOnlyDictionary<string, string[]> PreferredFields =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Users"] =
            [
                "id", "full_name", "first_name", "last_name", "email", "status",
                "created_time", "Modified_Time", "role"
            ],
            ["Accounts"] =
            [
                "id", "Account_Name", "Name", "Industry", "Billing_Code", "Billing_City",
                "Billing_Country", "Billing_State", "Billing_Street", "Owner", "Account_Status",
                "Tax_Number", "Website", "Created_Time", "Modified_Time"
            ],
            ["Contacts"] =
            [
                "id", "Full_Name", "First_Name", "Last_Name", "Account_Name", "Email", "Phone",
                "Mobile", "Mobile_Phone", "Title", "Contact_Status", "Owner", "Created_Time", "Modified_Time"
            ],
            ["Leads"] =
            [
                "id", "Full_Name", "First_Name", "Last_Name", "Company", "Email", "Phone",
                "Lead_Status", "Lead_Source", "Last_Activity_Time", "Last_Call", "Call_Attempts",
                "Calls_Since_Conversation", "Owner", "Created_Time", "Modified_Time"
            ],
            ["Products"] =
            [
                "id", "Product_Name", "Product_Code", "Description", "Product_Category", "Category",
                "Product_Active", "Active", "Created_Time", "Modified_Time"
            ],
            ["Deals"] =
            [
                "id", "Deal_Name", "Account_Name", "Amount", "Currency", "Stage", "Pipeline",
                "Product_Name", "Product", "Contract_Term", "Duration_Months", "Contract_Start_Date",
                "Contract_End_Date", "Closing_Date", "Owner", "Reason_for_Loss__s", "Last_Activity_Time",
                "Created_Time", "Modified_Time"
            ],
            ["Calls"] =
            [
                "id", "Subject", "What_Id", "Who_Id", "Owner", "Call_Start_Time", "Call_Duration",
                "Call_Type", "Call_Status", "Call_Result", "Created_Time", "Modified_Time"
            ],
            ["Tasks"] =
            [
                "id", "Subject", "What_Id", "Who_Id", "Owner", "Due_Date", "Status", "Priority",
                "Description", "Created_Time", "Modified_Time"
            ],
            ["Events"] =
            [
                "id", "Event_Title", "Subject", "What_Id", "Who_Id", "Owner", "Start_DateTime",
                "End_DateTime", "Event_Status", "Type", "$event_cancelled", "Created_Time", "Modified_Time"
            ],
            ["Pipelines"] = ["id", "display_value", "pipeline_name", "name", "sequence_number"],
            ["PipelineStages"] = ["id", "pipeline_id", "pipeline_name", "pick_list_value", "display_value", "probability", "sequence_number"],
            ["Emails"] =
            [
                "id", "subject", "Subject", "from", "to", "cc", "bcc", "time", "Sent_Time", "Received_Time", "Date_Time",
                "Created_Time", "Modified_Time", "owner", "Owner"
            ],
            ["DealStageHistory"] =
            ["id", "Stage", "Stage_Name", "From", "To", "Date", "Created_Time", "Modified_Time", "Last_Modified_Time"]
        };

    public string ProviderKey => CrmProviders.Zoho;

    public IReadOnlyCollection<string> GetPreferredFields(string module)
        => PreferredFields.TryGetValue(module, out var fields) ? fields : ["id"];

    public string GetEntityType(string module)
        => module.ToLowerInvariant() switch
        {
            "users" => CrmEntityTypes.Owner,
            "accounts" => CrmEntityTypes.Customer,
            "contacts" => CrmEntityTypes.Contact,
            "leads" => CrmEntityTypes.Lead,
            "products" => CrmEntityTypes.Product,
            "pipelines" => CrmEntityTypes.Pipeline,
            "pipelinestages" => CrmEntityTypes.PipelineStage,
            "deals" => CrmEntityTypes.Deal,
            "dealstagehistory" => CrmEntityTypes.DealStageHistory,
            "calls" or "tasks" or "emails" => CrmEntityTypes.Activity,
            "events" or "meetings" or "appointments" => CrmEntityTypes.Appointment,
            _ => module.ToLowerInvariant()
        };

    public CrmCanonicalRecord Map(CrmExternalRecord record)
        => record.Module.ToLowerInvariant() switch
        {
            "users" => MapOwner(record),
            "accounts" => MapCustomer(record),
            "contacts" => MapContact(record),
            "leads" => MapLead(record),
            "products" => MapProduct(record),
            "pipelines" => MapPipeline(record),
            "pipelinestages" => MapPipelineStage(record),
            "deals" => MapDeal(record),
            "dealstagehistory" => MapDealStageHistory(record),
            "calls" or "tasks" or "emails" => MapActivity(record),
            "events" or "meetings" or "appointments" => MapAppointment(record),
            _ => throw new InvalidOperationException(
                $"Das Zoho-Modul '{record.Module}' besitzt noch kein kanonisches Mapping.")
        };

    private static CrmCanonicalOwner MapOwner(CrmExternalRecord record)
    {
        var firstName = ZohoFieldReader.String(record.Payload, "first_name", "First_Name");
        var lastName = ZohoFieldReader.String(record.Payload, "last_name", "Last_Name");
        var displayName = ZohoFieldReader.String(record.Payload, "full_name", "Full_Name", "name")
            ?? JoinName(firstName, lastName)
            ?? record.ExternalId;
        return new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            record.ExternalId,
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "created_time", "Created_Time"),
            record.ModifiedAt ?? ZohoFieldReader.DateTimeOffset(record.Payload, "Modified_Time", "modified_time"),
            displayName,
            ZohoFieldReader.String(record.Payload, "email", "Email"),
            !string.Equals(ZohoFieldReader.String(record.Payload, "status"), "inactive", StringComparison.OrdinalIgnoreCase));
    }

    private static CrmCanonicalCustomer MapCustomer(CrmExternalRecord record)
    {
        var country = NormalizeCountryCode(ZohoFieldReader.String(record.Payload, "Billing_Country", "Country"));
        return new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            record.ExternalId,
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "CreatedTime"),
            record.ModifiedAt,
            ZohoFieldReader.String(record.Payload, "Account_Name", "Name") ?? record.ExternalId,
            ZohoFieldReader.String(record.Payload, "Industry"),
            ZohoFieldReader.String(record.Payload, "Billing_Code", "Zip", "Postal_Code"),
            ZohoFieldReader.String(record.Payload, "Billing_City", "City"),
            country,
            ZohoFieldReader.String(record.Payload, "Account_Status", "Status"),
            ZohoFieldReader.String(record.Payload, "Legal_Name", "Account_Name", "Name"),
            ZohoFieldReader.String(record.Payload, "Tax_Number", "USt_Id", "VAT_Number"),
            NormalizeDomain(ZohoFieldReader.String(record.Payload, "Website")),
            ZohoFieldReader.String(record.Payload, "Billing_State", "State"),
            ZohoFieldReader.String(record.Payload, "Billing_Street", "Street"),
            ZohoFieldReader.String(record.Payload, "House_Number"),
            ZohoFieldReader.LookupId(record.Payload, "Owner"));
    }

    private static CrmCanonicalContact MapContact(CrmExternalRecord record)
    {
        var firstName = ZohoFieldReader.String(record.Payload, "First_Name", "first_name");
        var lastName = ZohoFieldReader.String(record.Payload, "Last_Name", "last_name");
        return new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            record.ExternalId,
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "created_time"),
            record.ModifiedAt,
            ZohoFieldReader.String(record.Payload, "Full_Name", "Name")
                ?? JoinName(firstName, lastName)
                ?? record.ExternalId,
            firstName,
            lastName,
            ZohoFieldReader.LookupId(record.Payload, "Account_Name", "Account"),
            ZohoFieldReader.String(record.Payload, "Email"),
            ZohoFieldReader.String(record.Payload, "Phone"),
            ZohoFieldReader.String(record.Payload, "Mobile", "Mobile_Phone"),
            ZohoFieldReader.String(record.Payload, "Title", "Job_Title"),
            ZohoFieldReader.Bool(record.Payload, "Is_Primary", "Primary_Contact"));
    }

    private static CrmCanonicalLead MapLead(CrmExternalRecord record)
    {
        var firstName = ZohoFieldReader.String(record.Payload, "First_Name", "first_name");
        var lastName = ZohoFieldReader.String(record.Payload, "Last_Name", "last_name");
        return new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            record.ExternalId,
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "created_time"),
            record.ModifiedAt,
            ZohoFieldReader.String(record.Payload, "Full_Name", "Last_Name", "Company")
                ?? JoinName(firstName, lastName)
                ?? record.ExternalId,
            ZohoFieldReader.String(record.Payload, "Company"),
            ZohoFieldReader.String(record.Payload, "Email"),
            ZohoFieldReader.String(record.Payload, "Phone"),
            ZohoFieldReader.String(record.Payload, "Lead_Status", "Status"),
            ZohoFieldReader.String(record.Payload, "Lead_Source", "LeadSource"),
            NormalizePlaceholderDate(ZohoFieldReader.DateTimeOffset(record.Payload, "Last_Activity_Time", "Last_Contact")),
            NormalizePlaceholderDate(ZohoFieldReader.DateTimeOffset(record.Payload, "Last_Call")),
            ZohoFieldReader.Int32(record.Payload, "Calls_Since_Conversation") ?? 0,
            ZohoFieldReader.Int32(record.Payload, "Call_Attempts", "anrufversuche") ?? 0,
            ZohoFieldReader.LookupId(record.Payload, "Account_Name", "Account"),
            ZohoFieldReader.LookupId(record.Payload, "Contact_Name", "Contact"),
            ZohoFieldReader.LookupId(record.Payload, "Owner"));
    }

    private static CrmCanonicalProduct MapProduct(CrmExternalRecord record)
        => new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            record.ExternalId,
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "created_time"),
            record.ModifiedAt,
            ZohoFieldReader.String(record.Payload, "Product_Code") ?? record.ExternalId,
            ZohoFieldReader.String(record.Payload, "Product_Name", "Name") ?? record.ExternalId,
            ZohoFieldReader.String(record.Payload, "Description"),
            ZohoFieldReader.LookupId(record.Payload, "Product_Category", "Category"),
            ZohoFieldReader.String(record.Payload, "Product_Category", "Category"),
            IsActive(record.Payload));

    private static CrmCanonicalPipeline MapPipeline(CrmExternalRecord record)
        => new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            record.ExternalId,
            record.Payload,
            null,
            null,
            ZohoFieldReader.String(record.Payload, "id") ?? record.ExternalId,
            ZohoFieldReader.String(record.Payload, "pipeline_name", "display_value", "name") ?? record.ExternalId,
            ZohoFieldReader.String(record.Payload, "description"),
            ZohoFieldReader.Int32(record.Payload, "sequence_number") ?? 0);

    private static CrmCanonicalPipelineStage MapPipelineStage(CrmExternalRecord record)
    {
        var name = ZohoFieldReader.String(record.Payload, "pick_list_value", "display_value", "name") ?? record.ExternalId;
        var probability = ZohoFieldReader.Decimal(record.Payload, "probability", "Probability");
        if (probability > 1) probability /= 100;
        var stageType = GetStageType(name, record.Payload);
        return new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            record.ExternalId,
            record.Payload,
            null,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Modified_Time", "modified_time"),
            ZohoFieldReader.String(record.Payload, "pipeline_id", "pipeline_external_id") ?? "default",
            ZohoFieldReader.String(record.Payload, "id", "actual_value", "pick_list_value", "display_value")
                ?? record.ExternalId,
            name,
            stageType,
            ZohoFieldReader.Int32(record.Payload, "sequence_number") ?? 0,
            probability,
            stageType is "won" or "lost");
    }

    private static CrmCanonicalDeal MapDeal(CrmExternalRecord record)
    {
        var pipeline = ZohoFieldReader.String(record.Payload, "Pipeline");
        var stage = ZohoFieldReader.String(record.Payload, "Stage");
        return new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            record.ExternalId,
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "CreatedTime"),
            record.ModifiedAt,
            ZohoFieldReader.String(record.Payload, "Deal_Name", "Name") ?? record.ExternalId,
            ZohoFieldReader.LookupId(record.Payload, "Account_Name", "Account"),
            ZohoFieldReader.Decimal(record.Payload, "Amount"),
            ZohoFieldReader.String(record.Payload, "Currency", "Currency_Code"),
            pipeline,
            stage,
            ZohoFieldReader.String(record.Payload, "Product_Name", "Product", "Produkt"),
            ZohoFieldReader.Decimal(record.Payload, "Contract_Term", "Duration_Months", "Laufzeit"),
            ZohoFieldReader.Date(record.Payload, "Contract_Start_Date", "Contract_Start", "Start_Date"),
            ZohoFieldReader.Date(record.Payload, "Contract_End_Date", "Vertragsende"),
            ZohoFieldReader.Date(record.Payload, "Closing_Date", "closing_date"),
            ZohoFieldReader.String(record.Payload, "Stage", "Status"),
            ZohoFieldReader.String(record.Payload, "Reason_for_Loss__s", "Loss_Reason", "verlustgrund"),
            NormalizePlaceholderDate(ZohoFieldReader.DateTimeOffset(record.Payload, "Last_Activity_Time")),
            ZohoFieldReader.LookupId(record.Payload, "Owner", "owner"),
            ZohoFieldReader.LookupId(record.Payload, "Pipeline") ?? pipeline,
            ZohoFieldReader.LookupId(record.Payload, "Stage") ?? stage,
            ZohoFieldReader.LookupId(record.Payload, "Product", "Product_Name"));
    }

    private static CrmCanonicalDealStageHistory MapDealStageHistory(CrmExternalRecord record)
    {
        var stage = ZohoFieldReader.String(record.Payload, "Stage", "Stage_Name", "To", "New_Value")
            ?? ZohoFieldReader.String(record.Payload, "From", "Old_Value")
            ?? "unknown";
        var enteredAt = ZohoFieldReader.DateTimeOffset(
                record.Payload,
                "Date", "Stage_Changed_Time", "Created_Time", "Modified_Time", "Last_Modified_Time")
            ?? record.ModifiedAt
            ?? DateTimeOffset.UtcNow;
        return new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            record.ExternalId,
            record.Payload,
            enteredAt,
            record.ModifiedAt,
            record.Relations?.FirstOrDefault()?.ExternalId
                ?? ZohoFieldReader.String(record.Payload, "Deal_Id")
                ?? throw new InvalidOperationException("Die Zoho-Stage-Historie besitzt keine Deal-Zuordnung."),
            ZohoFieldReader.String(record.Payload, "Pipeline"),
            ZohoFieldReader.LookupId(record.Payload, "Stage"),
            stage,
            enteredAt,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Exited_At", "End_Date"));
    }

    private static CrmCanonicalActivity MapActivity(CrmExternalRecord record)
    {
        var type = record.Module.Equals("Emails", StringComparison.OrdinalIgnoreCase)
            ? "email"
            : record.Module.Equals("Calls", StringComparison.OrdinalIgnoreCase) ? "call" : "task";
        var occurredAt = NormalizePlaceholderDate(ZohoFieldReader.DateTimeOffset(
                record.Payload,
                "Call_Start_Time", "time", "Sent_Time", "Received_Time", "Date_Time", "Due_Date", "Created_Time"))
            ?? record.ModifiedAt
            ?? DateTimeOffset.UtcNow;
        var relations = (record.Relations ?? [])
            .Concat(LookupRelations(record.Payload, "What_Id", "Who_Id"))
            .DistinctBy(item => $"{item.EntityType}:{item.ExternalId}")
            .ToArray();
        var duration = ZohoFieldReader.Int32(record.Payload, "Call_Duration", "Duration", "Duration_Seconds");
        return new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            $"{record.Module}:{record.ExternalId}",
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "created_time"),
            record.ModifiedAt,
            type,
            ZohoFieldReader.String(record.Payload, "Subject", "subject", "Event_Title", "Task_Name"),
            occurredAt,
            duration,
            ZohoFieldReader.String(record.Payload, "Call_Type", "Direction"),
            ZohoFieldReader.String(record.Payload, "Call_Status", "Connection_Status"),
            duration is >= 20 ? "conversation" : duration is not null ? "attempt" : null,
            duration is not null ? duration >= 20 : null,
            ZohoFieldReader.String(record.Payload, "Call_Result", "Status", "Result"),
            ZohoFieldReader.LookupId(record.Payload, "Owner", "owner"),
            relations);
    }

    private static CrmCanonicalAppointment MapAppointment(CrmExternalRecord record)
    {
        var startsAt = ZohoFieldReader.DateTimeOffset(record.Payload, "Start_DateTime", "Start_Time", "Start")
            ?? record.ModifiedAt
            ?? DateTimeOffset.UtcNow;
        var endsAt = ZohoFieldReader.DateTimeOffset(record.Payload, "End_DateTime", "End_Time", "End")
            ?? startsAt.AddMinutes(30);
        var status = ZohoFieldReader.String(record.Payload, "Event_Status", "Status") ?? "planned";
        if (ZohoFieldReader.Bool(record.Payload, "$event_cancelled", "Cancelled")) status = "cancelled";
        var relations = (record.Relations ?? [])
            .Concat(LookupRelations(record.Payload, "What_Id", "Who_Id"))
            .DistinctBy(item => $"{item.EntityType}:{item.ExternalId}")
            .ToArray();
        return new(
            CrmProviders.Zoho,
            record.ConnectionKey(),
            $"{record.Module}:{record.ExternalId}",
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "created_time"),
            record.ModifiedAt,
            ZohoFieldReader.String(record.Payload, "Event_Title", "Subject"),
            startsAt,
            endsAt,
            status,
            ZohoFieldReader.String(record.Payload, "Type", "Appointment_Type"),
            ZohoFieldReader.LookupId(record.Payload, "Owner"),
            ZohoFieldReader.DateTimeOffset(record.Payload, "Original_Start_DateTime"),
            ZohoFieldReader.Int32(record.Payload, "Reschedule_Count") ?? 0,
            relations);
    }

    private static IEnumerable<CrmRecordRelation> LookupRelations(JsonElement payload, params string[] fieldNames)
    {
        foreach (var fieldName in fieldNames)
        {
            var externalId = ZohoFieldReader.LookupId(payload, fieldName);
            if (string.IsNullOrWhiteSpace(externalId)) continue;
            var entityType = fieldName.Equals("Who_Id", StringComparison.OrdinalIgnoreCase)
                ? CrmEntityTypes.Contact
                : CrmEntityTypes.Customer;
            yield return new CrmRecordRelation(entityType, externalId, "related_to");
        }
    }

    private static string GetStageType(string value, JsonElement payload)
    {
        var normalized = (value + " " + (ZohoFieldReader.String(
                payload,
                "forecast_type",
                "forecast_category",
                "Forecast_Type",
                "Forecast_Category") ?? string.Empty))
            .Trim()
            .ToLowerInvariant();
        if (normalized.Contains("won") || normalized.Contains("gewonnen")) return "won";
        if (normalized.Contains("lost") || normalized.Contains("verloren")) return "lost";
        if (normalized is "open" or "offen") return "open";
        return "other";
    }

    private static string? JoinName(string? firstName, string? lastName)
    {
        var value = string.Join(' ', new[] { firstName, lastName }.Where(item => !string.IsNullOrWhiteSpace(item)));
        return value.Length == 0 ? null : value;
    }

    private static DateTimeOffset? NormalizePlaceholderDate(DateTimeOffset? value)
        => value is { Year: <= 1900 } ? null : value;

    private static string? NormalizeCountryCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToUpperInvariant();
        return normalized switch
        {
            "DEUTSCHLAND" or "GERMANY" => "DE",
            "ÖSTERREICH" or "AUSTRIA" => "AT",
            "SCHWEIZ" or "SWITZERLAND" => "CH",
            _ when normalized.Length is 2 or 3 && normalized.All(char.IsAsciiLetter) => normalized,
            _ => null
        };
    }

    private static string? NormalizeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim().ToLowerInvariant();
        if (Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : $"https://{text}", UriKind.Absolute, out var uri))
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return text.Trim().TrimEnd('/');
    }

    private static bool IsActive(JsonElement payload)
    {
        var value = ZohoFieldReader.String(payload, "Product_Active", "Active");
        return string.IsNullOrWhiteSpace(value)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("active", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value == "1";
    }
}

internal static class CrmExternalRecordExtensions
{
    public static string ConnectionKey(this CrmExternalRecord record) => record.ConnectionKey;
}
