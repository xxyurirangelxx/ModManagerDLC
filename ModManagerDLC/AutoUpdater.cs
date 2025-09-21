using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization; // Requer referência a System.Web.Extensions

namespace DLCtoLML
{
    // Classes auxiliares para deserializar a resposta JSON da API do GitHub
    public class GitHubReleaseAsset
    {
        public string browser_download_url { get; set; }
        public string name { get; set; }
    }

    public class GitHubRelease
    {
        public string tag_name { get; set; }
        public GitHubReleaseAsset[] assets { get; set; }
    }

    public class AutoUpdater
    {
        // IMPORTANTE: Substitua com o seu nome de utilizador e nome do repositório no GitHub
        private const string GitHubApiUrl = "https://api.github.com/repos/xxyurirangelxx/ModManagerDLC/releases/latest";
        private readonly Version _currentVersion;

        public AutoUpdater()
        {
            _currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
        }

        public async Task CheckForUpdatesAsync()
        {
            Console.WriteLine($"Versão atual: {_currentVersion}");
            Console.WriteLine("A verificar se existem atualizações...");

            try
            {
                using (var client = new HttpClient())
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    client.DefaultRequestHeaders.Add("User-Agent", "ModManagerDLC-Updater");

                    var responseJson = await client.GetStringAsync(GitHubApiUrl);
                    var serializer = new JavaScriptSerializer();
                    var latestRelease = serializer.Deserialize<GitHubRelease>(responseJson);

                    // Remove o 'v' do início da tag_name (ex: "v1.1.0" -> "1.1.0")
                    var latestVersionString = latestRelease.tag_name.StartsWith("v") ? latestRelease.tag_name.Substring(1) : latestRelease.tag_name;
                    var latestVersion = new Version(latestVersionString);

                    if (latestVersion > _currentVersion)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\nNova versão encontrada: {latestVersion}");
                        Console.Write("Deseja atualizar agora? (s/n): ");
                        Console.ResetColor();

                        var input = Console.ReadLine();
                        if (input?.ToLower() == "s")
                        {
                            await PerformUpdate(latestRelease);
                        }
                        else
                        {
                            Console.WriteLine("Atualização ignorada.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Você já tem a versão mais recente.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nNão foi possível verificar se existem atualizações: {ex.Message}");
                Console.ResetColor();
            }
            Console.WriteLine("-----------------------------------");
            await Task.Delay(1500); // Pausa para o utilizador ler
        }

        private async Task PerformUpdate(GitHubRelease release)
        {
            var asset = release.assets.FirstOrDefault(a => a.name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (asset == null)
            {
                Console.WriteLine("ERRO: Não foi encontrado um ficheiro .exe na release mais recente do GitHub.");
                return;
            }

            string downloadUrl = asset.browser_download_url;
            string currentExePath = Process.GetCurrentProcess().MainModule.FileName;
            string newExePath = Path.Combine(Path.GetDirectoryName(currentExePath), "ModManagerDLC.new.exe");
            string batchFilePath = Path.Combine(Path.GetDirectoryName(currentExePath), "update.bat");

            try
            {
                Console.WriteLine($"A baixar {asset.name}...");
                using (var client = new WebClient())
                {
                    await client.DownloadFileTaskAsync(new Uri(downloadUrl), newExePath);
                }
                Console.WriteLine("Download concluído.");

                string batchCommands = $@"
@echo off
echo A aguardar que a aplicação feche...
timeout /t 2 /nobreak > nul
echo A substituir a versão antiga...
del ""{Path.GetFileName(currentExePath)}""
rename ""{Path.GetFileName(newExePath)}"" ""{Path.GetFileName(currentExePath)}""
echo Atualização concluída! A reiniciar...
start """" ""{Path.GetFileName(currentExePath)}""
del ""%~f0""
";
                File.WriteAllText(batchFilePath, batchCommands);

                // Inicia o script .bat e fecha a aplicação atual
                Process.Start(new ProcessStartInfo(batchFilePath) { CreateNoWindow = true, UseShellExecute = false });
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Ocorreu um erro durante a atualização: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}
