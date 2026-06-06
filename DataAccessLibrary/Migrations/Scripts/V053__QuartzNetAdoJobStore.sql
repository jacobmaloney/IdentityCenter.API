-- ============================================================================
-- V053: Quartz.NET AdoJobStore Tables
--
-- Creates the QRTZ_* tables required for Quartz.NET persistent job store and
-- clustering (Phase 5 of the distributed execution server architecture).
--
-- With AdoJobStore, Quartz persists all job and trigger state to the database
-- instead of keeping it in memory. This enables:
--   1. Trigger/job survival across application restarts
--   2. Clustered scheduling: multiple IdentityCenter primaries share one
--      Quartz scheduler without firing duplicate jobs (via QRTZ_LOCKS row-level
--      locking and QRTZ_SCHEDULER_STATE heartbeats)
--
-- Reference: https://github.com/quartznet/quartznet/blob/main/database/tables/tables_sqlServer.sql
-- Configure in Schedule/Extensions/ServiceCollectionExtensions.cs:
--   q.UsePersistentStore(s => { s.UseSqlServer(connStr); s.UseClustering(); })
--
-- Tables created (all guarded with IF NOT EXISTS):
--   1.  QRTZ_JOB_DETAILS        - job class + data definitions
--   2.  QRTZ_TRIGGERS            - trigger definitions (FK -> JOB_DETAILS)
--   3.  QRTZ_SIMPLE_TRIGGERS     - simple trigger repeat data (FK -> TRIGGERS)
--   4.  QRTZ_CRON_TRIGGERS       - cron expression data (FK -> TRIGGERS)
--   5.  QRTZ_SIMPROP_TRIGGERS    - simple property triggers (FK -> TRIGGERS)
--   6.  QRTZ_BLOB_TRIGGERS       - blob-serialized trigger data (FK -> TRIGGERS)
--   7.  QRTZ_CALENDARS           - calendar exclusion definitions
--   8.  QRTZ_PAUSED_TRIGGER_GRPS - paused trigger group tracking
--   9.  QRTZ_FIRED_TRIGGERS      - currently-firing triggers (clustering state)
--   10. QRTZ_SCHEDULER_STATE     - per-instance heartbeat (cluster membership)
--   11. QRTZ_LOCKS               - row-level locks used for cluster coordination
-- ============================================================================


-- ============================================================================
-- 1. QRTZ_JOB_DETAILS
--    Stores the job class name, durability, and serialized JobDataMap.
--    Must be created before QRTZ_TRIGGERS (FK dependency).
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_JOB_DETAILS'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_JOB_DETAILS] (
        [SCHED_NAME]        nvarchar(120) NOT NULL,
        [JOB_NAME]          nvarchar(150) NOT NULL,
        [JOB_GROUP]         nvarchar(150) NOT NULL,
        [DESCRIPTION]       nvarchar(250) NULL,
        [JOB_CLASS_NAME]    nvarchar(250) NOT NULL,
        [IS_DURABLE]        bit           NOT NULL,
        [IS_NONCONCURRENT]  bit           NOT NULL,
        [IS_UPDATE_DATA]    bit           NOT NULL,
        [REQUESTS_RECOVERY] bit           NOT NULL,
        [JOB_DATA]          varbinary(max) NULL,
        CONSTRAINT [PK_QRTZ_JOB_DETAILS] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [JOB_NAME],
            [JOB_GROUP]
        )
    );
    PRINT 'Created table QRTZ_JOB_DETAILS';
END;
GO


