using ModManagerDLC.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;

namespace DLCtoLML
{
    internal static class Program
    {
        private static readonly DlcInstaller _installer = new DlcInstaller();
        private static readonly LmlInstaller _lmlInstaller = new LmlInstaller();
        private static readonly AutoUpdater _updater = new AutoUpdater();
        private static readonly Dictionary<string, string> _handlingFiles = new Dictionary<string, string>();
        private static string _gtaPath;

        [STAThread]
        static async Task Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            // Regista o protocolo personalizado (ex: modmanagerdlc://) no Windows
            ProtocolHandler.Register();

            await _updater.CheckForUpdatesAsync();

            LoadHandlingFiles();
            LoadGtaPath();

            if (!string.IsNullOrEmpty(_gtaPath))
            {
                await _lmlInstaller.CheckAndInstallLmlAsync(_gtaPath);
            }

            // Verifica se a aplicação foi iniciada por um link personalizado
            if (args.Length > 0 && args[0].StartsWith("modmanagerdlc://"))
            {
                Console.WriteLine("Link personalizado detectado. A processar...");
                try
                {
                    var uri = new Uri(args[0]);
                    var query = HttpUtility.ParseQueryString(uri.Query);

                    string url = query.Get("link");
                    string metadataUrl = query.Get("metadata");

                    if (string.IsNullOrEmpty(url))
                    {
                        Console.WriteLine("ERRO: O parâmetro 'link' está em falta no URL personalizado.");
                    }
                    else
                    {
                        await ProcessUrl(url, metadataUrl);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERRO ao processar o link personalizado: {ex.Message}");
                }

                Console.WriteLine("\nPressione Enter para sair.");
                Console.ReadLine();
                return;
            }

            while (true)
            {
                Console.Clear();
                Console.WriteLine("===================================");
                Console.WriteLine("      Mod Manager DLC - Console");
                Console.WriteLine("===================================");
                Console.WriteLine($"Caminho do GTA V: {_gtaPath ?? "Não definido"}\n");
                Console.WriteLine("Escolha uma opção:");
                Console.WriteLine("1. Instalar Mod pela URL");
                Console.WriteLine("2. Instalar Mod Localmente");
                Console.WriteLine("3. Selecionar Caminho do GTA V");
                Console.WriteLine("4. Sair");
                Console.Write("\nOpção: ");

                switch (Console.ReadLine())
                {
                    case "1":
                        await HandleUrlInstallation();
                        break;
                    case "2":
                        await HandleLocalInstallation();
                        break;
                    case "3":
                        SetGtaPath();
                        if (!string.IsNullOrEmpty(_gtaPath))
                        {
                            await _lmlInstaller.CheckAndInstallLmlAsync(_gtaPath);
                        }
                        break;
                    case "4":
                        return;
                    default:
                        Console.WriteLine("Opção inválida. Pressione Enter para continuar.");
                        Console.ReadLine();
                        break;
                }
            }
        }

        private static async Task HandleLocalInstallation()
        {
            if (!CheckGtaPath()) return;

            string selectedFile = null;
            var thread = new Thread(() =>
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = "Selecione o arquivo .zip ou .rpf do mod";
                    ofd.Filter = "Arquivos de Mod (*.zip;*.rpf)|*.zip;*.rpf";
                    ofd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        selectedFile = ofd.FileName;
                    }
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (!string.IsNullOrEmpty(selectedFile))
            {
                await ProcessFile(selectedFile);
            }
            else
            {
                Console.WriteLine("\nNenhum ficheiro selecionado.");
            }

            Console.WriteLine("\nPressione Enter para voltar ao menu.");
            Console.ReadLine();
        }


        private static async Task HandleUrlInstallation()
        {
            if (!CheckGtaPath()) return;

            Console.WriteLine("\n--- Instalar Mod pela URL ---");
            Console.Write("Cole o link direto para o ficheiro (.zip ou .rpf): ");
            string url = Console.ReadLine()?.Trim();
            string metadataUrl = null;

            if (string.IsNullOrEmpty(url))
            {
                Console.WriteLine("Nenhuma URL inserida.");
            }
            else
            {
                if (url.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Write("Cole o link para o metadado .zip (opcional, para ELS): ");
                    metadataUrl = Console.ReadLine()?.Trim();
                }
                await ProcessUrl(url, metadataUrl);
            }

            Console.WriteLine("\nPressione Enter para voltar ao menu.");
            Console.ReadLine();
        }

