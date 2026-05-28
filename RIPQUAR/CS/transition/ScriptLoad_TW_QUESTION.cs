// UPDATED: Thread-safe logging with SafeLog() - 2026-03-27
/*
 * RIPQUAR - Script Task TW_QUESTION
 * Parse JSON examParts[].partIndexes[].question[] → STG.TW_QUESTION
 * 
 * Variables SSIS requises:
 * - ServerSource, BDSource, ServerDest, BDDest
 * 
 * NOTES IMPORTANTES:
 * - Pas de FK vers TW_PARTIE_EXAMEN (relation via TW_INDEX_PARTIE probablement)
 * - Pas de colonne texte question en STG (probablement en DIM)
 */

#region Namespaces
using System;
using System.Data;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using Newtonsoft.Json.Linq;
using System.IO;
#endregion

namespace ST_Load_TW_QUESTION
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

        private void LogInfo(string message)
        {
            SafeLog($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }

        private void LogWarning(string message)
        {
            bool fireAgain = false;
            Dts.Events.FireWarning(0, "TW_QUESTION", message, "", 0);
        }

        private void LogError(string message)
        {
            Dts.Events.FireError(0, "TW_QUESTION", message, "", 0);
        }

        private DateTime? ParseTestWeDate(string dateStr)
        {
            if (string.IsNullOrEmpty(dateStr))
                return null;

            try
            {
                return DateTime.ParseExact(dateStr, "MM/dd/yyyy HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
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

            string query = @"
                SELECT
                    ROW_ID,
                    CONVERT(NVARCHAR(MAX), X_GI_V_VECT_EXAM) AS JSON_EXAM
                FROM [dbo].[CX_INSC_EXAM]
                WHERE X_GI_V_VECT_EXAM IS NOT NULL
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

            LogInfo($"Récupéré {dt.Rows.Count} examen(s) source");
            return dt;
        }

        #endregion

        #region Logique Spécifique TW_QUESTION

        private void LoadTable()
        {
            // 1. Vider la table
            ClearTable("[SDGIC01].[STG].[TW_QUESTION]");

            // 2. Récupérer les données sources
            DataTable dtSource = GetExamSourceData();

            // 3. Parser et charger
            int nbQuestionsProcessed = 0;
            int nbErrors = 0;

            foreach (DataRow row in dtSource.Rows)
            {
                try
                {
                    int questionsFromExam = ParseAndLoadQuestionsFromExam(row);
                    nbQuestionsProcessed += questionsFromExam;
                }
                catch (Exception ex)
                {
                    nbErrors++;
                    LogWarning($"Erreur examen {row["ROW_ID"]}: {ex.Message}");
                }
            }

            LogInfo($"Traitement terminé - Questions chargées: {nbQuestionsProcessed}, Erreurs: {nbErrors}");
        }

        /// <summary>
        /// Parse un examen et charge toutes ses questions dans TW_QUESTION
        /// Navigation: examParts[] → partIndexes[] → question[]
        /// </summary>
        private int ParseAndLoadQuestionsFromExam(DataRow sourceRow)
        {
            string rowId = sourceRow["ROW_ID"].ToString();
            string jsonExamRaw = sourceRow["JSON_EXAM"].ToString();

            // Parser le JSON
            JObject jsonWrapper = JObject.Parse(jsonExamRaw);
            JObject respBody = (JObject)jsonWrapper["ListOfRes107TestWeAPI:res"]["Resp200"]["RespBody"][0];

            // Récupérer le tableau examParts
            JArray examParts = (JArray)respBody["examParts"];

            if (examParts == null || examParts.Count == 0)
            {
                LogWarning($"ROW_ID {rowId}: Aucune partie trouvée");
                return 0;
            }

            int nbQuestions = 0;
            int globalPosition = 0; // Position globale à travers toutes les parties

            // Boucler sur chaque partie
            foreach (JObject part in examParts)
            {
                // Récupérer partIndexes
                JArray partIndexes = (JArray)part["partIndexes"];

                if (partIndexes == null || partIndexes.Count == 0)
                    continue;

                // Boucler sur chaque partIndex
                foreach (JObject partIndex in partIndexes)
                {
                    // Récupérer le tableau question
                    JArray questions = (JArray)partIndex["question"];

                    if (questions == null || questions.Count == 0)
                        continue;

                    // Boucler sur chaque question
                    foreach (JObject question in questions)
                    {
                        InsertQuestion(question, globalPosition);
                        nbQuestions++;
                        globalPosition++;
                    }
                }
            }

            if (nbQuestions > 0)
                LogInfo($"ROW_ID {rowId}: {nbQuestions} question(s) chargée(s)");

            return nbQuestions;
        }

        /// <summary>
        /// Insère une question dans TW_QUESTION
        /// </summary>
        private void InsertQuestion(JObject question, int position)
        {
            // Extraire les champs depuis question
            string questionId = question["id"]?.ToString();
            string externalReference = question["externalReference"]?.ToString();
            decimal? numberPoint = question["numberPoint"] != null ? decimal.Parse(question["numberPoint"].ToString()) : (decimal?)null;
            DateTime? createdAt = ParseTestWeDate(question["createdAt"]?.ToString());
            DateTime? updatedAt = ParseTestWeDate(question["updatedAt"]?.ToString());

            // Points négatifs (si existe dans JSON)
            decimal? negativePoints = question["negativePoints"] != null ? decimal.Parse(question["negativePoints"].ToString()) : (decimal?)null;

            // Extraire depuis questionBank[0] (tableau d'1 élément)
            string uuidBanqQues = null;
            string typeQues = null;
            bool? choixAlea = null;
            string commCorr = null;

            JArray questionBank = (JArray)question["questionBank"];
            if (questionBank != null && questionBank.Count > 0)
            {
                JObject qBank = (JObject)questionBank[0];

                uuidBanqQues = qBank["id"]?.ToString();
                typeQues = qBank["type"]?.ToString();

                // Choix aléatoires
                choixAlea = qBank["randomizeChoices"]?.ToString().ToLower() == "true"
                         || qBank["shuffleAnswers"]?.ToString().ToLower() == "true";

                // Commentaire correction (varchar(1) - premier caractère seulement)
                string comment = qBank["correctionComment"]?.ToString() ?? qBank["feedback"]?.ToString();
                if (!string.IsNullOrEmpty(comment))
                {
                    commCorr = comment.Substring(0, 1);
                }
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
                    INSERT INTO [STG].[TW_QUESTION]
                    (
                        TW_N_QUES_ID,
                        TW_N_UUID_BANQ_QUES,
                        TW_C_REF_EXT,
                        TW_N_POSI,
                        TW_M_PTS,
                        TW_M_PTS_NEGA,
                        TW_C_TYPE_QUES,
                        TW_I_CHOI_ALEA,
                        TW_DE_COMM_CORR,
                        TW_DH_CREA,
                        TW_DH_MODI,
                        TW_DH_CHARG_ETL,
                        TW_C_SYST_SRCE,
                        TW_C_LOT_ETL
                    )
                    VALUES
                    (
                        @QuestionId,
                        @UuidBanqQues,
                        @ExternalReference,
                        @Position,
                        @NumberPoint,
                        @NegativePoints,
                        @TypeQues,
                        @ChoixAlea,
                        @CommCorr,
                        @CreatedAt,
                        @UpdatedAt,
                        GETDATE(),
                        'TestWe',
                        @LotETL
                    );
                ";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@QuestionId", questionId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@UuidBanqQues", uuidBanqQues ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ExternalReference", externalReference ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Position", position);
                    cmd.Parameters.AddWithValue("@NumberPoint", numberPoint ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NegativePoints", negativePoints ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@TypeQues", typeQues ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ChoixAlea", choixAlea ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@CommCorr", commCorr ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@CreatedAt", createdAt ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@UpdatedAt", updatedAt ?? (object)DBNull.Value);
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