// UPDATED: Thread-safe logging with SafeLog() - 2026-03-27
/*
 * ═══════════════════════════════════════════════════════════════════════════
 * RIPQUAR - Phase 3 FACT - FACT_REPONSE_QUESTION (Table 18/19)
 * ═══════════════════════════════════════════════════════════════════════════
 * 
 * Objectif:    Charger le détail des réponses question par question
 * Sources:     STG.TW_REPONSE + STG.TW_CORRECTION
 * Destination: SDGIC01.DWH.FACT_REPONSE_QUESTION
 * Grain:       Une ligne par réponse à une question
 * Prérequis:   FACT_RESULTAT_EXAMEN chargée
 * 
 * Logique lookups:
 *   - DIM_CANDIDAT: Via FACT_RESULTAT_EXAMEN (réutilise SK)
 *   - DIM_EXAMEN: Via FACT_RESULTAT_EXAMEN (réutilise SK)
 *   - DIM_QUESTION: Extraction UUID via RIGHT(TW_C_URI_QUES, 36) → Lookup direct
 *   - DIM_DATE: Réutilise date correction de FACT_RESULTAT_EXAMEN
 * 
 * Colonnes calculées:
 *   - DW_I_CORR: Indicateur bonne réponse (DW_M_PTS_OBTE > 0)
 *   - DW_C_LIBE_CHOI_DONN: Conversion position → lettre (0=A, 1=B, 2=C, 3=D)
 *   - DW_I_AUCU_REPN: Inverse logique de DW_I_REPN
 * ═══════════════════════════════════════════════════════════════════════════
 */

#region Namespaces
using System;
using System.Data;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
#endregion

