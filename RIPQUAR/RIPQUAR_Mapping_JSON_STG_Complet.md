# RIPQUAR - Mapping Complet JSON TestWe → Tables STG

## 📋 Vue d'ensemble

**Source JSON:** X_GI_V_VECT_EXAM (wrapper Siebel → RespBody)
**Cible:** 13 tables dans schéma [RIPQUAR_STG]

**Navigation JSON:**
```
X_GI_V_VECT_EXAM
└─ ListOfRes107TestWeAPI:res
   └─ Resp200
      └─ RespBody[0]              ← Racine des données TestWe
         ├─ id, publicKey, duration, maxPoints, ...
         ├─ classes[]
         ├─ examParts[]
         │  └─ partIndexes[]
         │     └─ question[]
         │        ├─ id, externalReference, ...
         │        └─ questionBank[]
         │           └─ choices[]
         └─ students[]
            └─ papers[]
               ├─ answers[]
               └─ corrections[]
```

---

## 1️⃣ STG_TW_EXAMEN (✅ POC FAIT)

**Source JSON:** `RespBody`

| Colonne STG | Chemin JSON | Type | Exemple |
|------------|-------------|------|---------|
| TW_N_EXAM_ID | `id` | UUID | "269bfc6c-7458-41ad-99ce-49d925b0772a" |
| TW_C_EXAM | `publicKey` | String | "265269" |
| TW_Q_DURE_SEC | `duration` | Int | 10800 |
| TW_Q_PTS_MAX | `maxPoints` | Decimal | 45.00 |
| TW_DH_DEBU | `createdAt` | DateTime | "01/19/2026 21:05:57" |
| TW_DH_FIN | `validatedAt` | DateTime | "01/19/2026 21:06:01" |
| TW_DH_MAJ | `updatedAt` | DateTime | "02/03/2026 06:30:34" |
| TW_Q_NB_ETUD | `nbStudents` | Int | 1 |
| TW_Q_NB_COPI_RECU | `nbCopiesReceived` | Int | 1 |
| TW_Q_NB_COPI_CORR | `nbCopiesCorrected` | Int | 1 |
| TW_NM_FUSE_HORA | `timezone` | String | "America/Toronto" |
| GIC_ROW_ID | (depuis GIC) | String | "1-3A9ZFF0" |
| GIC_EXAM_ID | (depuis GIC) | String | "1-39XNAFV" |
| GIC_CONTACT_ID | (depuis GIC) | String | "1-3A9TIPU" |

**Code C# POC:** ✅ Voir RIPQUAR_POC_ScriptTask.cs

---

## 2️⃣ STG_TW_PARTIE_EXAMEN

**Source JSON:** `RespBody.examParts[]` (tableau)

| Colonne STG | Chemin JSON | Type | Exemple |
|------------|-------------|------|---------|
| TW_N_PART_ID | `examParts[i].id` | UUID | "a57a0bd1-86a2-4cb7-8fd6-c4478f2f0064" |
| TW_N_EXAM_ID | (parent) `id` | UUID | "269bfc6c-..." |
| TW_NM_PART | `examParts[i].name` | String | "" (vide si pas de nom) |
| TW_Q_NB_PTS | `examParts[i].numberPoint` | Decimal | 45 |
| TW_N_POSI | `examParts[i].position` | Int | 0 |
| TW_I_ALEA_PART_INDEX | `examParts[i].randomPartIndexes` | Bool | false |
| TW_I_LIMI_DURE | `examParts[i].durationLimit` | Bool | false |
| TW_TX_INST | `examParts[i].instruction` | Text | "" |
| TW_DH_CREA | `examParts[i].createdAt` | DateTime | "01/19/2026 21:05:57" |

**Logique de parsing:**
```csharp
JArray examParts = (JArray)respBody["examParts"];
foreach (JObject part in examParts)
{
    string partId = part["id"].ToString();
    string examId = respBody["id"].ToString(); // Clé étrangère
    // ... Insérer dans STG_TW_PARTIE_EXAMEN
}
```

---

## 3️⃣ STG_TW_INDEX_PARTIE

**Source JSON:** `RespBody.examParts[].partIndexes[]` (tableau imbriqué)

| Colonne STG | Chemin JSON | Type | Notes |
|------------|-------------|------|-------|
| TW_N_INDE_PART_ID | (généré) | UUID/Int | Pas dans JSON, générer |
| TW_N_PART_ID | (parent) `examParts[i].id` | UUID | Clé étrangère vers partie |
| TW_N_QUES_ID | `partIndexes[j].question[0].id` | UUID | Première question |
| TW_N_POSI | (position dans tableau) | Int | Index j |

**Note:** partIndexes est un simple pointeur vers les questions, pas beaucoup d'info.

---

## 4️⃣ STG_TW_QUESTION

**Source JSON:** `RespBody.examParts[].partIndexes[].question[]`

