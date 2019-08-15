using MartinCostello.SqlLocalDb;
using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Test.IntegrationTests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            // download sqlpackage https://docs.microsoft.com/fr-fr/sql/tools/sqlpackage-download?view=sql-server-2017
            var databaseName = "Clients";
            var instanceName = "Clients";
            var currentDirectory = Directory.GetCurrentDirectory();

            var localDB = new SqlLocalDbApi();

            try
            {
                var instance = localDB.GetOrCreateInstance(instanceName);
                var manager = instance.Manage();

                if (!instance.IsRunning)
                {
                    manager.Start();
                }

                var connectionString = instance.GetConnectionString();
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    ExecuteSql(connection, $@"
USE master;
IF (EXISTS (SELECT name 
    FROM master.dbo.sysdatabases 
    WHERE ('[' + name + ']' = '[{databaseName}]' 
    OR name = '{databaseName}')))
BEGIN
    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{databaseName}];
END
");
                }

                var mdfFile = new FileInfo(
                    Path.Combine(currentDirectory, $"{databaseName}.mdf"));
                var ldfFile = new FileInfo(
                    Path.Combine(currentDirectory, $"{databaseName}.ldf"));

                if (mdfFile.Exists)
                {
                    mdfFile.Delete();
                }
                if (ldfFile.Exists)
                {
                    ldfFile.Delete();
                }

                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    // create database
                    ExecuteSql(connection, $@"
CREATE DATABASE [{databaseName}]
ON
(
    NAME = {databaseName}_data,  
    FILENAME = '{mdfFile.FullName}'
)  
LOG ON
(
    NAME = {databaseName}_log,  
    FILENAME = '{ldfFile.FullName}'
);
");

                    // publish dacpac to newly created database
                    PrepareDatabase(connectionString, databaseName);

                    ExecuteSql(connection, $"USE [{databaseName}]");

                    // modify datas
                    var insertNumber = 10000;
                    for (var i = 1; i <= insertNumber; i++)
                    {
                        ExecuteSql(connection, $"INSERT INTO [Client] ([Name]) VALUES ('Client{i}')");
                    }

                    // check datas
                    var sqlCommand = new SqlCommand("SELECT count(*) FROM Client", connection);
                    var result = sqlCommand.ExecuteScalar();

                    Assert.Equal(insertNumber, result);
                }

                manager.Stop();
            }
            finally
            {
                localDB.DeleteUserInstances(true);
                localDB.Dispose();
            }
        }

        private void PrepareDatabase(string connectionString, string databaseName)
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var sqlPackageExe = new FileInfo(Path.Combine(
                currentDirectory,
                "sqlpackage\\SqlPackage.exe"));
            var process = new Process();

            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = currentDirectory,
                FileName = sqlPackageExe.FullName,
                Arguments = $"/Action:Publish  /SourceFile:\"Sample.Database.dacpac\" /TargetConnectionString:\"{connectionString};Initial Catalog={databaseName}\"",
            };

            process.StartInfo = startInfo;
            process.Start();

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                // var output = process.StandardOutput.ReadToEnd();
                var errors = process.StandardError.ReadToEnd();

                throw new Exception($"An error occured during dacpac publication : {errors}");
            }
        }

        private void ExecuteSql(SqlConnection connection, string sql)
        {
            var command = new SqlCommand(sql, connection);
            command.ExecuteNonQuery();
        }
    }
}
