# RIPQUAR - Dictionnaire de Données - Tables Staging (STG)

**Projet:** RIPQUAR - Entrepôt de données examens TestWe
**Schéma:** [SDGIC01].[STG]
**Version:** 1.0
**Date:** Février 2026

---

## 📊 Vue d'ensemble du modèle

### **Architecture des données**

```
SOURCE: API TestWe (JSON)
    ↓
STAGING: 12 tables STG (zone temporaire)
    ↓
DATAWAREHOUSE: Dimensions + Faits (zone permanente)
```

### **12 Tables STG - Hiérarchie**

```
1. TW_EXAMEN (racine)
   ├─ 2. TW_PARTIE_EXAMEN
   │   └─ 3. TW_QUESTION
   │       └─ 4. TW_CHOIX
   │
   ├─ 5. TW_CLASSE
   ├─ 6. TW_ETABLISSEMENT
   ├─ 7. TW_ANNEE_ACADEMIQUE
   │
   └─ 8. TW_COPIE (passation étudiante)
       ├─ 9. TW_REPONSE
       └─ 10. TW_CORRECTION

11. TW_UTILISATEUR (étudiants + correcteurs)
12. TW_INDEX_PARTIE (table de liaison)
```

### **Cardinalités attendues (annuel)**

| Table | Volume estimé | Commentaire |
|-------|---------------|-------------|
| TW_EXAMEN | ~500 | Examens créés/an |
| TW_PARTIE_EXAMEN | ~500 | 1 partie/examen en moyenne |
| TW_QUESTION | ~20,000 | ~40 questions/examen |
| TW_CHOIX | ~80,000 | ~4 choix/question |
| TW_CLASSE | ~1,000 | Groupes d'étudiants |
| TW_COPIE | ~50,000 | Passations étudiantes |
| TW_REPONSE | ~2,000,000 | 40 réponses/copie |
| TW_CORRECTION | ~2,000,000 | 1 correction/réponse |
| TW_UTILISATEUR | ~10,000 | Étudiants + correcteurs |

---

## 1️⃣ TW_EXAMEN - Table principale examens

### **Description**
Contient les informations générales sur chaque examen TestWe. C'est la table racine du modèle - toutes les autres tables y sont reliées.

### **Source de données**
- **Primaire:** API TestWe - JSON RespBody (niveau racine)
- **Secondaire:** Généré par ETL (colonnes techniques)

### **Fréquence de chargement**
Quotidien (job nuit à 02h00)

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_EXAM_ID** | varchar(36) | NO | **PK** - Identifiant unique examen (UUID TestWe) | `id` | 269bfc6c-7458-41ad-99ce |
| TW_N_EXAM_ID_INT | int | YES | Clé de substitution (surrogate key) pour jointures | Généré phase DIM | 1, 2, 3... |
| TW_C_EXAM | varchar(255) | NO | Code/clé publique de l'examen | `publicKey` | 265269 |
| TW_NM_MATI | varchar(255) | YES | Nom de la matière | À clarifier (GIC?) | Électricité |
| TW_Q_DURE_SEC | int | YES | Durée de l'examen en secondes | `duration` | 10800 (3h) |
| TW_Q_PTS_MAX | int | YES | Points maximum de l'examen | `maxPoints` | 45 |
| TW_DH_DEBU | datetime | YES | Date/heure de création de l'examen | `createdAt` | 2026-01-19 21:05:57 |
| TW_DH_FIN | datetime | YES | Date/heure de validation de l'examen | `validatedAt` | 2026-01-19 21:06:01 |
| TW_DH_MODI | datetime | YES | Date/heure de dernière modification | `updatedAt` | 2026-02-03 06:30:34 |
| TW_C_STAT | varchar(30) | YES | Statut de l'examen | `status` | PUBLISHED, DRAFT |
| TW_C_FUSE_HORA | varchar(50) | YES | Fuseau horaire | `timezone` | America/Toronto |
| TW_C_CODE_PUBL | varchar(100) | YES | Code d'accès public alternatif | `examCode` ou `accessCode` | EXAM-2026-001 |
| TW_I_QUES_ALEA | bit | YES | Indicateur questions aléatoires (1=oui, 0=non) | `examParts[0].randomPartIndexes` | 0 |
| TW_I_CHOI_ALEA | bit | YES | Indicateur choix de réponse aléatoires | `examParts[0].randomizeChoices` | 1 |
| TW_Q_NB_QUES | int | YES | Nombre total de questions | Calculé (COUNT TW_QUESTION) | 45 |
| TW_Q_NB_ETUD | int | YES | Nombre d'étudiants inscrits | `nbStudents` | 120 |
| TW_Q_NB_COPI_RECU | int | YES | Nombre de copies reçues | `nbCopiesReceived` | 115 |
| TW_Q_NB_COPI_CORR | int | YES | Nombre de copies corrigées | `nbCopiesCorrected` | 110 |
| TW_M_NOTE_MOYE | decimal | YES | Note moyenne de l'examen | Calculé (AVG TW_COPIE) | 32.5 |
| TW_DH_VALI | datetime | YES | Date de validation (copie de TW_DH_FIN) | `validatedAt` | 2026-01-19 21:06:01 |
| TW_DH_CREA | datetime | YES | Date de création (copie de TW_DH_DEBU) | `createdAt` | 2026-01-19 21:05:57 |
| TW_DH_CHARG_ETL | datetime | NO | **Audit** - Date/heure du chargement ETL | GETDATE() | 2026-02-16 14:30:52 |
| TW_C_SYST_SRCE | varchar(20) | NO | **Audit** - Système source | Hardcodé | TestWe |
| TW_C_LOT_ETL | varchar(50) | YES | **Audit** - Identifiant du lot ETL | Généré | RIPQUAR_20260216_143052 |
| TW_C_EMPR_LIGN | varchar(64) | YES | **CDC** - Hash MD5 pour détecter changements | Calculé | a3f5e9... |

