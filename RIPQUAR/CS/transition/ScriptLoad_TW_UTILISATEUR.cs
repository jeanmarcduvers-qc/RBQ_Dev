// UPDATED: Thread-safe logging with SafeLog() - 2026-03-27
/*
 * RIPQUAR - Script Task TW_UTILISATEUR
 * Parse JSON students[] + graders[] → STG.TW_UTILISATEUR (avec DÉDUPLICATION + FUSION RÔLES)
 * 
 * Stratégie: 
 * - Extraire students et graders de tous les examens
 * - Dédupliquer par userId
 * - Fusionner rôles: "Etudiant", "Correcteur", ou "Etudiant,Correcteur"
 * 
 * Variables SSIS requises:
 * - ServerSource, BDSource, ServerDest, BDDest
 */

#region Namespaces
using System;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using Newtonsoft.Json.Linq;
using System.IO;
#endregion

namespace ST_Load_TW_UTILISATEUR
{
    /// <summary>
    /// Classe helper pour stocker les données utilisateur avant insertion
    /// </summary>
    public class UtilisateurData
    {
        public string UserId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public HashSet<string> Roles { get; set; }
        public bool? Blocked { get; set; }

        public UtilisateurData()
        {
            Roles = new HashSet<string>();
        }
    }

    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {

        private string _logFile;
        private string _scriptName;

        public void Main()
        {
            bool fireAgain = false;
            _logFile = Dts.Variables["User::LogFile"].Value.ToString();
            _scriptName = Dts.Variables["System::TaskName"].Value.ToString();

            try
            {
                // ═══════════════════════════════════════════════════════════
                // 3. LOG DÉBUT
                // ═══════════════════════════════════════════════════════════
                SafeLog($"\n=== DÉBUT {_scriptName} - {DateTime.Now} ===\n");

                string serverSource = Dts.Variables["User::ServerSource"].Value.ToString();
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                LogInfo($"Source: {serverSource} → Destination: {serverDest}");

                LoadTable();

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

        #region Template Commun

        private string BuildSourceConnectionString()
        {
            string server = Dts.Variables["User::ServerSource"].Value.ToString();
            string database = Dts.Variables["User::BDSource"].Value.ToString();
            return $"Data Source={server};Initial Catalog={database};Integrated Security=True;";
        }

        private string BuildDestinationConnectionString()
        {
            string server = Dts.Variables["User::ServerDest"].Value.ToString();
            string database = Dts.Variables["User::BDDest"].Value.ToString();
            return $"Data Source={server};Initial Catalog={database};Integrated Security=True;";
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

        private void LogInfo(string message)
        {
            SafeLog($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }

        private void LogWarning(string message)
        {
            bool fireAgain = false;
            Dts.Events.FireWarning(0, "TW_UTILISATEUR", message, "", 0);
        }

        private void LogError(string message)
        {
            Dts.Events.FireError(0, "TW_UTILISATEUR", message, "", 0);
        }

        private void ClearTable(string fullTableName)
        {
            string connString = BuildDestinationConnectionString();
            LogInfo($"Nettoyage table {fullTableName}...");

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand($"DELETE FROM {fullTableName}", conn))
                {
                    int rowsDeleted = cmd.ExecuteNonQuery();
                    LogInfo($"{rowsDeleted} ligne(s) supprimée(s)");
                }
            }
        }

        private DataTable GetExamSourceData()
        {
            DataTable dt = new DataTable();
            string connString = BuildSourceConnectionString();

            // Query EXAM + CORR pour avoir graders ET students
            string query = @"
                SELECT
                    ROW_ID,
                    CONVERT(NVARCHAR(MAX), X_GI_V_VECT_EXAM) AS JSON_EXAM,
                    CONVERT(NVARCHAR(MAX), X_GI_V_VECT_CORR_EXAM) AS JSON_CORR
                FROM [dbo].[CX_INSC_EXAM]
                WHERE X_GI_V_VECT_EXAM IS NOT NULL
                  AND X_GI_V_VECT_CORR_EXAM IS NOT NULL
                ORDER BY LAST_UPD DESC;
            ";

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.CommandTimeout = 300;
                    using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                    {
                        adapter.Fill(dt);
                    }
                }
            }

            LogInfo($"Récupéré {dt.Rows.Count} examen(s) source (EXAM + CORR)");
            return dt;
        }

        #endregion

        #region Logique Spécifique TW_UTILISATEUR avec Déduplication + Fusion

        private void LoadTable()
        {
            // 1. Vider la table
            ClearTable("[SDGIC01].[STG].[TW_UTILISATEUR]");

            // 2. Récupérer les données sources
            DataTable dtSource = GetExamSourceData();

            // 3. Extraire tous les utilisateurs avec DÉDUPLICATION + FUSION
            Dictionary<string, UtilisateurData> utilisateurs = new Dictionary<string, UtilisateurData>();
            int nbExamsProcessed = 0;
            int nbErrors = 0;

            LogInfo("Extraction utilisateurs (étudiants + correcteurs)...");

            foreach (DataRow row in dtSource.Rows)
            {
                try
                {
                    ExtractUtilisateursFromExam(row, utilisateurs);
                    nbExamsProcessed++;
                }
                catch (Exception ex)
                {
                    nbErrors++;
                    LogWarning($"Erreur examen {row["ROW_ID"]}: {ex.Message}");
                }
            }

            LogInfo($"{nbExamsProcessed} examen(s) traité(s), {utilisateurs.Count} utilisateur(s) unique(s) trouvé(s)");

            // 4. Charger les utilisateurs uniques
            int nbUtilLoaded = 0;
            foreach (var util in utilisateurs.Values)
            {
                try
                {
                    InsertUtilisateur(util);
                    nbUtilLoaded++;
                }
                catch (Exception ex)
                {
                    LogWarning($"Erreur insertion utilisateur userId={util.UserId}: {ex.Message}");
                }
            }

            LogInfo($"Traitement terminé - Utilisateurs chargés: {nbUtilLoaded}, Erreurs: {nbErrors}");
        }

        /// <summary>
        /// Extrait les utilisateurs (students + graders) d'un examen
        /// Fusionne dans le Dictionary avec déduplication par userId
        /// </summary>
        private void ExtractUtilisateursFromExam(DataRow sourceRow, Dictionary<string, UtilisateurData> utilisateurs)
        {
            string rowId = sourceRow["ROW_ID"].ToString();
            string jsonExamRaw = sourceRow["JSON_EXAM"].ToString();
            string jsonCorrRaw = sourceRow["JSON_CORR"].ToString();

            // Parser le JSON EXAM (pour graders)
            JObject jsonExamWrapper = JObject.Parse(jsonExamRaw);
            JObject respBodyExam = null;
            try
            {
                respBodyExam = (JObject)jsonExamWrapper["ListOfRes107TestWeAPI:res"]["Resp200"]["RespBody"][0];
            }
            catch
            {
                // Structure peut varier
            }

            // Parser le JSON CORR (pour student - OBJET UNIQUE pas tableau!)
            // Structure peut être complètement différente du JSON EXAM
            JObject jsonCorrWrapper = null;
            JObject respBodyCorr = null;

            try
            {
                jsonCorrWrapper = JObject.Parse(jsonCorrRaw);

                // STRATÉGIE 1: Même structure que EXAM
                JToken res = jsonCorrWrapper["ListOfRes107TestWeAPI:res"];
                if (res != null)
                {
                    JToken resp200 = res["Resp200"];
                    if (resp200 != null)
                    {
                        JToken respBody = resp200["RespBody"];
                        if (respBody != null && respBody.Type == JTokenType.Object)
                        {
                            respBodyCorr = (JObject)respBody;
                        }
                    }
                }

                // STRATÉGIE 2: Structure sans wrapper ListOfRes107TestWeAPI:res
                if (respBodyCorr == null)
                {
                    JToken resp200 = jsonCorrWrapper["Resp200"];
                    if (resp200 != null)
                    {
                        JToken respBody = resp200["RespBody"];
                        if (respBody != null && respBody.Type == JTokenType.Object)
                        {
                            respBodyCorr = (JObject)respBody;
                        }
                    }
                }

                // STRATÉGIE 3: RespBody directement à la racine (UTILISÉ ACTUELLEMENT)
                if (respBodyCorr == null)
                {
                    JToken respBody = jsonCorrWrapper["RespBody"];
                    if (respBody != null && respBody.Type == JTokenType.Object)
                    {
                        respBodyCorr = (JObject)respBody;
                    }
                }

                // STRATÉGIE 4: Le JSON wrapper EST déjà le RespBody
                if (respBodyCorr == null && jsonCorrWrapper["student"] != null)
                {
                    respBodyCorr = jsonCorrWrapper;
                }
            }
            catch (Exception ex)
            {
                LogWarning($"ROW_ID {rowId}: Erreur parse CORR - {ex.Message}");
            }

            // Extraire student (OBJET UNIQUE) depuis JSON CORR
            if (respBodyCorr != null)
            {
                try
                {
                    JObject student = (JObject)respBodyCorr["student"];
                    if (student != null)
                    {
                        string userId = student["userId"]?.ToString();

                        if (!string.IsNullOrEmpty(userId))
                        {
                            if (!utilisateurs.ContainsKey(userId))
                            {
                                utilisateurs[userId] = new UtilisateurData
                                {
                                    UserId = userId,
                                    FirstName = student["firstName"]?.ToString(),
                                    LastName = student["lastName"]?.ToString(),
                                    FullName = student["fullName"]?.ToString(),
                                    Email = student["email"]?.ToString()
                                };
                            }

                            utilisateurs[userId].Roles.Add("Etudiant");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogWarning($"ROW_ID {rowId}: Erreur extraction student - {ex.Message}");
                }
            }

            // Extraire graders depuis JSON EXAM
            if (respBodyExam != null)
            {
                JArray distributions = (JArray)respBodyExam["studentPapersDistributions"];
                if (distributions != null && distributions.Count > 0)
                {
                    foreach (JObject distrib in distributions)
                    {
                        JArray graders = (JArray)distrib["grader"];
                        if (graders == null || graders.Count == 0) continue;

                        JObject grader = graders.Count > 0 ? (JObject)graders[0] : (JObject)distrib["grader"];
                        if (grader == null) continue;

                        string userId = grader["userId"]?.ToString();
                        if (string.IsNullOrEmpty(userId)) continue;

                        if (!utilisateurs.ContainsKey(userId))
                        {
                            utilisateurs[userId] = new UtilisateurData
                            {
                                UserId = userId,
                                FirstName = grader["firstName"]?.ToString(),
                                LastName = grader["lastName"]?.ToString(),
                                FullName = grader["fullName"]?.ToString(),
                                Blocked = grader["blocked"]?.ToString().ToLower() == "true"
                            };
                        }
                        else
                        {
                            if (utilisateurs[userId].Blocked == null)
                            {
                                utilisateurs[userId].Blocked = grader["blocked"]?.ToString().ToLower() == "true";
                            }
                        }

                        utilisateurs[userId].Roles.Add("Correcteur");
                    }
                }
            }
        }

        /// <summary>
        /// Insère un utilisateur unique dans TW_UTILISATEUR
        /// </summary>
        private void InsertUtilisateur(UtilisateurData util)
        {
            // Générer UUID
            string utilId = Guid.NewGuid().ToString();

            // Fusionner rôles
            string roles = string.Join(",", util.Roles.OrderBy(r => r));

            // Lot ETL
            string lotETL = "RIPQUAR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // INSERT dans la table
            string connString = BuildDestinationConnectionString();

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();

                string insertQuery = @"
                    USE [SDGIC01];
                    INSERT INTO [STG].[TW_UTILISATEUR]
                    (
                        TW_N_UTIL_ID,
                        TW_N_UTIL_ID_INTR,
                        TW_NM_UTIL,
                        TW_NM_PREN,
                        TW_NM_NOM,
                        TW_A_COUR,
                        TW_C_ROLE,
                        TW_I_BLOQ,
                        TW_DH_CHARG_ETL,
                        TW_C_SYST_SRCE,
                        TW_C_LOT_ETL
                    )
                    VALUES
                    (
                        @UtilId,
                        @UserId,
                        @FullName,
                        @FirstName,
                        @LastName,
                        @Email,
                        @Roles,
                        @Blocked,
                        GETDATE(),
                        'TestWe',
                        @LotETL
                    );
                ";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@UtilId", utilId);
                    cmd.Parameters.AddWithValue("@UserId", util.UserId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@FullName", util.FullName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@FirstName", util.FirstName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@LastName", util.LastName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", util.Email ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Roles", roles);
                    cmd.Parameters.AddWithValue("@Blocked", util.Blocked ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@LotETL", lotETL ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
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
}