-- ============================================================================
-- 2. QRTZ_TRIGGERS
--    Central trigger table. Stores next fire time, state, misfire info, and
--    the serialized JobDataMap override. FK -> QRTZ_JOB_DETAILS.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_TRIGGERS'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_TRIGGERS] (
        [SCHED_NAME]    nvarchar(120) NOT NULL,
        [TRIGGER_NAME]  nvarchar(150) NOT NULL,
        [TRIGGER_GROUP] nvarchar(150) NOT NULL,
        [JOB_NAME]      nvarchar(150) NOT NULL,
        [JOB_GROUP]     nvarchar(150) NOT NULL,
        [DESCRIPTION]   nvarchar(250) NULL,
        [NEXT_FIRE_TIME] bigint        NULL,
        [PREV_FIRE_TIME] bigint        NULL,
        [PRIORITY]      int            NULL,
        [TRIGGER_STATE] nvarchar(16)  NOT NULL,
        [TRIGGER_TYPE]  nvarchar(8)   NOT NULL,
        [START_TIME]    bigint         NOT NULL,
        [END_TIME]      bigint         NULL,
        [CALENDAR_NAME] nvarchar(200) NULL,
        [MISFIRE_INSTR] int            NULL,
        [JOB_DATA]      varbinary(max) NULL,
        CONSTRAINT [PK_QRTZ_TRIGGERS] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ),
        CONSTRAINT [FK_QRTZ_TRIGGERS_QRTZ_JOB_DETAILS] FOREIGN KEY (
            [SCHED_NAME],
            [JOB_NAME],
            [JOB_GROUP]
        ) REFERENCES [dbo].[QRTZ_JOB_DETAILS] (
            [SCHED_NAME],
            [JOB_NAME],
            [JOB_GROUP]
        )
    );
    PRINT 'Created table QRTZ_TRIGGERS';
END;
GO


-- ============================================================================
-- 3. QRTZ_SIMPLE_TRIGGERS
--    Stores repeat count and interval for SimpleTrigger instances.
--    FK -> QRTZ_TRIGGERS with CASCADE DELETE.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_SIMPLE_TRIGGERS'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_SIMPLE_TRIGGERS] (
        [SCHED_NAME]      nvarchar(120) NOT NULL,
        [TRIGGER_NAME]    nvarchar(150) NOT NULL,
        [TRIGGER_GROUP]   nvarchar(150) NOT NULL,
        [REPEAT_COUNT]    int           NOT NULL,
        [REPEAT_INTERVAL] bigint        NOT NULL,
        [TIMES_TRIGGERED] int           NOT NULL,
        CONSTRAINT [PK_QRTZ_SIMPLE_TRIGGERS] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ),
        CONSTRAINT [FK_QRTZ_SIMPLE_TRIGGERS_QRTZ_TRIGGERS] FOREIGN KEY (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ) REFERENCES [dbo].[QRTZ_TRIGGERS] (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ) ON DELETE CASCADE
    );
    PRINT 'Created table QRTZ_SIMPLE_TRIGGERS';
END;
GO


-- ============================================================================
-- 4. QRTZ_CRON_TRIGGERS
--    Stores cron expression and time zone for CronTrigger instances.
--    FK -> QRTZ_TRIGGERS with CASCADE DELETE.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_CRON_TRIGGERS'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_CRON_TRIGGERS] (
        [SCHED_NAME]      nvarchar(120) NOT NULL,
        [TRIGGER_NAME]    nvarchar(150) NOT NULL,
        [TRIGGER_GROUP]   nvarchar(150) NOT NULL,
        [CRON_EXPRESSION] nvarchar(120) NOT NULL,
        [TIME_ZONE_ID]    nvarchar(80)  NULL,
        CONSTRAINT [PK_QRTZ_CRON_TRIGGERS] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ),
        CONSTRAINT [FK_QRTZ_CRON_TRIGGERS_QRTZ_TRIGGERS] FOREIGN KEY (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ) REFERENCES [dbo].[QRTZ_TRIGGERS] (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ) ON DELETE CASCADE
    );
    PRINT 'Created table QRTZ_CRON_TRIGGERS';
END;
GO


-- ============================================================================
-- 5. QRTZ_SIMPROP_TRIGGERS
--    Stores typed properties (string, int, long, decimal, bool) for
--    CalendarIntervalTrigger and DailyTimeIntervalTrigger.
--    FK -> QRTZ_TRIGGERS with CASCADE DELETE.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_SIMPROP_TRIGGERS'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_SIMPROP_TRIGGERS] (
        [SCHED_NAME]    nvarchar(120)  NOT NULL,
        [TRIGGER_NAME]  nvarchar(150)  NOT NULL,
        [TRIGGER_GROUP] nvarchar(150)  NOT NULL,
        [STR_PROP_1]    nvarchar(512)  NULL,
        [STR_PROP_2]    nvarchar(512)  NULL,
        [STR_PROP_3]    nvarchar(512)  NULL,
        [INT_PROP_1]    int            NULL,
        [INT_PROP_2]    int            NULL,
        [LONG_PROP_1]   bigint         NULL,
        [LONG_PROP_2]   bigint         NULL,
        [DEC_PROP_1]    numeric(13, 4) NULL,
        [DEC_PROP_2]    numeric(13, 4) NULL,
        [BOOL_PROP_1]   bit            NULL,
        [BOOL_PROP_2]   bit            NULL,
        [TIME_ZONE_ID]  nvarchar(80)   NULL,
        CONSTRAINT [PK_QRTZ_SIMPROP_TRIGGERS] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ),
        CONSTRAINT [FK_QRTZ_SIMPROP_TRIGGERS_QRTZ_TRIGGERS] FOREIGN KEY (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ) REFERENCES [dbo].[QRTZ_TRIGGERS] (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ) ON DELETE CASCADE
    );
    PRINT 'Created table QRTZ_SIMPROP_TRIGGERS';