### **Clés et index**
- **Primary Key:** TW_N_EXAM_ID
- **Index:** TW_N_EXAM_ID_INT (pour jointures DWH)

### **Relations**
- **Parent de:** TW_PARTIE_EXAMEN (1→N)
- **Parent de:** TW_CLASSE (1→N)
- **Parent de:** TW_COPIE (1→N)

---

## 2️⃣ TW_PARTIE_EXAMEN - Sections/parties d'examen

### **Description**
Un examen peut être divisé en plusieurs parties (ex: Partie A - Théorie, Partie B - Pratique). Chaque partie contient des questions et peut avoir ses propres règles (durée limite, instructions spécifiques).

### **Source de données**
API TestWe - JSON `RespBody.examParts[]` (tableau)

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_PART_ID** | varchar(36) | NO | **PK** - Identifiant unique partie (UUID) | `examParts[i].id` | a57a0bd1-86a2-4cb7... |
| **TW_N_EXAM_ID** | varchar(36) | NO | **FK** - Lien vers TW_EXAMEN | (parent) `id` | 269bfc6c-7458... |
| TW_NM_PART | varchar(255) | YES | Nom/titre de la partie | `examParts[i].name` | Partie A - Théorie |
| TW_Q_NB_PTS | decimal(5,2) | YES | Points attribués à cette partie | `examParts[i].numberPoint` | 45.00 |
| TW_N_POSI | int | YES | Position/ordre de la partie dans l'examen | `examParts[i].position` | 0, 1, 2... |
| TW_I_ALEA_PART_INDEX | bit | YES | Questions aléatoires dans cette partie | `examParts[i].randomPartIndexes` | 1 |
| TW_I_LIMI_DURE | bit | YES | Partie a une limite de durée | `examParts[i].durationLimit` | 0 |
| TW_TX_INST | text | YES | Instructions spécifiques pour la partie | `examParts[i].instruction` | Répondez à toutes... |
| TW_DH_CREA | datetime | YES | Date de création de la partie | `examParts[i].createdAt` | 2026-01-19 21:05:57 |

### **Clés et relations**
- **PK:** TW_N_PART_ID
- **FK:** TW_N_EXAM_ID → TW_EXAMEN
- **Cardinalité:** 1 examen = 1-N parties (moyenne: 1)

---

## 3️⃣ TW_QUESTION - Questions d'examen

### **Description**
Contient toutes les questions de tous les examens. Une question appartient à une partie d'examen et possède plusieurs choix de réponse.

