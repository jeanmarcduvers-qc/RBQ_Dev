/*
 * RIPQUAR - POC Script Task avec Variables SSIS
 * Parse JSON TestWe → STG_TW_EXAMEN
 * 
 * Variables SSIS requises:
 * - ServerSource (String): Serveur source GIC/Siebel
 * - BDSource (String): Base de données source
 * - ServerDest (String): Serveur destination InfoGestion
 * - BDDest (String): Base de données destination
 */

#region Namespaces
using System;
using System.Data;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using Newtonsoft.Json.Linq;
#endregion

namespace ST_ParseJSONTestWe
{
    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {
        public void Main()
        {
            bool fireAgain = false;

            try
            {
                Dts.Events.FireInformation(0, "POC Parse JSON", "Démarrage du POC...", "", 0, ref fireAgain);

                // Afficher les serveurs utilisés (pour debug)
                string serverSource = Dts.Variables["User::ServerSource"].Value.ToString();
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                Dts.Events.FireInformation(0, "POC Parse JSON",
                    $"Source: {serverSource} → Destination: {serverDest}", "", 0, ref fireAgain);

                // 1. Nettoyer la table staging
                TruncateStagingTable();

                // 2. Récupérer les données sources avec JSON
                DataTable dtSource = GetSourceData();
                Dts.Events.FireInformation(0, "POC Parse JSON",
                    $"Récupéré {dtSource.Rows.Count} ligne(s) source", "", 0, ref fireAgain);

                // 3. Parser et charger
                int nbProcessed = 0;
                int nbErrors = 0;

                foreach (DataRow row in dtSource.Rows)
                {
                    try
                    {
                        ParseAndLoadExam(row);
                        nbProcessed++;
                    }
                    catch (Exception ex)
                    {
                        nbErrors++;
                        Dts.Events.FireWarning(0, "POC Parse JSON",
                            $"Erreur ligne {row["ROW_ID"]}: {ex.Message}", "", 0);
                    }
                }

                Dts.Events.FireInformation(0, "POC Parse JSON",
                    $"Traitement terminé - Succès: {nbProcessed}, Erreurs: {nbErrors}", "", 0, ref fireAgain);

                Dts.TaskResult = (int)ScriptResults.Success;
            }
            catch (Exception ex)
            {
                Dts.Events.FireError(0, "POC Parse JSON", ex.Message, "", 0);
                Dts.TaskResult = (int)ScriptResults.Failure;
            }
        }

        /// <summary>
        /// Construit la connection string SOURCE depuis les variables SSIS
        /// </summary>
        private string BuildSourceConnectionString()
        {
            string server = Dts.Variables["User::ServerSource"].Value.ToString();
            string database = Dts.Variables["User::BDSource"].Value.ToString();

            // Connection string avec Windows Authentication
            return $"Data Source={server};Initial Catalog={database};Integrated Security=True;";
        }

        /// <summary>
        /// Construit la connection string DESTINATION depuis les variables SSIS
        /// </summary>
        private string BuildDestinationConnectionString()
        {
            string server = Dts.Variables["User::ServerDest"].Value.ToString();
            string database = Dts.Variables["User::BDDest"].Value.ToString();

            // Connection string avec Windows Authentication
            return $"Data Source={server};Initial Catalog={database};Integrated Security=True;";
        }

        /// <summary>
        /// Vide la table staging avant chargement
        /// </summary>
        private void TruncateStagingTable()
        {
            bool fireAgain = false;
            string connString = BuildDestinationConnectionString();

            Dts.Events.FireInformation(0, "POC Parse JSON",
                "Nettoyage table staging...", "", 0, ref fireAgain);

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();

                // Utiliser DELETE au lieu de TRUNCATE (fonctionne avec syntaxe 3 parties)
                using (SqlCommand cmd = new SqlCommand("DELETE FROM [SDGIC01].[STG].[TW_EXAMEN]", conn))
                {
                    int rowsDeleted = cmd.ExecuteNonQuery();
                    Dts.Events.FireInformation(0, "POC Parse JSON",
                        $"{rowsDeleted} ligne(s) supprimée(s)", "", 0, ref fireAgain);
                }
            }
        }