END;
GO


-- ============================================================================
-- 6. QRTZ_BLOB_TRIGGERS
--    Stores blob-serialized trigger data for custom trigger types that do not
--    have a dedicated sub-table. FK -> QRTZ_TRIGGERS with CASCADE DELETE.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_BLOB_TRIGGERS'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_BLOB_TRIGGERS] (
        [SCHED_NAME]    nvarchar(120)  NOT NULL,
        [TRIGGER_NAME]  nvarchar(150)  NOT NULL,
        [TRIGGER_GROUP] nvarchar(150)  NOT NULL,
        [BLOB_DATA]     varbinary(max) NULL,
        CONSTRAINT [PK_QRTZ_BLOB_TRIGGERS] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ),
        CONSTRAINT [FK_QRTZ_BLOB_TRIGGERS_QRTZ_TRIGGERS] FOREIGN KEY (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ) REFERENCES [dbo].[QRTZ_TRIGGERS] (
            [SCHED_NAME],
            [TRIGGER_NAME],
            [TRIGGER_GROUP]
        ) ON DELETE CASCADE
    );
    PRINT 'Created table QRTZ_BLOB_TRIGGERS';
END;
GO


-- ============================================================================
-- 7. QRTZ_CALENDARS
--    Stores serialized ICalendar objects used to exclude time ranges from
--    trigger fire times (e.g. holiday calendars, business-hours calendars).
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_CALENDARS'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_CALENDARS] (
        [SCHED_NAME]    nvarchar(120)  NOT NULL,
        [CALENDAR_NAME] nvarchar(200)  NOT NULL,
        [CALENDAR]      varbinary(max) NOT NULL,
        CONSTRAINT [PK_QRTZ_CALENDARS] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [CALENDAR_NAME]
        )
    );
    PRINT 'Created table QRTZ_CALENDARS';
END;
GO


-- ============================================================================
-- 8. QRTZ_PAUSED_TRIGGER_GRPS
--    Records which trigger groups are paused. Quartz checks this table to
--    skip firing triggers that belong to a paused group.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_PAUSED_TRIGGER_GRPS'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_PAUSED_TRIGGER_GRPS] (
        [SCHED_NAME]    nvarchar(120) NOT NULL,
        [TRIGGER_GROUP] nvarchar(150) NOT NULL,
        CONSTRAINT [PK_QRTZ_PAUSED_TRIGGER_GRPS] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [TRIGGER_GROUP]
        )
    );
    PRINT 'Created table QRTZ_PAUSED_TRIGGER_GRPS';
END;
GO


-- ============================================================================
-- 9. QRTZ_FIRED_TRIGGERS
--    Tracks triggers that are currently acquired or executing. Used by the
--    cluster to detect failed nodes and recover misfired triggers.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_FIRED_TRIGGERS'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_FIRED_TRIGGERS] (
        [SCHED_NAME]       nvarchar(120) NOT NULL,
        [ENTRY_ID]         nvarchar(140) NOT NULL,
        [TRIGGER_NAME]     nvarchar(150) NOT NULL,
        [TRIGGER_GROUP]    nvarchar(150) NOT NULL,
        [INSTANCE_NAME]    nvarchar(200) NOT NULL,
        [FIRED_TIME]       bigint        NOT NULL,
        [SCHED_TIME]       bigint        NOT NULL,
        [PRIORITY]         int           NOT NULL,
        [STATE]            nvarchar(16)  NOT NULL,
        [JOB_NAME]         nvarchar(150) NULL,
        [JOB_GROUP]        nvarchar(150) NULL,
        [IS_NONCONCURRENT] bit           NULL,
        [REQUESTS_RECOVERY] bit          NULL,
        CONSTRAINT [PK_QRTZ_FIRED_TRIGGERS] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [ENTRY_ID]
        )
    );
    PRINT 'Created table QRTZ_FIRED_TRIGGERS';
