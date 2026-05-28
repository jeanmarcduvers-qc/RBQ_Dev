/*
 * ═══════════════════════════════════════════════════════════════════════════
 * RIPQUAR - Phase 2 DIM - DIM_EXAMEN (Script Task)
 * ═══════════════════════════════════════════════════════════════════════════
 * 
 * Objectif: Charger DIM_EXAMEN avec SCD Type 2 (historisation)
 * Source:      [SDGIC01].[STG].[TW_EXAMEN]
 * Destination: [SDGIC01].[DWH].[DIM_EXAMEN]
 * 
 * Logique SCD Type 2:
 *   - Nouvel examen → INSERT
 *   - Examen changé → UPDATE (expirer ancien) + INSERT (nouveau)
 *   - Examen identique → Rien
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
using System.IO;
#endregion

namespace ST_DIM_EXAMEN
{
    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {
        private string logFile = @"C:\Temp\DIM_EXAMEN_LOG.txt";

        public void Main()
        {
            try
            {
                File.WriteAllText(logFile, $"=== DÉBUT DIM_EXAMEN - {DateTime.Now} ===\n\n");
                Log("Script démarre - SCD Type 2");

                // ===================================================================
                // 1. RÉCUPÉRATION DES VARIABLES SSIS
                // ===================================================================
                string serverSource = Dts.Variables["User::ServerSource"].Value.ToString();
                string bdSource = Dts.Variables["User::BDSource"].Value.ToString();
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                string bdDest = Dts.Variables["User::BDDest"].Value.ToString();

                Log($"Source: {serverSource} / {bdSource}");
                Log($"Dest: {serverDest} / {bdDest}");

                string connStrDest = $"Data Source={serverDest};Initial Catalog={bdDest};Integrated Security=True;";

                // ===================================================================
                // 2. CHARGER LES EXAMENS ACTUELS DEPUIS DWH
                // ===================================================================
                Log("Chargement examens actuels depuis DWH...");

                Dictionary<string, ExamenDWH> examensActuels = new Dictionary<string, ExamenDWH>();

                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();
                    string queryExamensActuels = @"
                        SELECT 
                            DW_N_EXAM_ID,
                            TW_N_EXAM_ID,
                            DW_C_EXAM,
                            DW_NM_EXAM,
                            DW_Q_DURE_MINT,
                            DW_Q_PTS_MAX
                        FROM [DWH].[DIM_EXAMEN]
                        WHERE DW_I_COUR = 1;";

                    using (SqlCommand cmd = new SqlCommand(queryExamensActuels, connDest))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var exam = new ExamenDWH
                                {
                                    SK = reader.GetInt32(0),
                                    NK = reader.GetString(1),
                                    CodeExam = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                    NomExam = reader.IsDBNull(3) ? "" : reader.GetString(3),
                                    DureeMin = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                                    PointsMax = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5)
                                };

                                examensActuels[exam.NK] = exam;
                            }
                        }
                    }
                }

                Log($"✓ {examensActuels.Count} examens actuels chargés depuis DWH");

                // ===================================================================
                // 3. LIRE LES EXAMENS DEPUIS STG
                // ===================================================================
                Log("Lecture examens depuis STG...");

                List<ExamenSTG> examensSTG = new List<ExamenSTG>();

                using (SqlConnection connDest2 = new SqlConnection(connStrDest))
                {
                    connDest2.Open();

                    string querySTG = $@"
                        SELECT DISTINCT
                            TW_N_EXAM_ID,
                            TW_C_EXAM,
                            TW_NM_MATI,
                            TW_Q_DURE_SEC / 60 AS DureeMinutes,
                            TW_Q_PTS_MAX
                        FROM [{bdDest}].[STG].[TW_EXAMEN]
                        WHERE TW_N_EXAM_ID IS NOT NULL;";

                    using (SqlCommand cmd = new SqlCommand(querySTG, connDest2))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var exam = new ExamenSTG
                                {
                                    NK = reader.GetString(0),
                                    CodeExam = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                    NomExam = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                    DureeMin = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                                    PointsMax = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4)
                                };

                                examensSTG.Add(exam);
                            }
                        }
                    }
                }

                Log($"✓ {examensSTG.Count} examens lus depuis STG");

                // ===================================================================
                // 4. TRAITER CHAQUE EXAMEN (Logique SCD Type 2)
                // ===================================================================
                int nouveaux = 0;
                int modifies = 0;
                int inchanges = 0;
                int erreurs = 0;

                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();

                    foreach (var examSTG in examensSTG)
                    {
                        try
                        {
                            if (!examensActuels.ContainsKey(examSTG.NK))
                            {
                                // NOUVEAU EXAMEN → INSERT
                                InsertNouvelExamen(connDest, bdDest, examSTG);
                                nouveaux++;
                            }
                            else
                            {
                                // EXAMEN EXISTANT → Vérifier si changé
                                var examDWH = examensActuels[examSTG.NK];

                                if (HasChanged(examDWH, examSTG))
                                {
                                    // CHANGÉ → UPDATE (expirer) + INSERT (nouveau)
                                    ExpireExamen(connDest, bdDest, examDWH.SK);
                                    InsertNouvelExamen(connDest, bdDest, examSTG);
                                    modifies++;

                                    Log($"Examen modifié: {examSTG.NK} - {examSTG.NomExam}");
                                }
                                else
                                {
                                    // IDENTIQUE → Rien
                                    inchanges++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Erreur examen {examSTG.NK}: {ex.Message}");
                            erreurs++;
                        }
                    }
                }

                // ===================================================================
                // 5. STATISTIQUES FINALES
                // ===================================================================
                Log("");
                Log("═══════════════════════════════════════════════════════════");
                Log("STATISTIQUES FINALES:");
                Log($"  - Examens STG: {examensSTG.Count}");
                Log($"  - Nouveaux insérés: {nouveaux}");
                Log($"  - Modifiés (historisés): {modifies}");
                Log($"  - Inchangés: {inchanges}");
                Log($"  - Erreurs: {erreurs}");
                Log("═══════════════════════════════════════════════════════════");

                Log("=== SUCCÈS ===");
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
        /// Vérifie si un examen a changé
        /// </summary>
        private bool HasChanged(ExamenDWH dwh, ExamenSTG stg)
        {
            return dwh.CodeExam != stg.CodeExam
                || dwh.NomExam != stg.NomExam
                || dwh.DureeMin != stg.DureeMin
                || dwh.PointsMax != stg.PointsMax;
        }

        /// <summary>
        /// Expire un examen (UPDATE pour marquer comme historique)
        /// </summary>
        private void ExpireExamen(SqlConnection conn, string bdDest, int sk)
        {
            string updateSql = $@"
                UPDATE [{bdDest}].[DWH].[DIM_EXAMEN]
                SET 
                    DW_I_COUR = 0,
                    DW_D_FIN_VALI = GETDATE()
                WHERE DW_N_EXAM_ID = @SK;";

            using (SqlCommand cmd = new SqlCommand(updateSql, conn))
            {
                cmd.Parameters.AddWithValue("@SK", sk);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Insert un nouvel examen (ou nouvelle version)
        /// </summary>
        private void InsertNouvelExamen(SqlConnection conn, string bdDest, ExamenSTG exam)
        {
            string insertSql = $@"
                INSERT INTO [{bdDest}].[DWH].[DIM_EXAMEN]
                (
                    TW_N_EXAM_ID,
                    DW_C_EXAM,
                    DW_NM_EXAM,
                    DW_Q_DURE_MINT,
                    DW_Q_PTS_MAX,
                    DW_I_COUR,
                    DW_D_DEBU_VALI,
                    DW_D_FIN_VALI,
                    DW_DH_CHARG_ETL,
                    DW_C_SYST_SRCE
                )
                VALUES
                (
                    @NK,
                    @CodeExam,
                    @NomExam,
                    @DureeMin,
                    @PointsMax,
                    1,                  -- DW_I_COUR = 1 (actuel)
                    GETDATE(),          -- DW_D_DEBU_VALI
                    NULL,               -- DW_D_FIN_VALI = NULL
                    GETDATE(),
                    'TestWe'
                );";

            using (SqlCommand cmd = new SqlCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("@NK", exam.NK);
                cmd.Parameters.AddWithValue("@CodeExam", string.IsNullOrEmpty(exam.CodeExam) ? (object)DBNull.Value : exam.CodeExam);
                cmd.Parameters.AddWithValue("@NomExam", string.IsNullOrEmpty(exam.NomExam) ? (object)DBNull.Value : exam.NomExam);
                cmd.Parameters.AddWithValue("@DureeMin", exam.DureeMin ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@PointsMax", exam.PointsMax ?? (object)DBNull.Value);

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
    /// Examen depuis STG (source)
    /// </summary>
    public class ExamenSTG
    {
        public string NK { get; set; }
        public string CodeExam { get; set; }
        public string NomExam { get; set; }
        public int? DureeMin { get; set; }
        public int? PointsMax { get; set; }
    }

    /// <summary>
    /// Examen depuis DWH (actuel)
    /// </summary>
    public class ExamenDWH
    {
        public int SK { get; set; }
        public string NK { get; set; }
        public string CodeExam { get; set; }
        public string NomExam { get; set; }
        public int? DureeMin { get; set; }
        public int? PointsMax { get; set; }
    }
}