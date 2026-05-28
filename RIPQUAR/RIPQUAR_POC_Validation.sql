/*
 * RIPQUAR POC - Scripts de Validation
 * Vérifier que le parsing JSON → STG_TW_EXAMEN fonctionne correctement
 */

USE [InfoGestion];
GO

PRINT '================================================================';
PRINT 'RIPQUAR POC - VALIDATION STG_TW_EXAMEN';
PRINT '================================================================';
PRINT '';

-- ========================================
-- 1. VÉRIFIER L'EXISTENCE DE LA TABLE
-- ========================================
PRINT '1. Vérification existence table STG_TW_EXAMEN...';

IF OBJECT_ID('[RIPQUAR_STG].[STG_TW_EXAMEN]', 'U') IS NOT NULL
    PRINT '   ✓ Table existe';
ELSE
BEGIN
    PRINT '   ✗ ERREUR: Table n''existe pas!';
    PRINT '   → Exécuter le script de création (voir Guide Configuration)';
END
PRINT '';

-- ========================================
-- 2. COMPTER LES LIGNES INSÉRÉES
-- ========================================
PRINT '2. Nombre de lignes chargées...';

DECLARE @RowCount INT;
SELECT @RowCount = COUNT(*) FROM [RIPQUAR_STG].[STG_TW_EXAMEN];

PRINT '   Lignes dans STG_TW_EXAMEN: ' + CAST(@RowCount AS VARCHAR(10));

