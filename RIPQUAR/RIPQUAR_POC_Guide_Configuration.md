# RIPQUAR POC - Guide Configuration Complète
## Script Task: Parse JSON TestWe → STG_TW_EXAMEN

---

## 📦 ÉTAPE 1 : Installer Newtonsoft.Json dans SSIS

### Option A : Via NuGet Package Manager Console (RECOMMANDÉ)

1. **Ouvrir le Script Editor dans SSIS**
   - Double-clic sur Script Task → Edit Script

2. **Dans Visual Studio Tools for Applications (VSTA):**
   - Menu → Tools → NuGet Package Manager → Package Manager Console

3. **Exécuter dans la console:**
   ```powershell
   Install-Package Newtonsoft.Json
   ```

4. **Vérifier l'installation:**
   - References → devrait voir "Newtonsoft.Json"

### Option B : Copie manuelle DLL (si NuGet bloqué)

1. **Télécharger Newtonsoft.Json.dll**
   - Aller sur: https://www.nuget.org/packages/Newtonsoft.Json
   - Download package → Extraire le ZIP
   - Récupérer: `lib\net45\Newtonsoft.Json.dll`

2. **Copier dans GAC (Global Assembly Cache):**
   ```cmd
   cd C:\Windows\Microsoft.NET\assembly\GAC_MSIL
   mkdir Newtonsoft.Json
   copy "C:\Downloads\Newtonsoft.Json.dll" "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\Newtonsoft.Json\"
   ```

3. **Ajouter référence dans VSTA:**
   - Project → Add Reference → Browse
   - Sélectionner la DLL copiée

---

## 🗄️ ÉTAPE 2 : Créer/Vérifier la table STG_TW_EXAMEN

**Si la table n'existe pas encore, créer avec ce script:**

```sql
USE [InfoGestion];
GO

-- Créer le schéma si nécessaire
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'RIPQUAR_STG')
BEGIN
    EXEC('CREATE SCHEMA [RIPQUAR_STG]');
END
GO

-- Créer la table STG_TW_EXAMEN
IF OBJECT_ID('[RIPQUAR_STG].[STG_TW_EXAMEN]', 'U') IS NOT NULL
    DROP TABLE [RIPQUAR_STG].[STG_TW_EXAMEN];
GO

CREATE TABLE [RIPQUAR_STG].[STG_TW_EXAMEN]
(
    -- Clé primaire staging
    STG_N_ID                INT IDENTITY(1,1) PRIMARY KEY,
    
    -- Identifiants TestWe
    TW_N_EXAM_ID            NVARCHAR(50),           -- UUID examen TestWe
    TW_C_EXAM               NVARCHAR(20),           -- Public Key ou code examen
    
    -- Informations examen
    TW_Q_DURE_SEC           INT,                    -- Durée en secondes
    TW_Q_PTS_MAX            DECIMAL(5,2),           -- Points maximum
    TW_DH_DEBU              DATETIME,               -- Date/heure début (createdAt)
    TW_DH_FIN               DATETIME,               -- Date/heure fin (validatedAt)
    TW_DH_MAJ               DATETIME,               -- Dernière mise à jour (updatedAt)
    
    -- Statistiques
    TW_Q_NB_ETUD            INT,                    -- Nombre d'étudiants
    TW_Q_NB_COPI_RECU       INT,                    -- Copies reçues
    TW_Q_NB_COPI_CORR       INT,                    -- Copies corrigées
    
    -- Autres
    TW_NM_FUSE_HORA         NVARCHAR(50),           -- Fuseau horaire
    
    -- Liens GIC (pour jointures ultérieures)
    GIC_ROW_ID              NVARCHAR(20),           -- ROW_ID CX_INSC_EXAM
    GIC_EXAM_ID             NVARCHAR(20),           -- PAR_ROW_ID__CXEXAMEN
    GIC_CONTACT_ID          NVARCHAR(20),           -- PAR_ROW_ID__SCONTACT
    
    -- Audit DW
    DW_DH_INST              DATETIME DEFAULT GETDATE()
);
GO

-- Index pour performance
CREATE NONCLUSTERED INDEX IX_STG_TW_EXAMEN_TW_N_EXAM_ID 
    ON [RIPQUAR_STG].[STG_TW_EXAMEN](TW_N_EXAM_ID);

CREATE NONCLUSTERED INDEX IX_STG_TW_EXAMEN_GIC_ROW_ID 
    ON [RIPQUAR_STG].[STG_TW_EXAMEN](GIC_ROW_ID);
GO

PRINT 'Table STG_TW_EXAMEN créée avec succès';
```

---

## 🔌 ÉTAPE 3 : Configurer Connection Manager

1. **Dans SSIS Package:**
   - Connection Managers area → Right-click → New OLE DB Connection

2. **Configuration:**
   - Provider: SQL Server Native Client 11.0 (ou Microsoft OLE DB Provider for SQL Server)
   - Server name: TON_SERVEUR_INFOGESTION
   - Database: InfoGestion
   - Authentication: Windows Authentication (ou SQL Server)

