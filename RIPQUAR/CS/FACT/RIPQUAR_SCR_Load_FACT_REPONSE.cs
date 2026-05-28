/*
 * ═══════════════════════════════════════════════════════════════════════════
 * RIPQUAR - FACT_REPONSE_QUESTION
 * ═══════════════════════════════════════════════════════════════════════════
 * PARTIE 1/4
 * 
 * Sources:
 *   - STG_TW_REPONSE (réponses données)
 *   - STG_TW_CORRECTION (points obtenus)
 *   - FACT_RESULTAT_EXAMEN (lookup FK résultat)
 * 
 * Transformations:
 *   - UUID extraction: RIGHT(TW_C_URI_QUES, 36)
 *   - JSON parsing: Position premier choix
 *   - Position → Lettre (0→A, 1→B, 2→C, 3→D)
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
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endregion

namespace ST_FACT_REPONSE_QUESTION
{
    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {
        private string logFile = @"C:\Temp\FACT_REPONSE_QUESTION_LOG.txt";

        public void Main()
        {
            try
            {
                File.WriteAllText(logFile, $"=== DÉBUT FACT_REPONSE_QUESTION - {DateTime.Now} ===\n\n");
                Log("Script démarre");

                // ═══════════════════════════════════════════════════════════════
                // 1. VARIABLES SSIS
                // ═══════════════════════════════════════════════════════════════
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                string bdDest = Dts.Variables["User::BDDest"].Value.ToString();

                Log($"Serveur: {serverDest} / Base: {bdDest}");

                string connStr = $"Data Source={serverDest};Initial Catalog={bdDest};Integrated Security=True;";

                // ═══════════════════════════════════════════════════════════════
                // 2. LOOKUPS EN MÉMOIRE
                // ═══════════════════════════════════════════════════════════════
                Log("Chargement lookups...");

                Dictionary<string, ResultatLookup> lkpResultats = new Dictionary<string, ResultatLookup>();
                Dictionary<string, int> lkpQuestions = new Dictionary<string, int>();
                Dictionary<int, int> lkpDates = new Dictionary<int, int>();

                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    // Lookup FACT_RESULTAT_EXAMEN
                    string sqlRes = @"
                        SELECT 
                            TW_N_COPI_ID,
                            DW_N_RESU_ID,
                            DW_N_CAND_ID,
                            DW_N_EXAM_ID,
                            DW_DH_CORR
                        FROM [DWH].[FACT_RESULTAT_EXAMEN];";

                    using (SqlCommand cmd = new SqlCommand(sqlRes, conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string copieId = rdr.IsDBNull(0) ? null : rdr.GetString(0);
                            if (!string.IsNullOrEmpty(copieId))
                            {
                                lkpResultats[copieId] = new ResultatLookup
                                {
                                    ResuSK = rdr.GetInt32(1),
                                    CandSK = rdr.GetInt32(2),
                                    ExamSK = rdr.GetInt32(3),
                                    DateCorr = rdr.IsDBNull(4) ? (DateTime?)null : rdr.GetDateTime(4)
                                };
                            }
                        }
                    }

                    // Lookup DIM_QUESTION (via UUID banque)
                    string sqlQues = @"
                        SELECT 
                            TW_N_UUID_BANQ_QUES, 
                            DW_N_QUES_ID 
                        FROM [DWH].[DIM_QUESTION] 
                        WHERE DW_I_COUR = 1;";

                    using (SqlCommand cmd = new SqlCommand(sqlQues, conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string uuid = rdr.IsDBNull(0) ? null : rdr.GetString(0);
                            if (!string.IsNullOrEmpty(uuid))
                                lkpQuestions[uuid] = rdr.GetInt32(1);
                        }
                    }

                    // Lookup DIM_DATE (YYYYMMDD)
                    string sqlDate = "SELECT DW_N_DATE_ID FROM [DWH].[DIM_DATE];";
                    using (SqlCommand cmd = new SqlCommand(sqlDate, conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            int dateId = rdr.GetInt32(0);
                            lkpDates[dateId] = dateId;
                        }
                    }
                }

                Log($"✓ Lookups: {lkpResultats.Count} résultats, {lkpQuestions.Count} questions, {lkpDates.Count} dates");

                if (lkpResultats.Count == 0)
                {
                    Log("ERREUR: FACT_RESULTAT_EXAMEN est vide! Charger FACT_RESULTAT_EXAMEN d'abord.");
                    MessageBox.Show("FACT_RESULTAT_EXAMEN doit être chargée AVANT FACT_REPONSE_QUESTION!");
                    Dts.TaskResult = (int)ScriptResults.Failure;
                    return;
                }

                // ══════════════════════════════════════════════════════════════════════════════
                // FIN PARTIE 1/4 - Continuer avec PART2
                // ══════════════════════════════════════════════════════════════════════════════

                // ══════════════════════════════════════════════════════════════════════════════
                // PARTIE 2/4 - DELETE FACT et lecture sources
                // ══════════════════════════════════════════════════════════════════════════════

                // ═══════════════════════════════════════════════════════════════
                // 3. DELETE FACT (pas TRUNCATE - permissions)
                // ═══════════════════════════════════════════════════════════════
                Log("DELETE FACT_REPONSE_QUESTION...");
                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();
                    new SqlCommand("DELETE FROM [SDGIC01].[DWH].[FACT_REPONSE_QUESTION];", conn).ExecuteNonQuery();
                }

                // ═══════════════════════════════════════════════════════════════
                // 4. LIRE SOURCES (STG_TW_REPONSE + STG_TW_CORRECTION)
                // ═══════════════════════════════════════════════════════════════
                Log("Lecture sources STG...");

                List<ReponseData> reponses = new List<ReponseData>();

                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    string queryMain = $@"
                        SELECT 
                            r.TW_C_REPN_ID,
                            r.TW_N_COPI_ID,
                            r.TW_C_URI_QUES,
                            r.TW_DE_REPN_TEXT,
                            r.TW_DE_CHOI_JSON,
                            c.TW_M_NOTE
                        FROM [{bdDest}].[STG].[TW_REPONSE] r
                        LEFT JOIN [{bdDest}].[STG].[TW_CORRECTION] c 
                            ON r.TW_C_REPN_ID = c.TW_C_REPN_ID
                        WHERE r.TW_C_REPN_ID IS NOT NULL;";

                    using (SqlCommand cmd = new SqlCommand(queryMain, conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            reponses.Add(new ReponseData
                            {
                                ReponseId = rdr.GetString(0),
                                CopieId = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                                URI_Question = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                                ReponseTexte = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                                ChoixJSON = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                                Points = rdr.IsDBNull(5) ? (decimal?)null : rdr.GetDecimal(5)
                            });
                        }
                    }
                }

                Log($"✓ {reponses.Count} réponses lues");

                // ══════════════════════════════════════════════════════════════════════════════
                // FIN PARTIE 2/4 - Continuer avec PART3
                // ══════════════════════════════════════════════════════════════════════════════

                // ══════════════════════════════════════════════════════════════════════════════
                // PARTIE 3/4 - Insertion avec transformations et calculs
                // ══════════════════════════════════════════════════════════════════════════════

                // ═══════════════════════════════════════════════════════════════
                // 5. INSERTION DANS FACT
                // ═══════════════════════════════════════════════════════════════
                Log("Insertion FACT_REPONSE_QUESTION...");

                int inserted = 0, skipped = 0;

                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    string insertSql = $@"
                        INSERT INTO [{bdDest}].[DWH].[FACT_REPONSE_QUESTION]
                        (
                            DW_N_CAND_ID,
                            DW_N_RESU_ID,
                            DW_N_EXAM_ID,
                            DW_N_QUES_ID,
                            DW_N_DATE_REPN_ID,
                            TW_C_URI_REPN,
                            DW_M_PTS_OBTE,
                            DW_I_CORR,
                            DW_N_POSI_CHOI_DONN,
                            DW_C_LIBE_CHOI_DONN,
                            DW_I_REPN,
                            DW_I_AUCU_REPN,
                            DW_I_REPN_PLUS_CHOI,
                            DW_DH_CHARG_ETL,
                            DW_C_SYST_SRCE
                        )
                        VALUES
                        (
                            @CandSK, @ResuSK, @ExamSK, @QuesSK, @DateSK,
                            @ReponseId, @Points, @Correct,
                            @Position, @Lettre,
                            @ARepondu, @AucuneReponse, @ChoixMultiples,
                            GETDATE(), 'TestWe'
                        );";

                    foreach (var rep in reponses)
                    {
                        try
                        {
                            // FK Resultat (via TW_N_COPI_ID)
                            if (string.IsNullOrEmpty(rep.CopieId) || !lkpResultats.ContainsKey(rep.CopieId))
                            {
                                skipped++;
                                continue;
                            }
                            var resInfo = lkpResultats[rep.CopieId];

                            // Extraction UUID (RIGHT 36 caractères)
                            string uuid = null;
                            if (!string.IsNullOrEmpty(rep.URI_Question) && rep.URI_Question.Length >= 36)
                            {
                                uuid = rep.URI_Question.Substring(rep.URI_Question.Length - 36);
                            }

                            // FK Question (via UUID)
                            if (string.IsNullOrEmpty(uuid) || !lkpQuestions.ContainsKey(uuid))
                            {
                                skipped++;
                                Log($"SKIP {rep.ReponseId}: UUID {uuid} introuvable");
                                continue;
                            }
                            int quesSK = lkpQuestions[uuid];

                            // FK Date (réutiliser date correction du résultat)
                            int? dateSK = null;
                            if (resInfo.DateCorr.HasValue)
                            {
                                int dateKey = ToYYYYMMDD(resInfo.DateCorr.Value);
                                dateSK = lkpDates.ContainsKey(dateKey) ? lkpDates[dateKey] : 19000101;
                            }
                            else
                            {
                                dateSK = 19000101;
                            }

                            // PARSE JSON: Extraire position
                            int? position = null;
                            int nbChoix = 0;

                            if (!string.IsNullOrEmpty(rep.ChoixJSON))
                            {
                                try
                                {
                                    var choixArray = JArray.Parse(rep.ChoixJSON);
                                    nbChoix = choixArray.Count;

                                    if (nbChoix > 0)
                                    {
                                        var premierChoix = choixArray[0];
                                        if (premierChoix["position"] != null)
                                        {
                                            position = premierChoix["position"].Value<int>();
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log($"WARN {rep.ReponseId}: JSON malformed - {ex.Message}");
                                }
                            }

                            // Position → Lettre (0→A, 1→B, 2→C, 3→D)
                            string lettre = null;
                            if (position.HasValue)
                            {
                                lettre = ConvertToLetter(position.Value);
                            }

                            // CALCULS FLAGS
                            bool correct = rep.Points.HasValue && rep.Points.Value > 0;
                            bool aRepondu = true; // Toujours 1 si ligne existe
                            bool aucuneReponse = string.IsNullOrEmpty(rep.ChoixJSON) && string.IsNullOrEmpty(rep.ReponseTexte);
                            bool choixMultiples = nbChoix > 1;

                            using (SqlCommand cmd = new SqlCommand(insertSql, conn))
                            {
                                cmd.Parameters.AddWithValue("@CandSK", resInfo.CandSK);
                                cmd.Parameters.AddWithValue("@ResuSK", resInfo.ResuSK);
                                cmd.Parameters.AddWithValue("@ExamSK", resInfo.ExamSK);
                                cmd.Parameters.AddWithValue("@QuesSK", quesSK);
                                cmd.Parameters.AddWithValue("@DateSK", dateSK ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@ReponseId", rep.ReponseId);
                                cmd.Parameters.AddWithValue("@Points", rep.Points ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@Correct", correct);
                                cmd.Parameters.AddWithValue("@Position", position ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@Lettre", lettre ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@ARepondu", aRepondu);
                                cmd.Parameters.AddWithValue("@AucuneReponse", aucuneReponse);
                                cmd.Parameters.AddWithValue("@ChoixMultiples", choixMultiples);

                                cmd.ExecuteNonQuery();
                                inserted++;
                            }

                            if (inserted % 100 == 0)
                                Log($"Inséré: {inserted}...");
                        }
                        catch (Exception ex)
                        {
                            Log($"Erreur {rep.ReponseId}: {ex.Message}");
                            skipped++;
                        }
                    }
                }

                // ═══════════════════════════════════════════════════════════════
                // 6. STATS
                // ═══════════════════════════════════════════════════════════════
                Log("");
                Log("═══════════════════════════════════════════════════════════");
                Log($"TOTAL: {reponses.Count} | Insérées: {inserted} | Ignorées: {skipped}");
                Log("═══════════════════════════════════════════════════════════");
                Log("=== SUCCÈS ===");

                Dts.TaskResult = (int)ScriptResults.Success;
            }
            catch (Exception ex)
            {
                string msg = $"ERREUR:\n{ex.Message}\n\n{ex.StackTrace}";
                Log(msg);
                MessageBox.Show(msg);
                Dts.TaskResult = (int)ScriptResults.Failure;
            }
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // FIN PARTIE 3/4 - Continuer avec PART4 (méthodes et classes)
        // ══════════════════════════════════════════════════════════════════════════════
        // ══════════════════════════════════════════════════════════════════════════════
        // PARTIE 4/4 - Méthodes et classes
        // ══════════════════════════════════════════════════════════════════════════════

        private int ToYYYYMMDD(DateTime date)
        {
            return date.Year * 10000 + date.Month * 100 + date.Day;
        }

        private string ConvertToLetter(int position)
        {
            // 0→A, 1→B, 2→C, 3→D, etc.
            if (position >= 0 && position < 26)
                return ((char)('A' + position)).ToString();
            return null;
        }

        private void Log(string msg)
        {
            try
            {
                File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            }
            catch { }
        }

        enum ScriptResults
        {
            Success = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Success,
            Failure = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Failure
        }
    }

    public class ReponseData
    {
        public string ReponseId { get; set; }
        public string CopieId { get; set; }
        public string URI_Question { get; set; }
        public string ReponseTexte { get; set; }
        public string ChoixJSON { get; set; }
        public decimal? Points { get; set; }
    }

    public class ResultatLookup
    {
        public int ResuSK { get; set; }
        public int CandSK { get; set; }
        public int ExamSK { get; set; }
        public DateTime? DateCorr { get; set; }
    }
}