| Colonne STG | Chemin JSON | Type | Exemple |
|------------|-------------|------|---------|
| TW_N_QUES_ID | `question[0].id` | UUID | "abc123..." |
| TW_N_PART_ID | (parent) `examParts[i].id` | UUID | Clé étrangère |
| TW_C_REF_EXT | `question[0].externalReference` | String | "M35000-00079-F" |
| TW_M_PTS | `question[0].numberPoint` | Decimal | 1.00 |
| TW_I_SANS_PTS | `question[0].noPoints` | Bool | false |
| TW_DH_CREA | `question[0].createdAt` | DateTime | "2026-01-19T21:05:57+00:00" |
| TW_N_UUID_BANQ_QUES | `question[0].questionBank[0].id` | UUID | "4d5c9d38-..." |

**Note:** 
- `question` est un tableau mais semble toujours avoir 1 élément → `question[0]`
- `questionBank` contient les choix de réponse

---

## 5️⃣ STG_TW_CHOIX

**Source JSON:** `question[0].questionBank[0].choices[]`

| Colonne STG | Chemin JSON | Type | Exemple |
|------------|-------------|------|---------|
| TW_N_CHOI_ID | `choices[k].id` | UUID | "c3fd3bff-..." |
| TW_N_QUES_ID | (parent) `question[0].id` | UUID | Clé étrangère |
| TW_DE_CHOI | `choices[k].name` | String | "2, 4 et 5 seulement" |
| TW_I_CORR | `choices[k].correct` | Bool | "true" / "false" |
| TW_N_POSI | `choices[k].position` | Int | 3 |

**Logique de parsing:**
```csharp
JArray choices = (JArray)questionBank["choices"];
foreach (JObject choice in choices)
{
    bool isCorrect = choice["correct"].ToString() == "true";
    // ... Insérer
}
```

---

## 6️⃣ STG_TW_CLASSE

**Source JSON:** `RespBody.classes[]`

| Colonne STG | Chemin JSON | Type | Exemple |
|------------|-------------|------|---------|
| TW_N_CLAS_ID | (généré ou hash?) | UUID | À définir |
| TW_N_EXAM_ID | (parent) `id` | UUID | Clé étrangère |
| TW_NM_CLAS | `classes[i].name` | String | "5873-4898" |

**Note:** Très simple, juste le nom de la classe

---

## 7️⃣ STG_TW_ETABLISSEMENT

**Source JSON:** `RespBody.establishment` (si existe)

| Colonne STG | Chemin JSON | Type | Notes |
|------------|-------------|------|-------|
| TW_N_ETAB_ID | `establishment.id` | UUID | Si disponible |
| TW_NM_ETAB | `establishment.name` | String | Nom établissement |
| ... | | | **À VALIDER - Pas dans JSON exemple** |

**⚠️ ATTENTION:** Pas vu dans le JSON exemple - vérifier avec JSON complet

---

## 8️⃣ STG_TW_ANNEE_ACADEMIQUE

**Source JSON:** `RespBody.academicYear` (si existe)

| Colonne STG | Chemin JSON | Type | Notes |
|------------|-------------|------|-------|
| TW_N_ANNE_ACAD_ID | `academicYear.id` | UUID | Si disponible |
| TW_NM_ANNE_ACAD | `academicYear.name` | String | Ex: "2025-2026" |
| ... | | | **À VALIDER - Pas dans JSON exemple** |

**⚠️ ATTENTION:** Pas vu dans JSON exemple - vérifier

---

## 9️⃣ STG_TW_UTILISATEUR

**Source JSON:** `RespBody.students[]` ou `RespBody.graders[]`

| Colonne STG | Chemin JSON | Type | Exemple |
|------------|-------------|------|---------|
| TW_N_UTIL_ID | `userId` | String | "324046" |
| TW_NM_UTIL | `firstName` + `lastName` | String | "Correcteur général bâtiment DQ" |
| TW_NM_PREN | `firstName` | String | "Correcteur général bâtiment" |
| TW_NM_NOM | `lastName` | String | "DQ" |
| TW_I_BLOQ | `blocked` | Bool | false |
| TW_C_TYPE_UTIL | (déduire) | String | "STUDENT" ou "GRADER" |

**Sources multiples:**
- `studentPapersDistributions[].grader[]`
- Possiblement `students[]` (à confirmer avec JSON complet)

---

## 🔟 STG_TW_COPIE

**Source JSON:** `RespBody.students[].papers[]` (probablement)

| Colonne STG | Chemin JSON | Type | Notes |
|------------|-------------|------|-------|
| TW_N_COPI_ID | `papers[i].id` | UUID | ID copie |
| TW_N_EXAM_ID | (parent) `id` | UUID | Clé étrangère examen |
| TW_N_UTIL_ID | `papers[i].studentId` | String | ID étudiant |
| TW_DH_SOUM | `papers[i].submittedAt` | DateTime | Date soumission |
| TW_M_NOTE_TOTA | `papers[i].totalScore` | Decimal | Note totale |
| TW_C_STAT | `papers[i].status` | String | "CORRECTED" / "PENDING" |

