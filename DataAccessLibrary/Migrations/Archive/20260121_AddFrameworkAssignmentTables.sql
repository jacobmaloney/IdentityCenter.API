-- ============================================================================
-- Migration: Add Framework Assignment Tables
-- Date: 2026-01-21
-- Description: Creates tables for framework-driven compliance system
--              Transforms frameworks from passive containers to active drivers
--              of policy execution.
-- ============================================================================

-- Check if tables already exist before creating
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FrameworkAssignments')
BEGIN
    -- ============================================================================
    -- FrameworkAssignments Table
    -- Represents a framework applied to a specific scope (connection, department, app)
    -- When assigned, all active policies in the framework execute against this scope
    -- ============================================================================
    CREATE TABLE FrameworkAssignments (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        FrameworkId UNIQUEIDENTIFIER NOT NULL,

        -- Scope definition - what the framework applies to
        ConnectionId UNIQUEIDENTIFIER NULL,
        DepartmentId UNIQUEIDENTIFIER NULL,
        ApplicationId UNIQUEIDENTIFIER NULL,
        ScopeExpression NVARCHAR(MAX) NULL,
        ScopeInheritance NVARCHAR(20) NOT NULL DEFAULT 'Inherit',

        -- Status and lifecycle
        IsActive BIT NOT NULL DEFAULT 1,
        ActivatedAt DATETIME2 NULL,
        DeactivatedAt DATETIME2 NULL,
        DeactivationReason NVARCHAR(1000) NULL,

        -- Compliance tracking (auto-calculated)
        ComplianceScore DECIMAL(5,2) NOT NULL DEFAULT 0,
        LastEvaluatedAt DATETIME2 NULL,
        TotalPolicies INT NOT NULL DEFAULT 0,
        PassingPolicies INT NOT NULL DEFAULT 0,
        FailingPolicies INT NOT NULL DEFAULT 0,
        TotalViolations INT NOT NULL DEFAULT 0,
        CriticalViolations INT NOT NULL DEFAULT 0,

        -- Audit fields
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy NVARCHAR(256) NOT NULL,
        ModifiedAt DATETIME2 NULL,
        ModifiedBy NVARCHAR(256) NULL,

        -- Ensure at least one scope is defined
        CONSTRAINT CK_FrameworkAssignment_HasScope CHECK (
            ConnectionId IS NOT NULL OR
            DepartmentId IS NOT NULL OR
            ApplicationId IS NOT NULL OR
            ScopeExpression IS NOT NULL
        ),

        -- Foreign keys
        CONSTRAINT FK_FrameworkAssignments_ComplianceFrameworks
            FOREIGN KEY (FrameworkId) REFERENCES ComplianceFrameworks(Id),
        CONSTRAINT FK_FrameworkAssignments_DirectoryConnections
            FOREIGN KEY (ConnectionId) REFERENCES DirectoryConnections(Id)
    );

    -- Create indexes for FrameworkAssignments
    CREATE INDEX IX_FrameworkAssignments_FrameworkId
        ON FrameworkAssignments(FrameworkId);

    CREATE INDEX IX_FrameworkAssignments_ConnectionId
        ON FrameworkAssignments(ConnectionId)
        WHERE ConnectionId IS NOT NULL;

    CREATE INDEX IX_FrameworkAssignments_DepartmentId
        ON FrameworkAssignments(DepartmentId)
        WHERE DepartmentId IS NOT NULL;

    CREATE INDEX IX_FrameworkAssignments_ApplicationId
        ON FrameworkAssignments(ApplicationId)
        WHERE ApplicationId IS NOT NULL;

    CREATE INDEX IX_FrameworkAssignments_IsActive
        ON FrameworkAssignments(IsActive);

    CREATE INDEX IX_FrameworkAssignments_LastEvaluatedAt
        ON FrameworkAssignments(LastEvaluatedAt);

    -- Unique constraint: Only one active assignment per framework+connection
    CREATE UNIQUE INDEX IX_FrameworkAssignments_FrameworkConnection
        ON FrameworkAssignments(FrameworkId, ConnectionId)
        WHERE ConnectionId IS NOT NULL AND IsActive = 1;

    PRINT 'Created FrameworkAssignments table with indexes';
END
ELSE
BEGIN
    PRINT 'FrameworkAssignments table already exists - skipping';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'FrameworkAssignmentPolicyOverrides')
BEGIN
    -- ============================================================================
    -- FrameworkAssignmentPolicyOverrides Table
    -- Allows overriding specific policy settings when a framework is assigned
    -- Example: HIPAA applied, but disable one policy for this specific connection
    -- ============================================================================
    CREATE TABLE FrameworkAssignmentPolicyOverrides (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        AssignmentId UNIQUEIDENTIFIER NOT NULL,
        PolicyId UNIQUEIDENTIFIER NOT NULL,

        -- Override settings (NULL = use policy default)
        IsEnabled BIT NULL,
        EnforcementMode NVARCHAR(20) NULL,
        CustomParameters NVARCHAR(MAX) NULL,
        Justification NVARCHAR(2000) NULL,
        ExpiresAt DATETIME2 NULL,

        -- Audit fields
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy NVARCHAR(256) NOT NULL,
        ModifiedAt DATETIME2 NULL,
        ModifiedBy NVARCHAR(256) NULL,

        -- Foreign keys
        CONSTRAINT FK_FrameworkAssignmentPolicyOverrides_FrameworkAssignments
            FOREIGN KEY (AssignmentId) REFERENCES FrameworkAssignments(Id) ON DELETE CASCADE,
        CONSTRAINT FK_FrameworkAssignmentPolicyOverrides_CompliancePolicies
            FOREIGN KEY (PolicyId) REFERENCES CompliancePolicies(Id)
    );

    -- Create indexes for FrameworkAssignmentPolicyOverrides
    CREATE INDEX IX_FrameworkAssignmentPolicyOverrides_AssignmentId
        ON FrameworkAssignmentPolicyOverrides(AssignmentId);

    CREATE INDEX IX_FrameworkAssignmentPolicyOverrides_PolicyId
        ON FrameworkAssignmentPolicyOverrides(PolicyId);

    -- Unique constraint: One override per policy per assignment
    CREATE UNIQUE INDEX IX_FrameworkAssignmentPolicyOverrides_AssignmentPolicy
        ON FrameworkAssignmentPolicyOverrides(AssignmentId, PolicyId);

    PRINT 'Created FrameworkAssignmentPolicyOverrides table with indexes';
END
ELSE
BEGIN
    PRINT 'FrameworkAssignmentPolicyOverrides table already exists - skipping';
END
GO

-- ============================================================================
-- Add navigation property support to ComplianceFramework
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ComplianceFrameworks') AND name = 'ActiveAssignmentCount')
BEGIN
    -- Add computed column for quick access to assignment count (optional, for dashboard queries)
    -- This is denormalized for performance but kept in sync by triggers or application logic
    ALTER TABLE ComplianceFrameworks ADD ActiveAssignmentCount INT NOT NULL DEFAULT 0;
    PRINT 'Added ActiveAssignmentCount column to ComplianceFrameworks';
END
GO

PRINT '============================================================================';
PRINT 'Framework Assignment migration completed successfully';
PRINT 'Tables created:';
PRINT '  - FrameworkAssignments (framework-to-scope binding)';
PRINT '  - FrameworkAssignmentPolicyOverrides (per-assignment policy customization)';
PRINT '============================================================================';
