/*
 * ═══════════════════════════════════════════════════════════════════════════
 * RIPQUAR - FACT_RESULTAT_EXAMEN - Version 2 connexions (Bronze + Silver/Gold)
 * ═══════════════════════════════════════════════════════════════════════════
 * PARTIE 1/4
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
#endregion

namespace ST_FACT_RESULTAT_EXAMEN_V3
{
    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {
        private string logFile = @"C:\Temp\FACT_RESULTAT_EXAMEN_LOG.txt";

        public void Main()
        {
            try
            {
                File.WriteAllText(logFile, $"=== DÉBUT FACT_RESULTAT_EXAMEN - {DateTime.Now} ===\n\n");
                Log("Script démarre - Version 2 connexions (Bronze + Silver/Gold)");

                // ═══════════════════════════════════════════════════════════════
                // 1. VARIABLES SSIS
                // ═══════════════════════════════════════════════════════════════
                string serverSource = Dts.Variables["User::ServerSource"].Value.ToString();
                string bdSource = Dts.Variables["User::BDSource"].Value.ToString();
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                string bdDest = Dts.Variables["User::BDDest"].Value.ToString();

                Log($"Bronze: {serverSource} / {bdSource}");
                Log($"Silver/Gold: {serverDest} / {bdDest}");

                string connStrBronze = $"Data Source={serverSource};Initial Catalog={bdSource};Integrated Security=True;";
                string connStrDest = $"Data Source={serverDest};Initial Catalog={bdDest};Integrated Security=True;";

                // ═══════════════════════════════════════════════════════════════
                // 2. CHARGER BRONZE CX_INSC_EXAM EN MÉMOIRE
                // ═══════════════════════════════════════════════════════════════
                Log("Chargement Bronze CX_INSC_EXAM...");

                Dictionary<string, BronzeInscription> lkpBronze = new Dictionary<string, BronzeInscription>();

                using (SqlConnection connBronze = new SqlConnection(connStrBronze))
                {
                    connBronze.Open();

                    string sqlBronze = @"
                        SELECT 
                            ROW_ID,
                            X_GI_I_REPR_EXAM
                        FROM [dbo].[CX_INSC_EXAM]
                        WHERE ROW_ID IS NOT NULL;";

                    using (SqlCommand cmd = new SqlCommand(sqlBronze, connBronze))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string rowId = rdr.GetString(0);
                            lkpBronze[rowId] = new BronzeInscription
                            {
                                RowId = rowId,
                                FlagReprise = rdr.IsDBNull(1) ? null : rdr.GetString(1)
                            };
                        }
                    }
                }

                Log($"✓ Bronze: {lkpBronze.Count} inscriptions chargées");

                // ══════════════════════════════════════════════════════════════════════════════
                // FIN PARTIE 1/4 - Continuer avec PART2
                // ══════════════════════════════════════════════════════════════════════════════

                // ══════════════════════════════════════════════════════════════════════════════
                // PARTIE 2/4 - Lookups dimensions et lecture STG
                // ══════════════════════════════════════════════════════════════════════════════

                // ═══════════════════════════════════════════════════════════════
                // 3. CHARGER LOOKUPS DIMENSIONS
                // ═══════════════════════════════════════════════════════════════
                Log("Chargement lookups dimensions...");

                Dictionary<string, int> lkpCandidats = new Dictionary<string, int>();
                Dictionary<string, ExamenInfo> lkpExamens = new Dictionary<string, ExamenInfo>();
                Dictionary<int, int> lkpDates = new Dictionary<int, int>();

                using (SqlConnection conn = new SqlConnection(connStrDest))
                {
                    conn.Open();

                    using (SqlCommand cmd = new SqlCommand("SELECT TW_N_UTIL_ID, DW_N_CAND_ID FROM [DWH].[DIM_CANDIDAT] WHERE DW_I_COUR = 1;", conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                            lkpCandidats[rdr.GetString(0)] = rdr.GetInt32(1);
                    }

                    using (SqlCommand cmd = new SqlCommand("SELECT TW_N_EXAM_ID, DW_N_EXAM_ID, DW_M_SEUI_PASS, DW_Q_NB_QUES FROM [DWH].[DIM_EXAMEN] WHERE DW_I_COUR = 1;", conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            lkpExamens[rdr.GetString(0)] = new ExamenInfo
                            {
                                SK = rdr.GetInt32(1),
                                Seuil = rdr.IsDBNull(2) ? (decimal?)null : rdr.GetDecimal(2),
                                NbQuestions = rdr.IsDBNull(3) ? (int?)null : rdr.GetInt32(3)
                            };
                        }
                    }

                    using (SqlCommand cmd = new SqlCommand("SELECT DW_N_DATE_ID FROM [DWH].[DIM_DATE];", conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            int dateId = rdr.GetInt32(0);
                            lkpDates[dateId] = dateId;
                        }
                    }
                }

                Log($"✓ Dimensions: {lkpCandidats.Count} candidats, {lkpExamens.Count} examens, {lkpDates.Count} dates");

                // TRUNCATE
                Log("TRUNCATE FACT_RESULTAT_EXAMEN...");
                using (SqlConnection conn = new SqlConnection(connStrDest))
                {
                    conn.Open();
                    // DIAGNOSTIC
                    SqlCommand diagCmd = new SqlCommand("SELECT DB_NAME() AS BaseActuelle", conn);
                    string baseActuelle = diagCmd.ExecuteScalar().ToString();
                    Log($"DEBUG: Base actuelle = {baseActuelle}");
                    // Fin diagnostic

                    new SqlCommand("DELETE FROM [SDGIC01].[DWH].[FACT_RESULTAT_EXAMEN];", conn).ExecuteNonQuery();
                }

                // ═══════════════════════════════════════════════════════════════
                // 5. LIRE STG + JOIN Bronze en mémoire
                // ═══════════════════════════════════════════════════════════════
                Log("Lecture STG.TW_COPIE...");

                List<ResultatData> resultats = new List<ResultatData>();

                using (SqlConnection conn = new SqlConnection(connStrDest))
                {
                    conn.Open();

                    string queryCopies = $@"
                        SELECT 
                            TW_N_COPI_ID, TW_N_FK_UTIL_ID, TW_N_FK_EXAM_ID,
                            TW_M_PCNT, TW_M_TOTA_PTS, TW_DH_DEBU, TW_DH_CORR,
                            TW_C_STAT, TW_Q_DURE_SEC
                        FROM [{bdDest}].[STG].[TW_COPIE]
                        WHERE TW_N_COPI_ID IS NOT NULL;";

                    using (SqlCommand cmd = new SqlCommand(queryCopies, conn))
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string copieId = rdr.GetString(0);

                            BronzeInscription bronzeInfo = null;
                            if (lkpBronze.ContainsKey(copieId))
                            {
                                bronzeInfo = lkpBronze[copieId];
                            }

                            resultats.Add(new ResultatData
                            {
                                CopieId = copieId,
                                UtilId = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                                ExamId = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                                NotePcnt = rdr.IsDBNull(3) ? (decimal?)null : rdr.GetDecimal(3),
                                NoteBrute = rdr.IsDBNull(4) ? (decimal?)null : rdr.GetDecimal(4),
                                DateDebut = rdr.IsDBNull(5) ? (DateTime?)null : rdr.GetDateTime(5),
                                DateCorr = rdr.IsDBNull(6) ? (DateTime?)null : rdr.GetDateTime(6),
                                Statut = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                                DureeSec = rdr.IsDBNull(8) ? (int?)null : rdr.GetInt32(8),
                                GI_RowId = bronzeInfo?.RowId,
                                FlagReprise = bronzeInfo?.FlagReprise
                            });
                        }
                    }
                }

                Log($"✓ {resultats.Count} copies lues");

                // ══════════════════════════════════════════════════════════════════════════════
                // FIN PARTIE 2/4 - Continuer avec PART3
                // ══════════════════════════════════════════════════════════════════════════════

                // ══════════════════════════════════════════════════════════════════════════════
                // PARTIE 3/4 - Insertion FACT
                // ══════════════════════════════════════════════════════════════════════════════

                // ═══════════════════════════════════════════════════════════════
                // 6. INSERTION DANS FACT
                // ═══════════════════════════════════════════════════════════════
                Log("Insertion FACT_RESULTAT_EXAMEN...");

                int inserted = 0, skipped = 0;

                using (SqlConnection conn = new SqlConnection(connStrDest))
                {
                    conn.Open();

                    string insertSql = $@"
                        INSERT INTO [{bdDest}].[DWH].[FACT_RESULTAT_EXAMEN]
                        (DW_N_CAND_ID, DW_N_EXAM_ID, DW_N_DATE_EXAM_ID, DW_N_DATE_CORR_ID,
                         GI_N_ROW_ID_INSC, TW_N_COPI_ID, DW_M_NOTE_PCNT, DW_M_NOTE_BRUT, DW_M_SEUI_REUS,
                         DW_I_REUS, DW_I_ABSE, DW_Q_NB_QUES_TOTA, DW_Q_DURE_PASS_SEC, DW_I_REPR,
                         DW_DH_SEAN, DW_DH_CORR, DW_DH_CHARG_ETL, DW_C_SYST_SRCE)
                        VALUES
                        (@CandSK, @ExamSK, @DateExamSK, @DateCorrSK, @GI_RowId, @CopieId, @NotePcnt, @NoteBrute,
                         @Seuil, @Reussi, @Absent, @NbQuestions, @DureeSec, @Reprise, @DateDebut, @DateCorr,
                         GETDATE(), 'TestWe');";

                    foreach (var r in resultats)
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(r.UtilId) || !lkpCandidats.ContainsKey(r.UtilId)) { skipped++; continue; }
                            if (string.IsNullOrEmpty(r.ExamId) || !lkpExamens.ContainsKey(r.ExamId)) { skipped++; continue; }

                            int candSK = lkpCandidats[r.UtilId];
                            var examInfo = lkpExamens[r.ExamId];

                            int? dateExamSK = r.DateDebut.HasValue ? (lkpDates.ContainsKey(ToYYYYMMDD(r.DateDebut.Value)) ? lkpDates[ToYYYYMMDD(r.DateDebut.Value)] : 19000101) : 19000101;
                            int? dateCorrSK = r.DateCorr.HasValue ? (lkpDates.ContainsKey(ToYYYYMMDD(r.DateCorr.Value)) ? lkpDates[ToYYYYMMDD(r.DateCorr.Value)] : 19000101) : 19000101;

                            bool absent = r.Statut == "absent" || !r.DateDebut.HasValue;
                            bool reussi = r.NotePcnt.HasValue && examInfo.Seuil.HasValue && !absent && r.NotePcnt.Value >= examInfo.Seuil.Value;
                            bool reprise = r.FlagReprise == "Y";

                            using (SqlCommand cmd = new SqlCommand(insertSql, conn))
                            {
                                cmd.Parameters.AddWithValue("@CandSK", candSK);
                                cmd.Parameters.AddWithValue("@ExamSK", examInfo.SK);
                                cmd.Parameters.AddWithValue("@DateExamSK", dateExamSK ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@DateCorrSK", dateCorrSK ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@GI_RowId", r.GI_RowId ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@CopieId", r.CopieId);
                                cmd.Parameters.AddWithValue("@NotePcnt", r.NotePcnt ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@NoteBrute", r.NoteBrute ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@Seuil", examInfo.Seuil ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@Reussi", reussi);
                                cmd.Parameters.AddWithValue("@Absent", absent);
                                cmd.Parameters.AddWithValue("@NbQuestions", examInfo.NbQuestions ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@DureeSec", r.DureeSec ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@Reprise", reprise);
                                cmd.Parameters.AddWithValue("@DateDebut", r.DateDebut ?? (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@DateCorr", r.DateCorr ?? (object)DBNull.Value);
                                cmd.ExecuteNonQuery();
                                inserted++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Erreur {r.CopieId}: {ex.Message}");
                            skipped++;
                        }
                    }
                }

                // ═══════════════════════════════════════════════════════════════
                // 7. STATS
                // ═══════════════════════════════════════════════════════════════
                Log("");
                Log("═══════════════════════════════════════════════════════════");
                Log($"TOTAL: {resultats.Count} | Insérées: {inserted} | Ignorées: {skipped}");
                Log($"Bronze matchés: {resultats.Count(r => r.GI_RowId != null)}");
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
        // FIN PARTIE 3/4 - Continuer avec PART4 (classes et méthodes)
        // ══════════════════════════════════════════════════════════════════════════════

        // ══════════════════════════════════════════════════════════════════════════════
        // PARTIE 4/4 - Méthodes et classes
        // ══════════════════════════════════════════════════════════════════════════════

        private int ToYYYYMMDD(DateTime date)
        {
            return date.Year * 10000 + date.Month * 100 + date.Day;
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

    public class ResultatData
    {
        public string CopieId { get; set; }
        public string UtilId { get; set; }
        public string ExamId { get; set; }
        public decimal? NotePcnt { get; set; }
        public decimal? NoteBrute { get; set; }
        public DateTime? DateDebut { get; set; }
        public DateTime? DateCorr { get; set; }
        public string Statut { get; set; }
        public int? DureeSec { get; set; }
        public string GI_RowId { get; set; }
        public string FlagReprise { get; set; }
    }

    public class ExamenInfo
    {
        public int SK { get; set; }
        public decimal? Seuil { get; set; }
        public int? NbQuestions { get; set; }
    }

    public class BronzeInscription
    {
        public string RowId { get; set; }
        public string FlagReprise { get; set; }
    }
}