### **Source de données**
API TestWe - JSON `examParts[].partIndexes[].question[]`

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_QUES_ID** | varchar(36) | NO | **PK** - Identifiant unique question | `question[0].id` | abc123-def456... |
| **TW_N_PART_ID** | varchar(36) | NO | **FK** - Lien vers TW_PARTIE_EXAMEN | (parent) `examParts[i].id` | a57a0bd1... |
| TW_C_REF_EXT | varchar(50) | YES | Référence externe GIC | `question[0].externalReference` | M35000-00079-F |
| TW_M_PTS | decimal(5,2) | YES | Points attribués à la question | `question[0].numberPoint` | 1.00 |
| TW_I_SANS_PTS | bit | YES | Question sans points (0/1) | `question[0].noPoints` | 0 |
| TW_DH_CREA | datetime | YES | Date de création | `question[0].createdAt` | 2026-01-19 21:05:57 |
| TW_N_UUID_BANQ_QUES | varchar(36) | YES | UUID banque de questions | `question[0].questionBank[0].id` | 4d5c9d38-9ffb... |
| TW_TX_QUES | text | YES | Texte de la question | `questionBank[0].text` | Quelle est la loi d'Ohm? |
| TW_N_POSI | int | YES | Position dans la partie | Index dans tableau | 1, 2, 3... |

### **Clés et relations**
- **PK:** TW_N_QUES_ID
- **FK:** TW_N_PART_ID → TW_PARTIE_EXAMEN
- **Cardinalité:** 1 partie = 1-N questions (moyenne: 40)

---

## 4️⃣ TW_CHOIX - Choix de réponse

### **Description**
Contient tous les choix de réponse possibles pour chaque question. Typiquement 4-5 choix par question (A, B, C, D, E), dont un seul est correct.

### **Source de données**
API TestWe - JSON `question[0].questionBank[0].choices[]`

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_CHOI_ID** | varchar(36) | NO | **PK** - Identifiant unique choix | `choices[k].id` | c3fd3bff-9c40... |
| **TW_N_QUES_ID** | varchar(36) | NO | **FK** - Lien vers TW_QUESTION | (parent) `question[0].id` | abc123-def456... |
| TW_DE_CHOI | text | YES | Texte du choix de réponse | `choices[k].name` | R = U / I |
| TW_I_CORR | bit | YES | Indicateur bonne réponse (1=correct) | `choices[k].correct` | 1 |
| TW_N_POSI | int | YES | Position du choix (A=1, B=2, etc.) | `choices[k].position` | 1, 2, 3, 4 |

### **Clés et relations**
- **PK:** TW_N_CHOI_ID
- **FK:** TW_N_QUES_ID → TW_QUESTION
- **Cardinalité:** 1 question = 2-10 choix (moyenne: 4)

---

## 5️⃣ TW_CLASSE - Groupes/classes d'étudiants

### **Description**
Représente les groupes d'étudiants assignés à un examen (ex: Groupe 5873-4898). Permet d'organiser les passations par cohorte.

### **Source de données**
API TestWe - JSON `RespBody.classes[]`

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_CLAS_ID** | int | NO | **PK** - Identifiant unique classe (auto-increment) | Généré | 1, 2, 3... |
| **TW_N_EXAM_ID** | varchar(36) | NO | **FK** - Lien vers TW_EXAMEN | (parent) `id` | 269bfc6c... |
| TW_NM_CLAS | varchar(100) | YES | Nom/code de la classe | `classes[i].name` | 5873-4898 |
| TW_Q_NB_ETUD | int | YES | Nombre d'étudiants dans la classe | Dérivé | 30 |

### **Clés et relations**
- **PK:** TW_N_CLAS_ID
- **FK:** TW_N_EXAM_ID → TW_EXAMEN
- **Cardinalité:** 1 examen = 1-N classes

---

## 6️⃣ TW_ETABLISSEMENT - Établissements d'enseignement

### **Description**
Liste des établissements (écoles, collèges) qui utilisent les examens TestWe. Optionnel selon la structure JSON.

### **Source de données**
API TestWe - JSON `RespBody.establishment` (si existe)

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_ETAB_ID** | varchar(36) | NO | **PK** - Identifiant établissement | `establishment.id` | xyz789... |
| TW_NM_ETAB | varchar(255) | YES | Nom de l'établissement | `establishment.name` | Cégep de Montréal |
| TW_C_CODE_ETAB | varchar(50) | YES | Code officiel établissement | `establishment.code` | CEGEP-MTL |

