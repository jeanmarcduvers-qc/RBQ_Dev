/*
 * ═══════════════════════════════════════════════════════════════════════════
 * RIPQUAR - Phase 1 STG - TW_REPONSE (Table 5/10)
 * ═══════════════════════════════════════════════════════════════════════════
 * 
 * Objectif: Charger les réponses des étudiants depuis X_GI_V_VECT_REPN_CAND_EXAM
 * Source:      CX_INSC_EXAM.X_GI_V_VECT_REPN_CAND_EXAM (Vecteur caractères)
 * Destination: SDGIC01.STG.TW_REPONSE
 * Format:      String de caractères simples (ex: "CAADDFBBDABDD")
 * Filtre:      Ne charge QUE les examens existants dans TW_COPIE (évite erreur FK)
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
using System.Text;
using System.Security.Cryptography;
using System.IO;
#endregion

namespace ST_TW_REPONSE
{
    [Microsoft.SqlServer.Dts.Tasks.ScriptTask.SSISScriptTaskEntryPointAttribute]
    public partial class ScriptMain : Microsoft.SqlServer.Dts.Tasks.ScriptTask.VSTARTScriptObjectModelBase
    {
        private string logFile = @"C:\Temp\TW_REPONSE_LOG.txt";

        public void Main()
        {
            try
            {
                // Créer le fichier de log
                File.WriteAllText(logFile, $"=== DÉBUT TW_REPONSE - {DateTime.Now} ===\n\n");
                Log("Script démarre");

                // ===================================================================
                // 1. RÉCUPÉRATION DES VARIABLES SSIS
                // ===================================================================
                Log("Lecture variables...");
                string serverSource = Dts.Variables["User::ServerSource"].Value.ToString();
                string bdSource = Dts.Variables["User::BDSource"].Value.ToString();
                string serverDest = Dts.Variables["User::ServerDest"].Value.ToString();
                string bdDest = Dts.Variables["User::BDDest"].Value.ToString();

                Log($"Source: {serverSource} / {bdSource}");
                Log($"Dest: {serverDest} / {bdDest}");

                // ===================================================================
                // 2. CONSTRUCTION CONNECTION STRINGS
                // ===================================================================
                Log("Construction connection strings...");
                string connStrSource = $"Data Source={serverSource};Initial Catalog={bdSource};Integrated Security=True;";
                string connStrDest = $"Data Source={serverDest};Initial Catalog={bdDest};Integrated Security=True;";

                // ===================================================================
                // 3. VIDAGE TABLE DESTINATION (DELETE - pas TRUNCATE)
                // ===================================================================
                Log("DELETE table destination...");
                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();
                    string deleteSql = $"DELETE FROM [{bdDest}].[STG].[TW_REPONSE];";
                    using (SqlCommand cmdDelete = new SqlCommand(deleteSql, connDest))
                    {
                        int rowsDeleted = cmdDelete.ExecuteNonQuery();
                        Log($"DELETE OK: {rowsDeleted} lignes");
                    }
                }

                // ===================================================================
                // 4. CHARGEMENT DES IDs VALIDES DEPUIS TW_COPIE (Destination)
                // ===================================================================
                Log("Chargement des IDs de copies existantes depuis TW_COPIE...");

                HashSet<string> copiesExistantes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (SqlConnection connDest = new SqlConnection(connStrDest))
                {
                    connDest.Open();
                    string queryCopiIds = $"SELECT TW_N_COPI_ID FROM [{bdDest}].[STG].[TW_COPIE];";

                    using (SqlCommand cmdCopies = new SqlCommand(queryCopiIds, connDest))
                    {
                        cmdCopies.CommandTimeout = 300;

                        using (SqlDataReader reader = cmdCopies.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string copiId = reader["TW_N_COPI_ID"]?.ToString();
                                if (!string.IsNullOrWhiteSpace(copiId))
                                {
                                    copiesExistantes.Add(copiId);
                                }
                            }
                        }
                    }
                }

                Log($"✓ {copiesExistantes.Count} copies chargées depuis TW_COPIE");

                if (copiesExistantes.Count == 0)
                {
                    Log("ATTENTION: Aucune copie dans TW_COPIE - Impossible de charger TW_REPONSE!");
                    MessageBox.Show("ATTENTION: Table TW_COPIE est vide!\n\nVeuillez charger TW_COPIE AVANT TW_REPONSE.");
                    Dts.TaskResult = (int)ScriptResults.Failure;
                    return;
                }

                // ===================================================================
                // 5. LECTURE DES DONNÉES SOURCE (avec filtre en mémoire)
                // ===================================================================
                Log("Lecture données source...");

                string queryReponses = @"
                    SELECT 
                        ROW_ID,
                        X_GI_V_VECT_REPN_CAND_EXAM AS VecteurReponses
                    FROM [dbo].[CX_INSC_EXAM]
                    WHERE X_GI_V_VECT_REPN_CAND_EXAM IS NOT NULL
                      AND LEN(X_GI_V_VECT_REPN_CAND_EXAM) > 0;";

                List<ReponseData> reponses = new List<ReponseData>();
                int totalExamens = 0;
                int totalExamensIgnores = 0;
                int totalReponses = 0;

                using (SqlConnection connSource = new SqlConnection(connStrSource))
                {
                    connSource.Open();
                    Log("Connexion source OK");

                    using (SqlCommand cmdSelect = new SqlCommand(queryReponses, connSource))
                    {
                        cmdSelect.CommandTimeout = 300;

                        using (SqlDataReader reader = cmdSelect.ExecuteReader())
                        {
                            Log("Début lecture données...");

                            while (reader.Read())
                            {
                                string rowId = reader["ROW_ID"]?.ToString();
                                string vecteur = reader["VecteurReponses"]?.ToString();

                                if (string.IsNullOrWhiteSpace(vecteur))
                                    continue;

                                // ============================================
                                // FILTRE: Ignorer si ROW_ID n'existe pas dans TW_COPIE
                                // ============================================
                                if (!copiesExistantes.Contains(rowId))
                                {
                                    totalExamensIgnores++;
                                    continue;
                                }

                                totalExamens++;

                                // ============================================
                                // PARSING VECTEUR - Caractère par Caractère
                                // ============================================
                                for (int i = 0; i < vecteur.Length; i++)
                                {
                                    char c = vecteur[i];

                                    // Ignorer les caractères non-alphabétiques
                                    if (!char.IsLetter(c))
                                        continue;

                                    ReponseData rep = new ReponseData
                                    {
                                        TW_C_REPN_ID = $"{rowId}-Q{i + 1}",
                                        TW_N_COPI_ID = rowId,
                                        TW_C_URI_QUES = $"Q{i + 1}",
                                        TW_DE_REPN_TEXT = c.ToString(),
                                        TW_DE_CHOI_JSON = "X",
                                        TW_DH_CHARG_ETL = DateTime.Now,
                                        TW_C_SYST_SRCE = "TestWe",
                                        TW_C_LOT_ETL = rowId,
                                        TW_C_EMPR_LIGN = CalculateHash($"{rowId}{i}{c}")
                                    };

                                    reponses.Add(rep);
                                    totalReponses++;
                                }

                                // Log tous les 100 examens
                                if (totalExamens % 100 == 0)
                                {
                                    Log($"Traité: {totalExamens} examens valides, {totalReponses} réponses ({totalExamensIgnores} ignorés)");
                                }
                            }
                        }
                    }
                }

                Log($"Lecture terminée: {totalExamens} examens traités, {totalReponses} réponses collectées");
                Log($"Examens ignorés (non dans TW_COPIE): {totalExamensIgnores}");

                // ===================================================================
                // 6. INSERTION DANS TABLE DESTINATION
                // ===================================================================
                if (reponses.Count > 0)
                {
                    Log($"Début insertion de {reponses.Count} réponses...");

                    using (SqlConnection connDest = new SqlConnection(connStrDest))
                    {
                        connDest.Open();

                        string insertSql = $@"
                            INSERT INTO [{bdDest}].[STG].[TW_REPONSE]
                            (TW_C_REPN_ID, TW_N_COPI_ID, TW_C_URI_QUES, TW_DE_REPN_TEXT, 
                             TW_DE_CHOI_JSON, TW_DH_CHARG_ETL, TW_C_SYST_SRCE, TW_C_LOT_ETL, TW_C_EMPR_LIGN)
                            VALUES
                            (@TW_C_REPN_ID, @TW_N_COPI_ID, @TW_C_URI_QUES, @TW_DE_REPN_TEXT, 
                             @TW_DE_CHOI_JSON, @TW_DH_CHARG_ETL, @TW_C_SYST_SRCE, @TW_C_LOT_ETL, @TW_C_EMPR_LIGN);";

                        int inserted = 0;

                        foreach (var rep in reponses)
                        {
                            using (SqlCommand cmd = new SqlCommand(insertSql, connDest))
                            {
                                cmd.Parameters.AddWithValue("@TW_C_REPN_ID", rep.TW_C_REPN_ID);
                                cmd.Parameters.AddWithValue("@TW_N_COPI_ID", rep.TW_N_COPI_ID);
                                cmd.Parameters.AddWithValue("@TW_C_URI_QUES", (object)rep.TW_C_URI_QUES ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@TW_DE_REPN_TEXT", (object)rep.TW_DE_REPN_TEXT ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@TW_DE_CHOI_JSON", (object)rep.TW_DE_CHOI_JSON ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@TW_DH_CHARG_ETL", rep.TW_DH_CHARG_ETL);
                                cmd.Parameters.AddWithValue("@TW_C_SYST_SRCE", rep.TW_C_SYST_SRCE);
                                cmd.Parameters.AddWithValue("@TW_C_LOT_ETL", (object)rep.TW_C_LOT_ETL ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@TW_C_EMPR_LIGN", (object)rep.TW_C_EMPR_LIGN ?? DBNull.Value);

                                cmd.ExecuteNonQuery();
                                inserted++;
                            }

                            if (inserted % 1000 == 0)
                            {
                                Log($"Inséré: {inserted} réponses");
                            }
                        }

                        Log($"Insertion terminée: {inserted} réponses");
                    }
                }
                else
                {
                    Log("ATTENTION: Aucune réponse à insérer!");
                }

                // ===================================================================
                // 7. STATISTIQUES FINALES
                // ===================================================================
                Log("");
                Log("═══════════════════════════════════════════════════════════");
                Log("STATISTIQUES FINALES:");
                Log($"  - Copies disponibles dans TW_COPIE: {copiesExistantes.Count}");
                Log($"  - Examens traités: {totalExamens}");
                Log($"  - Examens ignorés (FK manquante): {totalExamensIgnores}");
                Log($"  - Réponses insérées: {reponses.Count}");
                Log($"  - Moyenne réponses/examen: {(totalExamens > 0 ? (double)reponses.Count / totalExamens : 0):F1}");
                Log("═══════════════════════════════════════════════════════════");

                Log("=== SUCCÈS ===");
                Dts.TaskResult = (int)ScriptResults.Success;
            }
            catch (Exception ex)
            {
                string errMsg = $"ERREUR FATALE:\n{ex.Message}\n\nStack:\n{ex.StackTrace}";
                Log(errMsg);

                // Afficher aussi dans MessageBox pour être sûr
                MessageBox.Show(errMsg);

                Dts.TaskResult = (int)ScriptResults.Failure;
            }
        }

        #region Helper Methods

        /// <summary>
        /// Log dans fichier texte
        /// </summary>
        private void Log(string message)
        {
            try
            {
                File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        /// <summary>
        /// Calcule un hash MD5 pour identifier une ligne de façon unique
        /// </summary>
        private string CalculateHash(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            try
            {
                using (MD5 md5 = MD5.Create())
                {
                    byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                    byte[] hashBytes = md5.ComputeHash(inputBytes);

                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < hashBytes.Length; i++)
                    {
                        sb.Append(hashBytes[i].ToString("x2"));
                    }

                    return sb.ToString();
                }
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region ScriptResults declaration
        enum ScriptResults
        {
            Success = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Success,
            Failure = Microsoft.SqlServer.Dts.Runtime.DTSExecResult.Failure
        };
        #endregion
    }

    /// <summary>
    /// Classe de données pour une réponse d'étudiant
    /// </summary>
    public class ReponseData
    {
        public string TW_C_REPN_ID { get; set; }
        public string TW_N_COPI_ID { get; set; }
        public string TW_C_URI_QUES { get; set; }
        public string TW_DE_REPN_TEXT { get; set; }
        public string TW_DE_CHOI_JSON { get; set; }
        public DateTime TW_DH_CHARG_ETL { get; set; }
        public string TW_C_SYST_SRCE { get; set; }
        public string TW_C_LOT_ETL { get; set; }
        public string TW_C_EMPR_LIGN { get; set; }
    }
}