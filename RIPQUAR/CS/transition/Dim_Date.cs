// UPDATED: Thread-safe logging with SafeLog() - 2026-03-27
/*
 * ═══════════════════════════════════════════════════════════════════════════
 * RIPQUAR - DIM_DATE - Chargement avec vérification "if empty"
 * ═══════════════════════════════════════════════════════════════════════════
 * 
 * Objectif: Charger la dimension calendrier (2024-2027)
 * Comportement: 
 *   - Si DIM_DATE vide → Charge automatiquement
 *   - Si DIM_DATE déjà remplie → Skip (pas de rechargement)
 * 
 * Format: YYYYMMDD (ex: 20240101 = 1er janvier 2024)
 * Période: 01/01/2024 à 31/12/2027 + date défaut 19000101
 * 
 * ═══════════════════════════════════════════════════════════════════════════
 */

#region Namespaces
using System;
using System.Data;
using Microsoft.SqlServer.Dts.Runtime;
using System.Windows.Forms;
using System.Data.SqlClient;
using System.IO;
#endregion

namespace ST_DIM_DATE
{
    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {
        private string _logFile;
        private string _scriptName;

        public void Main()
        {

            _logFile = Dts.Variables["User::LogFile"].Value.ToString();
            _scriptName = Dts.Variables["System::TaskName"].Value.ToString();

            try
            {
                // ═══════════════════════════════════════════════════════════
                // 3. LOG DÉBUT
                // ═══════════════════════════════════════════════════════════
                SafeLog($"\n=== DÉBUT {_scriptName} - {DateTime.Now} ===\n");

                Log("Script démarre");

                // ═══════════════════════════════════════════════════════════════
                // 1. VARIABLES SSIS
                // ═══════════════════════════════════════════════════════════════
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                string bdDest = Dts.Variables["User::BDDest"].Value.ToString();

                Log($"Serveur: {serverDest} / Base: {bdDest}");

                string connStr = $"Data Source={serverDest};Initial Catalog={bdDest};Integrated Security=True;";

                // ═══════════════════════════════════════════════════════════════
                // 2. VÉRIFIER SI DIM_DATE EST VIDE
                // ═══════════════════════════════════════════════════════════════
                Log("Vérification DIM_DATE...");

                int nbDatesExistantes = 0;

                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();
                    string checkSql = $"SELECT COUNT(*) FROM [{bdDest}].[DWH].[DIM_DATE];";

                    using (SqlCommand cmd = new SqlCommand(checkSql, conn))
                    {
                        nbDatesExistantes = (int)cmd.ExecuteScalar();
                    }
                }

                Log($"DIM_DATE contient actuellement {nbDatesExistantes} dates");

                // ═══════════════════════════════════════════════════════════════
                // 3. SI VIDE → CHARGER, SINON → SKIP
                // ═══════════════════════════════════════════════════════════════
                if (nbDatesExistantes > 0)
                {
                    Log("✓ DIM_DATE déjà remplie → SKIP chargement");
                    Log($"=== SUCCÈS (Skip) - {nbDatesExistantes} dates déjà présentes ===");
                    Dts.TaskResult = (int)ScriptResults.Success;
                    return;
                }

                Log("⚠ DIM_DATE vide → Chargement automatique...");

                // ═══════════════════════════════════════════════════════════════
                // 4. CHARGEMENT DES DATES (2024-2027)
                // ═══════════════════════════════════════════════════════════════
                Log("Génération des dates 2024-2027...");

                int inserted = 0;

                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    conn.Open();

                    // Date défaut pour NULLs (1900-01-01)
                    string insertDefaultSql = $@"
                        INSERT INTO [{bdDest}].[DWH].[DIM_DATE]
                        (DW_N_DATE_ID, DW_D_DATE, DW_C_JOUR, DW_C_MOIS, DW_N_ANNE,
                         DW_C_JOUR_SEMA, DW_N_SEMA_ANNE, DW_C_TRIM)
                        VALUES
                        (19000101, '1900-01-01', 1, 1, 1900, 'Lundi', 1, 'Q1');";

                    using (SqlCommand cmd = new SqlCommand(insertDefaultSql, conn))
                    {
                        cmd.ExecuteNonQuery();
                        inserted++;
                    }

                    Log("✓ Date défaut 19000101 insérée");

                    // Générer toutes les dates de 2024 à 2027
                    DateTime startDate = new DateTime(2024, 1, 1);
                    DateTime endDate = new DateTime(2027, 12, 31);

                    string insertSql = $@"
                        INSERT INTO [{bdDest}].[DWH].[DIM_DATE]
                        (DW_N_DATE_ID, DW_D_DATE, DW_C_JOUR, DW_C_MOIS, DW_N_ANNE,
                         DW_C_JOUR_SEMA, DW_N_SEMA_ANNE, DW_C_TRIM)
                        VALUES
                        (@DateId, @Date, @Jour, @Mois, @Annee, @JourSemaine, @Semaine, @Trimestre);";

                    for (DateTime date = startDate; date <= endDate; date = date.AddDays(1))
                    {
                        int dateId = date.Year * 10000 + date.Month * 100 + date.Day;
                        string jourSemaine = GetJourSemaineFr(date.DayOfWeek);
                        int semaine = GetWeekOfYear(date);
                        string trimestre = $"Q{(date.Month - 1) / 3 + 1}";

                        using (SqlCommand cmd = new SqlCommand(insertSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@DateId", dateId);
                            cmd.Parameters.AddWithValue("@Date", date);
                            cmd.Parameters.AddWithValue("@Jour", date.Day);
                            cmd.Parameters.AddWithValue("@Mois", date.Month);
                            cmd.Parameters.AddWithValue("@Annee", date.Year);
                            cmd.Parameters.AddWithValue("@JourSemaine", jourSemaine);
                            cmd.Parameters.AddWithValue("@Semaine", semaine);
                            cmd.Parameters.AddWithValue("@Trimestre", trimestre);

                            cmd.ExecuteNonQuery();
                            inserted++;
                        }

                        if (inserted % 100 == 0)
                        {
                            Log($"Inséré: {inserted} dates...");
                        }
                    }
                }

                // ═══════════════════════════════════════════════════════════════
                // 5. STATS FINALES
                // ═══════════════════════════════════════════════════════════════
                Log("");
                Log("═══════════════════════════════════════════════════════════");
                Log($"TOTAL: {inserted} dates insérées (2024-2027 + date défaut)");
                Log("═══════════════════════════════════════════════════════════");
                Log("=== SUCCÈS ===");

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

        private string GetJourSemaineFr(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Monday: return "Lundi";
                case DayOfWeek.Tuesday: return "Mardi";
                case DayOfWeek.Wednesday: return "Mercredi";
                case DayOfWeek.Thursday: return "Jeudi";
                case DayOfWeek.Friday: return "Vendredi";
                case DayOfWeek.Saturday: return "Samedi";
                case DayOfWeek.Sunday: return "Dimanche";
                default: return "Inconnu";
            }
        }

        private int GetWeekOfYear(DateTime date)
        {
            System.Globalization.CultureInfo ciCurr = System.Globalization.CultureInfo.CurrentCulture;
            int weekNum = ciCurr.Calendar.GetWeekOfYear(date,
                System.Globalization.CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday);
            return weekNum;
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

        private void Log(string message)
        {
            SafeLog($"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }

        enum ScriptResults
        {
            Success = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Success,
            Failure = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Failure
        }
    }
}