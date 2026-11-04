using Microsoft.EntityFrameworkCore;

namespace SalesPlattform.Backend.Data;

/// <summary>Retained customer-local journal for a platform-authorized callback handoff.</summary>
public sealed class IntegrationHookHandoff : SalesEntity
{
    public Guid TransitionId { get; set; }
    public Guid InstallationId { get; set; }
    public long PlacementGeneration { get; set; }
    public required string ProviderKey { get; set; }
    public required string ConnectionKey { get; set; }
    public required string AddressMode { get; set; }
    public required string TargetBaseUrl { get; set; }
    public string Status { get; set; } = "pending";
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RelayConfirmedAt { get; set; }
    public string? RelayConfirmedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<IntegrationHookHandoffChannel> Channels { get; set; } = [];
}

public sealed class IntegrationHookHandoffChannel : SalesEntity
{
    public Guid HandoffId { get; set; }
    public required string Module { get; set; }
    public required string ChannelId { get; set; }
    public required string VerificationTokenHash { get; set; }
    public required string EventsJson { get; set; }
    public required string PreviousNotifyUrl { get; set; }
    public required string TargetNotifyUrl { get; set; }
    public DateTimeOffset? PatchAttemptedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? ErrorCode { get; set; }
    public IntegrationHookHandoff? Handoff { get; set; }
}

internal static class IntegrationHookHandoffModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<IntegrationHookHandoff>(b =>
        {
            b.ToTable("integration_hook_handoffs"); b.HasKey(x => x.Id);
            b.Property(x => x.ProviderKey).HasMaxLength(50);
            b.Property(x => x.ConnectionKey).HasMaxLength(100);
            b.Property(x => x.AddressMode).HasMaxLength(30);
            b.Property(x => x.TargetBaseUrl).HasMaxLength(2000);
            b.Property(x => x.Status).HasMaxLength(30);
            b.Property(x => x.ErrorCode).HasMaxLength(80);
            b.Property(x => x.RelayConfirmedBy).HasMaxLength(256);
            b.HasIndex(x => new { x.TenantId, x.ProviderKey, x.ConnectionKey, x.TransitionId }).IsUnique();
        });
        model.Entity<IntegrationHookHandoffChannel>(b =>
        {
            b.ToTable("integration_hook_handoff_channels"); b.HasKey(x => x.Id);
            b.Property(x => x.Module).HasMaxLength(100); b.Property(x => x.ChannelId).HasMaxLength(100);
            b.Property(x => x.VerificationTokenHash).HasMaxLength(128);
            b.Property(x => x.EventsJson).HasColumnType("jsonb");
            b.Property(x => x.PreviousNotifyUrl).HasMaxLength(2000); b.Property(x => x.TargetNotifyUrl).HasMaxLength(2000);
            b.Property(x => x.ErrorCode).HasMaxLength(80);
            b.HasIndex(x => new { x.TenantId, x.HandoffId, x.Module }).IsUnique();
            b.HasOne(x => x.Handoff).WithMany(x => x.Channels).HasForeignKey(x => x.HandoffId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
