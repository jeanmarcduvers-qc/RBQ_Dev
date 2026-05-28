/*
 * ???????????????????????????????????????????????????????????????????????????
 * RIPQUAR - Phase 2 DIM - DIM_QUESTION (Script Task)
 * ???????????????????????????????????????????????????????????????????????????
 * 
 * Objectif: Charger DIM_QUESTION avec SCD Type 2 (historisation)
 * Source:      [SDGIC01].[STG].[TW_QUESTION]
 * Destination: [SDGIC01].[DWH].[DIM_QUESTION]
 * 
 * Logique SCD Type 2:
 *   - Nouvelle question ? INSERT
 *   - Question chang�e ? UPDATE (expirer ancien) + INSERT (nouveau)
 *   - Question identique ? Rien
 * 
 * ???????????????????????????????????????????????????????????????????????????
 */

#region Namespaces
using System;
using System.Data;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.IO;
#endregion

namespace ST_DIM_QUESTION
{
    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {
        private string logFile = @"C:\Temp\DIM_QUESTION_LOG.txt";

        public void Main()
        {
            try
            {
                File.WriteAllText(logFile, $"=== D�BUT DIM_QUESTION - {DateTime.Now} ===\n\n");
                Log("Script d�marre - SCD Type 2");

                // ===================================================================
                // 1. R�CUP�RATION DES VARIABLES SSIS
                // ===================================================================
                string serverSource = Dts.Variables["User::ServerSource"].Value.ToString();
                string bdSource = Dts.Variables["User::BDSource"].Value.ToString();
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                string bdDest = Dts.Variables["User::BDDest"].Value.ToString();

                Log($"Source: {serverSource} / {bdSource}");
                Log($"Dest: {serverDest} / {bdDest}");

                string connStrDest = $"Data Source={serverDest};Initial Catalog={bdDest};Integrated Security=True;";

                // ===================================================================
                // 2. CHARGER LES QUESTIONS ACTUELLES DEPUIS DWH
                // ===================================================================
                Log("Chargement questions actuelles depuis DWH...");

                Dictionary<string, QuestionDWH> questionsActuelles = new Dictionary<string, QuestionDWH>();

                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();
                    string queryQuestionsActuelles = @"
                        SELECT 
                            DW_N_QUES_ID,
                            TW_N_QUES_ID,
                            TW_N_UUID_BANQ_QUES,
                            TW_C_REF_EXT,
                            DW_N_POSI_QUES,
                            DW_M_PTS,
                            DW_C_TYPE_QUES
                        FROM [DWH].[DIM_QUESTION]
                        WHERE DW_I_COUR = 1;";

                    using (SqlCommand cmd = new SqlCommand(queryQuestionsActuelles, connDest))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var ques = new QuestionDWH
                                {
                                    SK = reader.GetInt32(0),
                                    NK = reader.GetString(1),
                                    UUIDBanque = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                    RefExterne = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                    Position = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                                    Points = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),  // int dans DWH
                                    TypeQuestion = reader.IsDBNull(6) ? "" : reader.GetString(6)
                                };

                                questionsActuelles[ques.NK] = ques;
                            }
                        }
                    }
                }

                Log($"? {questionsActuelles.Count} questions actuelles charg�es depuis DWH");

                // ===================================================================
                // 3. LIRE LES QUESTIONS DEPUIS STG
                // ===================================================================
                Log("Lecture questions depuis STG...");

                List<QuestionSTG> questionsSTG = new List<QuestionSTG>();

                using (SqlConnection connDest2 = new SqlConnection(connStrDest))
                {
                    connDest2.Open();

                    string querySTG = $@"
                        SELECT DISTINCT
                            TW_N_QUES_ID,
                            TW_N_UUID_BANQ_QUES,
                            TW_C_REF_EXT,
                            TW_N_POSI,
                            TW_M_PTS,
                            TW_C_TYPE_QUES
                        FROM [{bdDest}].[STG].[TW_QUESTION]
                        WHERE TW_N_QUES_ID IS NOT NULL;";

                    using (SqlCommand cmd = new SqlCommand(querySTG, connDest2))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var ques = new QuestionSTG
                                {
                                    NK = reader.GetString(0),
                                    UUIDBanque = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                    RefExterne = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                    Position = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                                    // TW_M_PTS est decimal(18,0) dans STG, convertir en int
                                    Points = reader.IsDBNull(4) ? (int?)null : (int)reader.GetDecimal(4),
                                    TypeQuestion = reader.IsDBNull(5) ? "" : reader.GetString(5)
                                };

                                questionsSTG.Add(ques);
                            }
                        }
                    }
                }

                Log($"? {questionsSTG.Count} questions lues depuis STG");

                // ===================================================================
                // 4. TRAITER CHAQUE QUESTION (Logique SCD Type 2)
                // ===================================================================
                int nouveaux = 0;
                int modifies = 0;
                int inchanges = 0;
                int erreurs = 0;

                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();

                    foreach (var quesSTG in questionsSTG)
                    {
                        try
                        {
                            if (!questionsActuelles.ContainsKey(quesSTG.NK))
                            {
                                // NOUVELLE QUESTION ? INSERT
                                InsertNouvelleQuestion(connDest, bdDest, quesSTG);
                                nouveaux++;
                            }
                            else
                            {
                                // QUESTION EXISTANTE ? V�rifier si chang�e
                                var quesDWH = questionsActuelles[quesSTG.NK];

                                if (HasChanged(quesDWH, quesSTG))
                                {
                                    // CHANG�E ? UPDATE (expirer) + INSERT (nouveau)
                                    ExpireQuestion(connDest, bdDest, quesDWH.SK);
                                    InsertNouvelleQuestion(connDest, bdDest, quesSTG);
                                    modifies++;

                                    Log($"Question modifi�e: {quesSTG.NK}");
                                }
                                else
                                {
                                    // IDENTIQUE ? Rien
                                    inchanges++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Erreur question {quesSTG.NK}: {ex.Message}");
                            erreurs++;
                        }
                    }
                }

                // ===================================================================
                // 5. STATISTIQUES FINALES
                // ===================================================================
                Log("");
                Log("???????????????????????????????????????????????????????????");
                Log("STATISTIQUES FINALES:");
                Log($"  - Questions STG: {questionsSTG.Count}");
                Log($"  - Nouvelles ins�r�es: {nouveaux}");
                Log($"  - Modifi�es (historis�es): {modifies}");
                Log($"  - Inchang�es: {inchanges}");
                Log($"  - Erreurs: {erreurs}");
                Log("???????????????????????????????????????????????????????????");

                Log("=== SUCC�S ===");
                Dts.TaskResult = (int)ScriptResults.Success;
            }
            catch (Exception ex)
            {
                string errMsg = $"ERREUR FATALE:\n{ex.Message}\n\nStack:\n{ex.StackTrace}";
                Log(errMsg);
                MessageBox.Show(errMsg);
                Dts.TaskResult = (int)ScriptResults.Failure;
            }
        }

        #region Helper Methods

        /// <summary>
        /// V�rifie si une question a chang�
        /// </summary>
        private bool HasChanged(QuestionDWH dwh, QuestionSTG stg)
        {
            return dwh.UUIDBanque != stg.UUIDBanque
                || dwh.RefExterne != stg.RefExterne
                || dwh.Position != stg.Position
                || dwh.Points != stg.Points
                || dwh.TypeQuestion != stg.TypeQuestion;
        }

        /// <summary>
        /// Expire une question (UPDATE pour marquer comme historique)
        /// </summary>
        private void ExpireQuestion(SqlConnection conn, string bdDest, int sk)
        {
            string updateSql = $@"
                UPDATE [{bdDest}].[DWH].[DIM_QUESTION]
                SET 
                    DW_I_COUR = 0,
                    DW_D_FIN_VALI = GETDATE()
                WHERE DW_N_QUES_ID = @SK;";

            using (SqlCommand cmd = new SqlCommand(updateSql, conn))
            {
                cmd.Parameters.AddWithValue("@SK", sk);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Insert une nouvelle question (ou nouvelle version)
        /// </summary>
        private void InsertNouvelleQuestion(SqlConnection conn, string bdDest, QuestionSTG ques)
        {
            string insertSql = $@"
                INSERT INTO [{bdDest}].[DWH].[DIM_QUESTION]
                (
                    TW_N_QUES_ID,
                    TW_N_UUID_BANQ_QUES,
                    TW_C_REF_EXT,
                    DW_N_POSI_QUES,
                    DW_M_PTS,
                    DW_C_TYPE_QUES,
                    DW_I_COUR,
                    DW_D_DEBU_VALI,
                    DW_D_FIN_VALI,
                    DW_DH_CHARG_ETL,
                    DW_C_SYST_SRCE
                )
                VALUES
                (
                    @NK,
                    @UUIDBanque,
                    @RefExterne,
                    @Position,
                    @Points,
                    @TypeQuestion,
                    1,                  -- DW_I_COUR = 1 (actuel)
                    GETDATE(),          -- DW_D_DEBU_VALI
                    NULL,               -- DW_D_FIN_VALI = NULL
                    GETDATE(),
                    'TestWe'
                );";

            using (SqlCommand cmd = new SqlCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("@NK", ques.NK);
                cmd.Parameters.AddWithValue("@UUIDBanque", string.IsNullOrEmpty(ques.UUIDBanque) ? (object)DBNull.Value : ques.UUIDBanque);
                cmd.Parameters.AddWithValue("@RefExterne", string.IsNullOrEmpty(ques.RefExterne) ? (object)DBNull.Value : ques.RefExterne);
                cmd.Parameters.AddWithValue("@Position", ques.Position ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Points", ques.Points ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@TypeQuestion", string.IsNullOrEmpty(ques.TypeQuestion) ? (object)DBNull.Value : ques.TypeQuestion);

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Log dans fichier
        /// </summary>
        private void Log(string message)
        {
            try
            {
                File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        #endregion

        #region ScriptResults
        enum ScriptResults
        {
            Success = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Success,
            Failure = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Failure
        };
        #endregion
    }

    /// <summary>
    /// Question depuis STG (source)
    /// </summary>
    public class QuestionSTG
    {
        public string NK { get; set; }
        public string UUIDBanque { get; set; }
        public string RefExterne { get; set; }
        public int? Position { get; set; }
        public int? Points { get; set; }  // Converti de decimal STG vers int
        public string TypeQuestion { get; set; }
    }

    /// <summary>
    /// Question depuis DWH (actuel)
    /// </summary>
    public class QuestionDWH
    {
        public int SK { get; set; }
        public string NK { get; set; }
        public string UUIDBanque { get; set; }
        public string RefExterne { get; set; }
        public int? Position { get; set; }
        public int? Points { get; set; }  // int dans DWH
        public string TypeQuestion { get; set; }
    }
}