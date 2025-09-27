using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression; // Adicionado para extrair o .zip
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization; // Requer referência a System.Web.Extensions

// Coloque este ficheiro no seu novo projeto.
// Namespace pode ser alterado para o do seu projeto.
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
        // ===========================================================================================
        // IMPORTANTE: PASSO 1 - CONFIGURE O SEU REPOSITÓRIO
        // Substitua "SEU_USUARIO" e "SEU_REPOSITORIO" pelo seu nome de utilizador e
        // nome do repositório no GitHub onde as releases serão publicadas.
        // Exemplo: "https://api.github.com/repos/Yuri-FV/MeuOutroApp/releases/latest"
        // ===========================================================================================
        private const string GitHubApiUrl = "https://api.github.com/repos/xxyurirangelxx/ModManagerDLC/releases/latest";
        private readonly Version _currentVersion;
        private readonly string _appName;

        public AutoUpdater()
        {
            _currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            _appName = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
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
                    client.DefaultRequestHeaders.Add("User-Agent", "CSharp-AutoUpdater");

                    var responseJson = await client.GetStringAsync(GitHubApiUrl);
                    var serializer = new JavaScriptSerializer();
                    var latestRelease = serializer.Deserialize<GitHubRelease>(responseJson);

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
                            await PerformUpdateFromZip(latestRelease);
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

        private async Task PerformUpdateFromZip(GitHubRelease release)
        {
            // Procura por um ficheiro .zip na release em vez de .exe
            var asset = release.assets.FirstOrDefault(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null)
            {
                Console.WriteLine("ERRO: Não foi encontrado um ficheiro .zip na release mais recente do GitHub.");
                return;
            }

            string downloadUrl = asset.browser_download_url;
            string currentExePath = Process.GetCurrentProcess().MainModule.FileName;
            string currentDirectory = Path.GetDirectoryName(currentExePath);

            string tempZipPath = Path.Combine(currentDirectory, "update.zip");
            string tempExtractPath = Path.Combine(currentDirectory, "update_temp");
            string batchFilePath = Path.Combine(currentDirectory, "update.bat");

            try
            {
                Console.WriteLine($"A baixar {asset.name}...");
                using (var client = new WebClient())
                {
                    await client.DownloadFileTaskAsync(new Uri(downloadUrl), tempZipPath);
                }
                Console.WriteLine("Download concluído.");

                Console.WriteLine("A extrair atualização...");
                if (Directory.Exists(tempExtractPath)) Directory.Delete(tempExtractPath, true);
                ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);

                // VERSÃO CORRIGIDA DO SCRIPT .BAT
                // Utiliza uma abordagem mais segura de copiar primeiro e depois apagar.
                string batchCommands = $@"
@echo off
echo A aguardar que a aplicação feche...
timeout /t 3 /nobreak > nul

echo A substituir os ficheiros antigos...
robocopy ""{tempExtractPath}"" ""."" /E /IS /R:0 /W:0 > nul

echo A limpar a pasta de atualização...
rmdir /S /Q ""{tempExtractPath}""

echo A limpar ficheiros temporários...
del ""{Path.GetFileName(tempZipPath)}""

echo Atualização concluída! A reiniciar...
start """" ""{_appName}""

del ""%~f0""
";
                File.WriteAllText(batchFilePath, batchCommands);

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

