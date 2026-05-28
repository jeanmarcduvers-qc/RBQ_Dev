/*
 * RIPQUAR - Script Task TW_INDEX_PARTIE
 * Parse JSON examParts[].partIndexes[] ? STG.TW_INDEX_PARTIE
 * 
 * TABLE DE LIAISON: Lie TW_PARTIE_EXAMEN ? TW_QUESTION
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

namespace ST_Load_TW_INDEX_PARTIE
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
                LogInfo($"Source: {serverSource} ? Destination: {serverDest}");

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
            Dts.Events.FireInformation(0, "TW_INDEX_PARTIE", message, "", 0, ref fireAgain);
        }

        private void LogWarning(string message)
        {
            bool fireAgain = false;
            Dts.Events.FireWarning(0, "TW_INDEX_PARTIE", message, "", 0);
        }

        private void LogError(string message)
        {
            Dts.Events.FireError(0, "TW_INDEX_PARTIE", message, "", 0);
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
                    LogInfo($"{rowsDeleted} ligne(s) supprim�e(s)");
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

            LogInfo($"R�cup�r� {dt.Rows.Count} examen(s) source");
            return dt;
        }

        #endregion

        #region Logique Sp�cifique TW_INDEX_PARTIE

        private void LoadTable()
        {
            // 1. Vider la table
            ClearTable("[SDGIC01].[STG].[TW_INDEX_PARTIE]");

            // 2. R�cup�rer les donn�es sources
            DataTable dtSource = GetExamSourceData();

            // 3. Parser et charger
            int nbIndexProcessed = 0;
            int nbErrors = 0;

            foreach (DataRow row in dtSource.Rows)
            {
                try
                {
                    int indexFromExam = ParseAndLoadIndexFromExam(row);
                    nbIndexProcessed += indexFromExam;
                }
                catch (Exception ex)
                {
                    nbErrors++;
                    LogWarning($"Erreur examen {row["ROW_ID"]}: {ex.Message}");
                }
            }

            LogInfo($"Traitement termin� - Index charg�s: {nbIndexProcessed}, Erreurs: {nbErrors}");
        }

        /// <summary>
        /// Parse un examen et charge tous ses index partie-question dans TW_INDEX_PARTIE
        /// Navigation: examParts[] ? partIndexes[]
        /// </summary>
        private int ParseAndLoadIndexFromExam(DataRow sourceRow)
        {
            string rowId = sourceRow["ROW_ID"].ToString();
            string jsonExamRaw = sourceRow["JSON_EXAM"].ToString();

            // Parser le JSON
            JObject jsonWrapper = JObject.Parse(jsonExamRaw);
            JObject respBody = (JObject)jsonWrapper["ListOfRes107TestWeAPI:res"]["Resp200"]["RespBody"][0];

            // R�cup�rer le tableau examParts
            JArray examParts = (JArray)respBody["examParts"];

            if (examParts == null || examParts.Count == 0)
            {
                LogWarning($"ROW_ID {rowId}: Aucune partie trouv�e");
                return 0;
            }

            int nbIndex = 0;

            // Boucler sur chaque partie
            foreach (JObject part in examParts)
            {
                // ID de la partie (parent)
                string partId = part["id"]?.ToString();

                // R�cup�rer partIndexes
                JArray partIndexes = (JArray)part["partIndexes"];

                if (partIndexes == null || partIndexes.Count == 0)
                    continue;

                // Boucler sur chaque partIndex
                int position = 0;
                foreach (JObject partIndex in partIndexes)
                {
                    // R�cup�rer le tableau question (normalement 1 �l�ment)
                    JArray questions = (JArray)partIndex["question"];

                    if (questions == null || questions.Count == 0)
                    {
                        position++;
                        continue;
                    }

                    // Premier �l�ment question (devrait �tre le seul)
                    JObject question = (JObject)questions[0];
                    string questionId = question["id"]?.ToString();

                    // Type d'index (si existe dans JSON)
                    string indexType = partIndex["type"]?.ToString();

                    // Ins�rer l'index
                    InsertIndex(partId, questionId, position, indexType);

                    nbIndex++;
                    position++;
                }
            }

            if (nbIndex > 0)
                LogInfo($"ROW_ID {rowId}: {nbIndex} index charg�(s)");

            return nbIndex;
        }

        /// <summary>
        /// Ins�re un index partie-question dans TW_INDEX_PARTIE
        /// </summary>
        private void InsertIndex(string partId, string questionId, int position, string indexType)
        {
            // G�n�rer UUID pour l'index
            string indexId = Guid.NewGuid().ToString();

            // Lot ETL
            string lotETL = "RIPQUAR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // INSERT dans la table
            string connString = BuildDestinationConnectionString();

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();

                string insertQuery = @"
                    USE [SDGIC01];
                    INSERT INTO [STG].[TW_INDEX_PARTIE]
                    (
                        TW_N_INDX_ID,
                        TW_N_PART_ID,
                        TW_N_QUES_ID,
                        TW_N_POSI,
                        TW_C_TYPE,
                        TW_DH_CHARG_ETL,
                        TW_C_SYST_SRCE,
                        TW_C_LOT_ETL
                    )
                    VALUES
                    (
                        @IndexId,
                        @PartId,
                        @QuestionId,
                        @Position,
                        @IndexType,
                        GETDATE(),
                        'TestWe',
                        @LotETL
                    );
                ";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@IndexId", indexId);
                    cmd.Parameters.AddWithValue("@PartId", partId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@QuestionId", questionId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Position", position);
                    cmd.Parameters.AddWithValue("@IndexType", indexType ?? (object)DBNull.Value);
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