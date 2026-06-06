using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using DataAccessLibrary.Models;

namespace DataAccessLibrary.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Core entities
        public DbSet<Setting> Settings { get; set; }
        public DbSet<SystemConfiguration> SystemConfigurations { get; set; }
        public DbSet<IdentityProvider> IdentityProviders { get; set; }
        public DbSet<DirectoryConnection> DirectoryConnections { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<ChangeAuditLog> ChangeAuditLogs { get; set; }

        // Access management
        public DbSet<AccessRequest> AccessRequests { get; set; }
        public DbSet<ApprovalWorkflow> ApprovalWorkflows { get; set; }
        public DbSet<ApprovalWorkflowNode> ApprovalWorkflowNodes { get; set; }
        public DbSet<ApprovalWorkflowConnection> ApprovalWorkflowConnections { get; set; }
        public DbSet<UserAccess> UserAccess { get; set; }

        // Policy management
        public DbSet<AccessPolicy> AccessPolicies { get; set; }
        public DbSet<PolicyCondition> PolicyConditions { get; set; }
        public DbSet<PolicyAction> PolicyActions { get; set; }

        // Sync and Identity management (REFACTORED SCHEMA)
        public DbSet<Identity> Identities { get; set; } // Formerly: Person
        public DbSet<IdentityObject> Objects { get; set; } // Formerly: Identity
        public DbSet<ObjectAttribute> ObjectAttributes { get; set; } // Formerly: IdentityAttribute
        public DbSet<Group> Groups { get; set; }
        public DbSet<GroupAttribute> GroupAttributes { get; set; }
        public DbSet<ObjectGroupMembership> ObjectGroupMemberships { get; set; } // Formerly: IdentityGroupMembership
        public DbSet<IdentityGroupMembership> IdentityGroupMemberships { get; set; } // Formerly: PersonGroupMembership
        public DbSet<SyncExecution> SyncExecutions { get; set; }
        public DbSet<IdentityMatchLog> IdentityMatchLogs { get; set; } // Formerly: PersonMatchLog

        // NEW: Tagging system
        public DbSet<ObjectTag> ObjectTags { get; set; }
        public DbSet<IdentityTag> IdentityTags { get; set; }
        public DbSet<MembershipTag> MembershipTags { get; set; }
        public DbSet<SyncStepTag> SyncStepTags { get; set; }

        // NEW: Workflow templates
        public DbSet<SyncProjectTemplate> SyncProjectTemplates { get; set; }
        public DbSet<SyncWorkflowTemplate> SyncWorkflowTemplates { get; set; }

        // Sync Project Management (UC-SYNC-03)
        public DbSet<SyncProject> SyncProjects { get; set; }
        public DbSet<SyncProjectChain> SyncProjectChains { get; set; }
        public DbSet<SyncWorkflow> SyncWorkflows { get; set; }
        public DbSet<SyncStep> SyncSteps { get; set; }
        public DbSet<AttributeMapping> AttributeMappings { get; set; }
        public DbSet<SyncProjectRun> SyncProjectRuns { get; set; }
        public DbSet<SyncStepRun> SyncStepRuns { get; set; }
        public DbSet<SyncAuditLog> SyncAuditLogs { get; set; }
        public DbSet<PostSyncTask> PostSyncTasks { get; set; }

        // Internal Sync Operations
        public DbSet<InternalSyncRun> InternalSyncRuns { get; set; }
        public DbSet<InternalSyncStep> InternalSyncSteps { get; set; }
        public DbSet<InternalSyncStepMapping> InternalSyncStepMappings { get; set; }
        public DbSet<InternalSyncStepRun> InternalSyncStepRuns { get; set; }

        // Dev Center - Processing Scripts
        public DbSet<SyncProcessingScript> SyncProcessingScripts { get; set; }
        public DbSet<SyncStepScript> SyncStepScripts { get; set; }
        public DbSet<SyncScriptExecution> SyncScriptExecutions { get; set; }

        // Workflow Tagging System
        public DbSet<Tag> Tags { get; set; }
        public DbSet<WorkflowTag> WorkflowTags { get; set; }

        // Schedule Templates
        public DbSet<ScheduleTemplate> ScheduleTemplates { get; set; }

        // Compliance and Policy Management
        public DbSet<ComplianceFramework> ComplianceFrameworks { get; set; }
        public DbSet<CompliancePolicy> CompliancePolicies { get; set; }
        public DbSet<ComplianceFrameworkPolicyMapping> ComplianceFrameworkPolicyMappings { get; set; }
        public DbSet<CompliancePolicyExecution> CompliancePolicyExecutions { get; set; }
        public DbSet<CompliancePolicyViolation> CompliancePolicyViolations { get; set; }

        // Framework Assignment System - Transforms frameworks from passive containers to active drivers
        public DbSet<FrameworkAssignment> FrameworkAssignments { get; set; }
        public DbSet<FrameworkAssignmentPolicyOverride> FrameworkAssignmentPolicyOverrides { get; set; }

        // Email and Notifications
        public DbSet<SMTPConfiguration> SMTPConfigurations { get; set; }
        public DbSet<EmailTemplate> EmailTemplates { get; set; }
        public DbSet<EmailQueueItem> EmailQueue { get; set; }
        public DbSet<TeamsMessageTemplate> TeamsMessageTemplates { get; set; }
        public DbSet<TeamsMessageQueueItem> TeamsMessageQueue { get; set; }
        public DbSet<AdminNotification> AdminNotifications { get; set; }

        // Ticketing/ITSM Integration
        public DbSet<TicketingConfiguration> TicketingConfigurations { get; set; }
        public DbSet<TicketingLog> TicketingLogs { get; set; }

        // Maintenance Settings (automated cleanup and database health)
        public DbSet<MaintenanceSettings> MaintenanceSettings { get; set; }

        // Access Review System (UC-GRP-01-04)
        public DbSet<Campaign> Campaigns { get; set; }
        public DbSet<AccessReviewAssignment> AccessReviewAssignments { get; set; }
        public DbSet<ReviewDecisionHistory> ReviewDecisionHistories { get; set; }
        public DbSet<RemediationAction> RemediationActions { get; set; }
        public DbSet<CampaignTemplate> CampaignTemplates { get; set; }
        public DbSet<AccessReviewSettings> AccessReviewSettings { get; set; }

        // Reporting System
        public DbSet<Report> Reports { get; set; }
        public DbSet<ReportColumn> ReportColumns { get; set; }
        public DbSet<ReportParameter> ReportParameters { get; set; }
        public DbSet<ReportSchedule> ReportSchedules { get; set; }
        public DbSet<ReportExecution> ReportExecutions { get; set; }
        public DbSet<UserReportFavorite> UserReportFavorites { get; set; }
        public DbSet<ReportTemplate> ReportTemplates { get; set; }

        // Job Scheduling and Execution History
        public DbSet<JobExecutionHistory> JobExecutionHistory { get; set; }

        // Remote Agent and Job Queue (for distributed processing)
        public DbSet<RemoteAgent> RemoteAgents { get; set; }
        public DbSet<JobQueueEntry> JobQueue { get; set; }
        public DbSet<ApiKey> ApiKeys { get; set; }

        // Business Roles (maps org roles to AD groups for workflow routing)
        public DbSet<BusinessRole> BusinessRoles { get; set; }
        public DbSet<BusinessRoleMember> BusinessRoleMembers { get; set; }
        public DbSet<BusinessRoleCategory> BusinessRoleCategories { get; set; }

        // Workflow Triggers (event-driven workflow automation)
        public DbSet<WorkflowTrigger> WorkflowTriggers { get; set; }
        public DbSet<TriggerCondition> TriggerConditions { get; set; }
        public DbSet<TriggerAction> TriggerActions { get; set; }
        public DbSet<TriggerEvent> TriggerEvents { get; set; }
        public DbSet<TriggerExecution> TriggerExecutions { get; set; }
        public DbSet<TriggerActionLog> TriggerActionLogs { get; set; }
        public DbSet<WorkflowTriggerTemplate> WorkflowTriggerTemplates { get; set; }

        // NOTE: Workflow templates are seeded via SQL file, not EF models
        // See: Seed-WorkflowTemplates-FIXED.sql

        // Organization Management (departments, divisions, teams, manager hierarchy)
        public DbSet<OrganizationalFolder> OrganizationalFolders { get; set; }
        public DbSet<OrganizationalFolderMember> OrganizationalFolderMembers { get; set; }
        public DbSet<OrganizationalFolderPolicy> OrganizationalFolderPolicies { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // GLOBAL CONVENTION: Set all foreign keys to Restrict (no cascade) to prevent SQL Server cascade cycle errors
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                foreach (var foreignKey in entityType.GetForeignKeys())
                {
                    foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
                }
            }

            // Configure encrypted fields
            builder.Entity<Setting>()
                .HasIndex(s => new { s.Category, s.Key })
                .IsUnique();

            // SQLite doesn't support nvarchar(max), use TEXT instead
            builder.Entity<IdentityProvider>()
                .Property(i => i.Configuration);

            builder.Entity<DirectoryConnection>()
                .Property(d => d.ConnectionString);

            builder.Entity<DirectoryConnection>()
                .Property(d => d.Credentials);

            // Configure audit log
            builder.Entity<AuditLog>()
                .HasIndex(a => a.Timestamp)
                .HasDatabaseName("IX_AuditLogs_Timestamp");

            builder.Entity<AuditLog>()
                .HasIndex(a => a.UserId)
                .HasDatabaseName("IX_AuditLogs_UserId");

            builder.Entity<AuditLog>()
                .HasIndex(a => new { a.EntityType, a.EntityId })
                .HasDatabaseName("IX_AuditLogs_Entity");

            // Configure ChangeAuditLog indexes
            builder.Entity<ChangeAuditLog>()
                .HasIndex(c => c.Timestamp)
                .HasDatabaseName("IX_ChangeAuditLogs_Timestamp");

            builder.Entity<ChangeAuditLog>()
                .HasIndex(c => c.EntityId)
                .HasDatabaseName("IX_ChangeAuditLogs_EntityId");

            builder.Entity<ChangeAuditLog>()
                .HasIndex(c => c.UserId)
                .HasDatabaseName("IX_ChangeAuditLogs_UserId");

            builder.Entity<ChangeAuditLog>()
                .HasIndex(c => c.OperationType)
                .HasDatabaseName("IX_ChangeAuditLogs_OperationType");

            builder.Entity<ChangeAuditLog>()
                .HasIndex(c => new { c.EntityType, c.EntityId })
                .HasDatabaseName("IX_ChangeAuditLogs_Entity");

            builder.Entity<ChangeAuditLog>()
                .HasIndex(c => c.CorrelationId)
                .HasDatabaseName("IX_ChangeAuditLogs_CorrelationId");

            builder.Entity<ChangeAuditLog>()
                .HasIndex(c => c.Source)
                .HasDatabaseName("IX_ChangeAuditLogs_Source");

            // Configure Internal Sync Step indexes and relationships
            builder.Entity<InternalSyncStep>()
                .HasIndex(s => new { s.SyncProjectId, s.ExecutionOrder })
                .HasDatabaseName("IX_InternalSyncSteps_Project_Order");

            builder.Entity<InternalSyncStep>()
                .HasOne(s => s.SyncProject)
                .WithMany()
                .HasForeignKey(s => s.SyncProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<InternalSyncStepMapping>()
                .HasIndex(m => m.InternalSyncStepId)
                .HasDatabaseName("IX_InternalSyncStepMappings_Step");

            builder.Entity<InternalSyncStepMapping>()
                .HasOne(m => m.Step)
                .WithMany(s => s.Mappings)
                .HasForeignKey(m => m.InternalSyncStepId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<InternalSyncStepRun>()
                .HasIndex(r => r.InternalSyncRunId)
                .HasDatabaseName("IX_InternalSyncStepRuns_Run");

            builder.Entity<InternalSyncStepRun>()
                .HasOne(r => r.Run)
                .WithMany(run => run.StepRuns)
                .HasForeignKey(r => r.InternalSyncRunId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<InternalSyncStepRun>()
                .HasOne(r => r.Step)
                .WithMany(s => s.StepRuns)
                .HasForeignKey(r => r.InternalSyncStepId)
                .OnDelete(DeleteBehavior.NoAction);

            // Configure access request relationships
            builder.Entity<AccessRequest>()
                .HasOne(ar => ar.Requester)
                .WithMany()
                .HasForeignKey(ar => ar.RequesterId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AccessRequest>()
                .HasOne(ar => ar.Approver)
                .WithMany()
                .HasForeignKey(ar => ar.ApproverId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure workflow relationships
            builder.Entity<ApprovalWorkflow>()
                .HasMany(w => w.Steps)
                .WithOne(s => s.Workflow)
                .HasForeignKey(s => s.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure workflow connection relationships - NoAction to avoid cascade cycles
            builder.Entity<ApprovalWorkflowConnection>()
                .HasOne(c => c.SourceNode)
                .WithMany()
                .HasForeignKey(c => c.SourceNodeId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<ApprovalWorkflowConnection>()
                .HasOne(c => c.TargetNode)
                .WithMany()
                .HasForeignKey(c => c.TargetNodeId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<ApprovalWorkflowConnection>()
                .HasOne(c => c.Workflow)
                .WithMany()
                .HasForeignKey(c => c.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure policy relationships
            builder.Entity<AccessPolicy>()
                .HasMany(p => p.Conditions)
                .WithOne(c => c.Policy)
                .HasForeignKey(c => c.PolicyId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<AccessPolicy>()
                .HasMany(p => p.Actions)
                .WithOne(a => a.Policy)
                .HasForeignKey(a => a.PolicyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Identity indexes (formerly Person)
            builder.Entity<Identity>()
                .HasIndex(i => i.PrimaryEmail)
                .HasDatabaseName("IX_Identities_PrimaryEmail");

            builder.Entity<Identity>()
                .HasIndex(i => new { i.FirstName, i.LastName, i.Department })
                .HasDatabaseName("IX_Identities_NameDepartment");

            builder.Entity<Identity>()
                .HasIndex(i => i.IsActive)
                .HasDatabaseName("IX_Identities_IsActive");

            builder.Entity<Identity>()
                .HasIndex(i => i.ManagerIdentityId)
                .HasDatabaseName("IX_Identities_ManagerIdentityId");

            // Configure Identity self-referencing manager relationship
            builder.Entity<Identity>()
                .HasOne(i => i.Manager)
                .WithMany(i => i.DirectReports)
                .HasForeignKey(i => i.ManagerIdentityId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure IdentityObject indexes and relationships (formerly Identity)
            builder.Entity<IdentityObject>()
                .HasIndex(o => new { o.SourceConnectionId, o.SourceUniqueId })
                .IsUnique()
                .HasDatabaseName("IX_Objects_SourceUnique");

            builder.Entity<IdentityObject>()
                .HasIndex(o => o.IdentityId)
                .HasDatabaseName("IX_Objects_IdentityId");

            builder.Entity<IdentityObject>()
                .HasIndex(o => o.Email)
                .HasDatabaseName("IX_Objects_Email");

            builder.Entity<IdentityObject>()
                .HasIndex(o => o.Username)
                .HasDatabaseName("IX_Objects_Username");

            builder.Entity<IdentityObject>()
                .HasIndex(o => o.IsActive)
                .HasDatabaseName("IX_Objects_IsActive");

            builder.Entity<IdentityObject>()
                .HasIndex(o => o.ManagerObjectId)
                .HasDatabaseName("IX_Objects_ManagerObjectId");

            // Configure IdentityObject relationships
            builder.Entity<IdentityObject>()
                .HasOne(o => o.Identity)
                .WithMany(i => i.Objects)
                .HasForeignKey(o => o.IdentityId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<IdentityObject>()
                .HasOne(o => o.SourceConnection)
                .WithMany()
                .HasForeignKey(o => o.SourceConnectionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure IdentityObject self-referencing manager relationship
            builder.Entity<IdentityObject>()
                .HasOne(o => o.Manager)
                .WithMany(o => o.DirectReports)
                .HasForeignKey(o => o.ManagerObjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure IdentityObject owner relationship (for groups)
            builder.Entity<IdentityObject>()
                .HasOne(o => o.OwnerIdentity)
                .WithMany()
                .HasForeignKey(o => o.OwnerIdentityId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<IdentityObject>()
                .HasIndex(o => o.OwnerIdentityId)
                .HasDatabaseName("IX_Objects_OwnerIdentityId")
                .HasFilter("[OwnerIdentityId] IS NOT NULL");

            // Configure ObjectAttribute indexes (formerly IdentityAttribute)
            builder.Entity<ObjectAttribute>()
                .HasIndex(oa => new { oa.ObjectId, oa.AttributeName })
                .HasDatabaseName("IX_ObjectAttributes_ObjectAttribute");

            // Configure Group indexes
            builder.Entity<Group>()
                .HasIndex(g => new { g.SourceConnectionId, g.SourceUniqueId })
                .IsUnique()
                .HasDatabaseName("IX_Groups_SourceUnique");

            builder.Entity<Group>()
                .HasIndex(g => g.Name)
                .HasDatabaseName("IX_Groups_Name");

            builder.Entity<Group>()
                .HasIndex(g => g.Email)
                .HasDatabaseName("IX_Groups_Email");

            builder.Entity<Group>()
                .HasIndex(g => g.IsActive)
                .HasDatabaseName("IX_Groups_IsActive");

            // Configure Group decimal precision
            builder.Entity<Group>()
                .Property(g => g.RiskScore)
                .HasPrecision(5, 2); // 0-100 with 2 decimal places

            // Configure Group relationships
            builder.Entity<Group>()
                .HasOne(g => g.SourceConnection)
                .WithMany()
                .HasForeignKey(g => g.SourceConnectionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure GroupAttribute indexes
            builder.Entity<GroupAttribute>()
                .HasIndex(ga => new { ga.GroupId, ga.AttributeName })
                .HasDatabaseName("IX_GroupAttributes_GroupAttribute");

            // Configure ObjectGroupMembership indexes and relationships (formerly IdentityGroupMembership)
            builder.Entity<ObjectGroupMembership>()
                .HasIndex(ogm => new { ogm.ObjectId, ogm.GroupId })
                .IsUnique()
                .HasDatabaseName("IX_ObjectGroupMemberships_ObjectGroup");

            builder.Entity<ObjectGroupMembership>()
                .HasIndex(ogm => ogm.GroupId)
                .HasDatabaseName("IX_ObjectGroupMemberships_GroupId");

            builder.Entity<ObjectGroupMembership>()
                .HasOne(ogm => ogm.Object)
                .WithMany(o => o.GroupMemberships)
                .HasForeignKey(ogm => ogm.ObjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ObjectGroupMembership>()
                .HasOne(ogm => ogm.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(ogm => ogm.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure IdentityGroupMembership indexes and relationships (formerly PersonGroupMembership)
            builder.Entity<IdentityGroupMembership>()
                .HasIndex(igm => new { igm.IdentityId, igm.GroupId })
                .IsUnique()
                .HasDatabaseName("IX_IdentityGroupMemberships_IdentityGroup");

            builder.Entity<IdentityGroupMembership>()
                .HasIndex(igm => igm.GroupId)
                .HasDatabaseName("IX_IdentityGroupMemberships_GroupId");

            builder.Entity<IdentityGroupMembership>()
                .HasOne(igm => igm.Identity)
                .WithMany(i => i.GroupMemberships)
                .HasForeignKey(igm => igm.IdentityId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<IdentityGroupMembership>()
                .HasOne(igm => igm.Group)
                .WithMany()
                .HasForeignKey(igm => igm.GroupId)
                .OnDelete(DeleteBehavior.Restrict);

            // TODO: Re-enable after adding SourceObjectId column to database
            // builder.Entity<IdentityGroupMembership>()
            //     .HasOne(igm => igm.SourceObject)
            //     .WithMany()
            //     .HasForeignKey(igm => igm.SourceObjectId)
            //     .OnDelete(DeleteBehavior.Restrict);

            // Configure SyncExecution indexes
            builder.Entity<SyncExecution>()
                .HasIndex(se => se.DirectoryConnectionId)
                .HasDatabaseName("IX_SyncExecutions_DirectoryConnectionId");

            builder.Entity<SyncExecution>()
                .HasIndex(se => se.StartedAt)
                .HasDatabaseName("IX_SyncExecutions_StartedAt");

            builder.Entity<SyncExecution>()
                .HasIndex(se => se.Status)
                .HasDatabaseName("IX_SyncExecutions_Status");

            // Configure IdentityMatchLog indexes (formerly PersonMatchLog)
            builder.Entity<IdentityMatchLog>()
                .HasIndex(iml => iml.IdentityId)
                .HasDatabaseName("IX_IdentityMatchLogs_IdentityId");

            builder.Entity<IdentityMatchLog>()
                .HasIndex(iml => iml.ObjectId)
                .HasDatabaseName("IX_IdentityMatchLogs_ObjectId");

            builder.Entity<IdentityMatchLog>()
                .HasIndex(iml => iml.MatchedAt)
                .HasDatabaseName("IX_IdentityMatchLogs_MatchedAt");

            // Configure SyncProject indexes and relationships (UC-SYNC-03)
            builder.Entity<SyncProject>()
                .HasIndex(sp => sp.SourceConnectionId)
                .HasDatabaseName("IX_SyncProjects_SourceConnectionId");

            builder.Entity<SyncProject>()
                .HasIndex(sp => sp.TargetConnectionId)
                .HasDatabaseName("IX_SyncProjects_TargetConnectionId");

            builder.Entity<SyncProject>()
                .HasIndex(sp => sp.Name)
                .HasDatabaseName("IX_SyncProjects_Name");

            builder.Entity<SyncProject>()
                .HasIndex(sp => sp.IsEnabled)
                .HasDatabaseName("IX_SyncProjects_IsEnabled");

            builder.Entity<SyncProject>()
                .HasIndex(sp => sp.IsRunning)
                .HasDatabaseName("IX_SyncProjects_IsRunning");

            builder.Entity<SyncProject>()
                .HasIndex(sp => sp.NextScheduledRunAt)
                .HasDatabaseName("IX_SyncProjects_NextScheduledRunAt");

            builder.Entity<SyncProject>()
                .HasOne(sp => sp.SourceConnection)
                .WithMany()
                .HasForeignKey(sp => sp.SourceConnectionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SyncProject>()
                .HasOne(sp => sp.TargetConnection)
                .WithMany()
                .HasForeignKey(sp => sp.TargetConnectionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SyncProject>()
                .HasMany(sp => sp.Workflows)
                .WithOne(sw => sw.SyncProject)
                .HasForeignKey(sw => sw.SyncProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SyncProject>()
                .HasMany(sp => sp.Runs)
                .WithOne(spr => spr.SyncProject)
                .HasForeignKey(spr => spr.SyncProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure SyncWorkflow indexes and relationships
            builder.Entity<SyncWorkflow>()
                .HasIndex(sw => sw.SyncProjectId)
                .HasDatabaseName("IX_SyncWorkflows_SyncProjectId");

            builder.Entity<SyncWorkflow>()
                .HasIndex(sw => new { sw.SyncProjectId, sw.ExecutionOrder })
                .HasDatabaseName("IX_SyncWorkflows_ProjectOrder");

            builder.Entity<SyncWorkflow>()
                .HasIndex(sw => sw.ObjectClass)
                .HasDatabaseName("IX_SyncWorkflows_ObjectClass");

            builder.Entity<SyncWorkflow>()
                .HasIndex(sw => sw.WorkflowType)
                .HasDatabaseName("IX_SyncWorkflows_WorkflowType");

            builder.Entity<SyncWorkflow>()
                .HasIndex(sw => sw.IsEnabled)
                .HasDatabaseName("IX_SyncWorkflows_IsEnabled");

            builder.Entity<SyncWorkflow>()
                .HasMany(sw => sw.Steps)
                .WithOne(ss => ss.SyncWorkflow)
                .HasForeignKey(ss => ss.SyncWorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure SyncStep indexes and relationships
            builder.Entity<SyncStep>()
                .HasIndex(ss => ss.SyncWorkflowId)
                .HasDatabaseName("IX_SyncSteps_SyncWorkflowId");

            builder.Entity<SyncStep>()
                .HasIndex(ss => new { ss.SyncWorkflowId, ss.ExecutionOrder })
                .HasDatabaseName("IX_SyncSteps_WorkflowOrder");

            builder.Entity<SyncStep>()
                .HasIndex(ss => ss.ObjectClass)
                .HasDatabaseName("IX_SyncSteps_ObjectClass");

            builder.Entity<SyncStep>()
                .HasIndex(ss => ss.IsEnabled)
                .HasDatabaseName("IX_SyncSteps_IsEnabled");

            builder.Entity<SyncStep>()
                .HasMany(ss => ss.AttributeMappings)
                .WithOne(am => am.SyncStep)
                .HasForeignKey(am => am.SyncStepId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure AttributeMapping indexes
            builder.Entity<AttributeMapping>()
                .HasIndex(am => am.SyncStepId)
                .HasDatabaseName("IX_AttributeMappings_SyncStepId");

            builder.Entity<AttributeMapping>()
                .HasIndex(am => new { am.SyncStepId, am.SourceAttribute })
                .HasDatabaseName("IX_AttributeMappings_StepSource");

            builder.Entity<AttributeMapping>()
                .HasIndex(am => am.IsEnabled)
                .HasDatabaseName("IX_AttributeMappings_IsEnabled");

            builder.Entity<AttributeMapping>()
                .HasIndex(am => am.UseForMatching)
                .HasDatabaseName("IX_AttributeMappings_UseForMatching");

            // Configure SyncProjectRun indexes and relationships
            builder.Entity<SyncProjectRun>()
                .HasIndex(spr => spr.SyncProjectId)
                .HasDatabaseName("IX_SyncProjectRuns_SyncProjectId");

            builder.Entity<SyncProjectRun>()
                .HasIndex(spr => spr.StartedAt)
                .HasDatabaseName("IX_SyncProjectRuns_StartedAt");

            builder.Entity<SyncProjectRun>()
                .HasIndex(spr => spr.Status)
                .HasDatabaseName("IX_SyncProjectRuns_Status");

            builder.Entity<SyncProjectRun>()
                .HasIndex(spr => spr.TriggerType)
                .HasDatabaseName("IX_SyncProjectRuns_TriggerType");

            builder.Entity<SyncProjectRun>()
                .HasMany(spr => spr.StepRuns)
                .WithOne(ssr => ssr.SyncProjectRun)
                .HasForeignKey(ssr => ssr.SyncProjectRunId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure SyncStepRun indexes and relationships
            builder.Entity<SyncStepRun>()
                .HasIndex(ssr => ssr.SyncProjectRunId)
                .HasDatabaseName("IX_SyncStepRuns_SyncProjectRunId");

            builder.Entity<SyncStepRun>()
                .HasIndex(ssr => ssr.SyncStepId)
                .HasDatabaseName("IX_SyncStepRuns_SyncStepId");

            builder.Entity<SyncStepRun>()
                .HasIndex(ssr => ssr.Status)
                .HasDatabaseName("IX_SyncStepRuns_Status");

            builder.Entity<SyncStepRun>()
                .HasIndex(ssr => new { ssr.SyncProjectRunId, ssr.SyncStepId })
                .HasDatabaseName("IX_SyncStepRuns_ProjectStep");

            builder.Entity<SyncStepRun>()
                .HasOne(ssr => ssr.SyncStep)
                .WithMany(ss => ss.StepRuns)
                .HasForeignKey(ssr => ssr.SyncStepId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure decimal precision for SyncStepRun metrics
            builder.Entity<SyncStepRun>()
                .Property(ssr => ssr.AvgProcessingTimeMs)
                .HasPrecision(18, 2);

            builder.Entity<SyncStepRun>()
                .HasMany(ssr => ssr.AuditLogs)
                .WithOne(sal => sal.SyncStepRun)
                .HasForeignKey(sal => sal.SyncStepRunId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure SyncAuditLog indexes and relationships
            builder.Entity<SyncAuditLog>()
                .HasIndex(sal => sal.SyncStepRunId)
                .HasDatabaseName("IX_SyncAuditLogs_SyncStepRunId");

            builder.Entity<SyncAuditLog>()
                .HasIndex(sal => sal.ObjectId)
                .HasDatabaseName("IX_SyncAuditLogs_ObjectId");

            builder.Entity<SyncAuditLog>()
                .HasIndex(sal => sal.OperationType)
                .HasDatabaseName("IX_SyncAuditLogs_OperationType");

            builder.Entity<SyncAuditLog>()
                .HasIndex(sal => sal.Timestamp)
                .HasDatabaseName("IX_SyncAuditLogs_Timestamp");

            builder.Entity<SyncAuditLog>()
                .HasOne(sal => sal.Object)
                .WithMany()
                .HasForeignKey(sal => sal.ObjectId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure decimal precision for SyncAuditLog metrics
            builder.Entity<SyncAuditLog>()
                .Property(sal => sal.ProcessingTimeMs)
                .HasPrecision(18, 2);

            // ============================================================================
            // DEV CENTER - PROCESSING SCRIPTS CONFIGURATION
            // ============================================================================

            // Configure SyncProcessingScript indexes
            builder.Entity<SyncProcessingScript>()
                .HasIndex(sps => sps.Name)
                .HasDatabaseName("IX_SyncProcessingScripts_Name");

            builder.Entity<SyncProcessingScript>()
                .HasIndex(sps => sps.ScriptType)
                .HasDatabaseName("IX_SyncProcessingScripts_ScriptType");

            builder.Entity<SyncProcessingScript>()
                .HasIndex(sps => sps.Category)
                .HasDatabaseName("IX_SyncProcessingScripts_Category");

            builder.Entity<SyncProcessingScript>()
                .HasIndex(sps => sps.IsSystem)
                .HasDatabaseName("IX_SyncProcessingScripts_IsSystem");

            builder.Entity<SyncProcessingScript>()
                .HasIndex(sps => sps.IsEnabled)
                .HasDatabaseName("IX_SyncProcessingScripts_IsEnabled");

            // Configure SyncStepScript indexes and relationships
            builder.Entity<SyncStepScript>()
                .HasIndex(sss => sss.SyncStepId)
                .HasDatabaseName("IX_SyncStepScripts_SyncStepId");

            builder.Entity<SyncStepScript>()
                .HasIndex(sss => sss.ScriptId)
                .HasDatabaseName("IX_SyncStepScripts_ScriptId");

            builder.Entity<SyncStepScript>()
                .HasIndex(sss => new { sss.SyncStepId, sss.ExecutionPhase, sss.ExecutionOrder })
                .HasDatabaseName("IX_SyncStepScripts_StepPhaseOrder");

            builder.Entity<SyncStepScript>()
                .HasOne(sss => sss.SyncStep)
                .WithMany(ss => ss.StepScripts)
                .HasForeignKey(sss => sss.SyncStepId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SyncStepScript>()
                .HasOne(sss => sss.Script)
                .WithMany(sps => sps.StepScripts)
                .HasForeignKey(sss => sss.ScriptId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure SyncScriptExecution indexes and relationships
            builder.Entity<SyncScriptExecution>()
                .HasIndex(sse => sse.SyncStepRunId)
                .HasDatabaseName("IX_SyncScriptExecutions_SyncStepRunId");

            builder.Entity<SyncScriptExecution>()
                .HasIndex(sse => sse.ScriptId)
                .HasDatabaseName("IX_SyncScriptExecutions_ScriptId");

            builder.Entity<SyncScriptExecution>()
                .HasIndex(sse => sse.StartedAt)
                .HasDatabaseName("IX_SyncScriptExecutions_StartedAt");

            builder.Entity<SyncScriptExecution>()
                .HasIndex(sse => sse.Status)
                .HasDatabaseName("IX_SyncScriptExecutions_Status");

            builder.Entity<SyncScriptExecution>()
                .HasOne(sse => sse.StepRun)
                .WithMany()
                .HasForeignKey(sse => sse.SyncStepRunId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SyncScriptExecution>()
                .HasOne(sse => sse.Script)
                .WithMany(sps => sps.Executions)
                .HasForeignKey(sse => sse.ScriptId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Tag indexes
            builder.Entity<Tag>()
                .HasIndex(t => t.Name)
                .IsUnique()
                .HasDatabaseName("IX_Tags_Name");

            builder.Entity<Tag>()
                .HasIndex(t => t.Category)
                .HasDatabaseName("IX_Tags_Category");

            builder.Entity<Tag>()
                .HasIndex(t => t.IsSystem)
                .HasDatabaseName("IX_Tags_IsSystem");

            // Configure WorkflowTag indexes and relationships
            builder.Entity<WorkflowTag>()
                .HasIndex(wt => wt.SyncWorkflowId)
                .HasDatabaseName("IX_WorkflowTags_SyncWorkflowId");

            builder.Entity<WorkflowTag>()
                .HasIndex(wt => wt.TagId)
                .HasDatabaseName("IX_WorkflowTags_TagId");

            builder.Entity<WorkflowTag>()
                .HasIndex(wt => new { wt.SyncWorkflowId, wt.TagId })
                .IsUnique()
                .HasDatabaseName("IX_WorkflowTags_WorkflowTag");

            builder.Entity<WorkflowTag>()
                .HasOne(wt => wt.SyncWorkflow)
                .WithMany(sw => sw.WorkflowTags)
                .HasForeignKey(wt => wt.SyncWorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WorkflowTag>()
                .HasOne(wt => wt.Tag)
                .WithMany(t => t.WorkflowTags)
                .HasForeignKey(wt => wt.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure ObjectTag indexes and relationships
            builder.Entity<ObjectTag>()
                .HasIndex(ot => ot.ObjectId)
                .HasDatabaseName("IX_ObjectTags_ObjectId");

            builder.Entity<ObjectTag>()
                .HasIndex(ot => ot.TagId)
                .HasDatabaseName("IX_ObjectTags_TagId");

            builder.Entity<ObjectTag>()
                .HasIndex(ot => new { ot.ObjectId, ot.TagId })
                .IsUnique()
                .HasDatabaseName("IX_ObjectTags_ObjectTag");

            builder.Entity<ObjectTag>()
                .HasOne(ot => ot.Object)
                .WithMany(o => o.Tags)
                .HasForeignKey(ot => ot.ObjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ObjectTag>()
                .HasOne(ot => ot.Tag)
                .WithMany(t => t.ObjectTags)
                .HasForeignKey(ot => ot.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure IdentityTag indexes and relationships
            builder.Entity<IdentityTag>()
                .HasIndex(it => it.IdentityId)
                .HasDatabaseName("IX_IdentityTags_IdentityId");

            builder.Entity<IdentityTag>()
                .HasIndex(it => it.TagId)
                .HasDatabaseName("IX_IdentityTags_TagId");

            builder.Entity<IdentityTag>()
                .HasIndex(it => new { it.IdentityId, it.TagId })
                .IsUnique()
                .HasDatabaseName("IX_IdentityTags_IdentityTag");

            builder.Entity<IdentityTag>()
                .HasOne(it => it.Identity)
                .WithMany(i => i.Tags)
                .HasForeignKey(it => it.IdentityId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<IdentityTag>()
                .HasOne(it => it.Tag)
                .WithMany(t => t.IdentityTags)
                .HasForeignKey(it => it.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure MembershipTag indexes and relationships
            builder.Entity<MembershipTag>()
                .HasIndex(mt => mt.MembershipId)
                .HasDatabaseName("IX_MembershipTags_MembershipId");

            builder.Entity<MembershipTag>()
                .HasIndex(mt => mt.TagId)
                .HasDatabaseName("IX_MembershipTags_TagId");

            builder.Entity<MembershipTag>()
                .HasIndex(mt => new { mt.MembershipId, mt.TagId })
                .IsUnique()
                .HasDatabaseName("IX_MembershipTags_MembershipTag");

            builder.Entity<MembershipTag>()
                .HasOne(mt => mt.Membership)
                .WithMany(m => m.Tags)
                .HasForeignKey(mt => mt.MembershipId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<MembershipTag>()
                .HasOne(mt => mt.Tag)
                .WithMany(t => t.MembershipTags)
                .HasForeignKey(mt => mt.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure SyncProjectTemplate indexes and relationships
            builder.Entity<SyncProjectTemplate>()
                .HasIndex(spt => spt.Name)
                .HasDatabaseName("IX_SyncProjectTemplates_Name");

            builder.Entity<SyncProjectTemplate>()
                .HasIndex(spt => spt.Category)
                .HasDatabaseName("IX_SyncProjectTemplates_Category");

            builder.Entity<SyncProjectTemplate>()
                .HasIndex(spt => spt.IsSystem)
                .HasDatabaseName("IX_SyncProjectTemplates_IsSystem");

            builder.Entity<SyncProjectTemplate>()
                .HasMany(spt => spt.WorkflowTemplates)
                .WithOne(swt => swt.ProjectTemplate)
                .HasForeignKey(swt => swt.ProjectTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure SyncWorkflowTemplate indexes and relationships
            builder.Entity<SyncWorkflowTemplate>()
                .HasIndex(swt => swt.ProjectTemplateId)
                .HasDatabaseName("IX_SyncWorkflowTemplates_ProjectTemplateId");

            builder.Entity<SyncWorkflowTemplate>()
                .HasIndex(swt => swt.ObjectClass)
                .HasDatabaseName("IX_SyncWorkflowTemplates_ObjectClass");

            // ============================================================================
            // ACCESS REVIEW SYSTEM - EF Core Configuration
            // Models: Campaign, AccessReviewAssignment, ReviewDecisionHistory, RemediationAction
            // ============================================================================

            // Campaign indexes
            builder.Entity<Campaign>()
                .HasIndex(c => c.Status)
                .HasDatabaseName("IX_Campaigns_Status");

            builder.Entity<Campaign>()
                .HasIndex(c => c.StartDate)
                .HasDatabaseName("IX_Campaigns_StartDate");

            builder.Entity<Campaign>()
                .HasIndex(c => c.DueDate)
                .HasDatabaseName("IX_Campaigns_DueDate");

            builder.Entity<Campaign>()
                .HasIndex(c => c.CampaignType)
                .HasDatabaseName("IX_Campaigns_CampaignType");

            builder.Entity<Campaign>()
                .HasIndex(c => c.ComplianceFramework)
                .HasDatabaseName("IX_Campaigns_ComplianceFramework");

            // AccessReviewAssignment indexes and relationships
            builder.Entity<AccessReviewAssignment>()
                .HasIndex(a => a.CampaignId)
                .HasDatabaseName("IX_AccessReviewAssignments_CampaignId");

            builder.Entity<AccessReviewAssignment>()
                .HasIndex(a => a.ReviewerId)
                .HasDatabaseName("IX_AccessReviewAssignments_ReviewerId");

            builder.Entity<AccessReviewAssignment>()
                .HasIndex(a => a.Status)
                .HasDatabaseName("IX_AccessReviewAssignments_Status");

            builder.Entity<AccessReviewAssignment>()
                .HasIndex(a => a.ReviewTargetId)
                .HasDatabaseName("IX_AccessReviewAssignments_ReviewTargetId");

            builder.Entity<AccessReviewAssignment>()
                .HasIndex(a => new { a.CampaignId, a.ReviewerId })
                .HasDatabaseName("IX_AccessReviewAssignments_Campaign_Reviewer");

            // ReviewDecisionHistory indexes (immutable audit trail)
            builder.Entity<ReviewDecisionHistory>()
                .HasIndex(h => h.AssignmentId)
                .HasDatabaseName("IX_ReviewDecisionHistory_AssignmentId");

            builder.Entity<ReviewDecisionHistory>()
                .HasIndex(h => h.CampaignId)
                .HasDatabaseName("IX_ReviewDecisionHistory_CampaignId");

            builder.Entity<ReviewDecisionHistory>()
                .HasIndex(h => h.DecisionDate)
                .HasDatabaseName("IX_ReviewDecisionHistory_DecisionDate");

            builder.Entity<ReviewDecisionHistory>()
                .HasIndex(h => h.Decision)
                .HasDatabaseName("IX_ReviewDecisionHistory_Decision");

            // RemediationAction indexes
            builder.Entity<RemediationAction>()
                .HasIndex(r => r.AssignmentId)
                .HasDatabaseName("IX_RemediationActions_AssignmentId");

            builder.Entity<RemediationAction>()
                .HasIndex(r => r.CampaignId)
                .HasDatabaseName("IX_RemediationActions_CampaignId");

            builder.Entity<RemediationAction>()
                .HasIndex(r => r.Status)
                .HasDatabaseName("IX_RemediationActions_Status");

            builder.Entity<RemediationAction>()
                .HasIndex(r => new { r.Status, r.ScheduledFor })
                .HasDatabaseName("IX_RemediationActions_Status_ScheduledFor");

            // CampaignTemplate indexes
            builder.Entity<CampaignTemplate>()
                .HasIndex(t => t.TemplateType)
                .HasDatabaseName("IX_CampaignTemplates_TemplateType");

            builder.Entity<CampaignTemplate>()
                .HasIndex(t => t.ComplianceFramework)
                .HasDatabaseName("IX_CampaignTemplates_ComplianceFramework");

            builder.Entity<CampaignTemplate>()
                .HasIndex(t => t.IsActive)
                .HasDatabaseName("IX_CampaignTemplates_IsActive");

            // ============================================================================
            // DECIMAL PRECISION CONFIGURATION
            // Prevent silent truncation warnings
            // NOTE: Only configure for tables that exist in the database
            // Compliance tables are configured in their own migration
            // ============================================================================

            // Campaign table - configure if exists
            builder.Entity<Campaign>()
                .Property(c => c.CompletionPercentage)
                .HasPrecision(5, 2);

            // Compliance tables - These may not exist yet in all deployments
            // Configure them when the compliance migration runs
            builder.Entity<ComplianceFramework>()
                .Property(cf => cf.ComplianceScore)
                .HasPrecision(5, 2);

            builder.Entity<ComplianceFrameworkPolicyMapping>()
                .Property(cfpm => cfpm.CoveragePercentage)
                .HasPrecision(5, 2);

            builder.Entity<CompliancePolicyRule>()
                .Property(cpr => cpr.Weight)
                .HasPrecision(5, 2);

            builder.Entity<CompliancePolicyViolation>()
                .Property(cpv => cpv.ViolationScore)
                .HasPrecision(5, 2);

            // ============================================================================
            // FRAMEWORK ASSIGNMENT CONFIGURATION
            // Transforms frameworks from passive containers to active policy execution drivers
            // ============================================================================

            // Configure FrameworkAssignment indexes
            builder.Entity<FrameworkAssignment>()
                .HasIndex(fa => fa.FrameworkId)
                .HasDatabaseName("IX_FrameworkAssignments_FrameworkId");

            builder.Entity<FrameworkAssignment>()
                .HasIndex(fa => fa.ConnectionId)
                .HasDatabaseName("IX_FrameworkAssignments_ConnectionId")
                .HasFilter("[ConnectionId] IS NOT NULL");

            builder.Entity<FrameworkAssignment>()
                .HasIndex(fa => fa.DepartmentId)
                .HasDatabaseName("IX_FrameworkAssignments_DepartmentId")
                .HasFilter("[DepartmentId] IS NOT NULL");

            builder.Entity<FrameworkAssignment>()
                .HasIndex(fa => fa.ApplicationId)
                .HasDatabaseName("IX_FrameworkAssignments_ApplicationId")
                .HasFilter("[ApplicationId] IS NOT NULL");

            builder.Entity<FrameworkAssignment>()
                .HasIndex(fa => fa.IsActive)
                .HasDatabaseName("IX_FrameworkAssignments_IsActive");

            builder.Entity<FrameworkAssignment>()
                .HasIndex(fa => fa.LastEvaluatedAt)
                .HasDatabaseName("IX_FrameworkAssignments_LastEvaluatedAt");

            // Unique constraint: Only one active assignment per framework+connection combination
            builder.Entity<FrameworkAssignment>()
                .HasIndex(fa => new { fa.FrameworkId, fa.ConnectionId })
                .IsUnique()
                .HasDatabaseName("IX_FrameworkAssignments_FrameworkConnection")
                .HasFilter("[ConnectionId] IS NOT NULL AND [IsActive] = 1");

            // Configure FrameworkAssignment relationships
            builder.Entity<FrameworkAssignment>()
                .HasOne(fa => fa.Framework)
                .WithMany()
                .HasForeignKey(fa => fa.FrameworkId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<FrameworkAssignment>()
                .HasOne(fa => fa.Connection)
                .WithMany()
                .HasForeignKey(fa => fa.ConnectionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<FrameworkAssignment>()
                .HasMany(fa => fa.PolicyOverrides)
                .WithOne(po => po.Assignment)
                .HasForeignKey(po => po.AssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure FrameworkAssignment decimal precision
            builder.Entity<FrameworkAssignment>()
                .Property(fa => fa.ComplianceScore)
                .HasPrecision(5, 2);

            // Configure FrameworkAssignmentPolicyOverride indexes
            builder.Entity<FrameworkAssignmentPolicyOverride>()
                .HasIndex(po => po.AssignmentId)
                .HasDatabaseName("IX_FrameworkAssignmentPolicyOverrides_AssignmentId");

            builder.Entity<FrameworkAssignmentPolicyOverride>()
                .HasIndex(po => po.PolicyId)
                .HasDatabaseName("IX_FrameworkAssignmentPolicyOverrides_PolicyId");

            builder.Entity<FrameworkAssignmentPolicyOverride>()
                .HasIndex(po => new { po.AssignmentId, po.PolicyId })
                .IsUnique()
                .HasDatabaseName("IX_FrameworkAssignmentPolicyOverrides_AssignmentPolicy");

            // Configure FrameworkAssignmentPolicyOverride relationships
            builder.Entity<FrameworkAssignmentPolicyOverride>()
                .HasOne(po => po.Policy)
                .WithMany()
                .HasForeignKey(po => po.PolicyId)
                .OnDelete(DeleteBehavior.Restrict);

            // ============================================================================
            // SYNC PROJECT CHAINING CONFIGURATION
            // Support for multi-project workflows: ProjectA -> ProjectB -> ProjectC
            // ============================================================================

            builder.Entity<SyncProjectChain>()
                .HasIndex(spc => spc.SourceProjectId)
                .HasDatabaseName("IX_SyncProjectChains_SourceProjectId");

            builder.Entity<SyncProjectChain>()
                .HasIndex(spc => spc.TargetProjectId)
                .HasDatabaseName("IX_SyncProjectChains_TargetProjectId");

            builder.Entity<SyncProjectChain>()
                .HasIndex(spc => new { spc.SourceProjectId, spc.TargetProjectId })
                .IsUnique()
                .HasDatabaseName("IX_SyncProjectChains_SourceTarget");

            builder.Entity<SyncProjectChain>()
                .HasOne(spc => spc.SourceProject)
                .WithMany(sp => sp.OutgoingChains)
                .HasForeignKey(spc => spc.SourceProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SyncProjectChain>()
                .HasOne(spc => spc.TargetProject)
                .WithMany(sp => sp.IncomingChains)
                .HasForeignKey(spc => spc.TargetProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure SyncProject self-reference for SourceSyncProjectId
            // Note: Using NoAction to avoid cascade cycle issues in SQL Server
            builder.Entity<SyncProject>()
                .HasOne(sp => sp.SourceSyncProject)
                .WithMany()
                .HasForeignKey(sp => sp.SourceSyncProjectId)
                .OnDelete(DeleteBehavior.NoAction);

            // Seed default data
            SeedDefaultData(builder);
        }

        private void SeedDefaultData(ModelBuilder builder)
        {
            // Seed default roles - Use fixed GUIDs to prevent migration regeneration
            const string adminRoleId = "9c960570-0226-4d4a-a3bb-6e3507d6b509";
            const string userManagerRoleId = "3e055850-ecfa-4e16-abf2-a764a0fba89f";
            const string auditViewerRoleId = "5af6d2aa-47dd-4732-aa1c-1f7b8473d03d";

            var roleCreatedDate = new DateTime(2025, 10, 12, 17, 12, 4, 370, DateTimeKind.Utc).AddTicks(1989);

            builder.Entity<ApplicationRole>().HasData(
                new ApplicationRole
                {
                    Id = adminRoleId,
                    Name = "Admin",
                    NormalizedName = "ADMIN",
                    Description = "Full system administration access",
                    CreatedAt = roleCreatedDate
                },
                new ApplicationRole
                {
                    Id = userManagerRoleId,
                    Name = "UserManager",
                    NormalizedName = "USERMANAGER",
                    Description = "Can manage users and groups",
                    CreatedAt = roleCreatedDate.AddTicks(18)
                },
                new ApplicationRole
                {
                    Id = auditViewerRoleId,
                    Name = "AuditViewer",
                    NormalizedName = "AUDITVIEWER",
                    Description = "Can view audit logs and reports",
                    CreatedAt = roleCreatedDate.AddTicks(20)
                }
            );

            // Seed default settings - Use fixed timestamp to prevent migration regeneration
            var seedDate = new DateTime(2025, 10, 12, 17, 12, 4, 370, DateTimeKind.Utc).AddTicks(2135);

            builder.Entity<Setting>().HasData(
                new Setting
                {
                    Id = 1,
                    Category = "Security",
                    Key = "SessionTimeout",
                    Value = "30",
                    IsEncrypted = false,
                    DataType = "int",
                    ModifiedAt = seedDate
                },
                new Setting
                {
                    Id = 2,
                    Category = "Security",
                    Key = "MaxFailedAttempts",
                    Value = "5",
                    IsEncrypted = false,
                    DataType = "int",
                    ModifiedAt = seedDate.AddTicks(2)
                },
                new Setting
                {
                    Id = 3,
                    Category = "Security",
                    Key = "LockoutDuration",
                    Value = "30",
                    IsEncrypted = false,
                    DataType = "int",
                    ModifiedAt = seedDate.AddTicks(3)
                }
            );

            // Seed default system configuration - Use fixed timestamp to prevent migration regeneration
            builder.Entity<SystemConfiguration>().HasData(
                new SystemConfiguration
                {
                    Id = 1,
                    AllowSelfRegistration = false,
                    RequireEmailConfirmation = false,
                    AllowExternalLogins = true,
                    MinimumPasswordLength = 8,
                    RequireDigit = true,
                    RequireLowercase = true,
                    RequireUppercase = true,
                    RequireNonAlphanumeric = true,
                    MaxFailedAccessAttempts = 5,
                    LockoutDurationMinutes = 30,
                    SessionTimeoutMinutes = 30,
                    SlidingExpiration = true,
                    EnableAuditLogging = true,
                    AuditRetentionDays = 90,
                    CreatedAt = new DateTime(2025, 10, 12, 17, 12, 4, 370, DateTimeKind.Utc).AddTicks(2170)
                }
            );

            // Seed Schedule Templates - Built-in schedule presets
            var scheduleCreatedAt = new DateTime(2025, 11, 30, 18, 0, 0, DateTimeKind.Utc);
            builder.Entity<ScheduleTemplate>().HasData(
                // HOURLY SCHEDULES
                new ScheduleTemplate
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    Name = "Every Hour",
                    Description = "Runs at the top of every hour",
                    Category = "Hourly",
                    CronExpression = "0 0 * * * ?",
                    SortOrder = 1,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-clock",
                    Color = "#3b82f6",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                    Name = "Every 2 Hours",
                    Description = "Runs every 2 hours starting at midnight",
                    Category = "Hourly",
                    CronExpression = "0 0 0/2 * * ?",
                    SortOrder = 2,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-clock",
                    Color = "#3b82f6",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                    Name = "Every 4 Hours",
                    Description = "Runs every 4 hours (6 times per day)",
                    Category = "Hourly",
                    CronExpression = "0 0 0/4 * * ?",
                    SortOrder = 3,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-clock",
                    Color = "#3b82f6",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000004"),
                    Name = "Every 6 Hours",
                    Description = "Runs every 6 hours (4 times per day)",
                    Category = "Hourly",
                    CronExpression = "0 0 0/6 * * ?",
                    SortOrder = 4,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-clock",
                    Color = "#3b82f6",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000005"),
                    Name = "Every 8 Hours",
                    Description = "Runs every 8 hours (3 times per day)",
                    Category = "Hourly",
                    CronExpression = "0 0 0/8 * * ?",
                    SortOrder = 5,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-clock",
                    Color = "#3b82f6",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("10000000-0000-0000-0000-000000000006"),
                    Name = "Every 12 Hours (Twice Daily)",
                    Description = "Runs at midnight and noon",
                    Category = "Hourly",
                    CronExpression = "0 0 0,12 * * ?",
                    SortOrder = 6,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-clock",
                    Color = "#3b82f6",
                    CreatedAt = scheduleCreatedAt
                },

                // DAILY SCHEDULES
                new ScheduleTemplate
                {
                    Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                    Name = "Daily at Midnight",
                    Description = "Runs every day at 12:00 AM",
                    Category = "Daily",
                    CronExpression = "0 0 0 * * ?",
                    SortOrder = 1,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-sun",
                    Color = "#10b981",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                    Name = "Daily at 2 AM",
                    Description = "Runs every day at 2:00 AM (recommended for low-traffic)",
                    Category = "Daily",
                    CronExpression = "0 0 2 * * ?",
                    SortOrder = 2,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-sun",
                    Color = "#10b981",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("20000000-0000-0000-0000-000000000003"),
                    Name = "Daily at 6 AM",
                    Description = "Runs every day at 6:00 AM (before business hours)",
                    Category = "Daily",
                    CronExpression = "0 0 6 * * ?",
                    SortOrder = 3,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-sun",
                    Color = "#10b981",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("20000000-0000-0000-0000-000000000004"),
                    Name = "Daily at Noon",
                    Description = "Runs every day at 12:00 PM",
                    Category = "Daily",
                    CronExpression = "0 0 12 * * ?",
                    SortOrder = 4,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-sun",
                    Color = "#10b981",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("20000000-0000-0000-0000-000000000005"),
                    Name = "Daily at 6 PM",
                    Description = "Runs every day at 6:00 PM (after business hours)",
                    Category = "Daily",
                    CronExpression = "0 0 18 * * ?",
                    SortOrder = 5,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-sun",
                    Color = "#10b981",
                    CreatedAt = scheduleCreatedAt
                },

                // WEEKLY SCHEDULES
                new ScheduleTemplate
                {
                    Id = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                    Name = "Weekly on Sunday at 2 AM",
                    Description = "Runs every Sunday at 2:00 AM",
                    Category = "Weekly",
                    CronExpression = "0 0 2 ? * SUN",
                    SortOrder = 1,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-week",
                    Color = "#8b5cf6",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("30000000-0000-0000-0000-000000000002"),
                    Name = "Weekly on Monday at 6 AM",
                    Description = "Runs every Monday at 6:00 AM (start of work week)",
                    Category = "Weekly",
                    CronExpression = "0 0 6 ? * MON",
                    SortOrder = 2,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-week",
                    Color = "#8b5cf6",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("30000000-0000-0000-0000-000000000003"),
                    Name = "Weekly on Friday at 6 PM",
                    Description = "Runs every Friday at 6:00 PM (end of work week)",
                    Category = "Weekly",
                    CronExpression = "0 0 18 ? * FRI",
                    SortOrder = 3,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-week",
                    Color = "#8b5cf6",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("30000000-0000-0000-0000-000000000004"),
                    Name = "Weekly on Saturday at 2 AM",
                    Description = "Runs every Saturday at 2:00 AM",
                    Category = "Weekly",
                    CronExpression = "0 0 2 ? * SAT",
                    SortOrder = 4,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-week",
                    Color = "#8b5cf6",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("30000000-0000-0000-0000-000000000005"),
                    Name = "Weekdays at 6 AM",
                    Description = "Runs Monday through Friday at 6:00 AM",
                    Category = "Weekly",
                    CronExpression = "0 0 6 ? * MON-FRI",
                    SortOrder = 5,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-week",
                    Color = "#8b5cf6",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("30000000-0000-0000-0000-000000000006"),
                    Name = "Weekends at 3 AM",
                    Description = "Runs Saturday and Sunday at 3:00 AM",
                    Category = "Weekly",
                    CronExpression = "0 0 3 ? * SAT,SUN",
                    SortOrder = 6,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-week",
                    Color = "#8b5cf6",
                    CreatedAt = scheduleCreatedAt
                },

                // MONTHLY SCHEDULES
                new ScheduleTemplate
                {
                    Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                    Name = "Monthly on the 1st at 2 AM",
                    Description = "Runs on the 1st day of every month at 2:00 AM",
                    Category = "Monthly",
                    CronExpression = "0 0 2 1 * ?",
                    SortOrder = 1,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-alt",
                    Color = "#f59e0b",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("40000000-0000-0000-0000-000000000002"),
                    Name = "Monthly on the 15th at 2 AM",
                    Description = "Runs on the 15th day of every month at 2:00 AM",
                    Category = "Monthly",
                    CronExpression = "0 0 2 15 * ?",
                    SortOrder = 2,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-alt",
                    Color = "#f59e0b",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("40000000-0000-0000-0000-000000000003"),
                    Name = "Monthly on Last Day at 11 PM",
                    Description = "Runs on the last day of every month at 11:00 PM",
                    Category = "Monthly",
                    CronExpression = "0 0 23 L * ?",
                    SortOrder = 3,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-alt",
                    Color = "#f59e0b",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("40000000-0000-0000-0000-000000000004"),
                    Name = "Twice Monthly (1st & 15th)",
                    Description = "Runs on the 1st and 15th of every month at 2:00 AM",
                    Category = "Monthly",
                    CronExpression = "0 0 2 1,15 * ?",
                    SortOrder = 4,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-alt",
                    Color = "#f59e0b",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("40000000-0000-0000-0000-000000000005"),
                    Name = "First Monday of Month at 6 AM",
                    Description = "Runs on the first Monday of every month at 6:00 AM",
                    Category = "Monthly",
                    CronExpression = "0 0 6 ? * MON#1",
                    SortOrder = 5,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-alt",
                    Color = "#f59e0b",
                    CreatedAt = scheduleCreatedAt
                },

                // QUARTERLY SCHEDULES
                new ScheduleTemplate
                {
                    Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
                    Name = "Quarterly (Jan, Apr, Jul, Oct) 1st at 2 AM",
                    Description = "Runs on the 1st day of each quarter at 2:00 AM",
                    Category = "Quarterly",
                    CronExpression = "0 0 2 1 1,4,7,10 ?",
                    SortOrder = 1,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-check",
                    Color = "#ec4899",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("50000000-0000-0000-0000-000000000002"),
                    Name = "End of Quarter (Mar, Jun, Sep, Dec) Last Day",
                    Description = "Runs on the last day of each quarter at 11:00 PM",
                    Category = "Quarterly",
                    CronExpression = "0 0 23 L 3,6,9,12 ?",
                    SortOrder = 2,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-check",
                    Color = "#ec4899",
                    CreatedAt = scheduleCreatedAt
                },

                // YEARLY SCHEDULES
                new ScheduleTemplate
                {
                    Id = Guid.Parse("60000000-0000-0000-0000-000000000001"),
                    Name = "Yearly on January 1st at 2 AM",
                    Description = "Runs once a year on January 1st at 2:00 AM",
                    Category = "Yearly",
                    CronExpression = "0 0 2 1 1 ?",
                    SortOrder = 1,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-star",
                    Color = "#ef4444",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("60000000-0000-0000-0000-000000000002"),
                    Name = "Yearly on July 1st at 2 AM",
                    Description = "Runs once a year on July 1st at 2:00 AM (mid-year)",
                    Category = "Yearly",
                    CronExpression = "0 0 2 1 7 ?",
                    SortOrder = 2,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-star",
                    Color = "#ef4444",
                    CreatedAt = scheduleCreatedAt
                },
                new ScheduleTemplate
                {
                    Id = Guid.Parse("60000000-0000-0000-0000-000000000003"),
                    Name = "Yearly on December 31st at 11 PM",
                    Description = "Runs once a year on December 31st at 11:00 PM (year-end)",
                    Category = "Yearly",
                    CronExpression = "0 0 23 31 12 ?",
                    SortOrder = 3,
                    IsSystem = true,
                    IsActive = true,
                    IconClass = "fas fa-calendar-star",
                    Color = "#ef4444",
                    CreatedAt = scheduleCreatedAt
                }
            );
        }
    }
}