END;
GO


-- ============================================================================
-- 10. QRTZ_SCHEDULER_STATE
--     Each Quartz node writes a heartbeat row here. Other nodes monitor this
--     table to detect dead nodes and take over their misfired triggers.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_SCHEDULER_STATE'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_SCHEDULER_STATE] (
        [SCHED_NAME]       nvarchar(120) NOT NULL,
        [INSTANCE_NAME]    nvarchar(200) NOT NULL,
        [LAST_CHECKIN_TIME] bigint       NOT NULL,
        [CHECKIN_INTERVAL] bigint        NOT NULL,
        CONSTRAINT [PK_QRTZ_SCHEDULER_STATE] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [INSTANCE_NAME]
        )
    );
    PRINT 'Created table QRTZ_SCHEDULER_STATE';
END;
GO


-- ============================================================================
-- 11. QRTZ_LOCKS
--     Provides row-level pessimistic locking so that only one clustered node
--     at a time can acquire triggers or perform misfire recovery.
--     Quartz expects exactly these 5 lock rows to exist at startup.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'QRTZ_LOCKS'
)
BEGIN
    CREATE TABLE [dbo].[QRTZ_LOCKS] (
        [SCHED_NAME] nvarchar(120) NOT NULL,
        [LOCK_NAME]  nvarchar(40)  NOT NULL,
        CONSTRAINT [PK_QRTZ_LOCKS] PRIMARY KEY CLUSTERED (
            [SCHED_NAME],
            [LOCK_NAME]
        )
    );
    PRINT 'Created table QRTZ_LOCKS';
END;
GO


-- ============================================================================
-- INDEXES ON QRTZ_TRIGGERS
--    Cover the hot query paths: next fire time polling, state filtering,
--    misfire detection, and group-based lookups.
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_G_J' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_T_G_J]
        ON [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [JOB_GROUP], [JOB_NAME]);
    PRINT 'Created index IDX_QRTZ_T_G_J';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_C' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_T_C]
        ON [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [CALENDAR_NAME]);
    PRINT 'Created index IDX_QRTZ_T_C';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_N_G_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_T_N_G_STATE]
        ON [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [TRIGGER_GROUP], [TRIGGER_STATE]);
    PRINT 'Created index IDX_QRTZ_T_N_G_STATE';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_T_STATE]
        ON [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [TRIGGER_STATE]);
    PRINT 'Created index IDX_QRTZ_T_STATE';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_N_STATE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_T_N_STATE]
        ON [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [TRIGGER_NAME], [TRIGGER_GROUP], [TRIGGER_STATE]);
    PRINT 'Created index IDX_QRTZ_T_N_STATE';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NEXT_FIRE_TIME' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_T_NEXT_FIRE_TIME]
        ON [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [NEXT_FIRE_TIME]);
    PRINT 'Created index IDX_QRTZ_T_NEXT_FIRE_TIME';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_T_NFT_ST]
        ON [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [TRIGGER_STATE], [NEXT_FIRE_TIME]);
    PRINT 'Created index IDX_QRTZ_T_NFT_ST';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST_MISFIRE' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_T_NFT_ST_MISFIRE]
        ON [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [MISFIRE_INSTR], [NEXT_FIRE_TIME], [TRIGGER_STATE]);
    PRINT 'Created index IDX_QRTZ_T_NFT_ST_MISFIRE';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_T_NFT_ST_MISFIRE_GRP' AND object_id = OBJECT_ID('dbo.QRTZ_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_T_NFT_ST_MISFIRE_GRP]
        ON [dbo].[QRTZ_TRIGGERS] ([SCHED_NAME], [MISFIRE_INSTR], [NEXT_FIRE_TIME], [TRIGGER_GROUP], [TRIGGER_STATE]);
    PRINT 'Created index IDX_QRTZ_T_NFT_ST_MISFIRE_GRP';
END;
GO


