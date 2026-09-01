using System.Text.Json;
using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoCrmRecordMapper : ICrmRecordMapper
{
    private static readonly IReadOnlyDictionary<string, string[]> PreferredFields =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accounts"] =
            [
                "id", "Account_Name", "Name", "Industry", "Billing_Code", "Billing_City",
                "Billing_Country", "Billing_State", "Billing_Street", "Owner",
                "Account_Status", "Created_Time", "Modified_Time"
            ],
            ["Deals"] =
            [
                "id", "Deal_Name", "Account_Name", "Amount", "Currency", "Stage",
                "Pipeline", "Product_Name", "Product", "Contract_Term",
                "Contract_End_Date", "Closing_Date", "Owner", "Reason_for_Loss__s",
                "Last_Activity_Time", "Created_Time", "Modified_Time"
            ],
            ["Leads"] =
            [
                "id", "Full_Name", "Last_Name", "Company", "Email", "Phone",
                "Lead_Status", "Lead_Source", "Last_Activity_Time", "Last_Call",
                "Call_Attempts", "Owner", "Created_Time", "Modified_Time"
            ]
        };

    public string ProviderKey => CrmProviders.Zoho;

    public IReadOnlyCollection<string> GetPreferredFields(string module)
        => PreferredFields.TryGetValue(module, out var fields) ? fields : [];

    public string GetEntityType(string module)
        => module.ToLowerInvariant() switch
        {
            "accounts" => CrmEntityTypes.Customer,
            "deals" => CrmEntityTypes.Deal,
            "leads" => CrmEntityTypes.Lead,
            _ => module.ToLowerInvariant()
        };

    public CrmCanonicalRecord Map(CrmExternalRecord record)
    {
        return record.Module.ToLowerInvariant() switch
        {
            "accounts" => MapCustomer(record),
            "deals" => MapDeal(record),
            "leads" => MapLead(record),
            _ => throw new InvalidOperationException(
                $"Das Zoho-Modul '{record.Module}' besitzt noch kein kanonisches Mapping.")
        };
    }

    private static CrmCanonicalCustomer MapCustomer(CrmExternalRecord record)
    {
        var country = ZohoFieldReader.String(record.Payload, "Billing_Country", "Country");
        return new CrmCanonicalCustomer(
            CrmProviders.Zoho,
            "default",
            record.ExternalId,
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "CreatedTime"),
            record.ModifiedAt,
            ZohoFieldReader.String(record.Payload, "Account_Name", "Name") ?? record.ExternalId,
            ZohoFieldReader.String(record.Payload, "Industry"),
            ZohoFieldReader.String(record.Payload, "Billing_Code", "Zip", "Postal_Code"),
            ZohoFieldReader.String(record.Payload, "Billing_City", "City"),
            NormalizeCountryCode(country),
            ZohoFieldReader.String(record.Payload, "Account_Status", "Status"));
    }

    private static CrmCanonicalDeal MapDeal(CrmExternalRecord record)
        => new(
            CrmProviders.Zoho,
            "default",
            record.ExternalId,
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "CreatedTime"),
            record.ModifiedAt,
            ZohoFieldReader.String(record.Payload, "Deal_Name", "Name") ?? record.ExternalId,
            ZohoFieldReader.LookupId(record.Payload, "Account_Name", "Account"),
            ZohoFieldReader.Decimal(record.Payload, "Amount"),
            ZohoFieldReader.String(record.Payload, "Currency", "Currency_Code"),
            ZohoFieldReader.String(record.Payload, "Pipeline"),
            ZohoFieldReader.String(record.Payload, "Stage"),
            ZohoFieldReader.String(record.Payload, "Product_Name", "Product", "Produkt"),
            ZohoFieldReader.Decimal(record.Payload, "Contract_Term", "Duration_Months", "Laufzeit"),
            ZohoFieldReader.Date(record.Payload, "Contract_End_Date", "Vertragsende"),
            ZohoFieldReader.Date(record.Payload, "Closing_Date", "closing_date"),
            ZohoFieldReader.String(record.Payload, "Stage", "Status"),
            ZohoFieldReader.String(record.Payload, "Reason_for_Loss__s", "Loss_Reason", "verlustgrund"),
            ZohoFieldReader.DateTimeOffset(record.Payload, "Last_Activity_Time"));

    private static CrmCanonicalLead MapLead(CrmExternalRecord record)
        => new(
            CrmProviders.Zoho,
            "default",
            record.ExternalId,
            record.Payload,
            ZohoFieldReader.DateTimeOffset(record.Payload, "Created_Time", "CreatedTime"),
            record.ModifiedAt,
            ZohoFieldReader.String(record.Payload, "Full_Name", "Last_Name", "Company") ?? record.ExternalId,
            ZohoFieldReader.String(record.Payload, "Company"),
            ZohoFieldReader.String(record.Payload, "Email"),
            ZohoFieldReader.String(record.Payload, "Phone"),
            ZohoFieldReader.String(record.Payload, "Lead_Status", "Status"),
            ZohoFieldReader.String(record.Payload, "Lead_Source", "LeadSource"),
            ZohoFieldReader.DateTimeOffset(record.Payload, "Last_Call", "Last_Activity_Time", "Last_Contact"),
            ZohoFieldReader.Int32(record.Payload, "Call_Attempts", "anrufversuche"));

    private static string? NormalizeCountryCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length is 2 or 3 && normalized.All(char.IsAsciiLetter)
            ? normalized
            : null;
    }
}
