/*
 * ═══════════════════════════════════════════════════════════════════════════
 * RIPQUAR - Phase 2 DIM - DIM_CANDIDAT (Script Task)
 * ═══════════════════════════════════════════════════════════════════════════
 * 
 * Objectif: Charger DIM_CANDIDAT avec SCD Type 2 (historisation)
 * Source:      [SDGIC01].[STG].[TW_UTILISATEUR]
 * Destination: [SDGIC01].[DWH].[DIM_CANDIDAT]
 * 
 * Logique SCD Type 2:
 *   - Nouveau candidat → INSERT
 *   - Candidat changé → UPDATE (expirer ancien) + INSERT (nouveau)
 *   - Candidat identique → Rien
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

namespace ST_DIM_CANDIDAT
{
    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {
        private string logFile = @"C:\Temp\DIM_CANDIDAT_LOG.txt";

        public void Main()
        {
            try
            {
                File.WriteAllText(logFile, $"=== DÉBUT DIM_CANDIDAT - {DateTime.Now} ===\n\n");
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

                string connStrSource = $"Data Source={serverSource};Initial Catalog={bdSource};Integrated Security=True;";
                string connStrDest = $"Data Source={serverDest};Initial Catalog={bdDest};Integrated Security=True;";

                // ===================================================================
                // 2. CHARGER LES CANDIDATS ACTUELS DEPUIS DWH (pour comparaison)
                // ===================================================================
                Log("Chargement candidats actuels depuis DWH...");

                Dictionary<string, CandidatDWH> candidatsActuels = new Dictionary<string, CandidatDWH>();

                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();
                    string queryCandidatsActuels = @"
                        SELECT 
                            DW_N_CAND_ID,
                            TW_N_UTIL_ID,
                            DW_NM_CAND_COMPL,
                            DW_C_EMPR_COUR
                        FROM [DWH].[DIM_CANDIDAT]
                        WHERE DW_I_COUR = 1;"; 

                    using (SqlCommand cmd = new SqlCommand(queryCandidatsActuels, connDest))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var cand = new CandidatDWH
                                {
                                    SK = reader.GetInt32(0),
                                    NK = reader.GetString(1),
                                    NomComplet = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                    Email = reader.IsDBNull(3) ? "" : reader.GetString(3)
                                };

                                candidatsActuels[cand.NK] = cand;
                            }
                        }
                    }
                }

                Log($"✓ {candidatsActuels.Count} candidats actuels chargés depuis DWH");

                // ===================================================================
                // 3. LIRE LES CANDIDATS DEPUIS STG (dans SDGIC01, pas siebeldb!)
                // ===================================================================
                Log("Lecture candidats depuis STG...");

                List<CandidatSTG> candidatsSTG = new List<CandidatSTG>();

                using (SqlConnection connDest2 = new SqlConnection(connStrDest))
                {
                    connDest2.Open();

                    string querySTG = $@"
                        SELECT DISTINCT
                            TW_N_UTIL_ID,
                            RTRIM(LTRIM(ISNULL(TW_NM_PREN, '') + ' ' + ISNULL(TW_NM_NOM, ''))) AS NomComplet,
                            TW_A_COUR
                        FROM [{bdDest}].[STG].[TW_UTILISATEUR]
                        WHERE TW_N_UTIL_ID IS NOT NULL;";

                    using (SqlCommand cmd = new SqlCommand(querySTG, connDest2))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var cand = new CandidatSTG
                                {
                                    NK = reader.GetString(0),
                                    NomComplet = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                    Email = reader.IsDBNull(2) ? "" : reader.GetString(2)
                                };

                                candidatsSTG.Add(cand);
                            }
                        }
                    }
                }

                Log($"✓ {candidatsSTG.Count} candidats lus depuis STG");

                // ===================================================================
                // 4. TRAITER CHAQUE CANDIDAT (Logique SCD Type 2)
                // ===================================================================
                int nouveaux = 0;
                int modifies = 0;
                int inchanges = 0;
                int erreurs = 0;

                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();

                    foreach (var candSTG in candidatsSTG)
                    {
                        try
                        {
                            if (!candidatsActuels.ContainsKey(candSTG.NK))
                            {
                                // ============================================
                                // NOUVEAU CANDIDAT → INSERT
                                // ============================================
                                InsertNouveauCandidat(connDest, bdDest, candSTG);
                                nouveaux++;
                            }
                            else
                            {
                                // ============================================
                                // CANDIDAT EXISTANT → Vérifier si changé
                                // ============================================
                                var candDWH = candidatsActuels[candSTG.NK];

                                if (HasChanged(candDWH, candSTG))
                                {
                                    // CHANGÉ → UPDATE (expirer) + INSERT (nouveau)
                                    ExpireCandidat(connDest, bdDest, candDWH.SK);
                                    InsertNouveauCandidat(connDest, bdDest, candSTG);
                                    modifies++;

                                    Log($"Candidat modifié: {candSTG.NK} - {candSTG.NomComplet}");
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
                            Log($"Erreur candidat {candSTG.NK}: {ex.Message}");
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
                Log($"  - Candidats STG: {candidatsSTG.Count}");
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
        /// Vérifie si un candidat a changé
        /// </summary>
        private bool HasChanged(CandidatDWH dwh, CandidatSTG stg)
        {
            return dwh.NomComplet != stg.NomComplet || dwh.Email != stg.Email;
        }

        /// <summary>
        /// Expire un candidat (UPDATE pour marquer comme historique)
        /// </summary>
        private void ExpireCandidat(SqlConnection conn, string bdDest, int sk)
        {
            string updateSql = $@"
                UPDATE [{bdDest}].[DWH].[DIM_CANDIDAT]
                SET 
                    DW_I_COUR = 0,
                    DW_D_FIN_VALI = GETDATE()
                WHERE DW_N_CAND_ID = @SK;";

            using (SqlCommand cmd = new SqlCommand(updateSql, conn))
            {
                cmd.Parameters.AddWithValue("@SK", sk);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Insert un nouveau candidat (ou nouvelle version)
        /// </summary>
        private void InsertNouveauCandidat(SqlConnection conn, string bdDest, CandidatSTG cand)
        {
            string insertSql = $@"
                INSERT INTO [{bdDest}].[DWH].[DIM_CANDIDAT]
                (
                    TW_N_UTIL_ID,
                    DW_NM_CAND_COMPL,
                    DW_C_EMPR_COUR,
                    DW_I_COUR,
                    DW_D_DEBU_VALI,
                    DW_D_FIN_VALI,
                    DW_DH_CHARG_ETL,
                    DW_C_SYST_SRCE,
                    DW_C_LOT_ETL
                )
                VALUES
                (
                    @NK,
                    @NomComplet,
                    @Email,
                    1,                  -- DW_I_COUR = 1 (actuel)
                    GETDATE(),          -- DW_D_DEBU_VALI
                    NULL,               -- DW_D_FIN_VALI = NULL (pas expiré)
                    GETDATE(),
                    'TestWe',
                    NULL
                );";

            using (SqlCommand cmd = new SqlCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("@NK", cand.NK);
                cmd.Parameters.AddWithValue("@NomComplet", string.IsNullOrEmpty(cand.NomComplet) ? (object)DBNull.Value : cand.NomComplet);
                cmd.Parameters.AddWithValue("@Email", string.IsNullOrEmpty(cand.Email) ? (object)DBNull.Value : cand.Email);

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
    /// Candidat depuis STG (source)
    /// </summary>
    public class CandidatSTG
    {
        public string NK { get; set; }          // Business Key (TW_N_UTIL_ID)
        public string NomComplet { get; set; }
        public string Email { get; set; }
    }

    /// <summary>
    /// Candidat depuis DWH (actuel)
    /// </summary>
    public class CandidatDWH
    {
        public int SK { get; set; }             // Surrogate Key (DW_N_CAND_ID)
        public string NK { get; set; }          // Business Key
        public string NomComplet { get; set; }
        public string Email { get; set; }
    }
}