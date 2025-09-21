using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace DLCtoLML
{
    public class LmlInstaller
    {
        // URL corrigida para um ficheiro .ZIP funcional do LML.
        private const string LmlDownloadUrl = "https://files.catbox.moe/ytkxqm.zip";

        public async Task CheckAndInstallLmlAsync(string gtaPath)
        {
            if (string.IsNullOrEmpty(gtaPath) || !Directory.Exists(gtaPath))
            {
                // Esta mensagem só aparecerá se o settings.txt for apagado após a primeira execução.
                Console.WriteLine("Caminho do GTA V não definido ou inválido. Não é possível verificar a instalação do LML.");
                Console.WriteLine("Por favor, defina o caminho no menu principal.");
                Console.WriteLine("Pressione Enter para continuar...");
                Console.ReadLine();
                return;
            }

            string vfsDllPath = Path.Combine(gtaPath, "vfs.dll");
            string easyHookPatchDllPath = Path.Combine(gtaPath, "EasyHookPatch.dll");

            Console.WriteLine($"\nVerificando LML na pasta: {gtaPath}");

            if (File.Exists(vfsDllPath) && File.Exists(easyHookPatchDllPath))
            {
                Console.WriteLine("LML detectado. Nenhuma instalação é necessária.");
                Console.WriteLine("Pressione Enter para continuar...");
                Console.ReadLine(); // Pausa obrigatória para o utilizador ver a mensagem.
                return;
            }

            Console.WriteLine("Lenny's Mod Loader (LML) não foi detectado. Iniciando o download e a instalação...");

            string tempZipPath = Path.Combine(Path.GetTempPath(), "lml_download.zip");
            string tempExtractPath = Path.Combine(Path.GetTempPath(), "lml_extract_" + Guid.NewGuid().ToString());

            try
            {
                using (var client = new HttpClient())
                {
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                    Console.WriteLine($"A baixar LML de {LmlDownloadUrl}...");
                    var response = await client.GetAsync(LmlDownloadUrl);
                    response.EnsureSuccessStatusCode();

                    using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                    Console.WriteLine("Download concluído.");
                }

                Console.WriteLine("A extrair ficheiros...");
                Directory.CreateDirectory(tempExtractPath);
                ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);

                string vfsFile = Directory.GetFiles(tempExtractPath, "vfs.dll", SearchOption.AllDirectories).FirstOrDefault();
                if (vfsFile == null)
                {
                    throw new FileNotFoundException("Não foi possível encontrar 'vfs.dll' no ficheiro LML baixado. A estrutura do ficheiro pode ter mudado.");
                }

                string sourceFolder = Path.GetDirectoryName(vfsFile);

                Console.WriteLine($"A copiar ficheiros para a pasta do GTA V...");
                DirectoryCopy(sourceFolder, gtaPath, true);

                Console.WriteLine("\nInstalação do LML concluída com sucesso!");
                Console.WriteLine("Pressione Enter para continuar.");
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nOcorreu um erro ao instalar o LML: {ex.Message}");
                Console.WriteLine("Pressione Enter para continuar.");
                Console.ReadLine();
            }
            finally
            {
                if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
                if (Directory.Exists(tempExtractPath)) Directory.Delete(tempExtractPath, true);
            }
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException("O diretório de origem não existe ou não pôde ser encontrado: " + sourceDirName);
            }

            Directory.CreateDirectory(destDirName);

            foreach (FileInfo file in dir.GetFiles())
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, true);
            }

            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dir.GetDirectories())
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs);
                }
            }
        }
    }
}

