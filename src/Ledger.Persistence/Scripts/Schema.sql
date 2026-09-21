-- Schéma du moteur de consolidation.
--
-- Ce script est idempotent : il peut être rejoué autant de fois que
-- nécessaire sans effet de bord. C'est ce qui permet au batch de créer sa
-- structure au démarrage et à un environnement neuf de se monter en une
-- commande.
--
-- Aucun GO : le script est exécuté par l'application, pas par SQLCMD.

-- Référentiel des sources alimentant l'application.
IF OBJECT_ID(N'dbo.Source', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Source
    (
        SourceId INT           NOT NULL IDENTITY(1, 1) CONSTRAINT PK_Source PRIMARY KEY,
        Code     VARCHAR(10)   NOT NULL CONSTRAINT UQ_Source_Code UNIQUE,
        Label    NVARCHAR(100) NOT NULL
    );
END;

-- Correspondance entre le libellé de desk propre à une source et le
-- libellé canonique. C'est la table qui homogénéise.
IF OBJECT_ID(N'dbo.DeskMapping', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DeskMapping
    (
        DeskMappingId    INT         NOT NULL IDENTITY(1, 1) CONSTRAINT PK_DeskMapping PRIMARY KEY,
        SourceId         INT         NOT NULL CONSTRAINT FK_DeskMapping_Source REFERENCES dbo.Source (SourceId),
        ExternalDeskCode VARCHAR(50) NOT NULL,
        CanonicalDesk    VARCHAR(50) NOT NULL,
        CONSTRAINT UQ_DeskMapping_Source_External UNIQUE (SourceId, ExternalDeskCode)
    );
END;

-- Une exécution du batch pour une date métier.
IF OBJECT_ID(N'dbo.BatchRun', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BatchRun
    (
        BatchRunId   INT          NOT NULL IDENTITY(1, 1) CONSTRAINT PK_BatchRun PRIMARY KEY,
        BusinessDate DATE         NOT NULL,
        StartedAt    DATETIME2(3) NOT NULL CONSTRAINT DF_BatchRun_StartedAt DEFAULT SYSUTCDATETIME(),
        FinishedAt   DATETIME2(3) NULL,
        Status       VARCHAR(20)  NOT NULL
    );

    CREATE INDEX IX_BatchRun_BusinessDate ON dbo.BatchRun (BusinessDate);
END;

-- Le modèle commun : une ligne normalisée, quelle que soit sa source.
IF OBJECT_ID(N'dbo.PnlEntry', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PnlEntry
    (
        PnlEntryId       BIGINT        NOT NULL IDENTITY(1, 1) CONSTRAINT PK_PnlEntry PRIMARY KEY,
        BatchRunId       INT           NOT NULL CONSTRAINT FK_PnlEntry_BatchRun REFERENCES dbo.BatchRun (BatchRunId),
        SourceId         INT           NOT NULL CONSTRAINT FK_PnlEntry_Source REFERENCES dbo.Source (SourceId),
        BusinessDate     DATE          NOT NULL,
        CanonicalDesk    VARCHAR(50)   NOT NULL,
        InstrumentId     VARCHAR(20)   NOT NULL,
        Currency         CHAR(3)       NOT NULL,
        -- DECIMAL et jamais FLOAT : sur un montant, l'arrondi binaire
        -- fabriquerait les écarts que cette application doit détecter.
        Amount           DECIMAL(19, 4) NOT NULL,
        SourceLineNumber INT           NOT NULL,
        IngestedAt       DATETIME2(3)  NOT NULL CONSTRAINT DF_PnlEntry_IngestedAt DEFAULT SYSUTCDATETIME()
    );

    -- Sert la requête de réconciliation, qui lit toutes les lignes d'un run
    -- groupées par clé métier. Les colonnes INCLUDE évitent à SQL Server de
    -- revenir chercher les données dans la table.
    CREATE INDEX IX_PnlEntry_Run_Key
        ON dbo.PnlEntry (BatchRunId, BusinessDate, CanonicalDesk, InstrumentId, Currency)
        INCLUDE (SourceId, Amount);
END;

-- Les lignes refusées à l'ingestion, conservées brutes avec leur motif.
IF OBJECT_ID(N'dbo.IngestionReject', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.IngestionReject
    (
        IngestionRejectId BIGINT         NOT NULL IDENTITY(1, 1) CONSTRAINT PK_IngestionReject PRIMARY KEY,
        BatchRunId        INT            NOT NULL CONSTRAINT FK_IngestionReject_BatchRun REFERENCES dbo.BatchRun (BatchRunId),
        SourceId          INT            NOT NULL CONSTRAINT FK_IngestionReject_Source REFERENCES dbo.Source (SourceId),
        SourceLineNumber  INT            NOT NULL,
        RawLine           NVARCHAR(4000) NOT NULL,
        Reason            NVARCHAR(400)  NOT NULL,
        RejectedAt        DATETIME2(3)   NOT NULL CONSTRAINT DF_IngestionReject_RejectedAt DEFAULT SYSUTCDATETIME()
    );
END;

-- Un résultat de rapprochement par clé métier et par run.
IF OBJECT_ID(N'dbo.ReconciliationResult', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReconciliationResult
    (
        ReconciliationResultId BIGINT         NOT NULL IDENTITY(1, 1) CONSTRAINT PK_ReconciliationResult PRIMARY KEY,
        BatchRunId             INT            NOT NULL CONSTRAINT FK_ReconciliationResult_BatchRun REFERENCES dbo.BatchRun (BatchRunId),
        BusinessDate           DATE           NOT NULL,
        CanonicalDesk          VARCHAR(50)    NOT NULL,
        InstrumentId           VARCHAR(20)    NOT NULL,
        Currency               CHAR(3)        NOT NULL,
        -- Valeur de l'énumération ReconciliationStatus du domaine.
        Status                 TINYINT        NOT NULL,
        Difference             DECIMAL(19, 4) NOT NULL
    );

    -- Sert la consultation « montre-moi les écarts du jour ».
    CREATE INDEX IX_ReconciliationResult_Run_Status
        ON dbo.ReconciliationResult (BatchRunId, Status)
        INCLUDE (CanonicalDesk, InstrumentId, Currency, Difference);
END;

-- Ce qu'annonce chaque source pour une clé donnée. Une ligne par source,
-- afin qu'une troisième source n'oblige pas à modifier le schéma.
IF OBJECT_ID(N'dbo.ReconciliationLeg', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReconciliationLeg
    (
        ReconciliationLegId    BIGINT         NOT NULL IDENTITY(1, 1) CONSTRAINT PK_ReconciliationLeg PRIMARY KEY,
        ReconciliationResultId BIGINT         NOT NULL CONSTRAINT FK_ReconciliationLeg_Result
                                                  REFERENCES dbo.ReconciliationResult (ReconciliationResultId)
                                                  ON DELETE CASCADE,
        SourceId               INT            NOT NULL CONSTRAINT FK_ReconciliationLeg_Source REFERENCES dbo.Source (SourceId),
        Amount                 DECIMAL(19, 4) NOT NULL,
        EntryCount             INT            NOT NULL
    );

    CREATE INDEX IX_ReconciliationLeg_Result ON dbo.ReconciliationLeg (ReconciliationResultId);
END;
