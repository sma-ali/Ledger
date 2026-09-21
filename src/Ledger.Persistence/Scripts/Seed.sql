-- Données de référence. Idempotent : chaque insertion est conditionnée à
-- l'absence de la ligne, le script peut donc être rejoué à chaque
-- démarrage sans créer de doublon.

IF NOT EXISTS (SELECT 1 FROM dbo.Source WHERE Code = 'FO')
    INSERT dbo.Source (Code, Label) VALUES ('FO', N'Front office');

IF NOT EXISTS (SELECT 1 FROM dbo.Source WHERE Code = 'BO')
    INSERT dbo.Source (Code, Label) VALUES ('BO', N'Comptabilité');

-- Correspondance des desks. Le front et la compta nomment différemment
-- les mêmes équipes ; sans cette table, aucun rapprochement n'a lieu.
--
-- INSERT ... WHERE NOT EXISTS plutôt que MERGE : l'alias d'un MERGE nommé
-- « source » entre en collision avec la table dbo.Source, et MERGE traîne
-- par ailleurs une réputation de bugs qui lui vaut d'être déconseillé.
INSERT dbo.DeskMapping (SourceId, ExternalDeskCode, CanonicalDesk)
SELECT s.SourceId, v.ExternalDeskCode, v.CanonicalDesk
FROM
(
    VALUES
        ('FO', 'FXD-PARIS',        'PARIS-FX'),
        ('FO', 'RATES-PARIS',      'PARIS-RATES'),
        ('BO', 'PARIS_FX_DESK',    'PARIS-FX'),
        ('BO', 'PARIS_RATES_DESK', 'PARIS-RATES')
) AS v (SourceCode, ExternalDeskCode, CanonicalDesk)
JOIN dbo.Source AS s ON s.Code = v.SourceCode
WHERE NOT EXISTS
(
    SELECT 1
    FROM   dbo.DeskMapping AS m
    WHERE  m.SourceId = s.SourceId
      AND  m.ExternalDeskCode = v.ExternalDeskCode
);