3. **Renommer le Connection Manager:**
   - Right-click → Rename → `InfoGestion`
   - ⚠️ IMPORTANT: Le nom doit matcher celui dans le code C# ligne 225

4. **Tester:**
   - Right-click → Test Connection → OK

---

## 🎯 ÉTAPE 4 : Créer et configurer le Script Task

1. **Dans Control Flow:**
   - Drag & Drop "Script Task" depuis la Toolbox

2. **Double-clic → Configuration:**
   - Name: `SCR_Parse_JSON_to_STG_EXAMEN`
   - Description: `POC - Parse JSON TestWe vers STG`
   - ScriptLanguage: `Microsoft Visual C# 2019`

3. **Click "Edit Script..."**

4. **Copier le code:**
   - Remplacer TOUT le contenu de `ScriptMain.cs`
   - Avec le code du fichier `RIPQUAR_POC_ScriptTask.cs`

5. **Sauvegarder et fermer VSTA**

---

## ⚙️ ÉTAPE 5 : Ajuster le code pour ton environnement

**Choses à vérifier/modifier dans le code:**

### 1. Connection Manager (ligne 225)
```csharp
// CHANGER si ton Connection Manager a un autre nom
ConnectionManager cm = Dts.Connections["InfoGestion"];
```

### 2. Nom de la table source (ligne 90)
```sql
-- Si la table CX_INSC_EXAM est dans un autre schéma/DB
FROM [dbo].[CX_INSC_EXAM]
```

### 3. Nombre de lignes à traiter (ligne 84)
```sql
SELECT TOP 13  -- Pour POC, garder 13 lignes
```

---

## ✅ ÉTAPE 6 : Exécuter le POC

1. **Sauvegarder le package**
   - File → Save All

2. **Exécuter en mode Debug**
   - F5 ou Debug → Start Debugging

3. **Observer les logs**
   - Progress tab → Messages verts
   - Chercher: "ROW_ID XXX: JSON taille = ..."

4. **Vérifier les résultats**
```sql
-- Compter les lignes insérées
SELECT COUNT(*) AS NbExamens
FROM [RIPQUAR_STG].[STG_TW_EXAMEN];

-- Voir les données
SELECT TOP 10 *
FROM [RIPQUAR_STG].[STG_TW_EXAMEN]
ORDER BY DW_DH_INST DESC;

-- Vérifier les liens GIC
SELECT 
    TW_N_EXAM_ID,
    TW_C_EXAM,
    TW_Q_PTS_MAX,
    TW_DH_DEBU,
    GIC_ROW_ID,
    GIC_EXAM_ID
FROM [RIPQUAR_STG].[STG_TW_EXAMEN];
```

---

## 🐛 Troubleshooting

### Erreur: "Could not load file Newtonsoft.Json"
**Solution:** Réinstaller Newtonsoft.Json (voir Étape 1)

### Erreur: "The connection 'InfoGestion' is not found"
**Solution:** Vérifier le nom exact du Connection Manager (ligne 225 du code)

### Erreur: "Invalid object name 'CX_INSC_EXAM'"
**Solution:** Vérifier que tu as accès à la table source + bon schéma

### Erreur: "Unterminated string" ou JSON parsing
**Solution:** Vérifier que CONVERT(NVARCHAR(MAX), ...) est bien dans la requête (ligne 89)

### Aucune ligne retournée
**Solution:** Vérifier les données sources:
```sql
SELECT COUNT(*) 
FROM CX_INSC_EXAM 
WHERE X_GI_V_VECT_EXAM IS NOT NULL;
```

---

## 📊 Critères de succès du POC

✅ **Le POC est réussi si:**
- [ ] Package s'exécute sans erreur
- [ ] 13 lignes insérées dans STG_TW_EXAMEN
- [ ] JSON affiche taille > 65,535 caractères dans les logs
- [ ] Champs parsés correctement (TW_N_EXAM_ID, TW_Q_PTS_MAX, etc.)
- [ ] Liens GIC présents (GIC_ROW_ID, GIC_EXAM_ID)

✅ **Prochaines étapes après POC:**
- [ ] Parser les autres tables (examParts, questions, choix, etc.)
- [ ] Créer les 12 autres tables STG
- [ ] Développer le parsing complet (13 tables)
- [ ] Tests volumétrie (1000+ lignes)

---

## 📝 Notes

**Temps estimé installation:** 30-45 minutes
**Temps estimé premier run:** 5-10 minutes
**Durée exécution (13 lignes):** < 1 minute

**Questions fréquentes:**
- Q: "Pourquoi TRUNCATE TABLE avant INSERT?"
- R: Pour que le POC soit idempotent (rejouable sans doublons)

- Q: "Pourquoi CONVERT(NVARCHAR(MAX), ...) ?"
- R: Pour éviter la troncature SQL Server à 65,535 caractères

- Q: "Pourquoi TOP 13 ?"
- R: Ce sont les 13 lignes test fournies par le DBA

---

**Bon POC Marcus! 🚀**
