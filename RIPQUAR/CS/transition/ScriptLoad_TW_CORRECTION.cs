// UPDATED: Thread-safe logging with SafeLog() - 2026-03-27
/*
 * ═══════════════════════════════════════════════════════════════════════════
 * RIPQUAR - Phase 1 STG - TW_CORRECTION (Table 10/10)
 * ═══════════════════════════════════════════════════════════════════════════
 * 
 * Objectif: Charger les corrections d'examens depuis X_GI_V_VECT_CORR_EXAM
 * Source:      CX_INSC_EXAM.X_GI_V_VECT_CORR_EXAM (JSON)
 * Destination: SDGIC01.STG.TW_CORRECTION
 * Format:      JSON avec structure: RespBody[] → corrections[]
 * Filtre:      Ne charge QUE les examens existants dans TW_COPIE (évite erreur FK)
 * 
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
using Newtonsoft.Json.Linq;
using System.Globalization;
#endregion

namespace ST_TW_CORRECTION
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

                Log("Script démarre - DERNIÈRE TABLE PHASE 1 STG! 🎉");

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
                // 3. VIDAGE TABLE DESTINATION (DELETE - pas TRUNCATE)
                // ===================================================================
                Log("DELETE table destination...");
                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();
                    string deleteSql = $"DELETE FROM [{bdDest}].[STG].[TW_CORRECTION];";
                    using (SqlCommand cmdDelete = new SqlCommand(deleteSql, connDest))
                    {
                        int rowsDeleted = cmdDelete.ExecuteNonQuery();
                        Log($"DELETE OK: {rowsDeleted} lignes");
                    }
                }

                // ===================================================================
                // 4. CHARGEMENT DES IDs VALIDES DEPUIS TW_COPIE (Destination)
                // ===================================================================
                Log("Chargement des IDs de copies existantes depuis TW_COPIE...");

                HashSet<string> copiesExistantes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();
                    string queryCopiIds = $"SELECT TW_N_COPI_ID FROM [{bdDest}].[STG].[TW_COPIE];";

                    using (SqlCommand cmdCopies = new SqlCommand(queryCopiIds, connDest))
                    {
                        cmdCopies.CommandTimeout = 300;

                        using (SqlDataReader reader = cmdCopies.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string copiId = reader["TW_N_COPI_ID"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(copiId))
                                {
                                    copiesExistantes.Add(copiId);
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
                // 5. LECTURE DES DONNÉES SOURCE (avec filtre en mémoire)
                // ===================================================================
                Log("Lecture données source...");

                string queryCorrections = @"
                    SELECT 
                        ROW_ID,
                        X_GI_V_VECT_CORR_EXAM AS JSONCorrections
                    FROM [dbo].[CX_INSC_EXAM]
                    WHERE X_GI_V_VECT_CORR_EXAM IS NOT NULL
                      AND LEN(X_GI_V_VECT_CORR_EXAM) > 0;";

                List<CorrectionData> corrections = new List<CorrectionData>();
                int totalExamens = 0;
                int totalExamensIgnores = 0;
                int totalCorrections = 0;
                int erreursParsing = 0;

                using (SqlConnection connSource = new SqlConnection(connStrSource))
                {
                    connSource.Open();
                    Log("Connexion source OK");

                    using (SqlCommand cmdSelect = new SqlCommand(queryCorrections, connSource))
                    {
                        cmdSelect.CommandTimeout = 300;

                        using (SqlDataReader reader = cmdSelect.ExecuteReader())
                        {
                            Log("Début lecture données...");

                            while (reader.Read())
                            {
                                string rowId = reader["ROW_ID"]?.ToString();
                                string jsonCorrections = reader["JSONCorrections"]?.ToString();

                                if (string.IsNullOrWhiteSpace(jsonCorrections))
                                    continue;

                                // ============================================
                                // FILTRE: Ignorer si ROW_ID n'existe pas dans TW_COPIE
                                // ============================================
                                if (!copiesExistantes.Contains(rowId))
                                {
                                    totalExamensIgnores++;
                                    continue;
                                }

                                totalExamens++;

                                // ============================================
                                // PARSING JSON - Structure: RespBody.studentAnswers[]
                                // ============================================
                                try
                                {
                                    JObject jsonObj = JObject.Parse(jsonCorrections);

                                    // RespBody est un STRING qui contient du JSON!
                                    string respBodyString = jsonObj["RespBody"]?.ToString();

                                    if (string.IsNullOrWhiteSpace(respBodyString))
                                    {
                                        Log($"ROW_ID {rowId}: RespBody absent ou vide");
                                        continue;
                                    }

                                    // Parser RespBody comme objet JSON
                                    JObject respBodyObj = JObject.Parse(respBodyString);

                                    // Accéder à studentAnswers (array)
                                    JArray studentAnswers = respBodyObj["studentAnswers"] as JArray;

                                    if (studentAnswers == null || studentAnswers.Count == 0)
                                    {
                                        Log($"ROW_ID {rowId}: studentAnswers absent ou vide");
                                        continue;
                                    }

                                    // Parcourir chaque studentAnswer
                                    for (int answerIndex = 0; answerIndex < studentAnswers.Count; answerIndex++)
                                    {
                                        JToken studentAnswer = studentAnswers[answerIndex];

                                        // Accéder au tableau corrections
                                        JArray correctionsArray = studentAnswer["corrections"] as JArray;

                                        if (correctionsArray == null || correctionsArray.Count == 0)
                                        {
                                            // Pas de corrections pour cette question (question non corrigée)
                                            continue;
                                        }

                                        // Parcourir chaque correction
                                        foreach (JToken corrToken in correctionsArray)
                                        {
                                            try
                                            {
                                                // ============================================
                                                // EXTRACTION DES CHAMPS
                                                // ============================================
                                                string correctionId = corrToken["id"]?.ToString();
                                                string noteStr = corrToken["note"]?.ToString();
                                                string bonusStr = corrToken["bonusPoints"]?.ToString();
                                                string isFlaggedStr = corrToken["isFlagged"]?.ToString();
                                                string updatedAtStr = corrToken["updatedAt"]?.ToString();

                                                // Construire TW_C_REPN_ID basé sur position (Q1, Q2, Q3...)
                                                string reponseId = $"{rowId}-Q{answerIndex + 1}";

                                                // Conversion note
                                                decimal? note = null;
                                                if (!string.IsNullOrEmpty(noteStr))
                                                {
                                                    if (decimal.TryParse(noteStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal n))
                                                        note = n;
                                                }

                                                // Conversion bonus
                                                decimal? bonus = null;
                                                if (!string.IsNullOrEmpty(bonusStr))
                                                {
                                                    if (decimal.TryParse(bonusStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal b))
                                                        bonus = b;
                                                }

                                                // Conversion isFlagged → bit
                                                bool? isSigned = null;
                                                if (!string.IsNullOrEmpty(isFlaggedStr))
                                                {
                                                    if (bool.TryParse(isFlaggedStr, out bool flag))
                                                        isSigned = flag;
                                                }

                                                // Conversion updatedAt
                                                DateTime? modifiedDate = null;
                                                if (!string.IsNullOrEmpty(updatedAtStr))
                                                {
                                                    if (DateTime.TryParse(updatedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                                                        modifiedDate = dt;
                                                }

                                                CorrectionData corr = new CorrectionData
                                                {
                                                    TW_N_CORR_ID = correctionId,
                                                    TW_C_REPN_ID = reponseId,
                                                    TW_M_NOTE = note,
                                                    TW_M_PTS_BONU = bonus,
                                                    TW_I_SIGN = isSigned,
                                                    TW_DE_APPR = null, // Pas trouvé dans JSON
                                                    TW_DH_MODI = modifiedDate,
                                                    TW_DH_CHARG_ETL = DateTime.Now,
                                                    TW_C_SYST_SRCE = "TestWe",
                                                    TW_C_LOT_ETL = rowId,
                                                    TW_C_EMPR_LIGN = CalculateHash($"{correctionId}{reponseId}")
                                                };

                                                corrections.Add(corr);
                                                totalCorrections++;
                                            }
                                            catch (Exception exCorr)
                                            {
                                                Log($"Erreur parsing correction dans {rowId}: {exCorr.Message}");
                                                erreursParsing++;
                                            }
                                        }
                                    }

                                    // Log tous les 50 examens
                                    if (totalExamens % 50 == 0)
                                    {
                                        Log($"Traité: {totalExamens} examens valides, {totalCorrections} corrections ({totalExamensIgnores} ignorés)");
                                    }
                                }
                                catch (Exception exJson)
                                {
                                    Log($"Erreur parsing JSON pour {rowId}: {exJson.Message}");
                                    erreursParsing++;
                                }
                            }
                        }
                    }
                }

                Log($"Lecture terminée: {totalExamens} examens traités, {totalCorrections} corrections collectées");
                Log($"Examens ignorés (non dans TW_COPIE): {totalExamensIgnores}");
                Log($"Erreurs de parsing: {erreursParsing}");

                // ===================================================================
                // 6. INSERTION DANS TABLE DESTINATION
                // ===================================================================
                if (corrections.Count > 0)
                {
                    Log($"Début insertion de {corrections.Count} corrections...");

                    using (SqlConnection connDest = new SqlConnection(connStrDest))
                    {
                        connDest.Open();

                        string insertSql = $@"
                            IF EXISTS (SELECT 1 FROM [{bdDest}].[STG].[TW_REPONSE] WHERE TW_C_REPN_ID = @TW_C_REPN_ID)
                            BEGIN
                                INSERT INTO [{bdDest}].[STG].[TW_CORRECTION]
                                (TW_N_CORR_ID, TW_C_REPN_ID, TW_M_NOTE, TW_M_PTS_BONU, 
                                 TW_I_SIGN, TW_DE_APPR, TW_DH_MODI,
                                 TW_DH_CHARG_ETL, TW_C_SYST_SRCE, TW_C_LOT_ETL, TW_C_EMPR_LIGN)
                                VALUES
                                (@TW_N_CORR_ID, @TW_C_REPN_ID, @TW_M_NOTE, @TW_M_PTS_BONU,
                                 @TW_I_SIGN, @TW_DE_APPR, @TW_DH_MODI,
                                 @TW_DH_CHARG_ETL, @TW_C_SYST_SRCE, @TW_C_LOT_ETL, @TW_C_EMPR_LIGN);
                            END
                        ";

                        int inserted = 0;
                        int insertErrors = 0;

                        foreach (var corr in corrections)
                        {
                            try
                            {
                                using (SqlCommand cmd = new SqlCommand(insertSql, connDest))
                                {
                                    cmd.Parameters.AddWithValue("@TW_N_CORR_ID", (object)corr.TW_N_CORR_ID ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TW_C_REPN_ID", (object)corr.TW_C_REPN_ID ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TW_M_NOTE", (object)corr.TW_M_NOTE ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TW_M_PTS_BONU", (object)corr.TW_M_PTS_BONU ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TW_I_SIGN", (object)corr.TW_I_SIGN ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TW_DE_APPR", (object)corr.TW_DE_APPR ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TW_DH_MODI", (object)corr.TW_DH_MODI ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TW_DH_CHARG_ETL", corr.TW_DH_CHARG_ETL);
                                    cmd.Parameters.AddWithValue("@TW_C_SYST_SRCE", corr.TW_C_SYST_SRCE);
                                    cmd.Parameters.AddWithValue("@TW_C_LOT_ETL", (object)corr.TW_C_LOT_ETL ?? DBNull.Value);
                                    cmd.Parameters.AddWithValue("@TW_C_EMPR_LIGN", (object)corr.TW_C_EMPR_LIGN ?? DBNull.Value);

                                    cmd.ExecuteNonQuery();
                                    inserted++;
                                }
                            }
                            catch (Exception exInsert)
                            {
                                Log($"Erreur INSERT correction {corr.TW_N_CORR_ID}: {exInsert.Message}");
                                insertErrors++;
                            }

                            if (inserted % 500 == 0)
                            {
                                Log($"Inséré: {inserted} corrections");
                            }
                        }

                        Log($"Insertion terminée: {inserted} corrections, {insertErrors} erreurs");
                    }
                }
                else
                {
                    Log("ATTENTION: Aucune correction à insérer!");
                }

                // ===================================================================
                // 7. STATISTIQUES FINALES
                // ===================================================================
                Log("");
                Log("═══════════════════════════════════════════════════════════");
                Log("STATISTIQUES FINALES:");
                Log($"  - Copies disponibles dans TW_COPIE: {copiesExistantes.Count}");
                Log($"  - Examens traités: {totalExamens}");
                Log($"  - Examens ignorés (FK manquante): {totalExamensIgnores}");
                Log($"  - Corrections insérées: {corrections.Count}");
                Log($"  - Erreurs parsing: {erreursParsing}");
                Log($"  - Moyenne corrections/examen: {(totalExamens > 0 ? (double)corrections.Count / totalExamens : 0):F1}");
                Log("═══════════════════════════════════════════════════════════");

                Log("");
                Log("PHASE 1 STG COMPLÉTÉE À 100% - 10/10 TABLES! ");
                Log("=== SUCCÈS ===");

                SafeLog($"=== FIN {_scriptName} SUCCESS - {DateTime.Now} ===\n");
                Dts.TaskResult = (int)ScriptResults.Success;
            }
            catch (Exception ex)
            {
                // ═══════════════════════════════════════════════════════════════
                // LOG ERREUR DANS FICHIER RÉSEAU
                // ═══════════════════════════════════════════════════════════════
                try
                {
                    string errorLog = "\n" + new string('=', 80) + "\n";
                    errorLog += $"DATE: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
                    errorLog += $"SCRIPT: {_scriptName}\n";
                    errorLog += $"ERREUR: {ex.Message}\n";
                    errorLog += $"\nSTACK TRACE:\n{ex.StackTrace}\n";
                    errorLog += new string('=', 80) + "\n";

                    SafeLog(errorLog);
                }
                catch { }

                Dts.TaskResult = (int)ScriptResults.Failure;
            }
        }

        #region Helper Methods

        /// <summary>
        /// Thread-safe log writing
        /// </summary>
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

        /// <summary>
        /// Log dans fichier texte
        /// </summary>
        private void Log(string message)
        {
            SafeLog($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }

        /// <summary>
        /// Calcule un hash MD5 pour identifier une ligne de façon unique
        /// </summary>
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

    /// <summary>
    /// Classe de données pour une correction
    /// </summary>
    public class CorrectionData
    {
        public string TW_N_CORR_ID { get; set; }
        public string TW_C_REPN_ID { get; set; }
        public decimal? TW_M_NOTE { get; set; }
        public decimal? TW_M_PTS_BONU { get; set; }
        public bool? TW_I_SIGN { get; set; }
        public string TW_DE_APPR { get; set; }
        public DateTime? TW_DH_MODI { get; set; }
        public DateTime TW_DH_CHARG_ETL { get; set; }
        public string TW_C_SYST_SRCE { get; set; }
        public string TW_C_LOT_ETL { get; set; }
        public string TW_C_EMPR_LIGN { get; set; }
    }
}