-- ============================================================================
-- INDEXES ON QRTZ_FIRED_TRIGGERS
--    Cover node-instance recovery queries, job-group lookups, and trigger
--    group lookups used during cluster failover.
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_INST_JOB_REQ_RCVRY' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_FT_INST_JOB_REQ_RCVRY]
        ON [dbo].[QRTZ_FIRED_TRIGGERS] ([SCHED_NAME], [INSTANCE_NAME], [REQUESTS_RECOVERY]);
    PRINT 'Created index IDX_QRTZ_FT_INST_JOB_REQ_RCVRY';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_G_J' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_FT_G_J]
        ON [dbo].[QRTZ_FIRED_TRIGGERS] ([SCHED_NAME], [JOB_GROUP], [JOB_NAME]);
    PRINT 'Created index IDX_QRTZ_FT_G_J';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IDX_QRTZ_FT_G_T' AND object_id = OBJECT_ID('dbo.QRTZ_FIRED_TRIGGERS'))
BEGIN
    CREATE INDEX [IDX_QRTZ_FT_G_T]
        ON [dbo].[QRTZ_FIRED_TRIGGERS] ([SCHED_NAME], [TRIGGER_GROUP], [TRIGGER_NAME]);
    PRINT 'Created index IDX_QRTZ_FT_G_T';
END;
GO


-- ============================================================================
-- SEED: QRTZ_LOCKS rows
--    Quartz.NET requires these 5 named locks to exist before the scheduler
--    can start. AdoJobStore issues a SELECT ... WITH (UPDLOCK, ROWLOCK) on
--    these rows to serialize concurrent cluster operations.
--    Uses the scheduler name 'IdentityCenterScheduler' which must match
--    the quartz.scheduler.instanceName setting in ServiceCollectionExtensions.
-- ============================================================================

IF NOT EXISTS (SELECT 1 FROM [dbo].[QRTZ_LOCKS] WHERE [SCHED_NAME] = 'IdentityCenterScheduler' AND [LOCK_NAME] = 'TRIGGER_ACCESS')
BEGIN
    INSERT INTO [dbo].[QRTZ_LOCKS] ([SCHED_NAME], [LOCK_NAME])
    VALUES ('IdentityCenterScheduler', 'TRIGGER_ACCESS');
    PRINT 'Seeded QRTZ_LOCKS: TRIGGER_ACCESS';
END;
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[QRTZ_LOCKS] WHERE [SCHED_NAME] = 'IdentityCenterScheduler' AND [LOCK_NAME] = 'JOB_ACCESS')
BEGIN
    INSERT INTO [dbo].[QRTZ_LOCKS] ([SCHED_NAME], [LOCK_NAME])
    VALUES ('IdentityCenterScheduler', 'JOB_ACCESS');
    PRINT 'Seeded QRTZ_LOCKS: JOB_ACCESS';
END;
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[QRTZ_LOCKS] WHERE [SCHED_NAME] = 'IdentityCenterScheduler' AND [LOCK_NAME] = 'CALENDAR_ACCESS')
BEGIN
    INSERT INTO [dbo].[QRTZ_LOCKS] ([SCHED_NAME], [LOCK_NAME])
    VALUES ('IdentityCenterScheduler', 'CALENDAR_ACCESS');
    PRINT 'Seeded QRTZ_LOCKS: CALENDAR_ACCESS';
END;
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[QRTZ_LOCKS] WHERE [SCHED_NAME] = 'IdentityCenterScheduler' AND [LOCK_NAME] = 'STATE_ACCESS')
BEGIN
    INSERT INTO [dbo].[QRTZ_LOCKS] ([SCHED_NAME], [LOCK_NAME])
    VALUES ('IdentityCenterScheduler', 'STATE_ACCESS');
    PRINT 'Seeded QRTZ_LOCKS: STATE_ACCESS';
END;
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[QRTZ_LOCKS] WHERE [SCHED_NAME] = 'IdentityCenterScheduler' AND [LOCK_NAME] = 'MISFIRE_ACCESS')
BEGIN
    INSERT INTO [dbo].[QRTZ_LOCKS] ([SCHED_NAME], [LOCK_NAME])
    VALUES ('IdentityCenterScheduler', 'MISFIRE_ACCESS');
    PRINT 'Seeded QRTZ_LOCKS: MISFIRE_ACCESS';
END;
GO


PRINT 'V053 complete: Quartz.NET AdoJobStore tables, indexes, and lock seed data applied.';
GO
