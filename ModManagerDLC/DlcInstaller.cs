using ModManagerDLC;
using ModManagerDLC.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;

namespace DLCtoLML
{
    public class DlcInstaller
    {
        /// <summary>
        /// Método robusto para baixar um arquivo com múltiplas tentativas.
        /// </summary>
        private async Task DownloadFileAsync(HttpClient client, string url, string outputPath, IProgress<int> progress, int progressStart, int progressEnd)
        {
            int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    Console.WriteLine($"\nTentando baixar de: {url}");
                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        long totalBytesRead = 0;
                        var buffer = new byte[8192];
                        int bytesRead;
                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;
                            if (totalBytes > 0)
                            {
                                int currentProgress = progressStart + (int)((double)totalBytesRead / totalBytes * (progressEnd - progressStart));
                                progress.Report(currentProgress);
                            }
                        }
                    }
                    Console.WriteLine($"\nDownload de {Path.GetFileName(outputPath)} concluído com sucesso.");
                    return; // Sai do loop se o download for bem-sucedido
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"\nErro na tentativa {i + 1} de {maxRetries}: {ex.Message}");
                    if (i == maxRetries - 1) throw; // Lança a exceção na última tentativa
                    await Task.Delay(2000); // Espera 2 segundos antes de tentar novamente
                }
            }
        }

        public async Task InstallDlcFromFileAsync(string gtaPath, string rpfPath, IProgress<int> progress)
        {
            if (!File.Exists(rpfPath))
                throw new FileNotFoundException("O arquivo RPF não foi encontrado.", rpfPath);

            string dlcName = Path.GetFileNameWithoutExtension(rpfPath);
            string packageName = Sanitize(dlcName);
            string packageRoot = Path.Combine(gtaPath, "lml", packageName);

            Directory.CreateDirectory(packageRoot);
            await Task.Run(() => File.Copy(rpfPath, Path.Combine(packageRoot, "dlc.rpf"), true));

            await CreateLmlStructureForDlc(gtaPath, packageName, packageRoot, progress, 50);
        }

        public async Task InstallFromVehicleFileAsync(string gtaPath, string zipPath, Dictionary<string, string> handlingFiles, IProgress<int> progress)
        {
            if (!File.Exists(zipPath))
                throw new FileNotFoundException("O arquivo ZIP não foi encontrado.", zipPath);

            string tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                progress.Report(10);
                Directory.CreateDirectory(tempExtractPath);
                ZipFile.ExtractToDirectory(zipPath, tempExtractPath);
                progress.Report(25);

                var vehicleFolder = FindVehicleFolder(tempExtractPath);
                if (vehicleFolder == null)
                    throw new Exception("Não foi possível identificar um mod de veículo válido no arquivo .zip.");

                string configPath = Path.Combine(tempExtractPath, "dlc_config.ini");
                if (!File.Exists(configPath))
                    configPath = Path.Combine(vehicleFolder, "dlc_config.ini");

                DlcConfig config = File.Exists(configPath) ? IniParser.Parse(configPath) : null;

                string spawnName = config?.SpawnName ?? new DirectoryInfo(vehicleFolder).Name;
                string handlingContent = handlingFiles.First().Value;

                if (config != null && !string.IsNullOrEmpty(config.HandlingPreset) && handlingFiles.ContainsKey(config.HandlingPreset))
                {
                    handlingContent = handlingFiles[config.HandlingPreset];
                }

                await InstallFromFilesAsync(gtaPath, vehicleFolder, spawnName, handlingContent, progress, config);
            }
            finally
            {
                if (Directory.Exists(tempExtractPath)) Directory.Delete(tempExtractPath, true);
            }
        }

        public async Task InstallDlcFromUrlAsync(string gtaPath, string dlcUrl, string metadataUrl, IProgress<int> progress)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            if (!Directory.Exists(gtaPath) || !File.Exists(Path.Combine(gtaPath, "GTA5.exe")))
                throw new ArgumentException("O caminho do GTA V parece inválido.");

            string tempRpfPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".rpf");
            string tempMetadataZipPath = null;
            string tempMetadataExtractPath = null;

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                    // Baixar RPF usando o método robusto
                    await DownloadFileAsync(client, dlcUrl, tempRpfPath, progress, 0, 40);

                    DlcConfig config = null;
                    if (!string.IsNullOrEmpty(metadataUrl))
                    {
                        tempMetadataZipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".zip");
                        tempMetadataExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

                        // Baixar Metadados usando o MESMO método robusto
                        await DownloadFileAsync(client, metadataUrl, tempMetadataZipPath, progress, 40, 45);

                        progress.Report(50);
                        Directory.CreateDirectory(tempMetadataExtractPath);
                        ZipFile.ExtractToDirectory(tempMetadataZipPath, tempMetadataExtractPath);

                        string configPath = Path.Combine(tempMetadataExtractPath, "dlc_config.ini");
                        if (File.Exists(configPath))
                        {
                            config = IniParser.Parse(configPath);
                            await InstallElsFromMetadata(gtaPath, config, tempMetadataExtractPath);
                        }
                    }

                    string dlcName = config?.DlcName ?? Path.GetFileNameWithoutExtension(new Uri(dlcUrl).AbsolutePath);
                    string packageName = Sanitize(dlcName);
                    string packageRoot = Path.Combine(gtaPath, "lml", packageName);

                    Directory.CreateDirectory(packageRoot);
                    await Task.Run(() => File.Move(tempRpfPath, Path.Combine(packageRoot, "dlc.rpf")));

                    await CreateLmlStructureForDlc(gtaPath, packageName, packageRoot, progress, 50);
                }
            }
            finally
            {
                if (File.Exists(tempRpfPath)) File.Delete(tempRpfPath);
                if (File.Exists(tempMetadataZipPath)) File.Delete(tempMetadataZipPath);
                if (Directory.Exists(tempMetadataExtractPath)) Directory.Delete(tempMetadataExtractPath, true);
            }
        }

        public async Task InstallDlcFromUrlAsync(string gtaPath, string dlcUrl, IProgress<int> progress)
        {
            await InstallDlcFromUrlAsync(gtaPath, dlcUrl, null, progress);
        }

        public async Task InstallFromVehicleUrlAsync(string gtaPath, string url, Dictionary<string, string> handlingFiles, IProgress<int> progress)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            string downloadUrl;
            if (url.StartsWith("dlctolml:", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                downloadUrl = HttpUtility.ParseQueryString(uri.Query).Get("url");
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new Exception("O link de instalação (dlctolml://) não contém uma URL de download válida.");
                }
            }
            else
            {
                downloadUrl = url;
            }

            string tempZipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".zip");
            string tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    await DownloadFileAsync(client, downloadUrl, tempZipPath, progress, 0, 50);
                }

                progress.Report(55);
                Directory.CreateDirectory(tempExtractPath);
                ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);
                progress.Report(60);

                var vehicleFolder = FindVehicleFolder(tempExtractPath);
                if (vehicleFolder == null)
                    throw new Exception("Não foi possível identificar um mod de veículo válido no arquivo .zip baixado.");

                string configPath = Path.Combine(tempExtractPath, "dlc_config.ini");
                if (!File.Exists(configPath))
                    configPath = Path.Combine(vehicleFolder, "dlc_config.ini");

                DlcConfig config = File.Exists(configPath) ? IniParser.Parse(configPath) : null;

                string spawnName = config?.SpawnName ?? new DirectoryInfo(vehicleFolder).Name;
                string handlingContent = handlingFiles.First().Value;

                if (config != null && !string.IsNullOrEmpty(config.HandlingPreset) && handlingFiles.ContainsKey(config.HandlingPreset))
                {
                    handlingContent = handlingFiles[config.HandlingPreset];
                }

                await InstallFromFilesAsync(gtaPath, vehicleFolder, spawnName, handlingContent, progress, config);
            }
            finally
            {
                if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
                if (Directory.Exists(tempExtractPath)) Directory.Delete(tempExtractPath, true);
            }
        }


        private async Task InstallElsFromMetadata(string gtaPath, DlcConfig config, string metadataExtractPath)
        {
            string elsSourceFolder = Path.Combine(metadataExtractPath, "els");
            if (!Directory.Exists(elsSourceFolder)) return;

            string elsTargetFolder = Path.Combine(gtaPath, "ELS", "pack_default");
            Directory.CreateDirectory(elsTargetFolder);

            if (config.IsVehiclePack)
            {
                foreach (var vehicle in config.VehicleEls)
                {
                    // Verifica se o nome do arquivo no .ini não está vazio
                    if (string.IsNullOrWhiteSpace(vehicle.Value)) continue;

                    string sourceFile = Path.Combine(elsSourceFolder, vehicle.Value);
                    if (File.Exists(sourceFile))
                    {
                        // CORREÇÃO: Usa o nome do arquivo original (o valor do .ini) como destino, sem renomear.
                        string destFileName = Sanitize(Path.GetFileName(vehicle.Value));
                        string destFile = Path.Combine(elsTargetFolder, destFileName);
                        await Task.Run(() => File.Copy(sourceFile, destFile, true));
                    }
                }
            }
            else if (config.HasEls)
            {
                // Validação crucial para RPF de veículo único
                if (string.IsNullOrWhiteSpace(config.SpawnName))
                {
                    Console.WriteLine("\nAVISO: 'HasEls' está como true, mas 'SpawnName' não foi definido no dlc_config.ini. O arquivo ELS não pôde ser copiado.");
                    return;
                }

                string elsFile = Directory.GetFiles(elsSourceFolder, "*.xml").FirstOrDefault();
                if (elsFile != null)
                {
                    // Sanitiza o SpawnName para garantir um nome de arquivo válido
                    string sanitizedSpawnName = Sanitize(config.SpawnName.ToLower());
                    string destFile = Path.Combine(elsTargetFolder, $"{sanitizedSpawnName}.xml");
                    await Task.Run(() => File.Copy(elsFile, destFile, true));
                }
            }
        }

        private string FindVehicleFolder(string rootPath)
        {
            var directories = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories);
            foreach (var dir in directories)
            {
                var files = Directory.GetFiles(dir);
                if (files.Any(f => f.EndsWith(".yft", StringComparison.OrdinalIgnoreCase)) && files.Any(f => f.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)))
                    return dir;
            }
            var rootFiles = Directory.GetFiles(rootPath);
            if (rootFiles.Any(f => f.EndsWith(".yft", StringComparison.OrdinalIgnoreCase)) && rootFiles.Any(f => f.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)))
                return rootPath;
            return null;
        }

        private async Task CreateLmlStructureForDlc(string gtaPath, string packageName, string packagePath, IProgress<int> progress, int progressStart = 30)
        {
            progress.Report(progressStart + 20);

            var installXml = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement("EasyInstall",
                    new XElement("Name", packageName),
                    new XElement("Author", "Criado por DLCtoLML"),
                    new XElement("Version", "1.0"),
                    new XElement("Resources",
                        new XElement("Resource",
                            new XAttribute("name", packageName),
                            new XElement("DlcRpf", "dlc.rpf")
                        )
                    )
                )
            );

            await Task.Run(() => installXml.Save(Path.Combine(packagePath, "install.xml")));
            progress.Report(progressStart + 60);

            AddOrUpdateModInModsXml(gtaPath, packageName, packageName);

            progress.Report(100);
        }

        public async Task InstallFromFilesAsync(string gtaPath, string vehicleFolderPath, string spawnName, string handlingContent, IProgress<int> progress, DlcConfig config = null)
        {
            string finalSpawnName = config?.SpawnName ?? spawnName;
            string author = config?.Author ?? "Criado por DLCtoLML";
            string version = config?.Version ?? "1.0";

            if (string.IsNullOrWhiteSpace(finalSpawnName))
                throw new ArgumentException("O nome para spawn não pode estar vazio.");
            if (!Directory.Exists(vehicleFolderPath))
                throw new ArgumentException("A pasta do veículo selecionada é inválida.");

            var vehicleFiles = Directory.GetFiles(vehicleFolderPath);
            if (!vehicleFiles.Any(f => f.EndsWith(".yft")) || !vehicleFiles.Any(f => f.EndsWith(".ytd")))
                throw new FileNotFoundException("A pasta selecionada não contém os arquivos .yft e .ytd necessários.");

            progress.Report(65);
            string sanitizedSpawnName = Sanitize(finalSpawnName.ToLower());
            string packageName = $"_{sanitizedSpawnName}";
            string packageRoot = Path.Combine(gtaPath, "lml", packageName);
            string streamFolder = Path.Combine(packageRoot, "stream");
            string dataFolder = Path.Combine(packageRoot, "data");

            Directory.CreateDirectory(streamFolder);
            Directory.CreateDirectory(dataFolder);
            progress.Report(70);


            await Task.Run(() =>
            {
                CopyAndRenameFile(vehicleFiles, ".yft", streamFolder, $"{sanitizedSpawnName}.yft");
                CopyAndRenameFile(vehicleFiles, "_hi.yft", streamFolder, $"{sanitizedSpawnName}_hi.yft", false);
                CopyAndRenameFile(vehicleFiles, ".ytd", streamFolder, $"{sanitizedSpawnName}.ytd");
            });
            progress.Report(80);

            await Task.Run(() =>
            {
                if (config != null && config.HandlingValues.Any())
                {
                    File.WriteAllText(Path.Combine(dataFolder, "handling.meta"), GenerateHandlingMetaFromConfig(sanitizedSpawnName, config.HandlingValues));
                }
                else
                {
                    XDocument handlingDoc = XDocument.Parse(handlingContent);
                    var handlingItem = handlingDoc.Descendants("Item").FirstOrDefault(el => (string)el.Attribute("type") == "CHandlingData");
                    if (handlingItem != null)
                    {
                        handlingItem.Element("handlingName").Value = sanitizedSpawnName;
                    }
                    handlingDoc.Save(Path.Combine(dataFolder, "handling.meta"));
                }

                File.WriteAllText(Path.Combine(dataFolder, "vehicles.meta"), GenerateVehiclesMeta(sanitizedSpawnName));
                File.WriteAllText(Path.Combine(dataFolder, "carvariations.meta"), GenerateCarvariationsMeta(sanitizedSpawnName));
                File.WriteAllText(Path.Combine(dataFolder, "carcols.meta"), GenerateCarcolsMeta(sanitizedSpawnName));
                File.WriteAllText(Path.Combine(packageRoot, "install.xml"), GenerateInstallXml(packageName, sanitizedSpawnName, author, version));

                AddOrUpdateModInModsXml(gtaPath, packageName, sanitizedSpawnName);
            });
            progress.Report(95);

            bool elsGeneratedByIni = config?.HasEls ?? false;
            if (!elsGeneratedByIni)
            {
                string elsFile = vehicleFiles.FirstOrDefault(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(f).ToLower() != "carvariations");
                if (!string.IsNullOrEmpty(elsFile))
                {
                    string elsTargetFolder = Path.Combine(gtaPath, "ELS", "pack_default");
                    Directory.CreateDirectory(elsTargetFolder);
                    await Task.Run(() => File.Copy(elsFile, Path.Combine(elsTargetFolder, $"{sanitizedSpawnName}.xml"), true));
                }
            }
            progress.Report(100);
        }

        private void AddOrUpdateModInModsXml(string gtaPath, string folderName, string modName)
        {
            string modsXmlPath = Path.Combine(gtaPath, "lml", "mods.xml");
            XDocument modsXml;

            if (File.Exists(modsXmlPath))
            {
                try { modsXml = XDocument.Load(modsXmlPath); }
                catch { modsXml = new XDocument(new XElement("ModsManager")); }
            }
            else
            {
                modsXml = new XDocument(new XElement("ModsManager"));
            }

            var root = modsXml.Root;
            if (root == null)
            {
                root = new XElement("ModsManager");
                modsXml.Add(root);
            }

            var modsNode = root.Element("Mods");
            if (modsNode == null)
            {
                modsNode = new XElement("Mods");
                root.Add(modsNode);
            }

            var modElement = modsNode.Elements("Mod").FirstOrDefault(m => m.Attribute("folder")?.Value == folderName);

            if (modElement == null)
            {
                modElement = new XElement("Mod",
                    new XAttribute("folder", folderName),
                    new XElement("Name", modName),
                    new XElement("Enabled", "true"),
                    new XElement("Overwrite", "false"),
                    new XElement("DisabledGroups")
                );
                modsNode.Add(modElement);
            }
            else
            {
                modElement.Element("Enabled").Value = "true";
            }

            var loadOrderNode = root.Element("LoadOrder");
            if (loadOrderNode == null)
            {
                loadOrderNode = new XElement("LoadOrder");
                root.Add(loadOrderNode);
            }

            var loadOrderModElement = loadOrderNode.Elements("Mod").FirstOrDefault(m => m.Value == folderName);
            if (loadOrderModElement == null)
            {
                loadOrderNode.Add(new XElement("Mod", folderName));
            }

            modsXml.Save(modsXmlPath);
        }

        private void CopyAndRenameFile(IEnumerable<string> files, string extension, string destFolder, string newName, bool required = true)
        {
            var file = files.FirstOrDefault(f => f.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
            if (file != null)
                File.Copy(file, Path.Combine(destFolder, newName), true);
            else if (required)
                throw new FileNotFoundException($"Arquivo com extensão {extension} não encontrado.");
        }

        private string GenerateHandlingMetaFromConfig(string spawnName, Dictionary<string, string> values)
        {
            var baseHandling = XDocument.Parse(Resources.handling_s10);
            var handlingItem = baseHandling.Descendants("Item").First(el => (string)el.Attribute("type") == "CHandlingData");

            handlingItem.Element("handlingName").Value = spawnName;

            foreach (var pair in values)
            {
                var element = handlingItem.Element(pair.Key);
                if (element != null && element.Attribute("value") != null)
                {
                    element.Attribute("value").Value = pair.Value;
                }
            }
            return baseHandling.ToString();
        }

        private string GenerateInstallXml(string packageName, string spawnName, string author, string version) =>
            $@"<EasyInstall>
    <Name>{packageName}</Name>
    <Author>{author}</Author>
    <Version>{version}</Version>
    <Metadata>
        <VehicleType>Police</VehicleType>
        <ModelStatus>Unlocked</ModelStatus>
        <ELSCompatibility>ELS</ELSCompatibility>
        <CompletionState>Complete</CompletionState>
    </Metadata>
    <Addons>
        <Addon name=""{spawnName}"">
            <StreamingFiles>stream</StreamingFiles>
            <DataFiles>data</DataFiles>
        </Addon>
    </Addons>
</EasyInstall>";

        private string GenerateVehiclesMeta(string modelName) =>
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<CVehicleModelInfo__InitDataList>
	<residentTxd>vehshare</residentTxd>
	<residentAnims />
	<InitDatas>
<Item>
		<modelName>{modelName}</modelName>
		<txdName>{modelName}</txdName>
		<handlingId>{modelName}</handlingId>
		<gameName>{modelName}</gameName>
		<vehicleMakeName />
		<expressionDictName>null</expressionDictName>
		<expressionName>null</expressionName>
		<animConvRoofDictName>null</animConvRoofDictName>
		<animConvRoofName>null</animConvRoofName>
		<animConvRoofWindowsAffected />
		<ptfxAssetName>null</ptfxAssetName>
		<audioNameHash>police</audioNameHash>
		<layout>LAYOUT_STANDARD</layout>
		<coverBoundOffsets>POLICE_COVER_OFFSET_INFO</coverBoundOffsets>
		<explosionInfo>EXPLOSION_INFO_DEFAULT</explosionInfo>
		<scenarioLayout />
		<cameraName>DEFAULT_FOLLOW_VEHICLE_CAMERA</cameraName>
		<aimCameraName>DEFAULT_THIRD_PERSON_VEHICLE_AIM_CAMERA</aimCameraName>
		<bonnetCameraName>VEHICLE_BONNET_CAMERA_MID_HIGH</bonnetCameraName>
		<povCameraName>DEFAULT_POV_CAMERA</povCameraName>
		<FirstPersonDriveByIKOffset x=""0.000000"" y=""-0.060000"" z=""-0.050000"" />
		<FirstPersonDriveByUnarmedIKOffset x=""0.000000"" y=""-0.050000"" z=""-0.020000"" />
		<FirstPersonProjectileDriveByIKOffset x=""0.000000"" y=""-0.075000"" z=""-0.045000"" />
		<FirstPersonProjectileDriveByPassengerIKOffset x=""0.000000"" y=""-0.075000"" z=""-0.045000"" />
		<FirstPersonProjectileDriveByRearLeftIKOffset x=""0.000000"" y=""0.020000"" z=""0.030000"" />
		<FirstPersonProjectileDriveByRearRightIKOffset x=""0.000000"" y=""0.020000"" z=""0.030000"" />
		<FirstPersonDriveByLeftPassengerIKOffset x=""0.000000"" y=""0.000000"" z=""0.000000"" />
		<FirstPersonDriveByRightPassengerIKOffset x=""0.000000"" y=""-0.060000"" z=""-0.060000"" />
		<FirstPersonDriveByRightRearPassengerIKOffset x=""0.000000"" y=""0.000000"" z=""0.000000"" />
		<FirstPersonDriveByLeftPassengerUnarmedIKOffset x=""0.000000"" y=""0.000000"" z=""0.000000"" />
		<FirstPersonDriveByRightPassengerUnarmedIKOffset x=""0.000000"" y=""0.000000"" z=""0.000000"" />
		<FirstPersonMobilePhoneOffset x=""0.125000"" y=""0.293000"" z=""0.516000"" />
		<FirstPersonPassengerMobilePhoneOffset x=""0.136000"" y=""0.223000"" z=""0.415000"" />
		<FirstPersonMobilePhoneSeatIKOffset>
	<Item>
				<Offset x=""0.136000"" y=""0.146000"" z=""0.435000"" />
				<SeatIndex value=""2"" />
	</Item>
	<Item>
				<Offset x=""0.136000"" y=""0.146000"" z=""0.435000"" />
				<SeatIndex value=""3"" />
	</Item>
		</FirstPersonMobilePhoneSeatIKOffset>
		<PovCameraOffset x=""0.000000"" y=""-0.190000"" z=""0.650000"" />
		<PovCameraVerticalAdjustmentForRollCage value=""0.000000"" />
		<PovPassengerCameraOffset x=""0.000000"" y=""0.000000"" z=""0.000000"" />
		<PovRearPassengerCameraOffset x=""0.000000"" y=""0.000000"" z=""0.000000"" />
		<vfxInfoName>VFXVEHICLEINFO_CAR_GENERIC</vfxInfoName>
		<shouldUseCinematicViewMode value=""true"" />
		<shouldCameraTransitionOnClimbUpDown value=""false"" />
		<shouldCameraIgnoreExiting value=""false"" />
		<AllowPretendOccupants value=""true"" />
		<AllowJoyriding value=""false"" />
		<AllowSundayDriving value=""false"" />
		<AllowBodyColorMapping value=""true"" />
		<wheelScale value=""0.236900"" />
		<wheelScaleRear value=""0.236900"" />
		<dirtLevelMin value=""0.000000"" />
		<dirtLevelMax value=""0.300000"" />
		<envEffScaleMin value=""0.000000"" />
		<envEffScaleMax value=""1.000000"" />
		<envEffScaleMin2 value=""0.000000"" />
		<envEffScaleMax2 value=""1.000000"" />
		<damageMapScale value=""0.600000"" />
		<damageOffsetScale value=""1.000000"" />
		<diffuseTint value=""0x00FFFFFF"" />
		<steerWheelMult value=""1.000000"" />
		<HDTextureDist value=""5.000000"" />
		<lodDistances content=""float_array"">
			15.000000
			30.000000
			60.000000
			120.000000
			500.000000
			500.000000
		</lodDistances>
		<minSeatHeight value=""0.839"" />
		<identicalModelSpawnDistance value=""20"" />
		<maxNumOfSameColor value=""10"" />
		<defaultBodyHealth value=""1000.000000"" />
		<pretendOccupantsScale value=""1.000000"" />
		<visibleSpawnDistScale value=""1.000000"" />
		<trackerPathWidth value=""2.000000"" />
		<weaponForceMult value=""1.000000"" />
		<frequency value=""100"" />
		<swankness>SWANKNESS_1</swankness>
		<maxNum value=""2"" />
		<flags>FLAG_HAS_LIVERY FLAG_EXTRAS_REQUIRE FLAG_EXTRAS_STRONG FLAG_LAW_ENFORCEMENT FLAG_EMERGENCY_SERVICE FLAG_NO_RESPRAY FLAG_DONT_SPAWN_IN_CARGEN FLAG_REPORT_CRIME_IF_STANDING_ON</flags>
		<type>VEHICLE_TYPE_CAR</type>
		<plateType>VPT_FRONT_AND_BACK_PLATES</plateType>
		<dashboardType>VDT_GENTAXI</dashboardType>
		<vehicleClass>VC_EMERGENCY</vehicleClass>
		<wheelType>VWT_MUSCLE</wheelType>
		<trailers />
		<additionalTrailers />
		<drivers>
			<Item>
				<driverName>S_M_Y_Cop_01</driverName>
				<npcName />
			</Item>
		</drivers>
		<extraIncludes />
		<doorsWithCollisionWhenClosed />
		<driveableDoors />
		<bumpersNeedToCollideWithMap value=""false"" />
		<needsRopeTexture value=""false"" />
		<requiredExtras />
		<rewards>
			<Item>REWARD_WEAPON_PUMPSHOTGUN</Item>
			<Item>REWARD_AMMO_PUMPSHOTGUN_ENTER_VEHICLE</Item>
			<Item>REWARD_STAT_WEAPON</Item>
		</rewards>
		<cinematicPartCamera>
			<Item>WHEEL_FRONT_RIGHT_CAMERA</Item>
			<Item>WHEEL_FRONT_LEFT_CAMERA</Item>
			<Item>WHEEL_REAR_RIGHT_CAMERA</Item>
			<Item>WHEEL_REAR_LEFT_CAMERA</Item>
		</cinematicPartCamera>
		<NmBraceOverrideSet />
		<buoyancySphereOffset x=""0.000000"" y=""0.000000"" z=""0.000000"" />
		<buoyancySphereSizeScale value=""1.000000"" />
		<pOverrideRagdollThreshold type=""NULL"" />
		<firstPersonDrivebyData>
			<Item>STD_POLICE_FRONT_LEFT</Item>
			<Item>STD_POLICE_FRONT_RIGHT</Item>
	<Item>STD_POLICE_REAR_LEFT</Item>
	<Item>STD_POLICE_REAR_RIGHT</Item>
		</firstPersonDrivebyData>
</Item>
	</InitDatas>
	<txdRelationships>
	<Item>
		<parent>vehicles_poltax_interior</parent>
		<child>{modelName}</child>
	</Item>
	</txdRelationships>
</CVehicleModelInfo__InitDataList>";

        private string GenerateCarvariationsMeta(string modelName) =>
            $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<CVehicleModelInfoVariation>
	<variationData>
		<Item>
		<modelName>{modelName}</modelName>
		<colors>
		<Item>
		<indices content=""char_array"" >
			134
			134
			0
			156
			0
			0
		</indices>
		<liveries>
			<Item value=""false"" />
			<Item value=""false"" />
			<Item value=""false"" />
			<Item value=""false"" />
			<Item value=""false"" />
			<Item value=""false"" />
			<Item value=""false"" />
			<Item value=""false"" />
		</liveries>
		</Item>
		</colors>
		<kits>
		<Item>0_default_modkit</Item>
		</kits>
		<windowsWithExposedEdges />
		<plateProbabilities>
		<Probabilities>
			<Item>
				<Name>Standard White</Name>
				<Value value=""20"" />
			</Item>
			<Item>
				<Name>White Plate 2</Name>
				<Value value=""20"" />
			</Item>
			<Item>
				<Name>Blue Plate</Name>
				<Value value=""20"" />
			</Item>
			<Item>
				<Name>Yellow Plate</Name>
				<Value value=""20"" />
			</Item>
			<Item>
				<Name>yankton plate</Name>
				<Value value=""20"" />
			</Item>
			</Probabilities>
		</plateProbabilities>
		<lightSettings value=""0"" />
		<sirenSettings value=""0"" />
		</Item>
	</variationData>
</CVehicleModelInfoVariation>";

        private string GenerateCarcolsMeta(string modelName) =>
            $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<CVehicleModelInfo__Colours>
  <Kits>
    <Item>
      <id value=""0"" />
      <name>{modelName}_kit_1</name>
      <liveries>
        <Item>
          <id value=""0"" />
          <name>Livery_1</name>
        </Item>
      </liveries>
    </Item>
  </Kits>
</CVehicleModelInfo__Colours>";

        private string Sanitize(string name)
        {
            string invalidChars = new string(Path.GetInvalidFileNameChars());
            string escapedInvalidChars = Regex.Escape(invalidChars);
            string invalidRegex = $@"([{escapedInvalidChars}]*\.+$)|([{escapedInvalidChars}]+)";
            return Regex.Replace(name, invalidRegex, "_").Trim();
        }
    }
}
