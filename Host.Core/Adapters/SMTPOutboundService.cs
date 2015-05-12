using Host.Library.Services;
using Integrator.Hub.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace Host.Services.Core.Adapters
{
    public class SMTPOutboundService: OnewayOutboundService
    {
        private string _name = "smtpOutboundService";
        public override string Name
        {
            get
            {
                return _name;
            }
        }
        public override async Task Start()
        {
            try
            {
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
            actionConfig.title = "Send SMTP";
            actionConfig.assemblyType = this.GetType().FullName;
            actionConfig.description = "Sends Email";

            // Set collections
            actionConfig.config.staticConfig.AddRange(new List<Host.Library.Services.ConfigProperty> 
                {
                    new ConfigProperty{id="from", name = "From", description = "Sender Email", type= "string", value = ""},
                    new ConfigProperty{id="to", name = "To", description = "Receiver Email", type = "string", value = "" },
                    new ConfigProperty{id="subject", name = "Subject", description = "Subject", type = "string", value = "" },
                    new ConfigProperty{id="body", name = "Body", description = "Content", type = "string", value = "" },
                    new ConfigProperty{id="isHtml", name = "Is HTML", description = "Is the body content HTML", type = "bool", value = false },
                    new ConfigProperty{id="attachMessage", name = "Attach Message", description = "Attach Message", type = "bool", value = false }, 
                });
            actionConfig.config.securityConfig.AddRange(new List<Host.Library.Services.ConfigProperty> 
                {
                    new ConfigProperty{id="smtpServer", name = "SMTP Server", description = "Name of the server", type= "string"},
                    new ConfigProperty{id="port", name = "Port", description = "Port number", type= "string"},
                    new ConfigProperty{id="userName", name = "User name", description = "User name", type= "string"},
                    new ConfigProperty{id="password", name = "Password", description = "Password", type= "password"}
                });


            return actionConfig;

        }
        public override async Task ProcessMessage(IntegrationMessage message, bool isFirstAction)
        {
            try
            {
                message = RunInboundScript(message);

                // Overide configuration with variables
                var actionConfig = this.GetActionConfig(message.Variables);

                var from = actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "from").value.ToString();
                var to = actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "to").value.ToString();
                var subject = actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "subject").value.ToString();
                var body = actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "body").value.ToString();
                bool isHtml = bool.Parse( actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "isHtml").value.ToString());
                bool attachMessage = bool.Parse(actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "attachMessage").value.ToString());
                
                var smtpServer = actionConfig.config.securityConfig.FirstOrDefault(c => c.id == "smtpServer").value.ToString();
                var port = actionConfig.config.securityConfig.FirstOrDefault(c => c.id == "port").value.ToString();
                var username = actionConfig.config.securityConfig.FirstOrDefault(c => c.id == "userName").value.ToString();
                var password = actionConfig.config.securityConfig.FirstOrDefault(c => c.id == "password").value.ToString();

                from = message.Metadata.Keys.Contains("from") ? message.Metadata["from"] : from;
                to = message.Metadata.Keys.Contains("to") ? message.Metadata["to"] : to;
                subject = message.Metadata.Keys.Contains("subject") ? message.Metadata["subject"] : subject;
                body = message.Metadata.Keys.Contains("body") ? message.Metadata["body"] : body;

                smtpServer = message.Metadata.Keys.Contains("smtpServer") ? message.Metadata["smtpServer"] : smtpServer;
                port = message.Metadata.Keys.Contains("port") ? message.Metadata["port"] : port;
                username = message.Metadata.Keys.Contains("username") ? message.Metadata["username"] : username;
                password = message.Metadata.Keys.Contains("password") ? message.Metadata["password"] : password;

                try
                {
                    string tempFilePath = null;
                    var portNumber = Int32.Parse(port);
                    MailMessage msg = new MailMessage(from, to, subject, body);
                    var mailclient = new System.Net.Mail.SmtpClient(smtpServer, portNumber);
                    var auth = new System.Net.NetworkCredential(username, password);
                    msg.IsBodyHtml = isHtml;
                    mailclient.Credentials = auth;
                    if (attachMessage)
                    {
                        string tempPath = System.IO.Path.GetTempPath();
                        tempFilePath = Path.Combine(tempPath, Guid.NewGuid().ToString());

                        using (var fileStream = File.OpenWrite(tempFilePath))
                        {
                            fileStream.Write(message._messageBuffer, 0, message._messageBuffer.Length);
                        }
                        msg.Attachments.Add(new Attachment(tempFilePath));
                    }
                        
                     mailclient.Send(msg);

                     if (attachMessage)
                         File.Delete(tempFilePath);
                }
                catch (Exception e4)
                {

                }
                

                base.ProcessMessage(message, false);
            }
            catch (Exception)
            {
                this.HostChannel.LogEvent("Unable to send mail");
            }

        }
        public override byte[] GetImage()
        {
            return Host.Services.Core.Resources.SMTPAdapter;
        }
    }
}
