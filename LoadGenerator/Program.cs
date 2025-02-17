using Microsoft.Data.SqlClient;

namespace LoadGenerator
{
    class Program
    {
        private static string sqlServerConnectionString;
        private static int loadIntervalMilliseconds = 1000; // Adjust the interval as needed

        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting load generation...");

            // Build SQL Server connection string using SqlConnectionStringBuilder
            SqlConnectionStringBuilder sqlBuilder = new SqlConnectionStringBuilder
            {
                DataSource = "10.1.10.212",
                InitialCatalog = "PlayerManagement",
                UserID = "sa",
                Password = "IGT<@dm1n>",
                TrustServerCertificate = true
            };

            sqlServerConnectionString = sqlBuilder.ConnectionString;

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            // Handle cancellation
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellationTokenSource.Cancel();
            };

            try
            {
                await GenerateLoad(cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating load: {ex.Message}");
            }

            Console.WriteLine("Load generation stopped.");
        }

        private static async Task GenerateLoad(CancellationToken cancellationToken)
        {
            using SqlConnection conn = new(sqlServerConnectionString);
            await conn.OpenAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                using SqlCommand cmd = new("[dbo].[Proc_PlayerBonusView]", conn);
                cmd.CommandType = System.Data.CommandType.StoredProcedure;

                // Set parameters
                cmd.Parameters.AddWithValue("@@nPlayerID", 1); // Example value
                cmd.Parameters.AddWithValue("@@sSiteID", 1);   // Example value

                await cmd.ExecuteNonQueryAsync(cancellationToken);

                // Adjust the delay as needed to control the load
                await Task.Delay(loadIntervalMilliseconds, cancellationToken);
            }
        }
    }
}
