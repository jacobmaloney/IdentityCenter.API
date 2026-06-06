using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Text.Json;

namespace DataAccessLibrary.Services.SeedData;

/// <summary>
/// Seeds default process orchestration templates.
/// These templates provide ready-to-use process workflows that can be cloned and customized.
/// </summary>
public class DefaultProcessTemplatesSeedService
{
    private readonly string _connectionString;
    private readonly ILogger<DefaultProcessTemplatesSeedService> _logger;

    public DefaultProcessTemplatesSeedService(
        IConfiguration configuration,
        ILogger<DefaultProcessTemplatesSeedService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    public async Task SeedProcessTemplatesAsync()
    {
        _logger.LogInformation("Starting process orchestration template seeding...");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        // Check if process templates already exist
        var existingCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ApprovalWorkflows WHERE IsTemplate = 1 AND ProcessType = 'Process'");

        if (existingCount > 0)
        {
            _logger.LogInformation("Process templates already exist ({Count}), skipping seed", existingCount);
            return;
        }

        // Check if ProcessType column exists (V036 migration must have run)
        var hasColumn = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('ApprovalWorkflows') AND name = 'ProcessType'");
        if (hasColumn == 0)
        {
            _logger.LogWarning("ProcessType column not found on ApprovalWorkflows - V036 migration may not have run yet");
            return;
        }

        var templates = GetProcessTemplates();

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            foreach (var template in templates)
            {
                // Insert workflow
                await connection.ExecuteAsync(@"
                    INSERT INTO ApprovalWorkflows
                        (Id, Name, Description, Category, ResourceType, Priority, IsTemplate, IsActive,
                         ProcessType, TargetEntityType, CreatedBy, CreatedAt)
                    VALUES
                        (@Id, @Name, @Description, @Category, 'Process', 1, 1, 1,
                         'Process', @TargetEntityType, 'System', GETUTCDATE())",
                    new
                    {
                        template.Id,
                        template.Name,
                        template.Description,
                        Category = "Process",
                        template.TargetEntityType
                    },
                    transaction);

                // Insert nodes
                foreach (var node in template.Nodes)
                {
                    await connection.ExecuteAsync(@"
                        INSERT INTO ApprovalWorkflowNodes
                            (Id, WorkflowId, NodeType, NodeName, PositionX, PositionY, ConfigData, CreatedAt)
                        VALUES
                            (@Id, @WorkflowId, @NodeType, @NodeName, @PositionX, @PositionY, @ConfigData, GETUTCDATE())",
                        node, transaction);
                }

                // Insert connections
                foreach (var conn in template.Connections)
                {
                    await connection.ExecuteAsync(@"
                        INSERT INTO ApprovalWorkflowConnections
                            (Id, WorkflowId, SourceNodeId, TargetNodeId, SourcePort, TargetPort, CreatedAt)
                        VALUES
                            (@Id, @WorkflowId, @SourceNodeId, @TargetNodeId, @SourcePort, @TargetPort, GETUTCDATE())",
                        conn, transaction);
                }
            }

            await transaction.CommitAsync();
            _logger.LogInformation("Seeded {Count} process orchestration templates", templates.Count);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private List<ProcessTemplate> GetProcessTemplates()
    {
        var templates = new List<ProcessTemplate>();

        // === 1. Employee Onboarding ===
        var onboarding = CreateTemplate(
            "Employee Onboarding",
            "Automated employee onboarding: validate HR data, create account, assign groups, notify manager, wait for approval, then enable.",
            "Object");

        var obStart = AddNode(onboarding, "Start", "Start", 400, 50);
        var obDecision = AddNode(onboarding, "Decision", "HR Data Valid?", 400, 160, JsonSerializer.Serialize(new { ConditionType = "CustomField", FieldName = "EmployeeType", Operator = "NotEquals", Value = "", TrueOutputPort = "yes", FalseOutputPort = "no" }));
        var obCreate = AddNode(onboarding, "CreateAccount", "Create AD Account", 400, 280);
        var obGroups = AddNode(onboarding, "AssignGroups", "Assign Department Groups", 400, 400);
        var obEmail = AddNode(onboarding, "SendEmail", "Send Welcome Email", 400, 520);
        var obWait = AddNode(onboarding, "WaitForApproval", "Manager Approval", 400, 640, JsonSerializer.Serialize(new { ApproverType = "Manager", TimeoutHours = 48, RequireJustification = false, Instructions = "Please review and approve the new employee account." }));
        var obEnable = AddNode(onboarding, "EnableAccount", "Enable Account", 400, 760);
        var obEnd = AddNode(onboarding, "End", "Complete", 400, 880);
        var obEndFail = AddNode(onboarding, "End", "Invalid Data", 650, 280);

        AddConnection(onboarding, obStart, obDecision, "Bottom", "Top");
        AddConnection(onboarding, obDecision, obCreate, "Bottom", "Top");   // YES
        AddConnection(onboarding, obDecision, obEndFail, "Right", "Top");   // NO
        AddConnection(onboarding, obCreate, obGroups, "Bottom", "Top");
        AddConnection(onboarding, obGroups, obEmail, "Bottom", "Top");
        AddConnection(onboarding, obEmail, obWait, "Bottom", "Top");
        AddConnection(onboarding, obWait, obEnable, "Bottom", "Top");
        AddConnection(onboarding, obEnable, obEnd, "Bottom", "Top");
        templates.Add(onboarding);

        // === 2. Employee Offboarding (Full M365 + AD) ===
        var offboarding = CreateTemplate(
            "Employee Offboarding",
            "Full offboarding: revoke sessions, disable account, set OOF, forward mail to manager, remove licenses, remove all groups, transfer team ownership, move to Disabled OU, notify IT, create access review.",
            "Offboarding");

        var offStart          = AddNode(offboarding, "Start",                 "Start",                    400, 50);
        var offRevoke         = AddNode(offboarding, "RevokeActiveSessions",  "Revoke Active Sessions",   400, 160);
        var offDisable        = AddNode(offboarding, "DisableAccount",        "Disable Account",          400, 280);
        var offOOF            = AddNode(offboarding, "SetOutOfOffice",        "Set Out-of-Office",        400, 400,
            JsonSerializer.Serialize(new { InternalMessage = (string?)null, ExternalMessage = (string?)null }));
        var offForward        = AddNode(offboarding, "SetMailForwarding",     "Forward Mail to Manager",  400, 520,
            JsonSerializer.Serialize(new { ForwardToManager = true, ForwardToAddress = (string?)null }));
        var offRemoveLicenses = AddNode(offboarding, "RemoveAllLicenses",     "Remove All Licenses",      400, 640);
        var offRemoveGroups   = AddNode(offboarding, "RemoveGroups",          "Remove All Groups",        400, 760,
            JsonSerializer.Serialize(new { RemoveAll = true }));
        var offTransferTeams  = AddNode(offboarding, "TransferTeamOwnership", "Transfer Team Ownership",  400, 880,
            JsonSerializer.Serialize(new { TargetManagerEntraId = (string?)null }));
        var offMove           = AddNode(offboarding, "MoveOU",                "Move to Disabled OU",      400, 1000,
            JsonSerializer.Serialize(new { TargetOU = "OU=Disabled,DC=contoso,DC=com" }));
        var offEmail          = AddNode(offboarding, "SendEmail",             "Notify IT Team",           400, 1120);
        var offReview         = AddNode(offboarding, "CreateAccessReview",    "Create Access Review",     400, 1240,
            JsonSerializer.Serialize(new { CampaignName = "Offboarding Review", ReviewType = "EntitlementAccess", DurationDays = 7 }));
        var offEnd            = AddNode(offboarding, "End",                   "Complete",                 400, 1360);

        AddConnection(offboarding, offStart,          offRevoke,         "Bottom", "Top");
        AddConnection(offboarding, offRevoke,         offDisable,        "Bottom", "Top");
        AddConnection(offboarding, offDisable,        offOOF,            "Bottom", "Top");
        AddConnection(offboarding, offOOF,            offForward,        "Bottom", "Top");
        AddConnection(offboarding, offForward,        offRemoveLicenses, "Bottom", "Top");
        AddConnection(offboarding, offRemoveLicenses, offRemoveGroups,   "Bottom", "Top");
        AddConnection(offboarding, offRemoveGroups,   offTransferTeams,  "Bottom", "Top");
        AddConnection(offboarding, offTransferTeams,  offMove,           "Bottom", "Top");
        AddConnection(offboarding, offMove,           offEmail,          "Bottom", "Top");
        AddConnection(offboarding, offEmail,          offReview,         "Bottom", "Top");
        AddConnection(offboarding, offReview,         offEnd,            "Bottom", "Top");
        templates.Add(offboarding);

        // === 3. Contractor Provisioning ===
        var contractor = CreateTemplate(
            "Contractor Provisioning",
            "Contractor lifecycle: sponsor approval, create account, assign groups, tag as contractor, wait 90 days, check extension, then disable.",
            "Object");

        var cStart = AddNode(contractor, "Start", "Start", 400, 50);
        var cApproval = AddNode(contractor, "WaitForApproval", "Sponsor Approval", 400, 170, JsonSerializer.Serialize(new { ApproverType = "SpecificUser", TimeoutHours = 24, RequireJustification = true, Instructions = "Please approve this contractor provisioning request." }));
        var cCreate = AddNode(contractor, "CreateAccount", "Create Account", 400, 290);
        var cGroups = AddNode(contractor, "AssignGroups", "Assign Contractor Groups", 400, 410);
        var cTag = AddNode(contractor, "SetTag", "Tag as Contractor", 400, 530);
        var cWait = AddNode(contractor, "WaitForDuration", "Wait 90 Days", 400, 650, JsonSerializer.Serialize(new { DelayDays = 90 }));
        var cDecision = AddNode(contractor, "Decision", "Contract Extended?", 400, 770, JsonSerializer.Serialize(new { ConditionType = "CustomField", FieldName = "ContractExtended", Operator = "Equals", Value = "true", TrueOutputPort = "yes", FalseOutputPort = "no" }));
        var cDisable = AddNode(contractor, "DisableAccount", "Disable Account", 650, 880);
        var cEnd = AddNode(contractor, "End", "Complete", 650, 1000);
        var cExtEnd = AddNode(contractor, "End", "Extended", 400, 880);

        AddConnection(contractor, cStart, cApproval, "Bottom", "Top");
        AddConnection(contractor, cApproval, cCreate, "Bottom", "Top");
        AddConnection(contractor, cCreate, cGroups, "Bottom", "Top");
        AddConnection(contractor, cGroups, cTag, "Bottom", "Top");
        AddConnection(contractor, cTag, cWait, "Bottom", "Top");
        AddConnection(contractor, cWait, cDecision, "Bottom", "Top");
        AddConnection(contractor, cDecision, cExtEnd, "Bottom", "Top");   // YES = extended
        AddConnection(contractor, cDecision, cDisable, "Right", "Top");   // NO = disable
        AddConnection(contractor, cDisable, cEnd, "Bottom", "Top");
        templates.Add(contractor);

        // === 4. Department Transfer ===
        var transfer = CreateTemplate(
            "Department Transfer",
            "Automate department transfers: remove old groups, update attributes, assign new groups, notify managers.",
            "Object");

        var tStart = AddNode(transfer, "Start", "Start", 400, 50);
        var tRemove = AddNode(transfer, "RemoveGroups", "Remove Old Dept Groups", 400, 170);
        var tAttrs = AddNode(transfer, "UpdateAttributes", "Update Dept & Title", 400, 290);
        var tAssign = AddNode(transfer, "AssignGroups", "Assign New Dept Groups", 400, 410);
        var tEmail = AddNode(transfer, "SendEmail", "Notify Both Managers", 400, 530);
        var tEnd = AddNode(transfer, "End", "Complete", 400, 650);

        AddConnection(transfer, tStart, tRemove, "Bottom", "Top");
        AddConnection(transfer, tRemove, tAttrs, "Bottom", "Top");
        AddConnection(transfer, tAttrs, tAssign, "Bottom", "Top");
        AddConnection(transfer, tAssign, tEmail, "Bottom", "Top");
        AddConnection(transfer, tEmail, tEnd, "Bottom", "Top");
        templates.Add(transfer);

        // === 5. Emergency Access Revocation ===
        var emergency = CreateTemplate(
            "Emergency Access Revocation",
            "Immediate security response: disable account, remove all privileged groups, notify security, call SIEM webhook.",
            "Object");

        var eStart = AddNode(emergency, "Start", "Start", 400, 50);
        var eDisable = AddNode(emergency, "DisableAccount", "Disable Account Immediately", 400, 170);
        var eRemove = AddNode(emergency, "RemoveGroups", "Remove Privileged Groups", 400, 290, JsonSerializer.Serialize(new { RemoveAll = true }));
        var eEmail = AddNode(emergency, "SendEmail", "Alert Security Team", 400, 410);
        var eWebhook = AddNode(emergency, "CallWebhook", "Notify SIEM", 400, 530, JsonSerializer.Serialize(new { Url = "https://siem.example.com/api/incident", Method = "POST", TimeoutSeconds = 10 }));
        var eEnd = AddNode(emergency, "End", "Complete", 400, 650);

        AddConnection(emergency, eStart, eDisable, "Bottom", "Top");
        AddConnection(emergency, eDisable, eRemove, "Bottom", "Top");
        AddConnection(emergency, eRemove, eEmail, "Bottom", "Top");
        AddConnection(emergency, eEmail, eWebhook, "Bottom", "Top");
        AddConnection(emergency, eWebhook, eEnd, "Bottom", "Top");
        templates.Add(emergency);

        return templates;
    }

    // === Helper Methods ===

    private ProcessTemplate CreateTemplate(string name, string description, string targetEntityType)
    {
        return new ProcessTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            TargetEntityType = targetEntityType
        };
    }

    private Guid AddNode(ProcessTemplate template, string nodeType, string name, int x, int y, string? configData = null)
    {
        var id = Guid.NewGuid();
        template.Nodes.Add(new ProcessTemplateNode
        {
            Id = id,
            WorkflowId = template.Id,
            NodeType = nodeType,
            NodeName = name,
            PositionX = x,
            PositionY = y,
            ConfigData = configData
        });
        return id;
    }

    private void AddConnection(ProcessTemplate template, Guid sourceId, Guid targetId, string sourcePort, string targetPort)
    {
        template.Connections.Add(new ProcessTemplateConnection
        {
            Id = Guid.NewGuid(),
            WorkflowId = template.Id,
            SourceNodeId = sourceId,
            TargetNodeId = targetId,
            SourcePort = sourcePort,
            TargetPort = targetPort
        });
    }

    // === Internal Models ===

    private class ProcessTemplate
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? TargetEntityType { get; set; }
        public List<ProcessTemplateNode> Nodes { get; set; } = new();
        public List<ProcessTemplateConnection> Connections { get; set; } = new();
    }

    private class ProcessTemplateNode
    {
        public Guid Id { get; set; }
        public Guid WorkflowId { get; set; }
        public string NodeType { get; set; } = "";
        public string? NodeName { get; set; }
        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public string? ConfigData { get; set; }
    }

    private class ProcessTemplateConnection
    {
        public Guid Id { get; set; }
        public Guid WorkflowId { get; set; }
        public Guid SourceNodeId { get; set; }
        public Guid TargetNodeId { get; set; }
        public string? SourcePort { get; set; }
        public string? TargetPort { get; set; }
    }
}
