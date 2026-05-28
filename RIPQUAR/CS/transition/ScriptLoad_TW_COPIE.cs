// UPDATED: Thread-safe logging with SafeLog() - 2026-03-27
/*
 * RIPQUAR - Script Task TW_COPIE
 * Parse JSON CORR RespBody → STG.TW_COPIE
 * 
 * Source: 1 JSON CORR = 1 copie d'examen d'un étudiant
 * 
 * Variables SSIS requises:
 * - ServerSource, BDSource, ServerDest, BDDest
 */

#region Namespaces
using System;
using System.Data;
using System.Collections.Generic;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using Newtonsoft.Json.Linq;
using System.IO;
#endregion

namespace ST_Load_TW_COPIE
{
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
                //  LOG DÉBUT
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
            Dts.Events.FireWarning(0, "TW_COPIE", message, "", 0);
        }

        private void LogError(string message)
        {
            Dts.Events.FireError(0, "TW_COPIE", message, "", 0);
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

        private DataTable GetCorrSourceData()
        {
            DataTable dt = new DataTable();
            string connString = BuildSourceConnectionString();

            string query = @"
                SELECT
                    ROW_ID,
                    CONVERT(NVARCHAR(MAX), X_GI_V_VECT_CORR_EXAM) AS JSON_CORR
                FROM [dbo].[CX_INSC_EXAM]
                WHERE X_GI_V_VECT_CORR_EXAM IS NOT NULL
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

            LogInfo($"Récupéré {dt.Rows.Count} copie(s) source");
            return dt;
        }

        #endregion

        #region Lookup Helpers

        /// <summary>
        /// Trouve le TW_N_UTIL_ID (UUID) depuis userId
        /// </summary>
        private string LookupUtilisateurUUID(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return null;

            string connString = BuildDestinationConnectionString();

            string query = @"
                SELECT TW_N_UTIL_ID
                FROM [SDGIC01].[STG].[TW_UTILISATEUR]
                WHERE TW_N_UTIL_ID_INTR = @UserId;
            ";

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", int.Parse(userId));

                    object result = cmd.ExecuteScalar();

                    if (result == null)
                    {
                        LogWarning($"Lookup utilisateur: userId={userId} NOT FOUND");
                    }
                    else
                    {
                        LogInfo($"Lookup utilisateur: userId={userId} → UUID={result}");
                    }

                    return result?.ToString();
                }
            }
        }

        #endregion

        #region Logique Spécifique TW_COPIE

        private void LoadTable()
        {
            // 1. Vider la table
            ClearTable("[SDGIC01].[STG].[TW_COPIE]");

            // 2. Récupérer les données sources
            DataTable dtSource = GetCorrSourceData();

            // 3. Charger chaque copie
            int nbCopiesLoaded = 0;
            int nbErrors = 0;

            LogInfo("Chargement des copies...");

            foreach (DataRow row in dtSource.Rows)
            {
                try
                {
                    InsertCopie(row);
                    nbCopiesLoaded++;
                }
                catch (Exception ex)
                {
                    nbErrors++;
                    LogWarning($"Erreur copie {row["ROW_ID"]}: {ex.Message}");
                }
            }

            LogInfo($"Traitement terminé - Copies chargées: {nbCopiesLoaded}, Erreurs: {nbErrors}");
        }

        /// <summary>
        /// Insère une copie depuis JSON CORR
        /// </summary>
        private void InsertCopie(DataRow sourceRow)
        {
            string rowId = sourceRow["ROW_ID"].ToString();
            string jsonCorrRaw = sourceRow["JSON_CORR"].ToString();

            // Parser le JSON CORR
            JObject jsonCorrWrapper = JObject.Parse(jsonCorrRaw);

            // Structure: RespBody directement (pas de tableau)
            JObject respBody = null;
            JToken respBodyToken = jsonCorrWrapper["RespBody"];
            if (respBodyToken != null && respBodyToken.Type == JTokenType.Object)
            {
                respBody = (JObject)respBodyToken;
            }

            if (respBody == null)
            {
                LogWarning($"ROW_ID {rowId}: RespBody non trouvé");
                return;
            }

            // DEBUG: Lister tous les champs du RespBody
            string fieldNames = "";
            foreach (var prop in respBody.Properties())
            {
                fieldNames += prop.Name + ", ";
            }
            LogInfo($"ROW_ID {rowId}: Champs RespBody: {fieldNames.TrimEnd(',', ' ')}");

            // Extraire données copie
            string studentPaperId = respBody["studentPaperId"]?.ToString();

            // Student
            JObject student = (JObject)respBody["student"];
            string studentUserId = student?["userId"]?.ToString();
            string studentFirstName = student?["firstName"]?.ToString();
            string studentLastName = student?["lastName"]?.ToString();
            string studentFullName = $"{studentFirstName} {studentLastName}".Trim();

            // Exam
            JObject exam = (JObject)respBody["exam"];
            string examIdUrl = exam?["@id"]?.ToString(); // Format: "/v1/exams/UUID"
            string examId = null;

            if (!string.IsNullOrEmpty(examIdUrl))
            {
                // Extraire UUID depuis URL: /v1/exams/269bfc6c-... → 269bfc6c-...
                var parts = examIdUrl.Split('/');
                examId = parts[parts.Length - 1];
                LogInfo($"ROW_ID {rowId}: exam @id = {examIdUrl}, extracted ID = {examId}");
            }
            else
            {
                LogWarning($"ROW_ID {rowId}: exam @id non trouvé");
            }

            // Dates
            string endDateStr = respBody["endDate"]?.ToString();
            string submitDateStr = respBody["submitDate"]?.ToString();
            string gradingDateStr = respBody["gradingDate"]?.ToString();

            DateTime? endDate = ParseDateTime(endDateStr);
            DateTime? submitDate = ParseDateTime(submitDateStr);
            DateTime? gradingDate = ParseDateTime(gradingDateStr);

            // Flags
            string hasFlaggedAnswerStr = respBody["hasFlaggedAnswer"]?.ToString();
            bool? hasFlaggedAnswer = hasFlaggedAnswerStr?.ToLower() == "true";

            // Utiliser ROW_ID comme ID de copie (pour FK avec TW_REPONSE)
            string copieId = rowId;

            // LOOKUP: Trouver UUID utilisateur depuis userId
            string utilisateurUUID = LookupUtilisateurUUID(studentUserId);

            if (string.IsNullOrEmpty(utilisateurUUID))
            {
                LogWarning($"ROW_ID {rowId}: Utilisateur userId={studentUserId} non trouvé dans TW_UTILISATEUR - INSERT avec NULL");
                // Ne pas abandonner, continuer avec NULL
            }

            if (string.IsNullOrEmpty(examId))
            {
                LogWarning($"ROW_ID {rowId}: Exam ID null - INSERT avec NULL");
                // Ne pas abandonner, continuer avec NULL
            }

            // Lot ETL
            string lotETL = "RIPQUAR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // INSERT dans la table
            string connString = BuildDestinationConnectionString();

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();

                string insertQuery = @"
                    USE [SDGIC01];
                    INSERT INTO [STG].[TW_COPIE]
                    (
                        TW_N_COPI_ID,
                        TW_N_COPI_ID_INTR,
                        TW_N_EXAM_ID,
                        TW_N_UTIL_ID,
                        TW_N_FK_EXAM_ID,
                        TW_N_FK_UTIL_ID,
                        TW_NM_ETUD,
                        TW_DH_FIN,
                        TW_DH_SOUM,
                        TW_DH_CORR,
                        TW_I_REPN_SIGN,
                        TW_DH_CHARG_ETL,
                        TW_C_SYST_SRCE,
                        TW_C_LOT_ETL
                    )
                    VALUES
                    (
                        @CopieId,
                        @StudentPaperId,
                        @ExamIdFK,
                        @StudentUserIdFK,
                        @ExamIdFK,
                        @StudentUserIdFK,
                        @StudentFullName,
                        @EndDate,
                        @SubmitDate,
                        @GradingDate,
                        @HasFlaggedAnswer,
                        GETDATE(),
                        'TestWe',
                        @LotETL
                    );
                ";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@CopieId", copieId);
                    cmd.Parameters.AddWithValue("@StudentPaperId", string.IsNullOrEmpty(studentPaperId) ? (object)DBNull.Value : int.Parse(studentPaperId));
                    cmd.Parameters.AddWithValue("@ExamIdFK", examId);  // UUID direct → TW_N_EXAM_ID
                    cmd.Parameters.AddWithValue("@StudentUserIdFK", utilisateurUUID);  // UUID lookup → TW_N_UTIL_ID
                    cmd.Parameters.AddWithValue("@StudentFullName", studentFullName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@EndDate", endDate ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@SubmitDate", submitDate ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@GradingDate", gradingDate ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@HasFlaggedAnswer", hasFlaggedAnswer ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@LotETL", lotETL ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Parse une date ISO format
        /// </summary>
        private DateTime? ParseDateTime(string dateStr)
        {
            if (string.IsNullOrEmpty(dateStr))
                return null;

            DateTime result;
            if (DateTime.TryParse(dateStr, out result))
                return result;

            return null;
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