**⚠️ ATTENTION:** Structure exacte à valider avec JSON complet (tronqué dans exemple)

---

## 1️⃣1️⃣ STG_TW_REPONSE

**Source JSON:** `papers[].answers[]`

| Colonne STG | Chemin JSON | Type | Notes |
|------------|-------------|------|-------|
| TW_N_REPO_ID | `answers[i].id` | UUID | ID réponse |
| TW_N_COPI_ID | (parent) `papers[j].id` | UUID | Clé étrangère copie |
| TW_N_QUES_ID | `answers[i].questionId` | UUID | Question répondue |
| TW_N_CHOI_ID | `answers[i].choiceId` | UUID | Choix sélectionné |
| TW_DH_REPO | `answers[i].answeredAt` | DateTime | Timestamp réponse |

**⚠️ ATTENTION:** À valider - structure probable basée sur standard TestWe

---

## 1️⃣2️⃣ STG_TW_CORRECTION

**Source JSON:** `papers[].corrections[]`

| Colonne STG | Chemin JSON | Type | Notes |
|------------|-------------|------|-------|
| TW_N_CORR_ID | `corrections[i].id` | UUID | ID correction |
| TW_N_COPI_ID | (parent) `papers[j].id` | UUID | Clé étrangère copie |
| TW_N_QUES_ID | `corrections[i].questionId` | UUID | Question corrigée |
| TW_M_NOTE_ATTR | `corrections[i].pointsAwarded` | Decimal | Points attribués |
| TW_TX_COMM | `corrections[i].comment` | Text | Commentaire correcteur |
| TW_N_CORR_UTIL_ID | `corrections[i].correctorId` | String | ID correcteur |
| TW_DH_CORR | `corrections[i].correctedAt` | DateTime | Date correction |

**⚠️ ATTENTION:** À valider - structure probable

---

## 1️⃣3️⃣ STG_TW_JSON_BRUT

**Source:** Ligne complète

| Colonne STG | Source | Type | Notes |
|------------|--------|------|-------|
| TW_N_JSON_ID | (auto-increment) | Int | PK |
| GIC_ROW_ID | `CX_INSC_EXAM.ROW_ID` | String | Lien GIC |
| TW_TX_JSON_EXAM | `X_GI_V_VECT_EXAM` | NVARCHAR(MAX) | JSON complet examen |
| TW_TX_JSON_CORR | `X_GI_V_VECT_CORR_EXAM` | NVARCHAR(MAX) | JSON complet correction |
| TW_DH_IMPO | GETDATE() | DateTime | Date import |

**Utilité:** Archive complète pour audit / troubleshooting

---

## 📊 Ordre de parsing recommandé

**Niveau 1 - Racine:**
1. STG_TW_JSON_BRUT ← Backup complet
2. STG_TW_EXAMEN ← ✅ POC FAIT
3. STG_TW_CLASSE
4. STG_TW_ETABLISSEMENT (si existe)
5. STG_TW_ANNEE_ACADEMIQUE (si existe)

**Niveau 2 - Structure examen:**
6. STG_TW_PARTIE_EXAMEN
7. STG_TW_INDEX_PARTIE
8. STG_TW_QUESTION
9. STG_TW_CHOIX

**Niveau 3 - Passations (nécessite JSON complet):**
10. STG_TW_UTILISATEUR
11. STG_TW_COPIE
12. STG_TW_REPONSE
13. STG_TW_CORRECTION

---

## 🚨 Points d'attention

### JSON tronqué dans l'exemple
- **Problème:** Exemple à 65,535 caractères
- **Impact:** Tables 10-13 non validables
- **Solution:** Utiliser CONVERT(NVARCHAR(MAX)...) dans SSIS (pas de troncature)

### Structure exacte à confirmer
- **Tables incertaines:** 7, 8, 10, 11, 12, 13
- **Action:** Analyser JSON complet lors de l'exécution SSIS
- **Méthode:** Ajouter logs pour afficher structure JSON

### Clés de liaison
- **Important:** TW_N_EXAM_ID est la FK principale
- **Vérifier:** Que les UUID sont cohérents entre niveaux

---

## 💡 Prochaines étapes après POC

1. **Valider STG_TW_EXAMEN** ← ✅ EN COURS
2. **Parser examParts → STG_TW_PARTIE_EXAMEN**
3. **Parser questions/choix → STG_TW_QUESTION + STG_TW_CHOIX**
4. **Analyser JSON complet pour tables 10-13**
5. **Créer package complet 13 tables**
6. **Tests volumétrie (1000+ lignes)**

---

**Document vivant - À mettre à jour après analyse JSON complets**