namespace ST_FACT_REPONSE_QUESTION
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
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                string bdDest = Dts.Variables["User::BDDest"].Value.ToString();

                Log($"Destination: {serverDest} / {bdDest}");

                // ===================================================================
                // 2. CONSTRUCTION CONNECTION STRING
                // ===================================================================
                string connStrDest = $"Data Source={serverDest};Initial Catalog={bdDest};Integrated Security=True;MultipleActiveResultSets=True;";

                // ===================================================================
                // 3. CHARGEMENT LOOKUPS EN MÉMOIRE
                // ===================================================================
                Log("Chargement lookups...");

                Dictionary<string, ResultatInfo> lkpResultat = new Dictionary<string, ResultatInfo>();
                Dictionary<string, int> lkpQuestions = new Dictionary<string, int>();
                Dictionary<int, int> lkpDates = new Dictionary<int, int>();

                using (SqlConnection conn = new SqlConnection(connStrDest))
                {
                    conn.Open();

                    // Lookup FACT_RESULTAT_EXAMEN
                    Log("  - FACT_RESULTAT_EXAMEN...");
                    string sqlResultat = $@"
                        SELECT 
                            TW_N_COPI_ID,
                            DW_N_RESU_ID,
                            DW_N_CAND_ID,
                            DW_N_EXAM_ID,
                            DW_N_DATE_CORR_ID
                        FROM [{bdDest}].[DWH].[FACT_RESULTAT_EXAMEN];";

                    using (SqlCommand cmd = new SqlCommand(sqlResultat, conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string copyId = rdr["TW_N_COPI_ID"].ToString();
                            lkpResultat[copyId] = new ResultatInfo
                            {
                                ResuSK = rdr.GetInt32(rdr.GetOrdinal("DW_N_RESU_ID")),
                                CandSK = rdr.GetInt32(rdr.GetOrdinal("DW_N_CAND_ID")),
                                ExamSK = rdr.GetInt32(rdr.GetOrdinal("DW_N_EXAM_ID")),
                                DateCorrSK = rdr.IsDBNull(rdr.GetOrdinal("DW_N_DATE_CORR_ID")) ?
                                    (int?)null : rdr.GetInt32(rdr.GetOrdinal("DW_N_DATE_CORR_ID"))
                            };
                        }
                    }

                    Log($"    {lkpResultat.Count} résultats chargés");

                    // Lookup DIM_QUESTION via UUID
                    Log("  - DIM_QUESTION...");
                    string sqlQuestions = $@"
                        SELECT 
                            TW_N_QUES_ID,
                            DW_N_QUES_ID
                        FROM [{bdDest}].[DWH].[DIM_QUESTION]
                        WHERE DW_I_COUR = 1;";

                    using (SqlCommand cmd = new SqlCommand(sqlQuestions, conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string questionUuid = rdr["TW_N_QUES_ID"].ToString();
                            int quesSK = rdr.GetInt32(rdr.GetOrdinal("DW_N_QUES_ID"));
                            lkpQuestions[questionUuid] = quesSK;
                        }
                    }

                    Log($"    {lkpQuestions.Count} questions chargées");

                    // Lookup DIM_DATE
                    Log("  - DIM_DATE...");
                    string sqlDates = $@"
                        SELECT DW_N_DATE_ID 
                        FROM [{bdDest}].[DWH].[DIM_DATE];";

                    using (SqlCommand cmd = new SqlCommand(sqlDates, conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            int dateId = rdr.GetInt32(0);
                            lkpDates[dateId] = dateId;
                        }
                    }

                    Log($"    {lkpDates.Count} dates chargées");
                }

                // ===================================================================
                // 4. VÉRIFIER PRÉREQUIS
                // ===================================================================
                if (lkpResultat.Count == 0)
                {
                    Log("ERREUR: FACT_RESULTAT_EXAMEN est vide!");
                    Dts.TaskResult = (int)ScriptResults.Failure;
                    return;
                }

                // ===================================================================
                // 5. VIDAGE TABLE DESTINATION
                // ===================================================================
                Log("DELETE table destination...");
                using (SqlConnection conn = new SqlConnection(connStrDest))
                {
                    conn.Open();
                    string deleteSql = $"DELETE FROM [{bdDest}].[DWH].[FACT_REPONSE_QUESTION];";
                    using (SqlCommand cmdDelete = new SqlCommand(deleteSql, conn))
                    {
                        int rowsDeleted = cmdDelete.ExecuteNonQuery();
                        Log($"DELETE OK: {rowsDeleted} lignes");
                    }
                }

                // ===================================================================
                // 6. LECTURE TW_REPONSE + TW_CORRECTION ET INSERTION
                // ===================================================================
                Log("Traitement TW_REPONSE + TW_CORRECTION...");

                int totalInseres = 0;
                int totalSkipped = 0;
                int totalCopyNonTrouvee = 0;
                int totalQuestionNonTrouvee = 0;
                int totalUriInvalide = 0;

                using (SqlConnection conn = new SqlConnection(connStrDest))
                {
                    conn.Open();

                    // Préparer requête INSERT
                    string insertSql = $@"
                        INSERT INTO [{bdDest}].[DWH].[FACT_REPONSE_QUESTION]
                        (DW_N_CAND_ID, DW_N_RESU_ID, DW_N_EXAM_ID, DW_N_QUES_ID, 
                         DW_N_DATE_REPN_ID, TW_C_URI_REPN, DW_M_PTS_OBTE, DW_I_CORR,
                         DW_N_POSI_CHOI_DONN, DW_C_LIBE_CHOI_DONN, DW_I_REPN, 
                         DW_I_AUCU_REPN, DW_I_REPN_PLUS_CHOI, DW_DH_CHARG_ETL, 
                         DW_C_SYST_SRCE, DW_C_LOT_ETL)
                        VALUES
                        (@CandId, @ResuId, @ExamId, @QuesId, @DateId, @UriRepn, 
                         @PtsObte, @Corr, @PosiChoi, @LibeChoi, @Repn, @AucuRepn, 
                         @PlusChoi, GETDATE(), 'TestWe', @Lot);";

                    // Lire TW_REPONSE + TW_CORRECTION
                    string sqlReponses = $@"
                        SELECT 
                            r.TW_C_REPN_ID,
                            r.TW_N_COPI_ID,
                            r.TW_C_URI_QUES,
                            r.TW_DE_REPN_TEXT,
                            r.TW_DE_CHOI_JSON,
                            c.TW_M_NOTE
                        FROM [{bdDest}].[STG].[TW_REPONSE] r
                        LEFT JOIN [{bdDest}].[STG].[TW_CORRECTION] c 
                            ON r.TW_C_REPN_ID = c.TW_C_REPN_ID;";

                    using (SqlCommand cmdRead = new SqlCommand(sqlReponses, conn))
                    {
                        cmdRead.CommandTimeout = 600;

                        using (SqlDataReader rdr = cmdRead.ExecuteReader())
                        {
                            while (rdr.Read())
                            {
                                string repnId = rdr["TW_C_REPN_ID"].ToString();
                                string copyId = rdr["TW_N_COPI_ID"].ToString();
                                string uriQues = rdr.IsDBNull(rdr.GetOrdinal("TW_C_URI_QUES")) ?
                                    null : rdr["TW_C_URI_QUES"].ToString();
                                string repnText = rdr.IsDBNull(rdr.GetOrdinal("TW_DE_REPN_TEXT")) ?
                                    null : rdr["TW_DE_REPN_TEXT"].ToString();
                                string choiJson = rdr.IsDBNull(rdr.GetOrdinal("TW_DE_CHOI_JSON")) ?
                                    null : rdr["TW_DE_CHOI_JSON"].ToString();
                                decimal? ptsObte = rdr.IsDBNull(rdr.GetOrdinal("TW_M_NOTE")) ?
                                    (decimal?)null : rdr.GetDecimal(rdr.GetOrdinal("TW_M_NOTE"));

                                // Vérifier copie existe dans FACT_RESULTAT
                                if (!lkpResultat.ContainsKey(copyId))
                                {
                                    totalCopyNonTrouvee++;
                                    totalSkipped++;
                                    continue;
                                }

                                ResultatInfo resInfo = lkpResultat[copyId];

                                // ═══════════════════════════════════════════════════
                                // LOOKUP SIMPLIFIÉ: Extraire UUID depuis URI
                                // ═══════════════════════════════════════════════════
                                if (string.IsNullOrEmpty(uriQues) || uriQues.Length < 36)
                                {
                                    totalUriInvalide++;
                                    totalSkipped++;
                                    continue;
                                }

                                // Extraire UUID (36 derniers caractères)
                                string questionUuid = uriQues.Substring(uriQues.Length - 36);

                                // Lookup direct dans DIM_QUESTION
                                if (!lkpQuestions.ContainsKey(questionUuid))
                                {
                                    totalQuestionNonTrouvee++;
                                    totalSkipped++;
                                    continue;
                                }

                                int quesSK = lkpQuestions[questionUuid];

                                // Calculer DW_I_CORR
                                bool estCorrecte = ptsObte.HasValue && ptsObte.Value > 0;

                                // Parser choix JSON pour position
                                int? position = null;
                                string libelle = null;
                                bool plusieursChoix = false;

                                if (!string.IsNullOrEmpty(choiJson))
                                {
                                    try
                                    {
                                        JArray choix = JArray.Parse(choiJson);
                                        if (choix.Count > 0)
                                        {
                                            position = choix[0]["position"]?.Value<int>();
                                            plusieursChoix = choix.Count > 1;

                                            // Conversion position → lettre
                                            if (position.HasValue)
                                            {
                                                switch (position.Value)
                                                {
                                                    case 0: libelle = "A"; break;
                                                    case 1: libelle = "B"; break;
                                                    case 2: libelle = "C"; break;
                                                    case 3: libelle = "D"; break;
                                                    default: libelle = null; break;
                                                }
                                            }
                                        }
                                    }
                                    catch { /* JSON invalide */ }
                                }

                                bool aRepondu = !string.IsNullOrEmpty(repnText);

                                // INSERT
                                using (SqlCommand cmdIns = new SqlCommand(insertSql, conn))
                                {
                                    cmdIns.Parameters.AddWithValue("@CandId", resInfo.CandSK);
                                    cmdIns.Parameters.AddWithValue("@ResuId", resInfo.ResuSK);
                                    cmdIns.Parameters.AddWithValue("@ExamId", resInfo.ExamSK);
                                    cmdIns.Parameters.AddWithValue("@QuesId", quesSK);
                                    cmdIns.Parameters.AddWithValue("@DateId",
                                        resInfo.DateCorrSK ?? (object)DBNull.Value);
                                    cmdIns.Parameters.AddWithValue("@UriRepn", uriQues);
                                    cmdIns.Parameters.AddWithValue("@PtsObte",
                                        ptsObte ?? (object)DBNull.Value);
                                    cmdIns.Parameters.AddWithValue("@Corr", estCorrecte ? 1 : 0);
                                    cmdIns.Parameters.AddWithValue("@PosiChoi",
                                        position ?? (object)DBNull.Value);
                                    cmdIns.Parameters.AddWithValue("@LibeChoi",
                                        libelle ?? (object)DBNull.Value);
                                    cmdIns.Parameters.AddWithValue("@Repn", aRepondu ? 1 : 0);
                                    cmdIns.Parameters.AddWithValue("@AucuRepn", aRepondu ? 0 : 1);
                                    cmdIns.Parameters.AddWithValue("@PlusChoi", plusieursChoix ? 1 : 0);
                                    cmdIns.Parameters.AddWithValue("@Lot", copyId);

                                    cmdIns.ExecuteNonQuery();
                                    totalInseres++;
                                }

                                // Log progression
                                if (totalInseres % 100 == 0)
                                {
                                    Log($"  Inséré: {totalInseres} réponses...");
                                }
                            }
                        }
                    }
                }

                // ===================================================================
                // 7. STATISTIQUES FINALES
                // ===================================================================
                Log("");
                Log("═══════════════════════════════════════════════════════════");
                Log("STATISTIQUES FINALES:");
                Log($"  - Réponses insérées: {totalInseres}");
                Log($"  - Réponses skippées: {totalSkipped}");
                if (totalCopyNonTrouvee > 0)
                    Log($"    • Copie non trouvée: {totalCopyNonTrouvee}");
                if (totalQuestionNonTrouvee > 0)
                    Log($"    • Question UUID non trouvée: {totalQuestionNonTrouvee}");
                if (totalUriInvalide > 0)
                    Log($"    • URI invalide: {totalUriInvalide}");
                Log($"  - Lookup méthode: RIGHT(TW_C_URI_QUES, 36) → DIM_QUESTION");
                Log("═══════════════════════════════════════════════════════════");

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

        private void Log(string message)
        {
            SafeLog($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }

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

        #endregion

        #region Helper Classes

        public class ResultatInfo
        {
            public int ResuSK { get; set; }
            public int CandSK { get; set; }
            public int ExamSK { get; set; }
            public int? DateCorrSK { get; set; }
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
}