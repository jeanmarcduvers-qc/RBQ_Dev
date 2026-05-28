/*
 * RIPQUAR - Script Task TW_CLASSE
 * Parse JSON RespBody.classes[] ? STG.TW_CLASSE (avec D�DUPLICATION)
 * 
 * Strategie: Extraire toutes les classes de tous les examens, dedupliquer, charger 1x
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
#endregion

namespace ST_Load_TW_CLASSE
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
            Dts.Events.FireInformation(0, "TW_CLASSE", message, "", 0, ref fireAgain);
        }

        private void LogWarning(string message)
        {
            bool fireAgain = false;
            Dts.Events.FireWarning(0, "TW_CLASSE", message, "", 0);
        }

        private void LogError(string message)
        {
            Dts.Events.FireError(0, "TW_CLASSE", message, "", 0);
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

        #region Logique Sp�cifique TW_CLASSE avec D�duplication

        private void LoadTable()
        {
            // 1. Vider la table
            ClearTable("[SDGIC01].[STG].[TW_CLASSE]");

            // 2. R�cup�rer les donn�es sources
            DataTable dtSource = GetExamSourceData();

            // 3. Extraire toutes les classes avec D�DUPLICATION
            HashSet<string> uniqueClasses = new HashSet<string>();
            int nbExamsProcessed = 0;
            int nbErrors = 0;

            LogInfo("Extraction des classes uniques...");

            foreach (DataRow row in dtSource.Rows)
            {
                try
                {
                    ExtractClassesFromExam(row, uniqueClasses);
                    nbExamsProcessed++;
                }
                catch (Exception ex)
                {
                    nbErrors++;
                    LogWarning($"Erreur examen {row["ROW_ID"]}: {ex.Message}");
                }
            }

            LogInfo($"{nbExamsProcessed} examen(s) trait�(s), {uniqueClasses.Count} classe(s) unique(s) trouv�e(s)");

            // 4. Charger les classes uniques
            int nbClassesLoaded = 0;
            foreach (string className in uniqueClasses)
            {
                try
                {
                    InsertClasse(className);
                    nbClassesLoaded++;
                }
                catch (Exception ex)
                {
                    LogWarning($"Erreur insertion classe '{className}': {ex.Message}");
                }
            }

            LogInfo($"Traitement termin� - Classes charg�es: {nbClassesLoaded}, Erreurs: {nbErrors}");
        }

        /// <summary>
        /// Extrait les classes d'un examen et les ajoute au HashSet (d�duplication automatique)
        /// </summary>
        private void ExtractClassesFromExam(DataRow sourceRow, HashSet<string> uniqueClasses)
        {
            string rowId = sourceRow["ROW_ID"].ToString();
            string jsonExamRaw = sourceRow["JSON_EXAM"].ToString();

            // Parser le JSON
            JObject jsonWrapper = JObject.Parse(jsonExamRaw);
            JObject respBody = (JObject)jsonWrapper["ListOfRes107TestWeAPI:res"]["Resp200"]["RespBody"][0];

            // R�cup�rer le tableau classes
            JArray classes = (JArray)respBody["classes"];

            if (classes == null || classes.Count == 0)
            {
                return; // Pas de classes pour cet examen
            }

            // Extraire les noms de classes
            foreach (JObject classObj in classes)
            {
                string className = classObj["name"]?.ToString();

                if (!string.IsNullOrEmpty(className))
                {
                    uniqueClasses.Add(className); // HashSet d�duplique automatiquement
                }
            }
        }

        /// <summary>
        /// Ins�re une classe unique dans TW_CLASSE
        /// </summary>
        private void InsertClasse(string className)
        {
            // G�n�rer UUID pour la classe
            string classeId = Guid.NewGuid().ToString();

            // Lot ETL
            string lotETL = "RIPQUAR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // INSERT dans la table
            string connString = BuildDestinationConnectionString();

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();

                string insertQuery = @"
                    USE [SDGIC01];
                    INSERT INTO [STG].[TW_CLASSE]
                    (
                        TW_N_CLAS_ID,
                        TW_NM_CLAS,
                        TW_DH_CHARG_ETL,
                        TW_C_SYST_SRCE,
                        TW_C_LOT_ETL
                    )
                    VALUES
                    (
                        @ClasseId,
                        @ClassName,
                        GETDATE(),
                        'TestWe',
                        @LotETL
                    );
                ";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@ClasseId", classeId);
                    cmd.Parameters.AddWithValue("@ClassName", className ?? (object)DBNull.Value);
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