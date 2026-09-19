using System.Reflection;
using System.Text.Json.Nodes;
using SalesPlattform.Backend.Integrations.Abstractions;
using SalesPlattform.Backend.Integrations.Zoho;

// Exercise the actual payload builder used by both CreateTaskAsync and
// UpdateTaskAsync, without constructing services or sending CRM requests.
var builder = typeof(ZohoCrmAdapter).GetMethod("BuildTaskPayload", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("Task payload builder not found.");
JsonObject Build(CrmTaskWriteRequest request, bool create)
    => (JsonObject)builder.Invoke(null, [request, create])!;
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

const string recordId = "990000000000000001";
const string ownerId = "990000000000000002";
var cases = new (string Type, string Module)[]
{
    ("lead", "Leads"), ("customer", "Accounts"), ("deal", "Deals"),
    ("service-case", "Cases"), ("offer", "Quotes"),
    ("order", "Sales_Orders"), ("invoice", "Invoices")
};
var checkedCases = 0;
foreach (var (type, module) in cases)
foreach (var create in new[] { true, false })
foreach (var externalId in new[] { recordId, $" {module}:{recordId} " })
{
    var request = new CrmTaskWriteRequest(
        "Task mapping regression", new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero),
        "Synthetic test data", $" Users:{ownerId} ", $" {type.ToUpperInvariant()} ", externalId);
    var payload = Build(request, create);
    // Assert the wire JSON too, keeping large Zoho IDs as strings without loss.
    var wire = JsonNode.Parse(payload.ToJsonString())!.AsObject();
    Check(wire["What_Id"]?["id"]?.GetValue<string>() == recordId, $"{type}, create={create}: expected What_Id.id.");
    Check(!wire.ContainsKey("Who_Id"), $"{type}, create={create}: Who_Id must not receive a non-contact ID.");
    Check(wire["$se_module"]?.GetValue<string>() == module, $"Wrong module for {type}.");
    Check(wire["Owner"]?["id"]?.GetValue<string>() == ownerId, "Owner ID must stay normalized.");
    Check(wire["Subject"]?.GetValue<string>() == request.Subject, "Subject changed.");
    Check(wire["Description"]?.GetValue<string>() == request.Description, "Description changed.");
    Check(wire["Due_Date"]?.GetValue<string>() == "2026-09-20", "Due date changed.");
    if (create)
    {
        Check(wire["Status"]?.GetValue<string>() == "Not Started", "Create status default changed.");
        Check(wire["Priority"]?.GetValue<string>() == "High", "Create priority default changed.");
    }
    else
    {
        Check(!wire.ContainsKey("Status") && !wire.ContainsKey("Priority"), "Updates must preserve CRM status and priority.");
    }
    checkedCases++;
}

foreach (var create in new[] { true, false })
foreach (var target in new (string? Type, string? Id)[] { (null, null), ("lead", " "), ("unsupported", recordId) })
{
    var payload = Build(new CrmTaskWriteRequest("Unlinked task", null, null, null, target.Type, target.Id), create);
    Check(!payload.ContainsKey("Who_Id") && !payload.ContainsKey("What_Id") && !payload.ContainsKey("$se_module"),
        "Missing/unsupported target must not invent a CRM relationship.");
    Check(!payload.ContainsKey("Owner") && !payload.ContainsKey("Description") && !payload.ContainsKey("Due_Date"),
        "Missing optional values must stay omitted.");
    checkedCases++;
}
Console.WriteLine($"Zoho Task payload: {checkedCases} create/update mapping cases passed (no live CRM writes).");
