using Host.Library.Services;
using Integrator.Hub.Models;
using Jint;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace Host.Services.Core.Adapters
{
    public class RESTOutboundService : TwowayOutboundService
    {
        private string _name = "restOutboundService";
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
            actionConfig.baseType = "twowaysendadapter";
            actionConfig.title = "Send REST";
            actionConfig.assemblyType = this.GetType().FullName;
            actionConfig.description = "Transmits messages to a REST endpoint.";
            actionConfig.image = "../../Images/restSendAdapter.png";

            // Set collections
            actionConfig.config.staticConfig.AddRange(new List<Host.Library.Services.ConfigProperty> 
                            {
                                new Host.Library.Services.ConfigProperty{id="uri", name = "URI", description = "Service address", type= "string", value = "http://server/service/"},
                                new Host.Library.Services.ConfigProperty {id="verb", name = "HTTP Verb", description = "HTTP Verb, Eg POST, PUT", type = "string", value = "POST" },
                                new Host.Library.Services.ConfigProperty {id="accept", name = "Accept Header", description = "Accepted content type", type = "string", value = "application/json" },
                                new Host.Library.Services.ConfigProperty {id="contenttype", name = "Content Type Header", description = "Content type of the messae submitted", type = "string", value = "application/json" },
                            });
            actionConfig.config.securityConfig.AddRange(new List<Host.Library.Services.ConfigProperty> 
                            {
                                new ConfigProperty{id="userName", name = "User name", description = "User name", type= "string"},
                                new ConfigProperty{id="password", name = "Password", description = "Password", type= "password"}
                            });


            return actionConfig;

            //return new ActionConfig
            //{
            //    id = this.Name,
            //    type = this.Name,
            //    baseType = "twowaysendadapter",
            //    assemblyType = this.GetType().FullName,
            //    description = "Transmits messages to a REST endpoint.",
            //    image = "../../Images/restSendAdapter.png",
            //    config = new ConfigSection
            //    {
            //        generalConfig = new List<ConfigProperty> 
            //                {
            //                    new ConfigProperty{id="name", name = "Name", description = "Name of adapter", type= "string", value = this.Name},
            //                    new ConfigProperty {id="description", name = "Description", description = "Transmits files to a directory.", type = "string"},
            //                    new ConfigProperty {id="host", name = "Host", description = "Which host do you want to handle this adapter?", type = "string"}
            //                },
            //        staticConfig = new List<Host.Library.Services.ConfigProperty> 
            //                {
            //                    new Host.Library.Services.ConfigProperty{id="uri", name = "URI", description = "Service address", type= "string", value = "http://server/service/"},
            //                    new Host.Library.Services.ConfigProperty {id="verb", name = "HTTP Verb", description = "HTTP Verb, Eg POST, PUT", type = "string", value = "POST" },
            //                    new Host.Library.Services.ConfigProperty {id="accept", name = "Accept Header", description = "Accepted content type", type = "string", value = "application/json" },
            //                    new Host.Library.Services.ConfigProperty {id="contenttype", name = "Content Type Header", description = "Content type of the messae submitted", type = "string", value = "application/json" },

            //                },
            //        securityConfig = new List<ConfigProperty> 
            //                {
            //                    new ConfigProperty{id="userName", name = "User name", description = "User name", type= "string"},
            //                    new ConfigProperty{id="password", name = "Password", description = "Password", type= "password"},
            //                },
            //    }
            //};
        }
        public override async Task ProcessMessage(IntegrationMessage message, bool isFirstAction)
        {
            string uri = string.Empty;
            try
            {
                // Overide configuration with variables
                var actionConfig = this.GetActionConfig(message.Variables);

                uri = actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "uri").value.ToString();
                string verb = actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "verb").value.ToString();
                string accept = actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "accept").value.ToString();
                string contenttype = actionConfig.config.staticConfig.FirstOrDefault(p => p.id == "contenttype").value.ToString();

                message = RunInboundScript(message);

                Match match = Regex.Match(uri, "{(.*?)}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                while (match.Success)
                {
                    var field = match.Value.Replace("{", string.Empty).Replace("}", string.Empty);
                    string val = GetValue(message.ToString(), field);
                    uri = uri.Replace(match.Value, val);
                    match = match.NextMatch();
                }

                var client = new WebClient();
                client.Headers.Add(HttpRequestHeader.Accept, accept);
                client.Headers.Add(HttpRequestHeader.ContentType, contenttype);

                string response = string.Empty;
                switch (verb.ToUpper())
                {
                    case "GET":
                        response = client.DownloadString(uri);
                        break;
                    default:
                        throw new ApplicationException("Verb not supported.");
                }
               if(accept != "application/json")
               {
                   var xmlDocument = new XmlDocument();
                   xmlDocument.LoadXml(response);
                   response = JsonConvert.SerializeXmlNode(xmlDocument);
               }
               var msg = new IntegrationMessage
               {
                   InterchangeId = message.InterchangeId,
                   IntegrationId = message.IntegrationId,
                   ItineraryId = message.ItineraryId,
                   CreatedBy = this.Name,
                   ContentType = message.ContentType,
                   Itinerary = message.Itinerary
               };
               msg.Metadata.Add("uri", uri);
               msg.AddMessage(Encoding.UTF8.GetBytes(response));
               message = RunOutboundScript(msg);

               await this.HostChannel.SubmitMessage(msg, this.PersistOptions, this);


            }
            catch (Exception)
            {
                this.HostChannel.LogEvent(string.Format("Unable to submit request to: {0}", uri));
            }
        }
        public override byte[] GetImage()
        {
            return Host.Services.Core.Resources.RESTSendAdapter;
        }
        private string GetValue(string msg, string field)
        {
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
        }
    }
}
