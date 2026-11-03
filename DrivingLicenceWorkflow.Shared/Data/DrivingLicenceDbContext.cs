using DrivingLicenceWorkflow.Shared.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DrivingLicenceWorkflow.Shared.Data;

// Single context shared by Api and Worker; each registers it with its own connection string/role.
public class DrivingLicenceDbContext : DbContext
{
    public DrivingLicenceDbContext(DbContextOptions<DrivingLicenceDbContext> options) : base(options)
    {
    }

    public DbSet<Application> Applications => Set<Application>();
    public DbSet<AuditTrailEntry> AuditTrailEntries => Set<AuditTrailEntry>();
    public DbSet<ApplicationWorkflowState> WorkflowStates => Set<ApplicationWorkflowState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Table names must match the grants in docker/postgres/init/01-roles.sql.
        modelBuilder.Entity<Application>(entity =>
        {
            entity.ToTable("Applications");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).ValueGeneratedNever();
            entity.Property(a => a.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(a => a.LastName).IsRequired().HasMaxLength(100);
            entity.Property(a => a.DateOfBirth).HasColumnType("date");
            entity.Property(a => a.NationalId).IsRequired().HasMaxLength(50);
            entity.Property(a => a.Email).IsRequired().HasMaxLength(320);
            entity.Property(a => a.PhoneNumber).IsRequired().HasMaxLength(50);
            entity.Property(a => a.Address).IsRequired().HasMaxLength(500);
            entity.Property(a => a.LicenceCategory).IsRequired().HasMaxLength(10);
            entity.Property(a => a.PhotoBase64).IsRequired();
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(a => a.RejectionReason).HasMaxLength(1000);
            entity.Property(a => a.CreatedAt).HasColumnType("timestamp with time zone");
            entity.Property(a => a.UpdatedAt).HasColumnType("timestamp with time zone");
            entity.HasIndex(a => a.NationalId);
            entity.HasIndex(a => a.Status);

            entity.HasMany(a => a.AuditTrail)
                .WithOne()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditTrailEntry>(entity =>
        {
            entity.ToTable("AuditTrailEntry");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Timestamp).HasColumnType("timestamp with time zone");
            entity.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.Event).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Details).HasMaxLength(2000);
            entity.HasIndex(e => new { e.ApplicationId, e.Timestamp });
        });

        modelBuilder.Entity<ApplicationWorkflowState>(entity =>
        {
            entity.ToTable("ApplicationWorkflowState");
            entity.HasKey(s => s.CorrelationId);
            entity.Property(s => s.CorrelationId).ValueGeneratedNever();
            entity.Property(s => s.CurrentState).IsRequired().HasMaxLength(64);
            entity.Property(s => s.FailureReason).HasMaxLength(1000);
            entity.Property(s => s.RejectionReason).HasMaxLength(1000);
            entity.HasIndex(s => s.ApplicationId).IsUnique();

            entity.HasOne<Application>()
                .WithOne()
                .HasForeignKey<ApplicationWorkflowState>(s => s.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MassTransit outbox/inbox tables: OutboxMessage, OutboxState, InboxState.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
