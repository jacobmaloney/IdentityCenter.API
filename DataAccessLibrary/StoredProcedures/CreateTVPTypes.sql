-- Create Table-Valued Parameter types for high-performance bulk sync operations
-- These types allow passing entire tables of data in a single Dapper call

-- Drop existing types if they exist (for updates)
IF TYPE_ID('dbo.ObjectsToUpsertType') IS NOT NULL
    DROP TYPE dbo.ObjectsToUpsertType;

IF TYPE_ID('dbo.AttributesToUpsertType') IS NOT NULL
    DROP TYPE dbo.AttributesToUpsertType;
GO

-- Create Objects TVP type
CREATE TYPE dbo.ObjectsToUpsertType AS TABLE (
    Id UNIQUEIDENTIFIER,
    SourceConnectionId UNIQUEIDENTIFIER,
    SourceUniqueId NVARCHAR(450),
    SourceType NVARCHAR(100),
    ObjectClass NVARCHAR(100),
    DisplayName NVARCHAR(500),
    Email NVARCHAR(500),
    Username NVARCHAR(500),
    FirstName NVARCHAR(200),
    LastName NVARCHAR(200),
    Department NVARCHAR(200),
    JobTitle NVARCHAR(200),
    Phone NVARCHAR(100),
    DN NVARCHAR(MAX),
    CN NVARCHAR(500),
    ManagerSourceId NVARCHAR(500),
    IdentityId UNIQUEIDENTIFIER,
    IsActive BIT,
    IsAuthoritative BIT,
    MatchConfidence INT,
    MatchMethod NVARCHAR(100),
    IsBuiltIn BIT,
    IsAdminSDHolder BIT
);
GO

-- Create Attributes TVP type
CREATE TYPE dbo.AttributesToUpsertType AS TABLE (
    ObjectSourceConnectionId UNIQUEIDENTIFIER,
    ObjectSourceUniqueId NVARCHAR(450),
    AttributeName NVARCHAR(200),
    AttributeValue NVARCHAR(MAX),
    DataType NVARCHAR(50)
);
GO

PRINT 'TVP types created successfully';
