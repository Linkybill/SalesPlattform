using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations.Zoho;

public static class ImportFieldRegressions
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        var mapper = new ZohoCrmRecordMapper();
        CrmCanonicalRecord Map(string module, string json) => mapper.Map(new("zoho", module, "synthetic",
            JsonSerializer.Deserialize<JsonElement>(json), null));

        foreach (var module in new[] { "Events", "Meetings", "Appointments" })
        {
            foreach (var empty in new[] { "null", "\"\"", "\"   \"" })
            {
                var meeting = (CrmCanonicalAppointment)Map(module, "{\"Type\":" + empty
                    + ",\"Appointment_Type\":\"Folgetermin\",\"Start_DateTime\":\"2026-10-09T09:00:00Z\",\"End_DateTime\":\"2026-10-09T10:00:00Z\"}");
                Check(meeting.AppointmentType == "Folgetermin", module + ": empty primary type must not hide populated alternate type.");
            }
        }
        var explicitType = (CrmCanonicalAppointment)Map("Events", "{\"Type\":\"Erstgespräch\",\"Appointment_Type\":\"Folgetermin\"}");
        Check(explicitType.AppointmentType == "Erstgespräch", "Preserve existing priority for populated, conflicting fields.");

        foreach (var empty in new[] { "null", "\"\"", "\"   \"" })
        {
            var deal = (CrmCanonicalDeal)Map("Deals", "{\"Product_Name\":" + empty
                + ",\"Product\":{\"id\":\"product-1\",\"name\":\"Synthetic product\"},\"Amount\":0}");
            Check(deal.ProductName == "Synthetic product" && deal.ProductExternalId == "product-1",
                "Empty primary product must retain alternate name and lookup ID.");
            Check(deal.Amount == 0, "Zero is a valid amount, not a missing value.");
        }
        var localized = (CrmCanonicalDeal)Map("Deals",
            "{\"Product_Name\":null,\"Product\":null,\"Produkt\":{\"id\":\"product-2\",\"name\":\"Synthetic localized product\"}}");
        Check(localized.ProductName == "Synthetic localized product" && localized.ProductExternalId == "product-2",
            "Supported localized product lookup must map both its name and ID.");
        Check(mapper.GetPreferredFields("Deals").Contains("Produkt"),
            "Hook fetches must request the supported localized product alias.");
        var product = (CrmCanonicalProduct)Map("Products", "{\"Product_Name\":\"Synthetic\",\"Product_Active\":false,\"Active\":true}");
        Check(!product.IsActive, "False is a valid value and must not fall back to true.");

        var fields = Enumerable.Range(0, 65).Select(i => new CrmFieldMetadata("Custom_" + i, null, "text"))
            .Append(new("Produkt", "Produkt", "lookup")).ToArray();
        var schema = new ZohoSchemaCacheSnapshot(["Deals"],
            new Dictionary<string, IReadOnlyCollection<CrmFieldMetadata>> { ["Deals"] = fields },
            new Dictionary<string, IReadOnlyCollection<JsonElement>>(), [],
            new Dictionary<string, IReadOnlyCollection<JsonElement>>(), DateTimeOffset.UtcNow, null);
        var sync = new ZohoSyncService(null!, null!, mapper, null!, null!, null!, null!, null!, null!,
            NullLogger<ZohoSyncService>.Instance);
        var resolve = typeof(ZohoSyncService).GetMethod("ResolveFields", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var requested = (IReadOnlyCollection<string>)resolve.Invoke(sync, ["Deals", schema])!;
        Check(requested.Contains("Produkt") && requested.Count <= 50,
            "Supported product aliases must survive the schema field cap, even behind 65 custom fields.");
        Console.WriteLine($"Import field regressions: {checks} checks passed; synthetic payloads only.");
    }
}