        private static async Task ProcessUrl(string url, string metadataUrl)
        {
            if (!CheckGtaPath()) return;

            if (url.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                await RunInstaller(async (progress) =>
                {
                    await _installer.InstallDlcFromUrlAsync(_gtaPath, url, metadataUrl, progress);
                }, "A baixar e instalar .rpf...");
            }
            else if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || url.StartsWith("dlctolml:", StringComparison.OrdinalIgnoreCase))
            {
                await RunInstaller(async (progress) =>
                {
                    await _installer.InstallFromVehicleUrlAsync(_gtaPath, url, _handlingFiles, progress);
                }, "A baixar e instalar .zip...");
            }
            else
            {
                Console.WriteLine("\nERRO: URL inválida. O link deve terminar em .zip ou .rpf.");
            }
        }

        private static async Task ProcessFile(string filePath)
        {
            if (!CheckGtaPath()) return;

            if (filePath.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                await RunInstaller(async (progress) =>
                {
                    await _installer.InstallDlcFromFileAsync(_gtaPath, filePath, progress);
                }, "A instalar .rpf...");
            }
            else if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await RunInstaller(async (progress) =>
                {
                    await _installer.InstallFromVehicleFileAsync(_gtaPath, filePath, _handlingFiles, progress);
                }, "A instalar .zip...");
            }
            else
            {
                Console.WriteLine("\nERRO: Ficheiro inválido. Apenas ficheiros .zip ou .rpf são suportados.");
            }
        }

        private static void SetGtaPath()
        {
            string selectedFile = null;
            var thread = new Thread(() =>
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = "Selecione o executável GTA5.exe";
                    ofd.Filter = "GTA5 Executable (GTA5.exe)|GTA5.exe";
                    ofd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyComputer);

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        selectedFile = ofd.FileName;
                    }
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (!string.IsNullOrEmpty(selectedFile))
            {
                string gtaDirectory = Path.GetDirectoryName(selectedFile);
                if (Directory.Exists(gtaDirectory) && File.Exists(Path.Combine(gtaDirectory, "GTA5.exe")))
                {
                    _gtaPath = gtaDirectory;
                    string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
                    File.WriteAllText(settingsPath, _gtaPath);
                    Console.WriteLine($"\nCaminho do GTA V guardado com sucesso: {_gtaPath}");
                }
                else
                {
                    Console.WriteLine("\nCaminho inválido. A pasta selecionada não contém o GTA5.exe.");
                }
            }
            else
            {
                Console.WriteLine("\nNenhum ficheiro selecionado.");
            }
        }

        private static void LoadGtaPath()
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            if (File.Exists(settingsPath))
            {
                _gtaPath = File.ReadAllText(settingsPath).Trim();
                if (!Directory.Exists(_gtaPath) || !File.Exists(Path.Combine(_gtaPath, "GTA5.exe")))
                {
                    Console.WriteLine("O caminho do GTA V guardado é inválido. Por favor, defina-o novamente.");
                    _gtaPath = null;
                }
            }
        }

        private static bool CheckGtaPath()
        {
            if (string.IsNullOrEmpty(_gtaPath))
            {
                Console.WriteLine("\nO caminho do GTA V não está definido. Por favor, selecione-o primeiro no menu principal.");
                Console.WriteLine("Pressione Enter para continuar.");
                Console.ReadLine();
                return false;
            }
            return true;
        }

        private static async Task RunInstaller(Func<IProgress<int>, Task> installAction, string installingText = "A instalar...")
        {
            Console.WriteLine($"\n{installingText}");
            var progress = new Progress<int>(value =>
            {
                value = Math.Min(value, 100);
                Console.Write($"\rProgresso: {value}% ");
            });

            try
            {
                await installAction(progress);
                Console.WriteLine("\n\nOperação concluída com sucesso!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n\nERRO: {ex.Message}");
            }
        }

        private static void LoadHandlingFiles()
        {
            _handlingFiles.Clear();
            _handlingFiles.Add("Chevrolet Trailblazer", Resources.handling_trail);
            _handlingFiles.Add("Chevrolet S10", Resources.handling_s10);
            _handlingFiles.Add("Renault Duster", Resources.handling_duster);
        }
    }
}
