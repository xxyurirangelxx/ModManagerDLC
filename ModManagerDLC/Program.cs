using ModManagerDLC.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DLCtoLML
{
    internal static class Program
    {
        private static readonly DlcInstaller _installer = new DlcInstaller();
        private static readonly LmlInstaller _lmlInstaller = new LmlInstaller();
        private static readonly Dictionary<string, string> _handlingFiles = new Dictionary<string, string>();
        private static string _gtaPath;

        [STAThread] // Essencial para usar o seletor de ficheiros
        static async Task Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            LoadHandlingFiles();
            LoadGtaPath();

            // CORREÇÃO: A verificação do LML foi movida para o início, para ser executada sempre.
            if (!string.IsNullOrEmpty(_gtaPath))
            {
                await _lmlInstaller.CheckAndInstallLmlAsync(_gtaPath);
            }

            // Permite instalar arrastando um link para cima do .exe
            if (args.Length > 0)
            {
                // A verificação já foi feita, então apenas processamos a URL.
                await ProcessUrl(args[0], args.Length > 1 ? args[1] : null);
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
                Console.WriteLine("2. Selecionar Caminho do GTA V");
                Console.WriteLine("3. Sair");
                Console.Write("\nOpção: ");

                switch (Console.ReadLine())
                {
                    case "1":
                        await HandleUrlInstallation();
                        break;
                    case "2":
                        SetGtaPath();
                        // Após definir um novo caminho, verifica novamente o LML
                        if (!string.IsNullOrEmpty(_gtaPath))
                        {
                            await _lmlInstaller.CheckAndInstallLmlAsync(_gtaPath);
                        }
                        break;
                    case "3":
                        return;
                    default:
                        Console.WriteLine("Opção inválida. Pressione Enter para continuar.");
                        Console.ReadLine();
                        break;
                }
            }
        }

        private static async Task HandleUrlInstallation()
        {
            if (!CheckGtaPath()) return;

            // A verificação do LML já foi feita no início.

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

