namespace SalesPlattform.Backend.Integrations.Abstractions;

public interface ICrmRecordMapper
{
    string ProviderKey { get; }

    IReadOnlyCollection<string> GetPreferredFields(string module);

    string GetEntityType(string module);

    CrmCanonicalRecord Map(CrmExternalRecord record);
}
