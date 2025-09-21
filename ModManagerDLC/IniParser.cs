using System;
using System.Collections.Generic;
using System.IO;

namespace ModManagerDLC
{
    public class DlcConfig
    {
        public string SpawnName { get; set; }
        public string Author { get; set; }
        public string Version { get; set; }
        public string HandlingPreset { get; set; }
        public Dictionary<string, string> HandlingValues { get; set; }
        public bool HasEls { get; set; }

        // Novas propriedades para RPF
        public string DlcName { get; set; }
        public bool IsVehiclePack { get; set; }
        public Dictionary<string, string> VehicleEls { get; set; }


        public DlcConfig()
        {
            HandlingValues = new Dictionary<string, string>();
            VehicleEls = new Dictionary<string, string>();
            HasEls = false;
            IsVehiclePack = false;
        }
    }

    public static class IniParser
    {
        public static DlcConfig Parse(string filePath)
        {
            var config = new DlcConfig();
            if (!File.Exists(filePath)) return null;

            string currentSection = "";
            foreach (var line in File.ReadAllLines(filePath))
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith(";") || string.IsNullOrEmpty(trimmedLine)) continue;

                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2).ToLower();
                }
                // ... código anterior sem alterações ...

                else if (trimmedLine.Contains("="))
                {
                    var parts = trimmedLine.Split(new[] { '=' }, 2);
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    switch (currentSection)
                    {
                        case "metadata":
                            if (key.Equals("SpawnName", StringComparison.OrdinalIgnoreCase)) config.SpawnName = value;
                            if (key.Equals("Author", StringComparison.OrdinalIgnoreCase)) config.Author = value;
                            if (key.Equals("Version", StringComparison.OrdinalIgnoreCase)) config.Version = value;
                            if (key.Equals("HasEls", StringComparison.OrdinalIgnoreCase))
                            {
                                bool.TryParse(value, out bool hasEls);
                                config.HasEls = hasEls;
                            }
                            break;

                        case "handling":
                            if (key.Equals("Preset", StringComparison.OrdinalIgnoreCase))
                            {
                                config.HandlingPreset = value;
                            }
                            else
                            {
                                config.HandlingValues[key] = value;
                            }
                            break;

                        case "rpf_settings":
                            if (key.Equals("DlcName", StringComparison.OrdinalIgnoreCase)) config.DlcName = value;
                            // CORREÇÃO: Adicionada a leitura do SpawnName também nesta seção.
                            if (key.Equals("SpawnName", StringComparison.OrdinalIgnoreCase)) config.SpawnName = value;
                            if (key.Equals("IsVehiclePack", StringComparison.OrdinalIgnoreCase))
                            {
                                bool.TryParse(value, out bool isPack);
                                config.IsVehiclePack = isPack;
                            }
                            if (key.Equals("HasEls", StringComparison.OrdinalIgnoreCase))
                            {
                                bool.TryParse(value, out bool hasEls);
                                config.HasEls = hasEls;
                            }
                            break;

                        case "vehicles":
                            config.VehicleEls[key] = value;
                            break;
                    }
                }
            }
            return config;
        }
    }
}