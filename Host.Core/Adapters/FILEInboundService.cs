using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;
using Host.Library.Services;
using Newtonsoft.Json.Linq;
using Integrator.Hub.Models;
using Host.Library.Model;
using Newtonsoft.Json;
using Host.Services.Core.Helpers;

namespace Host.Services.Core.Adapters
{
    public class FILEInboundService : OnewayInboundService
    {
        private string _name = "fileInboundService";
        public override string Name
        {
            get
            {
                return _name;
            }
        }
        private string Extension = ".~TEMP";
        private bool scanFileForMimeType;
        private FileSystemWatcher _watcher = new FileSystemWatcher();
        public override async Task Start()
        {
            try
            {
                // Create a new FileSystemWatcher and set its properties.
                _watcher = new FileSystemWatcher();
                string path = this.Config.config.staticConfig.FirstOrDefault(p => p.id == "path").value.ToString();
                string filter = this.Config.config.staticConfig.FirstOrDefault(p => p.id == "filter").value.ToString();
                bool createDirectory = bool.Parse(this.Config.config.staticConfig.FirstOrDefault(p => p.id == "createDirectory").value.ToString());
                var sffmt = this.Config.config.staticConfig.FirstOrDefault(p => p.id == "scanFileForMimeType");

                if (sffmt == null)
                    scanFileForMimeType = true; //Default
                else
                    scanFileForMimeType = bool.Parse(this.Config.config.staticConfig.FirstOrDefault(p => p.id == "scanFileForMimeType").value.ToString());

                if (!Directory.Exists(path) && createDirectory)
                    Directory.CreateDirectory(path);

                _watcher.Path = path;
                /* Watch for changes in LastAccess and LastWrite times, and
                   the renaming of files or directories. */
                _watcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite
                   | NotifyFilters.FileName | NotifyFilters.DirectoryName;
                // Only watch text files.
                _watcher.Filter = filter;

                // Add event handlers.
                _watcher.Changed += new FileSystemEventHandler(OnChanged);
                _watcher.Created += new FileSystemEventHandler(OnChanged);
                _watcher.Renamed += new RenamedEventHandler(OnRenamed);

                // Begin watching.
                _watcher.EnableRaisingEvents = true;

                this.Address = path;

                // Pickup existing files
                foreach (var file in Directory.GetFiles(path, filter))
                {
                    if(Path.GetExtension(file).ToUpper() != Extension)  
                        System.IO.File.Move(file, string.Format("{0}{1}", file, Extension));
                }
                await base.Start();
            }
            catch (Exception ex)
            {
                this.HostChannel.LogEvent(string.Format("Receive Adapter {0} was could not be started. {1}", this.Name, ex.Message));
            }
        }
        public override async Task Stop()
        {
            _watcher.EnableRaisingEvents = false;
            base.Stop();
        }
        public override List<ValidationError> Validate()
        {
            var validationErros = base.Validate();
            var staticConfig = this.Config.config.staticConfig;

            if (staticConfig.FirstOrDefault(g => g.id == "path") == null ||
                staticConfig.FirstOrDefault(g => g.id == "path").value == null ||
                string.IsNullOrEmpty(staticConfig.FirstOrDefault(g => g.id == "path").value.ToString()))
                validationErros.Add(new ValidationError { Source = this.Config.id, Property = "Path", Error = "File path can not be left empty." });

            if (staticConfig.FirstOrDefault(g => g.id == "filter") == null ||
               staticConfig.FirstOrDefault(g => g.id == "filter").value == null ||
               string.IsNullOrEmpty(staticConfig.FirstOrDefault(g => g.id == "filter").value.ToString()))
               validationErros.Add(new ValidationError { Source = this.Config.id, Property = "Filter", Error = "Filter can not be left empty." });

            return validationErros;
        }
        public override ServiceConfig GetAdapterConfigMetadata()
        {
            var actionConfig = base.GetAdapterConfigMetadata();

            actionConfig.id = this.Name;
            actionConfig.type = this.Name;
            actionConfig.baseType = "onewayreceiveadapter";
            actionConfig.title = "Receive File";
            actionConfig.assemblyType = this.GetType().FullName;
            actionConfig.description = "Receives files from directories";
            actionConfig.image = "../../Images/fileReceiveAdapter.png";
            actionConfig.image = "~/Home/Image?actionName=fileReceiveAdapter";

            actionConfig.config.staticConfig.AddRange(new List<Host.Library.Services.ConfigProperty> 
                            {
                             new ConfigProperty{id="path", name = "Path", description = "Directory path", type= "string"},
                                new ConfigProperty {id="filter", name = "Filter", description = "The search filter", type = "string", value="*.*" },
                                new ConfigProperty {id="createDirectory", name = "Create Directory", description = "Would you like the directory created if it doesn't exist?", type = "bool", value=true },
                                new ConfigProperty {id="scanFileForMimeType", name = "Scan file for MimeType", description = "Would you like the adapter to automaticly detect and set the MimeType?", type = "bool", value=true }
                             });
            actionConfig.config.securityConfig.AddRange(new List<Host.Library.Services.ConfigProperty> 
                            {
                                new ConfigProperty{id="userName", name = "User name", description = "User name", type= "string"},
                                new ConfigProperty{id="password", name = "Password", description = "Password", type= "password"}
                            });

            

            return actionConfig;
        }
        public override byte[] GetImage()
        {
            return Host.Services.Core.Resources.FileReceiveAdapter;
        }
        private void OnChanged(object source, FileSystemEventArgs e)
        {
            try
            {
                System.IO.File.Move(e.FullPath, string.Format("{0}{1}", e.FullPath, Extension));
            }
            catch (Exception)
            {

            }
        }
        private void OnRenamed(object source, RenamedEventArgs e)
        {
            try
            {
                this.HostChannel.LogEvent(string.Format("File: {0} renamed to {1}", e.OldFullPath, e.FullPath));
                ProcessMessage(e.FullPath);
            }
            catch (Exception)
            {
                throw;
            }

        }
        private async Task ProcessMessage(string file)
        {
            try
            {
                if (Path.GetExtension(file) == Extension)
                {
                    string originalFileName = file.Replace(Extension, "");
                    this.ContentType = MimeTypesHelper.GetContentType(originalFileName, file, scanFileForMimeType);

                    using (var fileStream = File.OpenRead(file))
                    {
                        // Open the file and create a new IntegrationMessage
                        var message = new IntegrationMessage
                        {
                            InterchangeId = Guid.NewGuid(),
                            IntegrationId = this.Config.integrationId,
                            ItineraryId = this.Config.itineraryId,
                            CreatedBy = this.Name,
                            ContentType = this.ContentType,
                            Itinerary = this.Itinerary
                        };
                        message.AddMessage(GetBytes(fileStream));
                        message.Variables.Add(new ContextVariable { Type = "String", Variable = "_OriginalFileName", Value = Path.GetFileName(originalFileName) });
                        message.Variables.Add(new ContextVariable { Type = "String", Variable = "_Extension", Value = Path.GetExtension(originalFileName) });

                        message = RunOutboundScript(message);

                        this.HostChannel.SubmitMessage(message, this.PersistOptions, this);
                        base.ProcessMessage(message,false);
                    }
                    File.Delete(file);
                    this.HostChannel.LogEvent("File: " + file + " was successfully submitted");
                }
            }
            catch (Exception)
            {
                this.HostChannel.LogEvent(string.Format("Unable to submit file: {0}", file));
            }

        }
        private byte[] GetBytes(Stream input)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
        }
        
        
    }
}