IF @RowCount = 0
BEGIN
    PRINT '   ✗ Aucune ligne - Le package ne s''est pas exécuté ou a échoué';
    PRINT '';
    PRINT '   Vérifier les données sources:';
    EXEC('
        SELECT COUNT(*) AS NbLignesAvecJSON
        FROM [dbo].[CX_INSC_EXAM]
        WHERE X_GI_V_VECT_EXAM IS NOT NULL
          AND X_GI_V_VECT_CORR_EXAM IS NOT NULL;
    ');
END
ELSE IF @RowCount = 13
    PRINT '   ✓ OK - 13 lignes (comme attendu pour le POC)';
ELSE
    PRINT '   ⚠ Attention - Nombre de lignes différent de 13';
PRINT '';

-- ========================================
-- 3. VÉRIFIER LES VALEURS NULL
-- ========================================
PRINT '3. Analyse des valeurs NULL...';

SELECT 
    'TW_N_EXAM_ID' AS Colonne,
    COUNT(*) AS Total,
    SUM(CASE WHEN TW_N_EXAM_ID IS NULL THEN 1 ELSE 0 END) AS ValNull,
    SUM(CASE WHEN TW_N_EXAM_ID IS NOT NULL THEN 1 ELSE 0 END) AS ValNonNull
FROM [RIPQUAR_STG].[STG_TW_EXAMEN]

UNION ALL

SELECT 
    'TW_C_EXAM',
    COUNT(*),
    SUM(CASE WHEN TW_C_EXAM IS NULL THEN 1 ELSE 0 END),
    SUM(CASE WHEN TW_C_EXAM IS NOT NULL THEN 1 ELSE 0 END)
FROM [RIPQUAR_STG].[STG_TW_EXAMEN]

UNION ALL

SELECT 
    'TW_Q_PTS_MAX',
    COUNT(*),
    SUM(CASE WHEN TW_Q_PTS_MAX IS NULL THEN 1 ELSE 0 END),
    SUM(CASE WHEN TW_Q_PTS_MAX IS NOT NULL THEN 1 ELSE 0 END)
FROM [RIPQUAR_STG].[STG_TW_EXAMEN]

UNION ALL

SELECT 
    'TW_DH_DEBU',
    COUNT(*),
    SUM(CASE WHEN TW_DH_DEBU IS NULL THEN 1 ELSE 0 END),
    SUM(CASE WHEN TW_DH_DEBU IS NOT NULL THEN 1 ELSE 0 END)
FROM [RIPQUAR_STG].[STG_TW_EXAMEN]

UNION ALL

SELECT 
    'GIC_ROW_ID',
    COUNT(*),
    SUM(CASE WHEN GIC_ROW_ID IS NULL THEN 1 ELSE 0 END),
    SUM(CASE WHEN GIC_ROW_ID IS NOT NULL THEN 1 ELSE 0 END)
FROM [RIPQUAR_STG].[STG_TW_EXAMEN];

PRINT '';
PRINT '   ⚠ Si beaucoup de NULL → Vérifier le parsing JSON';
PRINT '';

-- ========================================
-- 4. ÉCHANTILLON DE DONNÉES
-- ========================================
PRINT '4. Échantillon de données (5 premières lignes)...';
PRINT '';

SELECT TOP 5
    STG_N_ID,
    TW_N_EXAM_ID,
    TW_C_EXAM,
    TW_Q_PTS_MAX,
    TW_DH_DEBU,
    TW_Q_NB_ETUD,
    GIC_ROW_ID,
    DW_DH_INST
FROM [RIPQUAR_STG].[STG_TW_EXAMEN]
ORDER BY DW_DH_INST DESC;

PRINT '';

-- ========================================
-- 5. VÉRIFIER LA COHÉRENCE DES DATES
-- ========================================
PRINT '5. Vérification cohérence des dates...';

SELECT 
    STG_N_ID,
    TW_N_EXAM_ID,
    TW_DH_DEBU AS DateDebut,
    TW_DH_FIN AS DateFin,
    TW_DH_MAJ AS DateMAJ,
    CASE 
        WHEN TW_DH_DEBU IS NULL THEN 'Début NULL'
        WHEN TW_DH_FIN IS NULL THEN 'Fin NULL'
        WHEN TW_DH_DEBU > TW_DH_FIN THEN '✗ INCOHÉRENCE: Début > Fin'
        ELSE '✓ OK'
    END AS StatutDates
FROM [RIPQUAR_STG].[STG_TW_EXAMEN]
WHERE TW_DH_DEBU > TW_DH_FIN 
   OR TW_DH_DEBU IS NULL 
   OR TW_DH_FIN IS NULL;

IF @@ROWCOUNT = 0
    PRINT '   ✓ Toutes les dates sont cohérentes';
ELSE
    PRINT '   ✗ Des incohérences détectées (voir résultats ci-dessus)';

PRINT '';

-- ========================================
-- 6. STATISTIQUES GLOBALES
-- ========================================
PRINT '6. Statistiques globales...';

SELECT 
    COUNT(*) AS NbExamens,
    MIN(TW_Q_PTS_MAX) AS MinPoints,
    MAX(TW_Q_PTS_MAX) AS MaxPoints,
    AVG(TW_Q_PTS_MAX) AS MoyPoints,
    MIN(TW_Q_DURE_SEC) AS MinDuree_Sec,
    MAX(TW_Q_DURE_SEC) AS MaxDuree_Sec,
    SUM(TW_Q_NB_ETUD) AS TotalEtudiants,
    SUM(TW_Q_NB_COPI_RECU) AS TotalCopiesRecues,
    SUM(TW_Q_NB_COPI_CORR) AS TotalCopiesCorrigees
FROM [RIPQUAR_STG].[STG_TW_EXAMEN];

PRINT '';

-- ========================================
-- 7. VÉRIFIER LES LIENS GIC
-- ========================================
PRINT '7. Vérification des liens vers tables GIC...';

-- Compter les correspondances avec CX_INSC_EXAM
DECLARE @NbCorrespondances INT;

SELECT @NbCorrespondances = COUNT(*)
FROM [RIPQUAR_STG].[STG_TW_EXAMEN] stg
INNER JOIN [dbo].[CX_INSC_EXAM] cie
    ON stg.GIC_ROW_ID = cie.ROW_ID;

PRINT '   Correspondances STG ↔ CX_INSC_EXAM: ' + CAST(@NbCorrespondances AS VARCHAR(10)) + ' / ' + CAST(@RowCount AS VARCHAR(10));

IF @NbCorrespondances = @RowCount
    PRINT '   ✓ Tous les liens GIC sont valides';
ELSE
    PRINT '   ✗ Certains liens GIC sont invalides';

PRINT '';

-- Afficher les orphelins (si existent)
IF EXISTS (
    SELECT 1 
    FROM [RIPQUAR_STG].[STG_TW_EXAMEN] stg
    LEFT JOIN [dbo].[CX_INSC_EXAM] cie ON stg.GIC_ROW_ID = cie.ROW_ID
    WHERE cie.ROW_ID IS NULL
)
BEGIN
    PRINT '   ⚠ ATTENTION: Lignes sans correspondance GIC:';
    
    SELECT 
        stg.STG_N_ID,
        stg.TW_N_EXAM_ID,
        stg.GIC_ROW_ID AS ROW_ID_Invalide
    FROM [RIPQUAR_STG].[STG_TW_EXAMEN] stg
    LEFT JOIN [dbo].[CX_INSC_EXAM] cie ON stg.GIC_ROW_ID = cie.ROW_ID
    WHERE cie.ROW_ID IS NULL;
END

PRINT '';

-- ========================================
-- 8. VALIDATION COMPLÈTE
-- ========================================
PRINT '================================================================';
PRINT 'RÉSUMÉ DE LA VALIDATION';
PRINT '================================================================';

DECLARE @ValidationStatus VARCHAR(20) = '✓ SUCCÈS';
DECLARE @ValidationMessages VARCHAR(MAX) = '';

-- Test 1: Table existe
IF OBJECT_ID('[RIPQUAR_STG].[STG_TW_EXAMEN]', 'U') IS NULL
BEGIN
    SET @ValidationStatus = '✗ ÉCHEC';
    SET @ValidationMessages = @ValidationMessages + CHAR(10) + '- Table n''existe pas';
END

-- Test 2: Lignes insérées
IF @RowCount = 0
BEGIN
    SET @ValidationStatus = '✗ ÉCHEC';
    SET @ValidationMessages = @ValidationMessages + CHAR(10) + '- Aucune ligne insérée';
END

-- Test 3: Liens GIC valides
IF @NbCorrespondances <> @RowCount
BEGIN
    SET @ValidationStatus = '⚠ AVERTISSEMENT';
    SET @ValidationMessages = @ValidationMessages + CHAR(10) + '- Certains liens GIC invalides';
END

PRINT '';
PRINT 'STATUT: ' + @ValidationStatus;

IF LEN(@ValidationMessages) > 0
BEGIN
    PRINT 'PROBLÈMES DÉTECTÉS:';
    PRINT @ValidationMessages;
END
ELSE
BEGIN
    PRINT '✓ POC validé avec succès!';
    PRINT '✓ ' + CAST(@RowCount AS VARCHAR(10)) + ' examens parsés et chargés';
    PRINT '✓ Liens GIC valides: ' + CAST(@NbCorrespondances AS VARCHAR(10)) + '/' + CAST(@RowCount AS VARCHAR(10));
    PRINT '';
    PRINT 'Prochaine étape: Parser les autres tables (examParts, questions, choix, etc.)';
END

PRINT '';
PRINT '================================================================';
GO