### **Note**
⚠️ Structure à confirmer - Pas observée dans le JSON POC. Peut nécessiter jointure avec tables GIC.

---

## 7️⃣ TW_ANNEE_ACADEMIQUE - Années scolaires

### **Description**
Années académiques durant lesquelles les examens sont administrés (ex: 2025-2026).

### **Source de données**
API TestWe - JSON `RespBody.academicYear` (si existe)

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_ANNE_ACAD_ID** | varchar(36) | NO | **PK** - Identifiant année académique | `academicYear.id` | year2025-2026 |
| TW_NM_ANNE_ACAD | varchar(50) | YES | Nom de l'année académique | `academicYear.name` | 2025-2026 |
| TW_DH_DEBU | datetime | YES | Date de début | `academicYear.startDate` | 2025-09-01 |
| TW_DH_FIN | datetime | YES | Date de fin | `academicYear.endDate` | 2026-06-30 |

### **Note**
⚠️ Structure à confirmer - Pas observée dans le JSON POC.

---

## 8️⃣ TW_COPIE - Passations d'examens (copies étudiantes)

### **Description**
Représente chaque passation individuelle d'examen par un étudiant. Contient le résultat global et le statut de correction.

### **Source de données**
API TestWe - JSON `RespBody.students[].papers[]` (probablement dans X_GI_V_VECT_CORR_EXAM)

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_COPI_ID** | varchar(36) | NO | **PK** - Identifiant unique copie | `papers[i].id` | copy123... |
| **TW_N_EXAM_ID** | varchar(36) | NO | **FK** - Lien vers TW_EXAMEN | (parent) `id` | 269bfc6c... |
| **TW_N_UTIL_ID** | varchar(36) | NO | **FK** - Lien vers TW_UTILISATEUR (étudiant) | `papers[i].studentId` | user456... |
| TW_DH_SOUM | datetime | YES | Date/heure de soumission | `papers[i].submittedAt` | 2026-01-20 10:45:23 |
| TW_DH_DEBU | datetime | YES | Date/heure de début | `papers[i].startedAt` | 2026-01-20 09:00:00 |
| TW_M_NOTE_TOTA | decimal(5,2) | YES | Note totale obtenue | `papers[i].totalScore` | 38.50 |
| TW_M_NOTE_MAX | decimal(5,2) | YES | Note maximale possible | `papers[i].maxScore` | 45.00 |
| TW_C_STAT | varchar(30) | YES | Statut de la copie | `papers[i].status` | CORRECTED, PENDING |
| TW_N_CORR_ID | varchar(36) | YES | **FK** - Correcteur assigné | `papers[i].correctorId` | grader789... |

### **Clés et relations**
- **PK:** TW_N_COPI_ID
- **FK:** TW_N_EXAM_ID → TW_EXAMEN
- **FK:** TW_N_UTIL_ID → TW_UTILISATEUR
- **FK:** TW_N_CORR_ID → TW_UTILISATEUR
- **Cardinalité:** 1 examen = N copies (~100 par examen)

---

## 9️⃣ TW_REPONSE - Réponses des étudiants

### **Description**
Contient chaque réponse donnée par un étudiant à chaque question. Permet l'analyse détaillée des résultats.

### **Source de données**
API TestWe - JSON `papers[].answers[]`

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_REPO_ID** | varchar(36) | NO | **PK** - Identifiant unique réponse | `answers[i].id` | ans123... |
| **TW_N_COPI_ID** | varchar(36) | NO | **FK** - Lien vers TW_COPIE | (parent) `papers[j].id` | copy123... |
| **TW_N_QUES_ID** | varchar(36) | NO | **FK** - Lien vers TW_QUESTION | `answers[i].questionId` | abc123... |
| **TW_N_CHOI_ID** | varchar(36) | YES | **FK** - Choix sélectionné | `answers[i].choiceId` | c3fd3bff... |
| TW_DH_REPO | datetime | YES | Date/heure de la réponse | `answers[i].answeredAt` | 2026-01-20 09:15:42 |
| TW_I_CORR | bit | YES | Indicateur réponse correcte (calculé) | Dérivé (TW_CHOIX.TW_I_CORR) | 1 |
| TW_M_PTS_OBTE | decimal(5,2) | YES | Points obtenus | Dérivé ou `pointsAwarded` | 1.00 |

