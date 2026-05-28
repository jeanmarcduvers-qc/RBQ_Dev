// UPDATED: Thread-safe logging with SafeLog() - 2026-03-27
/*
 * ???????????????????????????????????????????????????????????????????????????
 * RIPQUAR - Phase 2 DIM - DIM_BUREAU (Table 5/5)
 * ???????????????????????????????????????????????????????????????????????????
 * 
 * Objectif:    Charger la dimension des bureaux/salles d'examen
 * Source:      siebeldb.dbo.CX_SALLE_EXAMEN
 * Destination: SDGIC01.DWH.DIM_BUREAU
 * Type:        Dimension simple (pas SCD Type 2)
 * 
 * Logique:
 *   - DELETE + INSERT complet (dimension simple, pas d'historique)
 *   - Charger seulement salles actives (X_GI_D_FIN_UTIL_SALL_EXAM IS NULL)
 *   - DW_N_BURE_ID: Auto-incr�ment� (IDENTITY)
 * 
 * Colonnes:
 *   - DW_C_BURE: Code bureau (X_GI_NO_SALL_EXAM)
 *   - DW_NM_BURE: Nom bureau (X_GI_NM_SALL_EXAM)
 *   - DW_NM_REGN_ADMN: R�gion (NULL pour l'instant)
 * 
 * ???????????????????????????????????????????????????????????????????????????
 */

#region Namespaces
using System;
using System.Data;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.IO;
#endregion

