using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Workflow.Domain;
using Nexus.SharedKernel.Context;

namespace Nexus.Modules.Workflow.Infrastructure;

public sealed class WorkflowDbContext(
    DbContextOptions<WorkflowDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock)
    : ModuleDbContextBase(options, tenantContext, currentUser, clock)
{
    public override string SchemaName => "workflow";

    public DbSet<WorkflowDefinition> Definitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowVersion> Versions => Set<WorkflowVersion>();
    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();
    public DbSet<ApprovalTask> ApprovalTasks => Set<ApprovalTask>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();
    public DbSet<WorkflowHistory> History => Set<WorkflowHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowDefinition>(e =>
        {
            e.ToTable("definitions", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ResourceType).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        modelBuilder.Entity<WorkflowVersion>(e =>
        {
            e.ToTable("versions", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.ApproverRoleName).HasMaxLength(100);
            e.HasIndex(x => new { x.DefinitionId, x.VersionNumber }).IsUnique();
            e.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowInstance>(e =>
        {
            e.ToTable("instances", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.ResourceType).HasMaxLength(100).IsRequired();
            e.Property(x => x.ResourceId).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.PayloadSummaryJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkflowVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovalTask>(e =>
        {
            e.ToTable("approval_tasks", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.AssignedRoleName).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.WorkflowInstanceId, x.StepOrder }).IsUnique();
            e.HasOne<WorkflowInstance>().WithMany().HasForeignKey(x => x.WorkflowInstanceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovalDecision>(e =>
        {
            e.ToTable("approval_decisions", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.HasIndex(x => x.ApprovalTaskId).IsUnique();
            e.HasOne<ApprovalTask>().WithMany().HasForeignKey(x => x.ApprovalTaskId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkflowHistory>(e =>
        {
            e.ToTable("history", SchemaName);
            e.HasKey(x => x.Id);
            e.Property(x => x.FromStatus).HasMaxLength(32).IsRequired();
            e.Property(x => x.ToStatus).HasMaxLength(32).IsRequired();
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => new { x.WorkflowInstanceId, x.OccurredAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
