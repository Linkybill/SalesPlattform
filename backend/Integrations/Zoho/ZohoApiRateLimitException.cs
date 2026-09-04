using SalesPlattform.Backend.Integrations.Abstractions;

namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoApiRateLimitException(string message) : CrmApiRateLimitException(message);
