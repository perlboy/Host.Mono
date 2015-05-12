using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Host.Library;
using System.Diagnostics;
using System.Collections.ObjectModel;
using Integrator.Hub.Models;
using Newtonsoft.Json;

namespace Host
{
	class Program
	{
		static HostService _hostService = null;

		public static void Main(string[] args)
		{
            Guid orgId = Guid.Empty;
            System.Console.WriteLine("Welcome to the microServiceBus Console Host");
            System.Console.WriteLine();
            
            if (ConfigurationManager.AppSettings["OrganizationId"] == null)
            {
                System.Console.Write("Secret>");
                var secret = System.Console.ReadLine();
                UpdateAppSettings("OrganizationId", secret);
                orgId = Guid.Parse(secret);
            }
			bool exit = false;
			Console.Write("Host name>");
			var hostName = Console.ReadLine();

			_hostService = new HostService(hostName);
			_hostService.OnLogEvent += _hostService_OnLogEvent;
			_hostService.Connect();
			_hostService.SignIn(hostName);

			Console.WriteLine("Press any key to stop service");

			while (!exit)
			{
				ConsoleKeyInfo key = Console.ReadKey();
				switch (key.Key)
				{
				case ConsoleKey.C:
					Console.Clear();
					break;
				default:
					exit = true;
					break;
				}
			}
		}
		static void _hostService_OnLogEvent(string message, HostService.LogLevel logLevel)
		{
			switch (logLevel)
			{
			case HostService.LogLevel.Info:
				Console.WriteLine(message);
				break;
			case HostService.LogLevel.Warning:
				Console.WriteLine("WARNING:" + message);
				break;
			case HostService.LogLevel.Error:
				Console.WriteLine("ERROR:" + message);
				break;
			default:
				break;
			}
		}
        static public void UpdateAppSettings(string key, string value)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            config.AppSettings.Settings.Remove(key);
            config.AppSettings.Settings.Add(key, value);
            config.Save();
            ConfigurationManager.RefreshSection("applicationSettings");
        }
	}
}
