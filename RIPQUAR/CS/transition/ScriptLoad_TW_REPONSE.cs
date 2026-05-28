// UPDATED: Thread-safe logging with SafeLog() - 2026-03-27
/*
 * ═══════════════════════════════════════════════════════════════════════════
 * RIPQUAR - Phase 1 STG - TW_REPONSE (Table 11/12)
 * ═══════════════════════════════════════════════════════════════════════════
 * 
 * Objectif:    Charger les réponses individuelles des candidats par question
 * Source:      CX_INSC_EXAM.X_GI_V_VECT_REPN_CAND_EXAM (vecteur caractères)
 * Destination: SDGIC01.STG.TW_REPONSE
 * Filtre:      Copies existantes dans TW_COPIE uniquement
 * 
 * Logique:
 *   - Parse vecteur caractère par caractère ("CACCDDD...")
 *   - Lookup (examId, position) → questionUuid via TW_INDEX_PARTIE
 *   - TW_C_URI_QUES stocke URI complète: "/api/questions/{uuid}"
 *   - Permet lookup direct dans DIM_QUESTION via RIGHT(URI, 36)
 * 
 * Colonnes principales:
 *   - TW_C_URI_QUES: URI question complète (36 derniers chars = UUID)
 *   - TW_DE_REPN_TEXT: Réponse donnée (caractère du vecteur)
 * ═══════════════════════════════════════════════════════════════════════════
 */

#region Namespaces
using System;
using System.Data;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.IO;
#endregion

namespace ST_TW_REPONSE
{
    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {

        private string _logFile;
        private string _scriptName;

        public void Main()
        {
            _logFile = Dts.Variables["User::LogFile"].Value.ToString();
            _scriptName = Dts.Variables["System::TaskName"].Value.ToString();

            try
            {
                // ═══════════════════════════════════════════════════════════
                // 3. LOG DÉBUT
                // ═══════════════════════════════════════════════════════════
                SafeLog($"\n=== DÉBUT {_scriptName} - {DateTime.Now} ===\n");

                Log("Script démarre");

                // ===================================================================
                // 1. RÉCUPÉRATION DES VARIABLES SSIS
                // ===================================================================
                Log("Lecture variables...");
                string serverSource = Dts.Variables["User::ServerSource"].Value.ToString();
                string bdSource = Dts.Variables["User::BDSource"].Value.ToString();
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                string bdDest = Dts.Variables["User::BDDest"].Value.ToString();

                Log($"Source: {serverSource} / {bdSource}");
                Log($"Dest: {serverDest} / {bdDest}");

                // ===================================================================
                // 2. CONSTRUCTION CONNECTION STRINGS
                // ===================================================================
                Log("Construction connection strings...");
                string connStrSource = $"Data Source={serverSource};Initial Catalog={bdSource};Integrated Security=True;";
                string connStrDest = $"Data Source={serverDest};Initial Catalog={bdDest};Integrated Security=True;";

                // ===================================================================
                // 3. VIDAGE TABLE DESTINATION
                // ===================================================================
                Log("DELETE table destination...");
                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();
                    string deleteSql = $"DELETE FROM [{bdDest}].[STG].[TW_REPONSE];";
                    using (SqlCommand cmdDelete = new SqlCommand(deleteSql, connDest))
                    {
                        int rowsDeleted = cmdDelete.ExecuteNonQuery();
                        Log($"DELETE OK: {rowsDeleted} lignes");
                    }
                }

                // ===================================================================
                // 4. CHARGEMENT IDs COPIES EXISTANTES (TW_COPIE)
                // ===================================================================
                Log("Chargement copies existantes depuis TW_COPIE...");

                HashSet<string> copiesExistantes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, string> copyToExamMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();
                    string queryCopiIds = $@"
                        SELECT TW_N_COPI_ID, TW_N_EXAM_ID 
                        FROM [{bdDest}].[STG].[TW_COPIE];";

                    using (SqlCommand cmdCopies = new SqlCommand(queryCopiIds, connDest))
                    {
                        cmdCopies.CommandTimeout = 300;

                        using (SqlDataReader reader = cmdCopies.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string copiId = reader["TW_N_COPI_ID"]?.ToString();
                                string examId = reader["TW_N_EXAM_ID"]?.ToString();

                                if (!string.IsNullOrWhiteSpace(copiId) && !string.IsNullOrWhiteSpace(examId))
                                {
                                    copiesExistantes.Add(copiId);
                                    copyToExamMapping[copiId] = examId;
                                }
                            }
                        }
                    }
                }

