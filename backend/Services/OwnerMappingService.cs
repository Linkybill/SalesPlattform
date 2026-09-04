using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using IdentityPlatform.Shared.ApplicationSettings;
using IdentityPlatform.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesPlattform.Backend.Authorization;
using SalesPlattform.Backend.Data;

namespace SalesPlattform.Backend.Services;

public sealed record OwnerMappingCurrentUser(
    string? Subject,
    string? Email,
    string? DisplayName);

public sealed record CrmOwnerOption(
    Guid Id,
    string DisplayName,
    string? Email,
    bool IsActive);

public sealed record OwnerMappingDto(
    string? PlatformUserSubject,
    string PlatformUserEmail,
    Guid CrmOwnerId,
    string CrmOwnerName,
    string? CrmOwnerEmail,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy);

public sealed record OwnerMappingResponse(
    OwnerMappingCurrentUser CurrentUser,
    IReadOnlyCollection<CrmOwnerOption> CrmOwners,
    IReadOnlyCollection<OwnerMappingDto> Mappings);

public sealed record SaveOwnerMappingRequest(
    string PlatformUserEmail,
    Guid? CrmOwnerId,
    string? PlatformUserSubject = null);

/// <summary>
/// Stores the tenant-specific mapping between an Identity Platform account and
/// the corresponding CRM owner in the existing tenant application-settings
/// store. The CRM owner itself remains owned by the CRM synchronization.
/// </summary>
public sealed class OwnerMappingService(
    PlatformTenantDbContextFactory<SalesPlattformDbContext> dbFactory,
    IApplicationSettingsStore settingsStore,
    IOptions<ApplicationSettingsOptions> settingsOptions,
    TenantAdminAccessService tenantAdminAccess)
{
    public const string SettingKey = "crm.ownerMappings";

    private static readonly Regex EmailPattern = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public async Task<OwnerMappingResponse> GetAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        await EnsureTenantAdminAsync(user, cancellationToken);
        var currentUser = CurrentUser(user);
        var mappings = await LoadMappingsAsync(user, cancellationToken);

        await using var session = await dbFactory.OpenReadOnlyAsync(cancellationToken);
        var owners = await session.Context.SalesOwners
            .AsNoTracking()
            .OrderBy(owner => owner.DisplayName)
            .Select(owner => new CrmOwnerOption(
                owner.Id,
                owner.DisplayName,
                owner.Email,
                owner.IsActive))
            .ToArrayAsync(cancellationToken);

        var ownerById = owners.ToDictionary(owner => owner.Id);
        var ownerByEmail = owners
            .Where(owner => !string.IsNullOrWhiteSpace(owner.Email))
            .GroupBy(owner => NormalizeEmail(owner.Email) ?? owner.Email!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var responseMappings = mappings
            .Select(mapping => ResolveOwner(mapping, ownerById, ownerByEmail) is { } owner
                ? new OwnerMappingDto(
                    mapping.PlatformUserSubject,
                    mapping.PlatformUserEmail,
                    owner.Id,
                    owner.DisplayName,
                    owner.Email,
                    mapping.UpdatedAt,
                    mapping.UpdatedBy)
                : null)
            .Where(mapping => mapping is not null)
            .Cast<OwnerMappingDto>()
            .OrderBy(mapping => mapping.PlatformUserEmail)
            .ToArray();

        return new OwnerMappingResponse(currentUser, owners, responseMappings);
    }

    public async Task<OwnerMappingResponse> SaveAsync(
        SaveOwnerMappingRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        await EnsureTenantAdminAsync(user, cancellationToken);
        var email = NormalizeEmail(request.PlatformUserEmail);
        if (email is null)
            throw new ArgumentException("Bitte eine gültige Plattform-E-Mail-Adresse hinterlegen.", nameof(request));

        if (request.CrmOwnerId is null || request.CrmOwnerId == Guid.Empty)
            throw new ArgumentException("Bitte einen CRM-Besitzer auswählen.", nameof(request));

        await using var session = await dbFactory.OpenAsync(cancellationToken);
        var owner = await session.Context.SalesOwners
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == request.CrmOwnerId && candidate.IsActive, cancellationToken);
        if (owner is null)
            throw new ArgumentException("Der ausgewählte CRM-Besitzer ist nicht vorhanden oder nicht aktiv.", nameof(request));

        var mappings = await LoadMappingsAsync(user, cancellationToken);
        var subject = NormalizeSubject(request.PlatformUserSubject);
        var existing = mappings.FirstOrDefault(mapping =>
            (!string.IsNullOrWhiteSpace(subject)
                && string.Equals(mapping.PlatformUserSubject, subject, StringComparison.Ordinal))
            || string.Equals(mapping.PlatformUserEmail, email, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            mappings.Add(new PersistedOwnerMapping(
                subject,
                email,
                owner.Id,
                DateTimeOffset.UtcNow,
                ActorName(user),
                owner.Email));
        }
        else
        {
            existing.PlatformUserSubject = subject ?? existing.PlatformUserSubject;
            existing.PlatformUserEmail = email;
            existing.CrmOwnerId = owner.Id;
            existing.CrmOwnerEmail = owner.Email;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = ActorName(user);
        }

        await SaveMappingsAsync(user, mappings, cancellationToken);
        return await GetAsync(user, cancellationToken);
    }

    public async Task<OwnerMappingResponse> DeleteAsync(
        string platformUserEmail,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        await EnsureTenantAdminAsync(user, cancellationToken);
        var email = NormalizeEmail(platformUserEmail)
            ?? throw new ArgumentException("Bitte eine gültige Plattform-E-Mail-Adresse hinterlegen.", nameof(platformUserEmail));
        var mappings = await LoadMappingsAsync(user, cancellationToken);
        mappings.RemoveAll(mapping => string.Equals(mapping.PlatformUserEmail, email, StringComparison.OrdinalIgnoreCase));
        await SaveMappingsAsync(user, mappings, cancellationToken);
        return await GetAsync(user, cancellationToken);
    }

    public async Task<Guid?> ResolveOwnerIdAsync(
        ClaimsPrincipal user,
        SalesPlattformDbContext db,
        CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(UserEmail(user));
        var subject = NormalizeSubject(user.FindFirstValue("sub"));
        var mappings = await LoadMappingsAsync(user, cancellationToken);
        var mapping = mappings.FirstOrDefault(candidate =>
            (!string.IsNullOrWhiteSpace(subject)
                && string.Equals(candidate.PlatformUserSubject, subject, StringComparison.Ordinal))
            || (email is not null
                && string.Equals(candidate.PlatformUserEmail, email, StringComparison.OrdinalIgnoreCase)));

        if (mapping is not null
            && mapping.CrmOwnerId != Guid.Empty
            && await db.SalesOwners.AsNoTracking().AnyAsync(
                owner => owner.Id == mapping.CrmOwnerId && owner.IsActive,
                cancellationToken))
        {
            return mapping.CrmOwnerId;
        }

        if (mapping is not null && NormalizeEmail(mapping.CrmOwnerEmail) is { } mappedEmail)
        {
            var mappedOwnerId = await db.SalesOwners
                .AsNoTracking()
                .Where(owner => owner.IsActive
                    && owner.Email != null
                    && owner.Email.ToUpper() == mappedEmail.ToUpper())
                .Select(owner => (Guid?)owner.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (mappedOwnerId.HasValue)
                return mappedOwnerId;
        }

        if (email is null)
            return null;

        return await db.SalesOwners
            .AsNoTracking()
            .Where(owner => owner.Email != null && owner.Email.ToUpper() == email.ToUpper())
            .Select(owner => (Guid?)owner.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task EnsureTenantAdminAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (!await tenantAdminAccess.IsCurrentTenantAdminAsync(user, cancellationToken))
            throw new UnauthorizedAccessException("Nur Tenant-Administratoren dürfen CRM-Benutzerzuordnungen verwalten.");
    }

    private async Task<List<PersistedOwnerMapping>> LoadMappingsAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var context = SettingsContext(user);
        var value = (await settingsStore.LoadAsync(context, cancellationToken))
            .FirstOrDefault(item => string.Equals(item.Key, SettingKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Scope, ApplicationSettingScopes.TenantApp, StringComparison.OrdinalIgnoreCase));
        if (value is null)
            return [];

        try
        {
            if (value.Value.ValueKind == JsonValueKind.String)
            {
                var legacyJson = value.Value.GetString();
                return string.IsNullOrWhiteSpace(legacyJson)
                    ? []
                    : JsonSerializer.Deserialize<List<PersistedOwnerMapping>>(legacyJson, JsonOptions) ?? [];
            }

            return value.Value.Deserialize<List<PersistedOwnerMapping>>(JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Die Einstellung '{SettingKey}' enthält kein gültiges Mapping-JSON.");
        }
    }

    private async Task SaveMappingsAsync(
        ClaimsPrincipal user,
        IReadOnlyCollection<PersistedOwnerMapping> mappings,
        CancellationToken cancellationToken)
    {
        var context = SettingsContext(user);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(mappings, JsonOptions));
        await settingsStore.SetAsync(
            context,
            SettingKey,
            ApplicationSettingScopes.TenantApp,
            document.RootElement.Clone(),
            ActorName(user),
            cancellationToken);
    }

    private static CrmOwnerOption? ResolveOwner(
        PersistedOwnerMapping mapping,
        IReadOnlyDictionary<Guid, CrmOwnerOption> ownerById,
        IReadOnlyDictionary<string, CrmOwnerOption> ownerByEmail)
    {
        if (mapping.CrmOwnerId != Guid.Empty && ownerById.TryGetValue(mapping.CrmOwnerId, out var ownerByIdValue))
            return ownerByIdValue;

        return !string.IsNullOrWhiteSpace(mapping.CrmOwnerEmail)
            && ownerByEmail.TryGetValue(mapping.CrmOwnerEmail, out var ownerByEmailValue)
            ? ownerByEmailValue
            : null;
    }

    private ApplicationSettingsContext SettingsContext(ClaimsPrincipal user)
        => new(
            settingsOptions.Value.ApplicationKey,
            TenantId(user),
            Guid.Empty,
            user.FindFirstValue("sub") ?? "system:owner-mapping");

    private static OwnerMappingCurrentUser CurrentUser(ClaimsPrincipal user)
        => new(
            user.FindFirstValue("sub"),
            UserEmail(user),
            user.FindFirstValue("name")
                ?? user.FindFirstValue(ClaimTypes.Name)
                ?? UserEmail(user));

    private static string? UserEmail(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("email")
            ?? user.FindFirstValue("preferred_username");

    private static string? NormalizeEmail(string? email)
    {
        var value = email?.Trim();
        if (string.IsNullOrWhiteSpace(value) || !EmailPattern.IsMatch(value))
            return null;
        try
        {
            return new MailAddress(value).Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? NormalizeSubject(string? subject)
        => string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();

    private static string? ActorName(ClaimsPrincipal user)
        => user.FindFirstValue("email")
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue("sub");

    private static Guid TenantId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId) && tenantId != Guid.Empty
            ? tenantId
            : throw new InvalidOperationException("Der Access Token enthält keine gültige tenant_id.");

    private sealed class PersistedOwnerMapping(
        string? platformUserSubject,
        string platformUserEmail,
        Guid crmOwnerId,
        DateTimeOffset updatedAt,
        string? updatedBy,
        string? crmOwnerEmail = null)
    {
        public string? PlatformUserSubject { get; set; } = platformUserSubject;
        public string PlatformUserEmail { get; set; } = platformUserEmail;
        public Guid CrmOwnerId { get; set; } = crmOwnerId;
        public string? CrmOwnerEmail { get; set; } = crmOwnerEmail;
        public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
        public string? UpdatedBy { get; set; } = updatedBy;
    }
}
