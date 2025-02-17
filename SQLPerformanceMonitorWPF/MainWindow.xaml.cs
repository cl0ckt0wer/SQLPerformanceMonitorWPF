using Microsoft.Data.SqlClient;
using System.Data.SQLite;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SQLPerformanceMonitorWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string sqlServerConnectionString;
        private string sqliteConnectionString;
        private int pollIntervalSeconds;
        private Dictionary<string, long> previousExecutionCounts = new();
        private Dictionary<string, string> previousExecutionPlans = new();
        private CancellationTokenSource cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            StopButton.IsEnabled = false;

            // Bind IntegratedSecurityCheckBox Checked event
            IntegratedSecurityCheckBox.Checked += IntegratedSecurityCheckBox_Changed;
            IntegratedSecurityCheckBox.Unchecked += IntegratedSecurityCheckBox_Changed;

            // Initialize User Credentials fields
            UpdateUserCredentialsState();
        }

        private void IntegratedSecurityCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateUserCredentialsState();
        }

        private void UpdateUserCredentialsState()
        {
            bool isIntegratedSecurity = IntegratedSecurityCheckBox.IsChecked == true;
            UserIDTextBox.IsEnabled = !isIntegratedSecurity;
            PasswordBox.IsEnabled = !isIntegratedSecurity;
        }

        private void InitializeSQLiteDatabase()
        {
            using SQLiteConnection conn = new(sqliteConnectionString);
            conn.Open();
            string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS ExecutionPlanStatistics (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SqlText TEXT,
                    ObjectName TEXT,
                    DatabaseID INTEGER,
                    ExecutionCount INTEGER,
                    ExecutionsPerMinute INTEGER,
                    LastElapsedTime INTEGER,
                    LastExecutionTime TEXT,
                    PlanChanged INTEGER,
                    LogTime TEXT
                )";
            using SQLiteCommand cmd = new(createTableQuery, conn);
            cmd.ExecuteNonQuery();
        }

        private void StartMonitoring()
        {
            try
            {
                // Build SQL Server connection string using SqlConnectionStringBuilder
                SqlConnectionStringBuilder sqlBuilder = new();

                // Data Source
                if (string.IsNullOrWhiteSpace(DataSourceTextBox.Text))
                {
                    MessageBox.Show("Please provide a Data Source.");
                    return;
                }
                sqlBuilder.DataSource = DataSourceTextBox.Text;

                // Initial Catalog
                if (!string.IsNullOrWhiteSpace(InitialCatalogTextBox.Text))
                {
                    sqlBuilder.InitialCatalog = InitialCatalogTextBox.Text;
                }

                // Integrated Security
                if (IntegratedSecurityCheckBox.IsChecked == true)
                {
                    sqlBuilder.IntegratedSecurity = true;
                }
                else
                {
                    sqlBuilder.IntegratedSecurity = false;

                    // User ID
                    if (string.IsNullOrWhiteSpace(UserIDTextBox.Text))
                    {
                        MessageBox.Show("Please provide a User ID for SQL Server Authentication.");
                        return;
                    }
                    sqlBuilder.UserID = UserIDTextBox.Text;

                    // Password
                    if (string.IsNullOrWhiteSpace(PasswordBox.Password))
                    {
                        MessageBox.Show("Please provide a Password for SQL Server Authentication.");
                        return;
                    }
                    sqlBuilder.Password = PasswordBox.Password;
                }

                // Trust Server Certificate
                sqlBuilder.TrustServerCertificate = TrustServerCertificateCheckBox.IsChecked == true;

                // Validate and get the connection string
                sqlServerConnectionString = sqlBuilder.ConnectionString;

                // SQLite Database Path
                string sqliteDatabasePath = SQLiteDatabasePathTextBox.Text;
                if (string.IsNullOrWhiteSpace(sqliteDatabasePath))
                {
                    MessageBox.Show("Please provide a valid SQLite database path.");
                    return;
                }
                sqliteConnectionString = $"Data Source={sqliteDatabasePath};Version=3;";

                // Polling Interval
                if (!int.TryParse(PollingIntervalTextBox.Text, out pollIntervalSeconds) || pollIntervalSeconds <= 0)
                {
                    MessageBox.Show("Please enter a valid polling interval in seconds.");
                    return;
                }

                InitializeSQLiteDatabase();

                cancellationTokenSource = new CancellationTokenSource();
                StopButton.IsEnabled = true;
                StartButton.IsEnabled = false;
                previousExecutionCounts.Clear();
                previousExecutionPlans.Clear();

                Task.Run(async () =>
                {
                    while (!cancellationTokenSource.IsCancellationRequested)
                    {
                        await GetExecutionPlanStatistics();
                        await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), cancellationTokenSource.Token);
                    }
                }, cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting monitoring: {ex.Message}");
            }
        }

        private void StopMonitoring()
        {
            cancellationTokenSource?.Cancel();
            StopButton.IsEnabled = false;
            StartButton.IsEnabled = true;
        }

        //private async Task GetExecutionPlanStatistics()
        //{
        //    try
        //    {
        //        using SqlConnection conn = new(sqlServerConnectionString);
        //        await conn.OpenAsync();

        //        string query = @"
        //            SELECT 
        //                qs.sql_handle,
        //                qs.plan_handle,
        //                st.text AS sql_text,
        //                qs.execution_count,
        //                qs.total_elapsed_time,
        //                qs.last_elapsed_time,
        //                qs.last_execution_time,
        //                qp.query_plan
        //            FROM sys.dm_exec_query_stats qs
        //            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
        //            CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
        //            ORDER BY qs.last_execution_time DESC";

        //        using SqlCommand cmd = new(query, conn);
        //        using SqlDataReader reader = await cmd.ExecuteReaderAsync();

        //        while (await reader.ReadAsync())
        //        {
        //            string sqlHandle = reader["sql_handle"].ToString();
        //            string sqlText = reader["sql_text"].ToString();
        //            long executionCount = (long)reader["execution_count"];
        //            long lastElapsedTime = (long)reader["last_elapsed_time"];
        //            DateTime lastExecutionTime = (DateTime)reader["last_execution_time"];
        //            string queryPlan = reader["query_plan"].ToString();

        //            // Calculate executions per minute
        //            long executionsPerMinute = CalculateExecutionsPerMinute(sqlHandle, executionCount);

        //            // Check for execution plan changes
        //            bool hasPlanChanged = CheckForPlanChanges(sqlHandle, queryPlan);

        //            // Log or process the data as needed
        //            LogExecutionPlanStatistics(sqlText, executionCount, executionsPerMinute, lastElapsedTime, lastExecutionTime, hasPlanChanged);

        //            // Update previous values
        //            previousExecutionCounts[sqlHandle] = executionCount;
        //            previousExecutionPlans[sqlHandle] = queryPlan;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Dispatcher.Invoke(() =>
        //        {
        //            OutputTextBox.AppendText($"Error retrieving execution plan statistics: {ex.Message}\n");
        //        });
        //    }
        //}

        private long CalculateExecutionsPerMinute(string sqlHandle, long currentExecutionCount)
        {
            if (previousExecutionCounts.TryGetValue(sqlHandle, out long previousCount))
            {
                return (currentExecutionCount - previousCount) / (pollIntervalSeconds / 60);
            }
            return 0;
        }

        private bool CheckForPlanChanges(string sqlHandle, string currentQueryPlan)
        {
            if (previousExecutionPlans.TryGetValue(sqlHandle, out string previousPlan))
            {
                return !string.Equals(previousPlan, currentQueryPlan);
            }
            return false;
        }

        private void LogExecutionPlanStatistics(
            string sqlText,
            string objectName,
            short? databaseID,
            long executionCount,
            long executionsPerMinute,
            long lastElapsedTime,
            DateTime lastExecutionTime,
            bool hasPlanChanged)
        {
            // Create a summary of the query
            string querySummary = GetQuerySummary(sqlText);

            // Update UI
            Dispatcher.Invoke(() =>
            {
                OutputTextBox.AppendText($"Object Name: {objectName ?? "N/A"}\n");
                OutputTextBox.AppendText($"Database ID: {(databaseID.HasValue ? databaseID.Value.ToString() : "N/A")}\n");
                OutputTextBox.AppendText($"Query Summary: {querySummary}\n");
                OutputTextBox.AppendText($"Execution Count: {executionCount}\n");
                OutputTextBox.AppendText($"Executions Per Minute: {executionsPerMinute}\n");
                OutputTextBox.AppendText($"Last Elapsed Time (ms): {lastElapsedTime / 1000}\n");
                OutputTextBox.AppendText($"Last Execution Time: {lastExecutionTime}\n");
                OutputTextBox.AppendText($"Execution Plan Changed: {hasPlanChanged}\n");
                OutputTextBox.AppendText(new string('-', 50) + "\n");
            });

            // Check if logging is enabled
            Dispatcher.Invoke(() =>
            {
                if (LoggingCheckBox.IsChecked == true)
                {
                    // Log to SQLite database
                    using SQLiteConnection conn = new(sqliteConnectionString);
                    conn.Open();
                    string insertQuery = @"
                        INSERT INTO ExecutionPlanStatistics 
                        (SqlText, ObjectName, DatabaseID, ExecutionCount, ExecutionsPerMinute, LastElapsedTime, LastExecutionTime, PlanChanged, LogTime)
                        VALUES
                        (@SqlText, @ObjectName, @DatabaseID, @ExecutionCount, @ExecutionsPerMinute, @LastElapsedTime, @LastExecutionTime, @PlanChanged, @LogTime)";
                    using SQLiteCommand cmd = new(insertQuery, conn);
                    cmd.Parameters.AddWithValue("@SqlText", sqlText);
                    cmd.Parameters.AddWithValue("@ObjectName", objectName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DatabaseID", databaseID.HasValue ? (object)databaseID.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@ExecutionCount", executionCount);
                    cmd.Parameters.AddWithValue("@ExecutionsPerMinute", executionsPerMinute);
                    cmd.Parameters.AddWithValue("@LastElapsedTime", lastElapsedTime / 1000);
                    cmd.Parameters.AddWithValue("@LastExecutionTime", lastExecutionTime.ToString("o"));
                    cmd.Parameters.AddWithValue("@PlanChanged", hasPlanChanged ? 1 : 0);
                    cmd.Parameters.AddWithValue("@LogTime", DateTime.UtcNow.ToString("o"));

                    cmd.ExecuteNonQuery();
                }
            });
        }

        // Helper method to create a query summary
        private string GetQuerySummary(string sqlText)
        {
            if (string.IsNullOrWhiteSpace(sqlText))
            {
                return "N/A";
            }

            // Extract the first line and remove extra whitespace
            string firstLine = sqlText.Trim().Split('\n').FirstOrDefault()?.Trim();

            if (string.IsNullOrEmpty(firstLine))
            {
                return "N/A";
            }

            // Extract the query type and first few words
            string pattern = @"^\b(SELECT|INSERT|UPDATE|DELETE|EXEC|ALTER|CREATE|DROP|TRUNCATE|MERGE)\b";
            Match match = Regex.Match(firstLine, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string queryType = match.Value.ToUpperInvariant();

                // Get the first 50 characters for summary
                string summary = firstLine.Length > 50 ? firstLine.Substring(0, 50) + "..." : firstLine;

                return $"{queryType}: {summary}";
            }

            // If query type not found, return the first 50 characters
            string defaultSummary = firstLine.Length > 50 ? firstLine.Substring(0, 50) + "..." : firstLine;
            return $"QUERY: {defaultSummary}";
        }
        private async Task GetExecutionPlanStatistics()
        {
            try
            {
                using SqlConnection conn = new(sqlServerConnectionString);
                await conn.OpenAsync();

                string query = @"
                    SELECT 
                        qs.sql_handle,
                        qs.plan_handle,
                        st.text AS sql_text,
                        qs.execution_count,
                        qs.total_elapsed_time,
                        qs.last_elapsed_time,
                        qs.last_execution_time,
                        qp.query_plan,
                        qs.statement_start_offset,
                        qs.statement_end_offset,
                        st.dbid AS database_id,
                        OBJECT_NAME(st.objectid, st.dbid) AS object_name
                    FROM sys.dm_exec_query_stats qs
                    CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
                    CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
                    ORDER BY qs.last_execution_time DESC";

                using SqlCommand cmd = new(query, conn);
                using SqlDataReader reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    string sqlHandle = reader["sql_handle"].ToString();
                    string sqlText = reader["sql_text"].ToString();
                    long executionCount = (long)reader["execution_count"];
                    long lastElapsedTime = (long)reader["last_elapsed_time"];
                    DateTime lastExecutionTime = (DateTime)reader["last_execution_time"];
                    string queryPlan = reader["query_plan"].ToString();
                    int statementStartOffset = (int)reader["statement_start_offset"];
                    int statementEndOffset = (int)reader["statement_end_offset"];

                    // Handle possible DBNull values
                    short? databaseID = reader["database_id"] != DBNull.Value ? (short?)Convert.ToInt16(reader["database_id"]) : null;
                    string objectName = reader["object_name"] != DBNull.Value ? reader["object_name"].ToString() : null;

                    // Extract the individual statement text
                    string statementText = ExtractStatementText(sqlText, statementStartOffset, statementEndOffset);

                    // Calculate executions per minute
                    long executionsPerMinute = CalculateExecutionsPerMinute(sqlHandle, executionCount);

                    // Check for execution plan changes
                    bool hasPlanChanged = CheckForPlanChanges(sqlHandle, queryPlan);

                    // Log or process the data as needed
                    LogExecutionPlanStatistics(
                        statementText,
                        objectName,
                        databaseID,
                        executionCount,
                        executionsPerMinute,
                        lastElapsedTime,
                        lastExecutionTime,
                        hasPlanChanged);

                    // Update previous values
                    previousExecutionCounts[sqlHandle] = executionCount;
                    previousExecutionPlans[sqlHandle] = queryPlan;
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputTextBox.AppendText($"Error retrieving execution plan statistics: {ex.Message}\n");
                });
            }
        }

        private string ExtractStatementText(string sqlText, int startOffset, int endOffset)
        {
            if (startOffset == -1 || endOffset == -1 || startOffset >= endOffset)
            {
                return sqlText;
            }

            int startIndex = startOffset / 2;
            int endIndex = endOffset / 2;

            return sqlText.Substring(startIndex, endIndex - startIndex);
        }
        
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
        }
    }
}