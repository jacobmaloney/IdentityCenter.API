-- Migration: Add EnableManagerAssignment column to SyncProjects table
-- Date: 2025-12-29
-- Purpose: Allow toggling the Identity Manager Assignment post-sync task per project

-- Add EnableManagerAssignment column (default TRUE for existing projects)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('SyncProjects') AND name = 'EnableManagerAssignment')
BEGIN
    ALTER TABLE SyncProjects ADD EnableManagerAssignment BIT NOT NULL DEFAULT 1;
    PRINT 'Added EnableManagerAssignment column to SyncProjects';
END
ELSE
BEGIN
    PRINT 'EnableManagerAssignment column already exists';
END
GO
