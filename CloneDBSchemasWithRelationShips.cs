using System;
using System.Diagnostics;
using System.IO;
using Renci.SshNet;

class Program
{
    static void Main(string[] args)
    {
        string sshHost = "88.88.88.88";
        string sshUser = "yaso";
        string sshPassword = "blabla";

        string postgresUser = "yaso";
        string postgresPassword = "blabla";
        string sourceDb = "test";
        string targetDb = "postgres";
        int postgresPort = 5432;

        string remoteDumpPath = $"/tmp/{sourceDb}_schema.sql"; // Remote file path
        string localDumpPath = Path.Combine(Path.GetTempPath(), $"{sourceDb}_schema.sql"); // Local file path

        using (var sshClient = new SshClient(sshHost, 22, sshUser, sshPassword))
        {
            sshClient.Connect();

            Console.WriteLine("Connected to SSH server.");

            string command = $@"PGPASSWORD='{postgresPassword}' pg_dump --host=127.0.0.1 --port={postgresPort} --username={postgresUser} --dbname={sourceDb} --schema=public --schema-only --exclude-table=public.hypertable --exclude-table=public.chunk > {remoteDumpPath}";

            string dumpCommand = $@"
    PGPASSWORD={postgresPassword} pg_dump --host=127.0.0.1 --port={postgresPort} --username={postgresUser} --dbname={sourceDb} --schema=public --schema-only --exclude-table=public.hypertable --exclude-table=public.chunk > {remoteDumpPath}
";
 


            var dumpCmd = sshClient.CreateCommand(command);
            var dumpResult = dumpCmd.Execute();

            if (!string.IsNullOrEmpty(dumpCmd.Error))
            {
                Console.WriteLine($"Error during remote dump: {dumpCmd.Error}");
                sshClient.Disconnect();
                return;
            }

            Console.WriteLine($"Schema dumped successfully to remote file: {remoteDumpPath}");

            // Download the dump file to the local machine
            using (var sftpClient = new SftpClient(sshHost, 22, sshUser, sshPassword))
            {
                sftpClient.Connect();

                using (var remoteFile = sftpClient.OpenRead(remoteDumpPath))
                using (var localFile = File.Create(localDumpPath))
                {
                    remoteFile.CopyTo(localFile);
                }

                Console.WriteLine($"Schema file downloaded successfully to: {localDumpPath}");

                sftpClient.Disconnect();
            }

            sshClient.Disconnect();
        }

        // Restore the schema into the local database
        Console.WriteLine("Starting schema restoration...");

        string restoreCommand = $@" set PGPASSWORD='postgres' && psql --host=127.0.0.1 --port=5432 --username=postgres --dbname=postgres --file={localDumpPath} ";


        var restoreProcessInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/C {restoreCommand}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var restoreProcess = Process.Start(restoreProcessInfo);
        restoreProcess.WaitForExit();

        string restoreOutput = restoreProcess.StandardOutput.ReadToEnd();
        string restoreError = restoreProcess.StandardError.ReadToEnd();

        if (restoreProcess.ExitCode == 0)
        {
            Console.WriteLine("Schema restored successfully.");
        }
        else
        {
            Console.WriteLine($"Error during restoration: {restoreError}");
        }
    }
}