                Log($"✓ {copiesExistantes.Count} copies chargées depuis TW_COPIE");

                if (copiesExistantes.Count == 0)
                {
                    Log("ATTENTION: Aucune copie dans TW_COPIE!");
                    Dts.TaskResult = (int)ScriptResults.Failure;
                    return;
                }

                // ===================================================================
                // 5. CHARGEMENT LOOKUP (examId, position) → questionUuid
                // ===================================================================
                Log("Chargement lookup questions (examId, position) → UUID...");

                // Dictionary: Key = (examId, positionTriee), Value = questionUuid
                Dictionary<(string, int), string> questionLookup = new Dictionary<(string, int), string>();

                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();

                    string queryQuestions = $@"
                        SELECT 
                            pe.TW_N_EXAM_ID,
                            ip.TW_N_QUES_ID,
                            ROW_NUMBER() OVER (PARTITION BY pe.TW_N_EXAM_ID ORDER BY ip.TW_N_POSI) - 1 AS PositionTriee
                        FROM [{bdDest}].[STG].[TW_INDEX_PARTIE] ip
                        JOIN [{bdDest}].[STG].[TW_PARTIE_EXAMEN] pe ON ip.TW_N_PART_ID = pe.TW_N_PART_ID
                        ORDER BY pe.TW_N_EXAM_ID, PositionTriee;";

