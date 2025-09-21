using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace DLCtoLML
{
    public static class ProtocolHandler
    {
        private const string ProtocolName = "modmanagerdlc";

        public static void Register()
        {
            try
            {
                // Abre a chave HKEY_CLASSES_ROOT, que é onde os protocolos são registados
                RegistryKey key = Registry.ClassesRoot.OpenSubKey(ProtocolName, true);

                // Se a chave não existir, cria-a
                if (key == null)
                {
                    key = Registry.ClassesRoot.CreateSubKey(ProtocolName);
                }

                string currentExePath = Process.GetCurrentProcess().MainModule.FileName;

                // Define os valores necessários para o protocolo
                key.SetValue("", $"URL:{ProtocolName} Protocol");
                key.SetValue("URL Protocol", "");

                // Cria a estrutura de chaves para o comando de execução
                // Ex: HKEY_CLASSES_ROOT\modmanagerdlc\shell\open\command
                RegistryKey commandKey = key.CreateSubKey(@"shell\open\command");

                // Define o valor do comando para executar a sua aplicação, passando o link como argumento
                // O "%1" é o placeholder para o link completo que foi clicado
                commandKey.SetValue("", $"\"{currentExePath}\" \"%1\"");

                Console.WriteLine("Protocolo de URL personalizado registado com sucesso.");
                commandKey.Close();
                key.Close();
            }
            catch (UnauthorizedAccessException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\nAviso: Não foi possível registar o protocolo de URL personalizado.");
                Console.WriteLine("Por favor, execute a aplicação como administrador uma vez para ativar esta funcionalidade.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nOcorreu um erro inesperado ao registar o protocolo: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}