namespace ST_DIM_BUREAU
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

                Log("Script d�marre");

                // ===================================================================
                // 1. R�CUP�RATION DES VARIABLES SSIS
                // ===================================================================
                Log("Lecture variables...");
                string serverSource = Dts.Variables["User::ServerSource"].Value.ToString();
                string bdSource = Dts.Variables["User::BDSource"].Value.ToString();
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                string bdDest = Dts.Variables["User::BDDest"].Value.ToString();

                Log($"Source: {serverSource} / {bdSource}");
                Log($"Destination: {serverDest} / {bdDest}");

                // ===================================================================
                // 2. CONSTRUCTION CONNECTION STRINGS
                // ===================================================================
                string connStrSource = $"Data Source={serverSource};Initial Catalog={bdSource};Integrated Security=True;";
                string connStrDest = $"Data Source={serverDest};Initial Catalog={bdDest};Integrated Security=True;";

                // ===================================================================
                // 3. DELETE TABLE DESTINATION
                // ===================================================================
                Log("DELETE table destination...");
                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();
                    string deleteSql = $"DELETE FROM [{bdDest}].[DWH].[DIM_BUREAU];";
                    using (SqlCommand cmd = new SqlCommand(deleteSql, connDest))
                    {
                        int deleted = cmd.ExecuteNonQuery();
                        Log($"DELETE OK: {deleted} lignes");
                    }
                }

                // ===================================================================
                // 4. CHARGEMENT DONN�ES
                // ===================================================================
                Log("Lecture source CX_SALLE_EXAMEN...");

                int totalInseres = 0;

                using (SqlConnection connSource = new SqlConnection(connStrSource))
                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connSource.Open();
                    connDest.Open();

                    // Requ�te source: Salles actives uniquement
                    string sqlSource = $@"
                        SELECT 
                            X_GI_NO_SALL_EXAM AS CodeBureau,
                            X_GI_NM_SALL_EXAM AS NomBureau
                        FROM [{bdSource}].[dbo].[CX_SALLE_EXAMEN]
                        WHERE X_GI_D_FIN_UTIL_SALL_EXAM IS NULL  -- Actives seulement
                        ORDER BY X_GI_NO_SALL_EXAM;";

                    // Requ�te INSERT
                    string insertSql = $@"
                        INSERT INTO [{bdDest}].[DWH].[DIM_BUREAU] 
                        (DW_C_BURE, DW_NM_BURE, DW_NM_REGN_ADMN)
                        VALUES (@Code, @Nom, @Region);";

                    using (SqlCommand cmdRead = new SqlCommand(sqlSource, connSource))
                    using (SqlDataReader rdr = cmdRead.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string codeBureau = rdr.IsDBNull(rdr.GetOrdinal("CodeBureau")) ?
                                null : rdr["CodeBureau"].ToString();
                            string nomBureau = rdr.IsDBNull(rdr.GetOrdinal("NomBureau")) ?
                                null : rdr["NomBureau"].ToString();

                            // Extraction simple r�gion depuis le nom (optionnel)
                            // Pour l'instant, on met NULL - � affiner selon besoin m�tier
                            string region = ExtraireRegion(nomBureau);

                            // INSERT
                            using (SqlCommand cmdIns = new SqlCommand(insertSql, connDest))
                            {
                                cmdIns.Parameters.AddWithValue("@Code",
                                    codeBureau ?? (object)DBNull.Value);
                                cmdIns.Parameters.AddWithValue("@Nom",
                                    nomBureau ?? (object)DBNull.Value);
                                cmdIns.Parameters.AddWithValue("@Region",
                                    region ?? (object)DBNull.Value);

                                cmdIns.ExecuteNonQuery();
                                totalInseres++;
                            }

                            // Log progression
                            if (totalInseres % 10 == 0)
                            {
                                Log($"  Ins�r�: {totalInseres} bureaux...");
                            }
                        }
                    }
                }

                // ===================================================================
                // 5. STATISTIQUES FINALES
                // ===================================================================
                Log("");
                Log("???????????????????????????????????????????????????????????");
                Log("STATISTIQUES FINALES:");
                Log($"  - Bureaux ins�r�s: {totalInseres}");
                Log("???????????????????????????????????????????????????????????");
                Log("");
                Log($"=== FIN DIM_BUREAU - {DateTime.Now} ===");

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

        /// <summary>
        /// Tente d'extraire une r�gion depuis le nom du bureau
        /// Logique simplifi�e - � affiner selon besoin m�tier
        /// </summary>
        private string ExtraireRegion(string nomBureau)
        {
            if (string.IsNullOrEmpty(nomBureau))
                return null;

            // Logique simple bas�e sur grandes r�gions du Qu�bec
            string nom = nomBureau.ToUpper();

            // Montr�al et environs
            if (nom.Contains("MONTR�AL") || nom.Contains("MONTREAL") ||
                nom.Contains("LAVAL") || nom.Contains("LONGUEUIL") ||
                nom.Contains("BROSSARD") || nom.Contains("REPENTIGNY") ||
                nom.Contains("ST-J�R�ME"))
                return "Montr�al";

            // Qu�bec et environs
            if (nom.Contains("QU�BEC") || nom.Contains("QUEBEC"))
                return "Qu�bec";

            // R�gions sp�cifiques
            if (nom.Contains("GATINEAU"))
                return "Outaouais";

            if (nom.Contains("SHERBROOKE"))
                return "Estrie";

            if (nom.Contains("TROIS RIVI�RES") || nom.Contains("TROIS-RIVI�RES"))
                return "Mauricie";

            if (nom.Contains("JONQUI�RE") || nom.Contains("SAGUENAY"))
                return "Saguenay-Lac-Saint-Jean";

            if (nom.Contains("RIMOUSKI"))
                return "Bas-Saint-Laurent";

            if (nom.Contains("SEPT-�LES") || nom.Contains("SEPT �LES"))
                return "C�te-Nord";

            if (nom.Contains("ROUYN") || nom.Contains("NORANDA"))
                return "Abitibi-T�miscamingue";

            if (nom.Contains("�LES DE LA MADELEINE") || nom.Contains("MADELEINE"))
                return "Gasp�sie-�les-de-la-Madeleine";

            // Par d�faut
            return null;
        }

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

        #region ScriptResults declaration
        enum ScriptResults
        {
            Success = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Success,
            Failure = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Failure
        };
        #endregion
    }
}