                    using (SqlCommand cmdQuestions = new SqlCommand(queryQuestions, connDest))
                    {
                        cmdQuestions.CommandTimeout = 300;

                        using (SqlDataReader reader = cmdQuestions.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string examId = reader["TW_N_EXAM_ID"]?.ToString();
                                string questionUuid = reader["TW_N_QUES_ID"]?.ToString();
                                int positionTriee = Convert.ToInt32(reader["PositionTriee"]);

                                if (!string.IsNullOrWhiteSpace(examId) && !string.IsNullOrWhiteSpace(questionUuid))
                                {
                                    questionLookup[(examId, positionTriee)] = questionUuid;
                                }
                            }
                        }
                    }
                }

                Log($"✓ {questionLookup.Count} mappings (exam, position) → question chargés");

                // ===================================================================
                // 6. LECTURE DONNÉES SOURCE (VECTEUR RÉPONSES)
                // ===================================================================
                Log("Lecture vecteurs réponses depuis source...");

                string queryReponses = @"
                    SELECT 
                        ROW_ID,
                        X_GI_V_VECT_REPN_CAND_EXAM AS VecteurReponses
                    FROM [dbo].[CX_INSC_EXAM]
                    WHERE X_GI_V_VECT_REPN_CAND_EXAM IS NOT NULL
                      AND LEN(X_GI_V_VECT_REPN_CAND_EXAM) > 0;";

                List<ReponseData> reponses = new List<ReponseData>();
                int totalExamens = 0;
                int totalExamensIgnores = 0;
                int totalReponses = 0;
                int totalReponsesSansQuestion = 0;

                using (SqlConnection connSource = new SqlConnection(connStrSource))
                {
                    connSource.Open();
                    Log("Connexion source OK");

                    using (SqlCommand cmdSelect = new SqlCommand(queryReponses, connSource))
                    {
                        cmdSelect.CommandTimeout = 300;

                        using (SqlDataReader reader = cmdSelect.ExecuteReader())
                        {
                            Log("Début lecture vecteurs...");

                            while (reader.Read())
                            {
                                string rowId = reader["ROW_ID"]?.ToString();
                                string vecteur = reader["VecteurReponses"]?.ToString();

                                if (string.IsNullOrWhiteSpace(vecteur))
                                    continue;

                                // Filtre: Ignorer si copie n'existe pas dans TW_COPIE
                                if (!copiesExistantes.Contains(rowId))
                                {
                                    totalExamensIgnores++;
                                    continue;
                                }

                                totalExamens++;

                                // Récupérer examId correspondant
                                string examId = copyToExamMapping[rowId];

                                // Parse vecteur caractère par caractère
                                for (int i = 0; i < vecteur.Length; i++)
                                {
                                    char c = vecteur[i];

                                    // Ignorer caractères non-alphabétiques
                                    if (!char.IsLetter(c))
                                        continue;

                                    // Lookup questionUuid via (examId, position)
                                    string questionUuid = null;
                                    if (questionLookup.ContainsKey((examId, i)))
                                    {
                                        questionUuid = questionLookup[(examId, i)];
                                    }
                                    else
                                    {
                                        totalReponsesSansQuestion++;
                                        // Skip cette réponse - pas de question correspondante
                                        continue;
                                    }

                                    // Construire URI complète
                                    string uriQuestion = $"/api/questions/{questionUuid}";

                                    // Construire JSON choix depuis le caractère
                                    int position = -1;
                                    char upperChar = char.ToUpper(c);
                                    switch (upperChar)
                                    {
                                        case 'A': position = 0; break;
                                        case 'B': position = 1; break;
                                        case 'C': position = 2; break;
                                        case 'D': position = 3; break;
                                        default: position = -1; break;  // ← "X" tombe ici!
                                    }

                                    string choixJson = null;
                                    if (position >= 0)  // ← FALSE si position = -1
                                    {
                                        choixJson = $"[{{\"position\":{position}}}]";
                                    }
                                    // Résultat: choixJson = NULL pour "X"

                                    ReponseData rep = new ReponseData
                                    {
                                        TW_C_REPN_ID = $"{rowId}_Q{i + 1}",
                                        TW_N_COPI_ID = rowId,
                                        TW_C_URI_QUES = uriQuestion,  // ← URI COMPLÈTE!
                                        TW_DE_REPN_TEXT = c.ToString(),
                                        TW_DE_CHOI_JSON = choixJson,
                                        TW_DH_CHARG_ETL = DateTime.Now,
                                        TW_C_SYST_SRCE = "TestWe",
                                        TW_C_LOT_ETL = rowId,
                                        TW_C_EMPR_LIGN = CalculateHash($"{rowId}{i}{c}")
                                    };

                                    reponses.Add(rep);
                                    totalReponses++;
                                }

                                // Log progression
                                if (totalExamens % 100 == 0)
                                {
                                    Log($"Traité: {totalExamens} examens, {totalReponses} réponses ({totalExamensIgnores} ignorés)");
                                }
                            }
                        }
                    }
                }

                Log($"Lecture terminée: {totalExamens} examens, {totalReponses} réponses");
                Log($"Examens ignorés (non dans TW_COPIE): {totalExamensIgnores}");

                if (totalReponsesSansQuestion > 0)
                {
                    Log($"ATTENTION: {totalReponsesSansQuestion} réponses ignorées (question non trouvée dans lookup)");
                }

                // ===================================================================
                // 7. INSERTION DANS TABLE DESTINATION
                // ===================================================================
                if (reponses.Count > 0)
                {
                    Log($"Début insertion de {reponses.Count} réponses...");

                    using (SqlConnection connDest = new SqlConnection(connStrDest))
                    {
                        connDest.Open();

                        string insertSql = $@"
                            INSERT INTO [{bdDest}].[STG].[TW_REPONSE]
                            (TW_C_REPN_ID, TW_N_COPI_ID, TW_C_URI_QUES, TW_DE_REPN_TEXT, 
                             TW_DE_CHOI_JSON, TW_DH_CHARG_ETL, TW_C_SYST_SRCE, TW_C_LOT_ETL, TW_C_EMPR_LIGN)
                            VALUES
                            (@TW_C_REPN_ID, @TW_N_COPI_ID, @TW_C_URI_QUES, @TW_DE_REPN_TEXT, 
                             @TW_DE_CHOI_JSON, @TW_DH_CHARG_ETL, @TW_C_SYST_SRCE, @TW_C_LOT_ETL, @TW_C_EMPR_LIGN);";

                        int inserted = 0;

                        foreach (var rep in reponses)
                        {
                            using (SqlCommand cmd = new SqlCommand(insertSql, connDest))
                            {
                                cmd.Parameters.AddWithValue("@TW_C_REPN_ID", rep.TW_C_REPN_ID);
                                cmd.Parameters.AddWithValue("@TW_N_COPI_ID", rep.TW_N_COPI_ID);
                                cmd.Parameters.AddWithValue("@TW_C_URI_QUES", (object)rep.TW_C_URI_QUES ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@TW_DE_REPN_TEXT", (object)rep.TW_DE_REPN_TEXT ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@TW_DE_CHOI_JSON", (object)rep.TW_DE_CHOI_JSON ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@TW_DH_CHARG_ETL", rep.TW_DH_CHARG_ETL);
                                cmd.Parameters.AddWithValue("@TW_C_SYST_SRCE", rep.TW_C_SYST_SRCE);
                                cmd.Parameters.AddWithValue("@TW_C_LOT_ETL", (object)rep.TW_C_LOT_ETL ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@TW_C_EMPR_LIGN", (object)rep.TW_C_EMPR_LIGN ?? DBNull.Value);

                                cmd.ExecuteNonQuery();
                                inserted++;
                            }

                            if (inserted % 1000 == 0)
                            {
                                Log($"Inséré: {inserted} réponses");
                            }
                        }

                        Log($"Insertion terminée: {inserted} réponses");
                    }
                }
                else
                {
                    Log("ATTENTION: Aucune réponse à insérer!");
                }

                // ===================================================================
                // 8. STATISTIQUES FINALES
                // ===================================================================
                Log("");
                Log("═══════════════════════════════════════════════════════════");
                Log("STATISTIQUES FINALES:");
                Log($"  - Copies disponibles: {copiesExistantes.Count}");
                Log($"  - Examens traités: {totalExamens}");
                Log($"  - Examens ignorés (FK manquante): {totalExamensIgnores}");
                Log($"  - Réponses insérées: {reponses.Count}");
                Log($"  - Réponses sans question: {totalReponsesSansQuestion}");
                Log($"  - Format TW_C_URI_QUES: /api/questions/{{uuid}}");
                Log($"  - Moyenne réponses/examen: {(totalExamens > 0 ? (double)reponses.Count / totalExamens : 0):F1}");
                Log("═══════════════════════════════════════════════════════════");

                SafeLog($"=== FIN {_scriptName} SUCCESS - {DateTime.Now} ===\n");
                Dts.TaskResult = (int)ScriptResults.Success;
            }
            catch (Exception ex)
            {
                // ═══════════════════════════════════════════════════════════════
                // LOG ERREUR DANS FICHIER RÉSEAU
                // ═══════════════════════════════════════════════════════════════
                SafeLog("\n" + new string('=', 80) + "\n"
                    + $"DATE: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n"
                    + $"SCRIPT: {_scriptName}\n"
                    + $"ERREUR: {ex.Message}\n"
                    + $"\nSTACK TRACE:\n{ex.StackTrace}\n"
                    + new string('=', 80) + "\n");

                Dts.TaskResult = (int)ScriptResults.Failure;
            }
        }

        #region Helper Methods

        private void SafeLog(string message)
        {
            try
            {
                using (FileStream fs = new FileStream(_logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    sw.Write(message);
                }
            }
            catch { }
        }

        private void Log(string message)
        {
            SafeLog($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }

        private string CalculateHash(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            try
            {
                using (MD5 md5 = MD5.Create())
                {
                    byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                    byte[] hashBytes = md5.ComputeHash(inputBytes);

                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < hashBytes.Length; i++)
                    {
                        sb.Append(hashBytes[i].ToString("x2"));
                    }

                    return sb.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region ScriptResults declaration
        enum ScriptResults
        {
            Success = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Success,
            Failure = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Failure
        };
        #endregion
    }

    public class ReponseData
    {
        public string TW_C_REPN_ID { get; set; }
        public string TW_N_COPI_ID { get; set; }
        public string TW_C_URI_QUES { get; set; }
        public string TW_DE_REPN_TEXT { get; set; }
        public string TW_DE_CHOI_JSON { get; set; }
        public DateTime TW_DH_CHARG_ETL { get; set; }
        public string TW_C_SYST_SRCE { get; set; }
        public string TW_C_LOT_ETL { get; set; }
        public string TW_C_EMPR_LIGN { get; set; }
    }
}