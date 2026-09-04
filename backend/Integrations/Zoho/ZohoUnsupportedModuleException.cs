namespace SalesPlattform.Backend.Integrations.Zoho;

public sealed class ZohoUnsupportedModuleException(string message) : InvalidOperationException(message);
