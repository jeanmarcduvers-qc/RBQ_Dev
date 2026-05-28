/*
 * RIPQUAR - Script Task TW_CHOIX
 * Parse JSON question[].questionBank[].choices[] ? STG.TW_CHOIX
 * 
 * Variables SSIS requises:
 * - ServerSource, BDSource, ServerDest, BDDest
 * 
 * NOTE: TW_DE_CHOI = varchar(1) - Premier caract�re seulement!
 */

#region Namespaces
using System;
using System.Data;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using Newtonsoft.Json.Linq;
#endregion

namespace ST_Load_TW_CHOIX
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
            Dts.Events.FireInformation(0, "TW_CHOIX", message, "", 0, ref fireAgain);
        }

        private void LogWarning(string message)
        {
            bool fireAgain = false;
            Dts.Events.FireWarning(0, "TW_CHOIX", message, "", 0);
        }

        private void LogError(string message)
        {
            Dts.Events.FireError(0, "TW_CHOIX", message, "", 0);
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

        #region Logique Sp�cifique TW_CHOIX

        private void LoadTable()
        {
            // 1. Vider la table
            ClearTable("[SDGIC01].[STG].[TW_CHOIX]");

            // 2. R�cup�rer les donn�es sources
            DataTable dtSource = GetExamSourceData();

            // 3. Parser et charger
            int nbChoixProcessed = 0;
            int nbErrors = 0;

            foreach (DataRow row in dtSource.Rows)
            {
                try
                {
                    int choixFromExam = ParseAndLoadChoixFromExam(row);
                    nbChoixProcessed += choixFromExam;
                }
                catch (Exception ex)
                {
                    nbErrors++;
                    LogWarning($"Erreur examen {row["ROW_ID"]}: {ex.Message}");
                }
            }

            LogInfo($"Traitement termin� - Choix charg�s: {nbChoixProcessed}, Erreurs: {nbErrors}");
        }

        /// <summary>
        /// Parse un examen et charge tous ses choix dans TW_CHOIX
        /// Navigation: examParts[] ? partIndexes[] ? question[] ? questionBank[] ? choices[]
        /// </summary>
        private int ParseAndLoadChoixFromExam(DataRow sourceRow)
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

            int nbChoix = 0;

            // Boucle 1: examParts
            foreach (JObject part in examParts)
            {
                JArray partIndexes = (JArray)part["partIndexes"];
                if (partIndexes == null || partIndexes.Count == 0)
                    continue;

                // Boucle 2: partIndexes
                foreach (JObject partIndex in partIndexes)
                {
                    JArray questions = (JArray)partIndex["question"];
                    if (questions == null || questions.Count == 0)
                        continue;

                    // Boucle 3: questions
                    foreach (JObject question in questions)
                    {
                        string questionId = question["id"]?.ToString();

                        JArray questionBank = (JArray)question["questionBank"];
                        if (questionBank == null || questionBank.Count == 0)
                            continue;

                        // Boucle 4: questionBank (normalement 1 �l�ment)
                        foreach (JObject qBank in questionBank)
                        {
                            JArray choices = (JArray)qBank["choices"];
                            if (choices == null || choices.Count == 0)
                                continue;

                            // Boucle 5: choices
                            int position = 0;
                            foreach (JObject choice in choices)
                            {
                                InsertChoix(questionId, choice, position);
                                nbChoix++;
                                position++;
                            }
                        }
                    }
                }
            }

            if (nbChoix > 0)
                LogInfo($"ROW_ID {rowId}: {nbChoix} choix charg�(s)");

            return nbChoix;
        }

        /// <summary>
        /// Ins�re un choix dans TW_CHOIX
        /// </summary>
        private void InsertChoix(string questionId, JObject choice, int position)
        {
            // Extraire les champs
            string choiceId = choice["id"]?.ToString();
            string choiceName = choice["name"]?.ToString();
            bool? isCorrect = choice["correct"]?.ToString().ToLower() == "true";
            int? choicePosition = choice["position"] != null ? int.Parse(choice["position"].ToString()) : position;

            // TW_DE_CHOI = varchar(1) - Premier caract�re seulement!
            string deChoix = null;
            if (!string.IsNullOrEmpty(choiceName))
            {
                deChoix = choiceName.Substring(0, 1);
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
                    INSERT INTO [STG].[TW_CHOIX]
                    (
                        TW_N_CHOI_ID,
                        TW_N_FK_QUES_ID,
                        TW_DE_CHOI,
                        TW_I_CORR,
                        TW_N_POSI,
                        TW_DH_CHARG_ETL,
                        TW_C_SYST_SRCE,
                        TW_C_LOT_ETL
                    )
                    VALUES
                    (
                        @ChoiceId,
                        @QuestionIdFK,
                        @DeChoix,
                        @IsCorrect,
                        @Position,
                        GETDATE(),
                        'TestWe',
                        @LotETL
                    );
                ";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@ChoiceId", choiceId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@QuestionIdFK", questionId ?? (object)DBNull.Value); // M�me valeur
                    cmd.Parameters.AddWithValue("@DeChoix", deChoix ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@IsCorrect", isCorrect ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Position", choicePosition ?? (object)DBNull.Value);
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