### **Clés et relations**
- **PK:** TW_N_REPO_ID
- **FK:** TW_N_COPI_ID → TW_COPIE
- **FK:** TW_N_QUES_ID → TW_QUESTION
- **FK:** TW_N_CHOI_ID → TW_CHOIX
- **Cardinalité:** 1 copie = N réponses (~40 par copie)

---

## 🔟 TW_CORRECTION - Corrections et annotations

### **Description**
Contient les corrections détaillées par question, incluant points attribués, commentaires du correcteur, et ajustements manuels.

### **Source de données**
API TestWe - JSON `papers[].corrections[]`

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_CORR_ID** | varchar(36) | NO | **PK** - Identifiant unique correction | `corrections[i].id` | corr123... |
| **TW_N_COPI_ID** | varchar(36) | NO | **FK** - Lien vers TW_COPIE | (parent) `papers[j].id` | copy123... |
| **TW_N_QUES_ID** | varchar(36) | NO | **FK** - Question corrigée | `corrections[i].questionId` | abc123... |
| TW_M_NOTE_ATTR | decimal(5,2) | YES | Points attribués par le correcteur | `corrections[i].pointsAwarded` | 0.75 |
| TW_M_NOTE_AUTO | decimal(5,2) | YES | Points calculés automatiquement | `corrections[i].autoScore` | 1.00 |
| TW_TX_COMM | text | YES | Commentaire du correcteur | `corrections[i].comment` | Bonne approche mais... |
| TW_N_CORR_UTIL_ID | varchar(36) | YES | **FK** - Correcteur | `corrections[i].correctorId` | grader789... |
| TW_DH_CORR | datetime | YES | Date/heure de correction | `corrections[i].correctedAt` | 2026-01-21 14:30:15 |

### **Clés et relations**
- **PK:** TW_N_CORR_ID
- **FK:** TW_N_COPI_ID → TW_COPIE
- **FK:** TW_N_QUES_ID → TW_QUESTION
- **FK:** TW_N_CORR_UTIL_ID → TW_UTILISATEUR
- **Cardinalité:** 1 copie = N corrections (~40 par copie)

---

## 1️⃣1️⃣ TW_UTILISATEUR - Étudiants et correcteurs

### **Description**
Contient tous les utilisateurs du système TestWe: étudiants qui passent les examens et correcteurs qui les évaluent.

### **Source de données**
API TestWe - JSON `RespBody.students[]` et `RespBody.graders[]` ou `studentPapersDistributions[].grader[]`

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_UTIL_ID** | varchar(36) | NO | **PK** - Identifiant unique utilisateur | `userId` | 324046 |
| TW_NM_UTIL | varchar(255) | YES | Nom complet | `fullName` | Jean Tremblay |
| TW_NM_PREN | varchar(100) | YES | Prénom | `firstName` | Jean |
| TW_NM_NOM | varchar(100) | YES | Nom de famille | `lastName` | Tremblay |
| TW_C_COURRIEL | varchar(255) | YES | Adresse courriel | `email` | jean.tremblay@... |
| TW_I_BLOQ | bit | YES | Indicateur compte bloqué | `blocked` | 0 |
| TW_C_TYPE_UTIL | varchar(20) | YES | Type utilisateur | Dérivé | STUDENT, GRADER |
| TW_C_CODE_PERM | varchar(20) | YES | Code permanent (étudiant) | `studentCode` | TREJ12345678 |

### **Clés et relations**
- **PK:** TW_N_UTIL_ID
- **Référencé par:** TW_COPIE, TW_CORRECTION

---

## 1️⃣2️⃣ TW_INDEX_PARTIE - Table de liaison (optionnelle)

### **Description**
Table de liaison entre parties d'examen et questions. Permet de gérer l'ordre et l'organisation des questions dans les parties.

### **Source de données**
API TestWe - JSON `examParts[].partIndexes[]`

### **Colonnes**