        /// <summary>
        /// Récupère les données sources (GIC + JSON TestWe)
        /// </summary>
        private DataTable GetSourceData()
        {
            DataTable dt = new DataTable();
            string connString = BuildSourceConnectionString();

            // Requête pour récupérer les examens avec JSON
            string query = @"
                SELECT
                    ROW_ID,
                    PAR_ROW_ID__CXEXAMEN,
                    PAR_ROW_ID__SCONTACT,
                    X_GI_V_NOTE_INIT_EXAM,
                    X_GI_D_CORR_EXAM,
                    CONVERT(NVARCHAR(MAX), X_GI_V_VECT_EXAM) AS JSON_EXAM,
                    CONVERT(NVARCHAR(MAX), X_GI_V_VECT_CORR_EXAM) AS JSON_CORR,
                    CREATED,
                    LAST_UPD
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
                    cmd.CommandTimeout = 300; // 5 minutes
                    using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                    {
                        adapter.Fill(dt);
                    }
                }
            }

            return dt;
        }

        /// <summary>
        /// Parse une ligne source et charge dans STG_TW_EXAMEN
        /// </summary>
        private void ParseAndLoadExam(DataRow sourceRow)
        {
            // 1. Extraire les métadonnées GIC
            string rowId = sourceRow["ROW_ID"].ToString();
            string jsonExamRaw = sourceRow["JSON_EXAM"].ToString();

            // LOG: Taille du JSON
            bool fireAgain = false;
            Dts.Events.FireInformation(0, "POC Parse JSON",
                $"ROW_ID {rowId}: JSON taille = {jsonExamRaw.Length:N0} caractères", "", 0, ref fireAgain);

            // 2. Parser le JSON (Wrapper Siebel)
            JObject jsonWrapper = JObject.Parse(jsonExamRaw);

            // 3. Naviguer vers RespBody (le vrai contenu TestWe)
            JObject respBody = (JObject)jsonWrapper["ListOfRes107TestWeAPI:res"]["Resp200"]["RespBody"][0];

            // 4. Extraire les champs pour STG_TW_EXAMEN
            string examId = respBody["id"]?.ToString();
            string publicKey = respBody["publicKey"]?.ToString();
            int? duration = respBody["duration"] != null ? int.Parse(respBody["duration"].ToString()) : (int?)null;
            decimal? maxPoints = respBody["maxPoints"] != null ? decimal.Parse(respBody["maxPoints"].ToString()) : (decimal?)null;
            DateTime? createdAt = ParseTestWeDate(respBody["createdAt"]?.ToString());
            DateTime? validatedAt = ParseTestWeDate(respBody["validatedAt"]?.ToString());
            DateTime? updatedAt = ParseTestWeDate(respBody["updatedAt"]?.ToString());
            int? nbStudents = respBody["nbStudents"] != null ? int.Parse(respBody["nbStudents"].ToString()) : (int?)null;
            int? nbCopiesReceived = respBody["nbCopiesReceived"] != null ? int.Parse(respBody["nbCopiesReceived"].ToString()) : (int?)null;
            int? nbCopiesCorrected = respBody["nbCopiesCorrected"] != null ? int.Parse(respBody["nbCopiesCorrected"].ToString()) : (int?)null;
            string timezone = respBody["timezone"]?.ToString();

            // NOUVELLES COLONNES AJOUTÉES
            // Status de l'examen
            string status = respBody["status"]?.ToString();

            // Code public (peut être différent de publicKey, à vérifier)
            string codePublic = respBody["examCode"]?.ToString() ?? respBody["accessCode"]?.ToString() ?? publicKey;

            // Questions aléatoires (depuis examParts)
            bool? questionsAleatoires = null;
            bool? choixAleatoires = null;
            JArray examParts = (JArray)respBody["examParts"];
            if (examParts != null && examParts.Count > 0)
            {
                JObject firstPart = (JObject)examParts[0];
                questionsAleatoires = firstPart["randomPartIndexes"]?.ToString().ToLower() == "true";
                choixAleatoires = firstPart["randomizeChoices"]?.ToString().ToLower() == "true"
                               || firstPart["shuffleAnswers"]?.ToString().ToLower() == "true";
            }

            // Lot ETL (identifiant du run)
            string lotETL = "RIPQUAR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // 5. Insérer dans STG_TW_EXAMEN
            string connString = BuildDestinationConnectionString();

            using (SqlConnection conn = new SqlConnection(connString))
            {
                conn.Open();

                string insertQuery = @"
                    USE [SDGIC01];
                    INSERT INTO [STG].[TW_EXAMEN]
                    (
                        TW_N_EXAM_ID,
                        TW_C_EXAM,
                        TW_Q_DURE_SEC,
                        TW_Q_PTS_MAX,
                        TW_DH_DEBU,
                        TW_DH_FIN,
                        TW_DH_MODI,
                        TW_Q_NB_ETUD,
                        TW_Q_NB_COPI_RECU,
                        TW_Q_NB_COPI_CORR,
                        TW_C_FUSE_HORA,
                        TW_DH_CREA,
                        TW_DH_VALI,
                        TW_DH_CHARG_ETL,
                        TW_C_SYST_SRCE,
                        TW_C_STAT,
                        TW_C_CODE_PUBL,
                        TW_I_QUES_ALEA,
                        TW_I_CHOI_ALEA,
                        TW_C_LOT_ETL
                    )
                    VALUES
                    (
                        @ExamId,
                        @PublicKey,
                        @Duration,
                        @MaxPoints,
                        @CreatedAt,
                        @ValidatedAt,
                        @UpdatedAt,
                        @NbStudents,
                        @NbCopiesReceived,
                        @NbCopiesCorrected,
                        @Timezone,
                        @CreatedAt,
                        @ValidatedAt,
                        GETDATE(),
                        'TestWe',
                        @Status,
                        @CodePublic,
                        @QuestionsAleatoires,
                        @ChoixAleatoires,
                        @LotETL
                    );
                ";

                using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@ExamId", examId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@PublicKey", publicKey ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Duration", duration ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@MaxPoints", maxPoints ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@CreatedAt", createdAt ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ValidatedAt", validatedAt ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@UpdatedAt", updatedAt ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NbStudents", nbStudents ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NbCopiesReceived", nbCopiesReceived ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@NbCopiesCorrected", nbCopiesCorrected ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Timezone", timezone ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Status", status ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@CodePublic", codePublic ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@QuestionsAleatoires", questionsAleatoires ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@ChoixAleatoires", choixAleatoires ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@LotETL", lotETL ?? (object)DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Parse les dates TestWe format "MM/dd/yyyy HH:mm:ss"
        /// </summary>
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

        #region ScriptResults declaration
        enum ScriptResults
        {
            Success = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Success,
            Failure = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Failure
        };
        #endregion
    }
}