/*
 * RIPQUAR - Script Task TW_PARTIE_EXAMEN
 * Parse JSON examParts[] → STG.TW_PARTIE_EXAMEN
 * 
 * Variables SSIS requises:
 * - ServerSource, BDSource, ServerDest, BDDest
 */

#region Namespaces
using System;
using System.Data;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using Newtonsoft.Json.Linq;
#endregion

namespace ST_Load_TW_PARTIE_EXAMEN
{
    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {
        public void Main()
        {
            bool fireAgain = false;

            try
            {
                string serverSource = Dts.Variables["User::ServerSource"].Value.ToString();
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                LogInfo($"Source: {serverSource} → Destination: {serverDest}");

                LoadTable();

                Dts.TaskResult = (int)ScriptResults.Success;
            }
            catch (Exception ex)
            {
                LogError($"Erreur globale: {ex.Message}");
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

        private void LogInfo(string message)
        {
            bool fireAgain = false;
            Dts.Events.FireInformation(0, "TW_PARTIE_EXAMEN", message, "", 0, ref fireAgain);
        }

        private void LogWarning(string message)
        {
            bool fireAgain = false;
            Dts.Events.FireWarning(0, "TW_PARTIE_EXAMEN", message, "", 0);
        }

        private void LogError(string message)
        {
            Dts.Events.FireError(0, "TW_PARTIE_EXAMEN", message, "", 0);
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

        #region Logique Spécifique TW_PARTIE_EXAMEN

        private void LoadTable()
        {
            // 1. Vider la table
            ClearTable("[SDGIC01].[STG].[TW_PARTIE_EXAMEN]");

            // 2. Récupérer les données sources
            DataTable dtSource = GetExamSourceData();

            // 3. Parser et charger
            int nbPartiesProcessed = 0;
            int nbErrors = 0;

            foreach (DataRow row in dtSource.Rows)
            {
                try
                {
                    int partiesFromExam = ParseAndLoadPartiesFromExam(row);
                    nbPartiesProcessed += partiesFromExam;
                }
                catch (Exception ex)
                {
                    nbErrors++;
                    LogWarning($"Erreur examen {row["ROW_ID"]}: {ex.Message}");
                }
            }

            LogInfo($"Traitement terminé - Parties chargées: {nbPartiesProcessed}, Erreurs: {nbErrors}");
        }

        /// <summary>
        /// Parse un examen et charge toutes ses parties dans TW_PARTIE_EXAMEN
        /// </summary>
        private int ParseAndLoadPartiesFromExam(DataRow sourceRow)
        {
            string rowId = sourceRow["ROW_ID"].ToString();
            string jsonExamRaw = sourceRow["JSON_EXAM"].ToString();

            // Parser le JSON
            JObject jsonWrapper = JObject.Parse(jsonExamRaw);
            JObject respBody = (JObject)jsonWrapper["ListOfRes107TestWeAPI:res"]["Resp200"]["RespBody"][0];

            // Récupérer l'ID de l'examen (parent)
            string examId = respBody["id"]?.ToString();

            // Récupérer le tableau examParts
            JArray examParts = (JArray)respBody["examParts"];

            if (examParts == null || examParts.Count == 0)
            {
                LogWarning($"ROW_ID {rowId}: Aucune partie trouvée");
                return 0;
            }

            LogInfo($"ROW_ID {rowId}: {examParts.Count} partie(s) à charger");

            // Boucler sur chaque partie
            int nbParties = 0;
            foreach (JObject part in examParts)
            {
                InsertPartie(examId, part);
                nbParties++;
            }

            return nbParties;
        }

        /// <summary>
        /// Insère une partie d'examen dans TW_PARTIE_EXAMEN
        /// </summary>
        private void InsertPartie(string examId, JObject part)
        {
            // Extraire les champs
            string partId = part["id"]?.ToString();
            string partName = part["name"]?.ToString();
            decimal? numberPoint = part["numberPoint"] != null ? decimal.Parse(part["numberPoint"].ToString()) : (decimal?)null;
            int? position = part["position"] != null ? int.Parse(part["position"].ToString()) : (int?)null;
            bool? randomPartIndexes = part["randomPartIndexes"]?.ToString().ToLower() == "true";

            // TW_DE_INST = varchar(1) - Flag ou code court uniquement
            string instruction = part["instruction"]?.ToString();
            string deInst = null;
            if (!string.IsNullOrEmpty(instruction))
            {
                deInst = instruction.Length > 0 ? instruction.Substring(0, 1) : null; // Premier caractère seulement
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
                    INSERT INTO [STG].[TW_PARTIE_EXAMEN]
                    (
                        TW_N_PART_ID,
                        TW_N_EXAM_ID,
                        TW_NM_PART,
                        TW_DE_INST,
                        TW_Q_NB_PTS,
                        TW_N_POSI,
                        TW_I_QUES_ALEA,
                        TW_DH_CHARG_ETL,
                        TW_C_SYST_SRCE,
                        TW_C_LOT_ETL
                    )
                    VALUES
                    (
                        @PartId,
                        @ExamId,
                        @PartName,
                        @DeInst,
                        @NumberPoint,
                        @Position,
                        @RandomPartIndexes,
                        GETDATE(),
                        'TestWe',
                        @LotETL
                    );
                ";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@PartId", partId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ExamId", examId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@PartName", partName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DeInst", deInst ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NumberPoint", numberPoint ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Position", position ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@RandomPartIndexes", randomPartIndexes ?? (object)DBNull.Value);
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