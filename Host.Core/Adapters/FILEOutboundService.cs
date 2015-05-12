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
using System.Text.RegularExpressions;
using Jint;


namespace Host.Services.Core.Adapters
{
    public class FILEOutboundService : OnewayOutboundService
    {
        private string _name = "fileOutboundService";

        public override string Name
        {
            get
            {
                return _name;
            }
        }

        private string Extension = ".~TEMP";
        public override async Task Start()
        {
            try
            {
                // Create a new FileSystemWatcher and set its properties.
                string path = this.Config.config.staticConfig.FirstOrDefault(p => p.id == "path").value.ToString();
                bool createDirectory = bool.Parse(this.Config.config.staticConfig.FirstOrDefault(p => p.id == "createDirectory").value.ToString());
                

                if (!Directory.Exists(path) && createDirectory)
                    Directory.CreateDirectory(path);

                base.Start();
            }
            catch (Exception ex)
            {
                this.HostChannel.LogEvent(string.Format("Send Adapter {0} was could not be started. {1}", this.Name, ex.Message));
            }
        }
        public override async Task Stop()
        {
            base.Stop();
        }
        public override List<ValidationError> Validate()
        {
            return base.Validate();
        }
        public override ServiceConfig GetAdapterConfigMetadata()
        {
            // Get base config
            var actionConfig = base.GetAdapterConfigMetadata();
            actionConfig.id = this.Name;
            actionConfig.type = this.Name;
            actionConfig.baseType = "onewaysendadapter";
            actionConfig.title = "Send File";
            actionConfig.assemblyType = this.GetType().FullName;
            actionConfig.description = "Transmits files to a directory.";
            actionConfig.image = "../../Images/fileSendAdapter.png";

            // Set collections
            actionConfig.config.staticConfig.AddRange(new List<Host.Library.Services.ConfigProperty> 
                            {
                                new Host.Library.Services.ConfigProperty{id="path", name = "Path", description = "Directory path", type= "string"},
                                new Host.Library.Services.ConfigProperty {id="fileName", name = "File name", description = "The name of the file", type = "string", value = "%MessageId%" },
                                new Host.Library.Services.ConfigProperty {id="createDirectory", name = "Create Directory", description = "Would yo like the directory created if it doesn't exist?", type = "bool", value=true }
                          });
            actionConfig.config.securityConfig.AddRange(new List<Host.Library.Services.ConfigProperty> 
                            {
                                new ConfigProperty{id="userName", name = "User name", description = "User name", type= "string"},
                                new ConfigProperty{id="password", name = "Password", description = "Password", type= "password"}
                            });
            
            return actionConfig;
        }
        public override async Task ProcessMessage(IntegrationMessage message, bool isFirstAction)
        {
            string filename = string.Empty;
            try
            {
                message = RunInboundScript(message);

                // Overide configuration with variables
                var actionConfig = this.GetActionConfig(message.Variables);

                filename = actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "fileName").value.ToString();
                string path = actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "path").value.ToString();
                bool createDirectory = (bool)actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "createDirectory").value;

                if (!Directory.Exists(path) && createDirectory)
                    Directory.CreateDirectory(path);

                filename = message.Metadata.Keys.Contains("filename") ? message.Metadata["filename"] : filename;
                filename = filename.Replace("%MessageId%", Guid.NewGuid().ToString());
                filename = filename.Replace("%datatime%", DateTime.Now.ToString("yyyy-MM-DD hh:mm:ss"));
                filename = filename.Replace("%utcdatatime%", DateTime.UtcNow.ToString("yyyy-MM-DD hh:mm:ss"));

                Match match = Regex.Match(filename, "%metadata:(.*?)%", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                while (match.Success)
                {
                    var field = match.Value.Replace("%metadata:", string.Empty);
                    field = field.Substring(0, field.Length - 1);
                    string val = GetValue(message.ToString(), field);
                    filename = filename.Replace(match.Value, val);
                    match = match.NextMatch();
                }

                filename = Path.Combine(path, filename);

                using (var fileStream = File.OpenWrite(filename))
                {
                    fileStream.Write(message.MessageBuffer, 0, message.MessageBuffer.Length);

                    this.HostChannel.LogEvent("File: " + Path.GetFileName(filename) + " was successfully submitted");
                }
                base.ProcessMessage(message,false);
            }
            catch (Exception)
            {
                this.HostChannel.LogEvent(string.Format("Unable to submit file: {0}", Path.GetFileName(filename)));
            }

        }
        public override byte[] GetImage()
        {
            return Host.Services.Core.Resources.FileSendAdapter;
        }
        private string GetValue(string msg, string field)
        {
#if MONO_Symbols
            return string.Empty;
#else
            try
            {
                var engine = new Engine();
                engine.Execute("root=" + msg);
                engine.Execute("val = " + field);
                string val = engine.GetValue("val").AsString(); ;
                return val;
            }
            catch (Exception ex)
            {
                return "UNIDENTIFIDE_FIELD";
            }
#endif

        }
    }
}