| Colonne | Type | Null | Description | Source JSON | Exemple |
|---------|------|------|-------------|-------------|---------|
| **TW_N_INDE_PART_ID** | int | NO | **PK** - Identifiant unique (auto-increment) | Généré | 1, 2, 3... |
| **TW_N_PART_ID** | varchar(36) | NO | **FK** - Lien vers TW_PARTIE_EXAMEN | (parent) `examParts[i].id` | a57a0bd1... |
| **TW_N_QUES_ID** | varchar(36) | NO | **FK** - Lien vers TW_QUESTION | `partIndexes[j].question[0].id` | abc123... |
| TW_N_POSI | int | YES | Position dans la partie | Index j | 1, 2, 3... |

### **Note**
Table technique pour gérer les relations many-to-many si nécessaire. Peut être optionnelle si TW_QUESTION.TW_N_PART_ID suffit.

---

## 📊 Matrice des relations

| Table Parent | Table Enfant | Type | Cardinalité |
|--------------|--------------|------|-------------|
| TW_EXAMEN | TW_PARTIE_EXAMEN | 1→N | 1 exam = 1-5 parties |
| TW_EXAMEN | TW_CLASSE | 1→N | 1 exam = 1-10 classes |
| TW_EXAMEN | TW_COPIE | 1→N | 1 exam = 10-200 copies |
| TW_PARTIE_EXAMEN | TW_QUESTION | 1→N | 1 partie = 10-50 questions |
| TW_QUESTION | TW_CHOIX | 1→N | 1 question = 2-10 choix |
| TW_COPIE | TW_REPONSE | 1→N | 1 copie = N réponses |
| TW_COPIE | TW_CORRECTION | 1→N | 1 copie = N corrections |
| TW_UTILISATEUR | TW_COPIE | 1→N | 1 étudiant = N copies |
| TW_UTILISATEUR | TW_CORRECTION | 1→N | 1 correcteur = N corrections |

---

## 🔧 Colonnes techniques récurrentes

### **Audit ETL (présentes dans la plupart des tables)**

| Colonne | Description | Utilité |
|---------|-------------|---------|
| TW_DH_CHARG_ETL | Date/heure du chargement | Traçabilité - quand les données ont été chargées |
| TW_C_SYST_SRCE | Système source (ex: "TestWe") | Traçabilité - d'où viennent les données |
| TW_C_LOT_ETL | Identifiant du lot ETL | Traçabilité - quel run ETL a chargé ces données |
| TW_C_EMPR_LIGN | Hash MD5 de la ligne | CDC - détecter si la ligne a changé |

### **Conventions de nommage**

| Préfixe | Signification | Type SQL | Exemple |
|---------|---------------|----------|---------|
| TW_N_ | Numérique/ID | int, varchar(36) | TW_N_EXAM_ID |
| TW_C_ | Code/Caractère | varchar | TW_C_STAT |
| TW_NM_ | Nom | varchar | TW_NM_MATI |
| TW_DE_ | Description | varchar, text | TW_DE_CHOI |
| TW_DH_ | Date/Heure | datetime | TW_DH_DEBU |
| TW_Q_ | Quantité | int | TW_Q_NB_ETUD |
| TW_M_ | Montant/Mesure | decimal | TW_M_NOTE_MOYE |
| TW_I_ | Indicateur | bit | TW_I_QUES_ALEA |
| TW_TX_ | Texte long | text | TW_TX_INST |

---

## 📝 Notes importantes

### **Gestion des NULL**
- Colonnes optionnelles permettent NULL
- Le code ETL gère `(object)DBNull.Value` pour les valeurs manquantes
- NULL ≠ erreur, juste absence d'information

### **Sources de données multiples**
Certaines tables combinent plusieurs sources:
- **Primaire:** JSON TestWe (la plupart des colonnes)
- **Dérivé:** Calculs à partir d'autres tables (notes moyennes, compteurs)
- **Généré:** Clés de substitution, timestamps ETL, identifiants de lot

### **Zones temporelles**
- Toutes les dates/heures sont en UTC ou timezone spécifiée dans TW_C_FUSE_HORA
- Conversion en heure locale doit être faite au niveau présentation (Power BI)

---

**Document:** RIPQUAR_Dictionnaire_Donnees_STG.md
**Version:** 1.0
**Date:** Février 2026
**Auteur:** Marcus Duverger - BI Analyst-Developer
**Statut:** ✅ Validé pour TW_EXAMEN | 🔄 En cours pour autres tables
