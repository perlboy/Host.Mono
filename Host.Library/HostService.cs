using Host.Library.Services;
using Host.Library.Services.Interfaces;
using Host.Library.Helpers;
using Host.Library.Model;
using Host.Library.Persistance;
using Integrator.Hub.Models;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Host.Library
{
    public class HostService : IDisposable
    {
        #region Private Members
        private const int CONNECT_RETRY_TIME = 10; //time in seconds in which a connection re-connect is attempted.
        private HubConnection _hub = null; //this is the main connection point through the server talks to the client and client to the server.

        private IHubProxy _hubProxy = null;
        private string _hubName = "integrationHub";
        private string _hubUrl = string.Empty;
        private string _name = string.Empty;
        private bool _hasSignedIn = false;
        private HostChannel _hostChannel = null;
        private List<ServiceConfig> _adapterConfigurations = new List<ServiceConfig>();
        private TrackingStore _trackingStore;
        private List<IntegrationItinerary> _itineraries = new List<IntegrationItinerary>();
        public static List<MicroService> _microServices = new List<MicroService>();
        private Guid _organizationId;
        private int reconnectTrials = 0;
        private int maxReconnectTrials = 3;
        private int reconnectInterval = 10000;

        // Timer for checking up on updates
        private int _checkForUpdatesIntervall = 120;
        private DateTime _lastUpdate;
        private AutoResetEvent _autoEvent;
        private TimerCallback _callBack;
        private Timer _stateTimer;
        private delegate void CheckForUpdatesHandler();
        private event CheckForUpdatesHandler OnReleaseEvent;
        #endregion
        
        #region Public Members
        public string Name { get { return _name; } }
        public HostChannel HostChannel { get { return _hostChannel; } }
        #endregion

        #region Public Methods
        public HostService(string name, Guid organizationId, string hubUrl)
        {
            ServicePointManager.DefaultConnectionLimit = 100;
            _name = name;
            _hostChannel = new HostChannel(_name);
            _hostChannel.OnLogEvent += LogHostChannelEvent;
            _hostChannel.OnMessageSubmitted += Send;
            _hostChannel.OnTrackEvent += _hostChannel_OnTrackEvent;
            _trackingStore = new TrackingStore(10);
            _trackingStore.OnReleaseEvent += _trackingStore_OnReleaseEvent;

            _hubUrl = hubUrl;
            _organizationId = organizationId;

            _autoEvent = new AutoResetEvent(false);
            _callBack = this.CheckForUpdates;
            
        }

        void _hostChannel_OnTrackEvent(TrackingMessage trackingMessage)
        {
            trackingMessage.Host = this._name;
            trackingMessage.OrganizationId = this._organizationId;
            _trackingStore.Enqueue(trackingMessage);

        }

        void _trackingStore_OnReleaseEvent(List<TrackingMessage> trackingMessages)
        {
            if (_hubProxy != null)
            {
                try
                {
                    _hubProxy.Invoke("trackData", trackingMessages);
                    
                }
                catch (Exception)
                {
                }
            }
        }

        public void Connect()
        {
			try{
            _hub = new HubConnection(this._hubUrl);
            LogEvent("Connecting to " + this._hubUrl, LogLevel.Info);
            //_hub.Credentials = ....
            _hub.StateChanged += _hub_StateChanged;
            _hub.Reconnected += _hub_Reconnected;
            _hub.Error += _hub_Error;
            _hub.Closed += _hub_Closed;
            _hub.ConnectionSlow += _hub_ConnectionSlow;
            _hub.Reconnecting += _hub_Reconnecting;
            _hubProxy = _hub.CreateHubProxy(this._hubName); //our client side object to talk through the hub with.

            // Receive Messages sent to everyone...
            _hubProxy.On<string>("broadcastMessage", message =>
            {
                LogEvent("Received Broadcast message...", LogLevel.Info);
            });

            _hubProxy.On<string>("errorMessage", message =>
            {
                LogEvent(message, LogLevel.Error);
            });

            _hubProxy.On<string>("ping", message =>
            {
                LogEvent("Received Ping message...", LogLevel.Info);
                if (_hubProxy != null)
                {
                    try
                    {
                        _hubProxy.Invoke("pingResponse", _name, System.Environment.MachineName, "Online", this._organizationId.ToString());
                    }
                    catch (Exception)
                    {
                    }
                }
            });

            _hubProxy.On<string>("getEndpoints", message =>
            {
                LogEvent("Received getEndpoints message...", LogLevel.Info);
                var adapterList = new List<dynamic>();
                foreach (var action in _microServices)
                {
                    if (action is IOnewayInboundService || action is ITwowayInboundService)
                    {
                        var name = action.Config.config.generalConfig.FirstOrDefault(c => c.id == "name").value.ToString();
                        adapterList.Add(new
                        {
                            name = name,
                            integration = "comming...",
                            host = this.Name,
                            machineName = System.Environment.MachineName,
                            transport = action.Config.title
                        });
                    }
                }

                if (_hubProxy != null)
                {
                    try
                    {
                        _hubProxy.Invoke("getEndpointsResponse", adapterList);

                    }
                    catch (Exception)
                    {
                    }
                }

            });

            _hubProxy.On<dynamic>("updateItinerary", updatedItinerary =>
            {
                LogEvent("Received updateItinerary message...", LogLevel.Info);

                Guid itineraryId = Guid.Parse((string)updatedItinerary.itineraryId);
                var integrationItinerary = _itineraries.FirstOrDefault(i => i.IntegrationId == itineraryId);
                if (integrationItinerary == null)
                {
                    _itineraries.Add(new IntegrationItinerary(itineraryId, updatedItinerary));
                }
                else
                    integrationItinerary.Itinerary = updatedItinerary;

                _adapterConfigurations.Clear();
                foreach (var i in _itineraries)
                {
                    dynamic itinerary = i.Itinerary;
                    foreach (dynamic activity in itinerary.activities)
                    {
                        if (activity.userData.baseType == ServiceTypes.onewayreceiveadapter ||
                            activity.userData.baseType == ServiceTypes.onewaysendadapter ||
                            activity.userData.baseType == ServiceTypes.twowayreceiveadapter ||
                            activity.userData.baseType == ServiceTypes.twowaysendadapter ||
                            activity.userData.baseType == ServiceTypes.javascriptaction)
                        {
                            var json = JsonConvert.SerializeObject(activity.userData);
                            ServiceConfig adapterConfig = JsonConvert.DeserializeObject<ServiceConfig>(json);

                            var hostConfig = adapterConfig.config.generalConfig.FirstOrDefault(c => c.id == "host");
                            if (hostConfig != null && hostConfig.value.ToString() == this._name)
                                _adapterConfigurations.Add(adapterConfig);
                        }
                    }
                }
                LogEvent("", LogLevel.Info);
                LogEvent("-----------------------------------------------------------------------------", LogLevel.Info);
                LogEvent(String.Format("| {0,20}| {1,10}| {2,40}|", "Service".PadRight(20), "State   ", "Address".PadRight(40)), LogLevel.Info);
                LogEvent("-----------------------------------------------------------------------------", LogLevel.Info); 
    
                foreach (var action in _microServices)
                {
                    action.Config = this._adapterConfigurations.FirstOrDefault(c => c.type == action.Name);
                    action.Itinerary = _itineraries.FirstOrDefault(i => i.IntegrationId == action.Config.itineraryId).Itinerary;

                    if (action.GetType().GetInterfaces().Contains(typeof(IAdapterService)))
                    {
                        ((IAdapterService)action).Stop();
                    }
                }
    
                LogEvent("-----------------------------------------------------------------------------", LogLevel.Info);
           
                _microServices.Clear();
                LoadAdapters();

            });
            // Receive Messages to this host
            _hubProxy.On<IntegrationMessage, string>("sendMessage", (message, destination) =>
            {
                LogEvent("Received message...", LogLevel.Info);
                ReceiveMessage(message, destination);
            });
            // Receive confirmation on signin
            _hubProxy.On<Guid, List<dynamic>>("signInMessage", (organizationId, itineraries) =>
            {
                //this._organizationId = organizationId;
                foreach (dynamic itinerary in itineraries)
                {
                    string itineraryId = (string)itinerary.itineraryId;
                    _itineraries.Add(new IntegrationItinerary(Guid.Parse(itineraryId), itinerary));
                    foreach (dynamic activity in itinerary.activities)
                    {
                        if (activity.userData.baseType == ServiceTypes.onewayreceiveadapter ||
                            activity.userData.baseType == ServiceTypes.onewaysendadapter ||
                            activity.userData.baseType == ServiceTypes.twowayreceiveadapter ||
                            activity.userData.baseType == ServiceTypes.twowaysendadapter ||
                            activity.userData.baseType == ServiceTypes.javascriptaction)
                        {
                            var json = JsonConvert.SerializeObject(activity.userData);
                            ServiceConfig adapterConfig = JsonConvert.DeserializeObject<ServiceConfig>(json);

                            var hostConfig = adapterConfig.config.generalConfig.FirstOrDefault(c => c.id == "host");
                            if (hostConfig != null && hostConfig.value.ToString() == this._name)
                                _adapterConfigurations.Add(adapterConfig);
                        }
                    }
                }

                LogEvent("", LogLevel.Info);
                LogEvent("-----------------------------------------------------------------------------", LogLevel.Info); 
                LogEvent(String.Format("| {0,20}| {1,10}| {2,40}|", "Service".PadRight(20), "State   ", "Address".PadRight(40)), LogLevel.Info);
                LogEvent("-----------------------------------------------------------------------------", LogLevel.Info); 
    
                LoadAdapters();
                LogEvent("-----------------------------------------------------------------------------", LogLevel.Info);
                LogEvent("", LogLevel.Info);
                
                _trackingStore.Start();
                _hasSignedIn = true;
                LogEvent("SignIn successfull", LogLevel.Info);
            });

            _hub.Start().Wait(); //we're now connected.
			}
			catch(Exception ex) {
			
				Log (ex.Message);
			}
        }

        public void Disconnect()
        {
            try
            {
                _trackingStore.Stop();
                _hub.Stop();
                _hub.StateChanged -= _hub_StateChanged;
                _hub.Error -= _hub_Error;
                _hub.Reconnected -= _hub_Reconnected;
                _hub.Closed -= _hub_Closed;
                _hub.ConnectionSlow -= _hub_ConnectionSlow;
                _hub.Reconnecting -= _hub_Reconnecting;
                _hub.Dispose();

                foreach (var action in _microServices)
                {
                    action.Config = this._adapterConfigurations.FirstOrDefault(c => c.type == action.Name);
                    //action.Itinerary = _itineraries.FirstOrDefault(i => i.IntegrationId == action.Config.integrationId).Itinerary;

                    LogEvent("", LogLevel.Info);
                    LogEvent("-----------------------------------------------------------------------------", LogLevel.Info);
                    LogEvent(String.Format("| {0,20}| {1,10}| {2,40}|", "Service".PadRight(20), "State   ", "Address".PadRight(40)), LogLevel.Info);
                    LogEvent("-----------------------------------------------------------------------------", LogLevel.Info); 
    
                    if (action.GetType().GetInterfaces().Contains(typeof(IAdapterService)))
                    {
                        ((IAdapterService)action).Stop();
                    }
    
                    LogEvent("-----------------------------------------------------------------------------", LogLevel.Info);
                    LogEvent("", LogLevel.Info);
    
                }
                _microServices.Clear();


            }
            catch (Exception ex)
            {

            }
            _hub = null;
            _hubProxy = null;
        }

        public void SignIn(string hostName)
        {
            if (_hubProxy != null)
            {
                var hostData = new
                {
                    Name = _name,
                    MachineName = System.Environment.MachineName,
                    OrganizationID = this._organizationId.ToString()
                };
                _hubProxy.Invoke("SignIn", hostData); //can add other details about machine/process.
            }
        }

        public void Send(IntegrationMessage msg, MicroService action)
        {
            try
            {
                if (msg.OrganizationId == null)
                    msg.OrganizationId = this._organizationId;

                var json = JsonConvert.SerializeObject(msg.Itinerary);
                var newItinerary = JsonConvert.DeserializeObject(json);

                msg.Itinerary = newItinerary;

                var correlationNode = action.Config.config.generalConfig.FirstOrDefault(c => c.id == "correlationId");

                // Check if the incomming message is following a correlation
                if (correlationNode != null &&
                     correlationNode.value != null &&
                     !string.IsNullOrEmpty(correlationNode.value.ToString()))
                {
                    FollowCorrelation(correlationNode.value.ToString(), msg, action);
                }
                else if (action.Config.type == "mobileaction")
                {
                    if (_hubProxy != null) // Next activity is on a different host...
                    {
                        LogEvent(string.Format("{0} is sent to hub", action.Name), LogLevel.Info);
                        LogEvent(string.Format("CorrelationId : {0}", msg.CorrelationId), LogLevel.Info);
                        _hubProxy.Invoke("sendMobileMessage", action.Config.id, msg);
                    }
                }
                else
                {
                    // Track incomming message
                    if (msg.IsFault)
                        _trackingStore.Enqueue(new TrackingMessage(msg.MessageBuffer, msg.ContentType)
                        {
                            LastActivity = action.Config.id,
                            NextActivity = null,
                            Host = this._name,
                            Variables = msg.Variables,
                            OrganizationId = this._organizationId,
                            IntegrationId = msg.IntegrationId,
                            InterchangeId = msg.InterchangeId,
                            ItineraryId = msg.ItineraryId,
                            FaultCode = msg.FaultCode,
                            FaultDescription = msg.FaultDescripton,
                            TimeStamp = DateTime.UtcNow,
                            State = TrackingMessageStatusCodes.Failed
                        });
                    else if (action is IOnewayInboundService || action is ITwowayInboundService)
                       _trackingStore.Enqueue(new TrackingMessage(msg.MessageBuffer, msg.ContentType)
                        {
                            LastActivity = action.Config.id,
                            NextActivity = null,
                            Host = this._name,
                            Variables = msg.Variables,
                            OrganizationId = this._organizationId,
                            IntegrationId = msg.IntegrationId,
                            InterchangeId = msg.InterchangeId,
                            ItineraryId = msg.ItineraryId,
                            TimeStamp = DateTime.UtcNow,
                            State = TrackingMessageStatusCodes.Started,
                            IsFirstAction = true
                        });
                    else
                        _trackingStore.Enqueue(new TrackingMessage(msg.MessageBuffer, msg.ContentType)
                        {
                            LastActivity = action.Config.id,
                            NextActivity = null,
                            Host = this._name,
                            Variables = msg.Variables,
                            OrganizationId = this._organizationId,
                            IntegrationId = msg.IntegrationId,
                            InterchangeId = msg.InterchangeId,
                            ItineraryId = msg.ItineraryId,
                            TimeStamp = DateTime.UtcNow,
                            State = TrackingMessageStatusCodes.InProcess
                        });
                    ForwardMessage(msg, action);
                }
            }
            catch (Exception ex)
            {
                _trackingStore.Enqueue(new TrackingMessage(msg.MessageBuffer, msg.ContentType)
                {
                    LastActivity = action.Config.id,
                    NextActivity = null,
                    Host = null,
                    Variables = msg.Variables,
                    OrganizationId = this._organizationId,
                    IntegrationId = msg.IntegrationId,
                    InterchangeId = msg.InterchangeId,
                    ItineraryId = msg.ItineraryId,
                    TimeStamp = DateTime.UtcNow,
                    State = TrackingMessageStatusCodes.Failed,
                    FaultCode = "100",
                    FaultDescription = ex.Message
                });
                throw;
            }
        }

        public void Log(string msg)
        {
            if (_hubProxy != null)
            {
                try
                {
                    _hubProxy.Invoke("logMessage", _name, msg, this._organizationId.ToString());

                }
                catch (Exception)
                {
                }
            }
        }

        #endregion

        #region Private Methods
        private void LoadAdapters()
        {
            GetLatestAssemblies();
            DirectoryInfo di = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            FileInfo[] assemblies = di.GetFiles("*.dll");
            List<Type> types = new List<Type>();
            foreach (FileInfo assembly in assemblies)
            {
                try
                {
                    Assembly a = Assembly.LoadFrom(assembly.FullName);
                    foreach (Type type in a.GetTypes())
                    {
                        if (type.GetInterfaces().Contains(typeof(IOnewayInboundService)) && !type.IsAbstract)
                            types.Add(type);
                        else if (type.GetInterfaces().Contains(typeof(IOnewayOutboundService)) && !type.IsAbstract)
                            types.Add(type);
                        else if (type.GetInterfaces().Contains(typeof(IMicroService)) && !type.IsAbstract)
                            types.Add(type);
                    }
                }
                catch { }
            }

            foreach (var adapterConfiguration in this._adapterConfigurations)
            {
                var type = types.FirstOrDefault(a => a.FullName == adapterConfiguration.assemblyType);
                if (type == null)
                {
                    LogEvent(adapterConfiguration.assemblyType + " could not be loaded. Make sure the assembly is installed. Consider changing the value of the ServiceUpdatePolicy in the application config file.", LogLevel.Error);
                    return;
                }
                if (type.GetInterfaces().Contains(typeof(IAdapterService)) && !type.IsAbstract)
                {
                    var microService = (AdapterService)Activator.CreateInstance(type);
                    microService.OrganizationId = this._organizationId;
                    microService.HostChannel = this._hostChannel;
                    microService.Config = adapterConfiguration;
                    microService.Itinerary = _itineraries.FirstOrDefault(i => i.IntegrationId == microService.Config.itineraryId).Itinerary;
                    microService.IntegrationId = microService.Config.integrationId;
                    microService.ItineraryId = microService.Config.itineraryId;
                    if (microService.Config != null)
                    {
                        var enableConfig = microService.Config.config.generalConfig.FirstOrDefault(g => g.id == "enabled");
                        if (enableConfig != null && (bool)enableConfig.value == true)
                            microService.Start();
                        else
                            Log(string.Format("{0} was stopped.", microService.Name));
                        
                        _microServices.Add(microService);
                    }
                }
                else if (type.GetInterfaces().Contains(typeof(IMicroService)) && !type.IsAbstract)
                {
                    var microService = (MicroService)Activator.CreateInstance(type);
                    microService.OrganizationId = this._organizationId;
                    microService.HostChannel = this._hostChannel;
                    microService.Config = adapterConfiguration;
                    microService.IntegrationId = microService.Config.integrationId;
                    microService.ItineraryId = microService.Config.itineraryId;
                    if (microService.Config != null)
                    {
                        _microServices.Add(microService);
                    }
                }
            }
            this._lastUpdate = DateTime.Now;
        }
        private void GetLatestAssemblies()
        {
            var settings = ConfigurationManager.AppSettings["ServiceUpdatePolicy"];
            if (settings == "NEVER")
            {
                LogEvent("Ignoring fetching assemblies", LogLevel.Info);
                return;
            }
            using (var client = new WebClient())
            {
                try
                {
                    var uri = string.Format("{0}/Home/GetLatetestAssemblies?organizationId={1}", ConfigurationManager.AppSettings["hubUrl"].ToString(), this._organizationId);

                    var json = client.DownloadString(uri);
                    var assemblyList = JsonConvert.DeserializeObject<List<dynamic>>(json);

                    foreach (var assembly in assemblyList)
                    {

                        var fileName = Path.GetFileName((string)assembly.uri);
                        try
                        {
                            Version version;
                            Version.TryParse((string)assembly.version, out version);
                            
                            if (!File.Exists(fileName))
                            {
                                LogEvent("Downloading " + fileName, LogLevel.Info);
                                client.DownloadFile((string)assembly.uri, fileName);
                            }
                            else if (settings == "ALL" && File.Exists(fileName))
                            {
                                Version currentVersion = AssemblyName.GetAssemblyName(fileName).Version;

                                if (currentVersion.CompareTo(version) < 0)
                                {
                                    LogEvent("Downloading " + fileName + " (version:" + (string)assembly.version + ")", LogLevel.Info);
                                    client.DownloadFile((string)assembly.uri, fileName);
                                }
                            }

                            
                        }
                        catch(Exception ex)
                        {
                            LogEvent("Unable to load " + fileName, LogLevel.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogEvent(ex.Message, LogLevel.Error);
                }
            }
        }
        private async Task ReceiveMessage(IntegrationMessage message, string destination)
        {
            try
            {
                if (message.IsReplyMessage)
                    LogEvent(string.Format("Reply from {0} - the transmit {1}", message.Sender, message.IsFault ? "failed" : "was successful"), LogLevel.Info);
                else
                {
                    LogEvent(string.Format("Message received from the {0} host to {1}", message.Sender, destination), LogLevel.Info);

                    // Find action from itinerary
                    var service = _microServices.FirstOrDefault(s => ((MicroService)s).Config.id == destination && 
                        ((MicroService)s).ItineraryId == message.ItineraryId &&
                        ((MicroService)s).IntegrationId == message.IntegrationId);

                    if (!service.IsEnabled())
                    {
                        LogEvent(string.Format("{0} is DISABLED and sent to hub", destination), LogLevel.Info);
                        _hubProxy.Invoke("persistMessage", destination, message);
                        
                        _trackingStore.Enqueue(new TrackingMessage(message.MessageBuffer, message.ContentType)
                        {
                            LastActivity = service.Config.id,
                            NextActivity = null,
                            Host = this._name,
                            Variables = message.Variables,
                            OrganizationId = this._organizationId,
                            IntegrationId = message.IntegrationId,
                            InterchangeId = message.InterchangeId,
                            ItineraryId = message.ItineraryId,
                            TimeStamp = DateTime.UtcNow,
                            State = TrackingMessageStatusCodes.Disabled
                        });

                        return;

                    }

                    var msgClone = message.Clone();

                    if (service.ValidateRoutingExpression(msgClone))
                    {
                        service.ProcessMessage(message,false);

                        // Check correlation
                        var correlationNode = service.Config.config.generalConfig.FirstOrDefault(c => c.id == "correlationId");
                        if (correlationNode != null &&
                            correlationNode.value != null &&
                            !string.IsNullOrEmpty(correlationNode.value.ToString()))
                        {
                            // Persist Correlation
                            if (_hubProxy != null)
                            {
                                // Send correlation to HUB
                                var correlationValue = correlationNode.value.ToString();
                                if (correlationNode.value.ToString().StartsWith("{") && correlationNode.value.ToString().EndsWith("}"))
                                {
                                    var corr = message.Variables.FirstOrDefault(c => c.Variable == correlationValue.Replace("{", "").Replace("}", ""));
                                    if (corr == null)
                                        throw new ApplicationException("Correlation value could not be found.");
                                    correlationValue = corr.Value.ToString();
                                }

                                LogEvent(string.Format("{0} is sent to hub", service.Config.id), LogLevel.Info);

                                _hubProxy.Invoke("persistCorrelation", service.Config.id,
                                    this._name,
                                    correlationValue,
                                    message);

                                _trackingStore.Enqueue(new TrackingMessage(message.MessageBuffer, message.ContentType)
                                {
                                    LastActivity = service.Config.id,
                                    NextActivity = null,
                                    Host = this._name,
                                    Variables = message.Variables,
                                    OrganizationId = this._organizationId,
                                    IntegrationId = message.IntegrationId,
                                    InterchangeId = message.InterchangeId,
                                    ItineraryId = message.ItineraryId,
                                    TimeStamp = DateTime.UtcNow,
                                    State = TrackingMessageStatusCodes.CreatingCorrelation
                                });

                            }
                            else
                                throw new ApplicationException("Unable to persist correlation value.");
                        }
                    }

                    if (message.ReplyToSender)
                    {
                        var replyMessage = new IntegrationMessage
                        {
                            InterchangeId = message.InterchangeId,
                            ItineraryId = message.ItineraryId,
                            ReplyToSender = false,
                            IsFault = false,
                            Sender = _name,
                            IsReplyMessage = true
                        };
                        Send(replyMessage, null);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        private void FollowCorrelation(string correlationValue, IntegrationMessage msg, MicroService action)
        {
            if (_hubProxy != null)
            {
                if (correlationValue.StartsWith("{") && correlationValue.EndsWith("}"))
                {
                    var corr = msg.Variables.FirstOrDefault(c => c.Variable == correlationValue.Replace("{", "").Replace("}", ""));
                    if (corr == null)
                        throw new ApplicationException("Correlation value could not be found.");

                    correlationValue = corr.Value.ToString();
                }

                _hubProxy.Invoke("followCorrelation",
                    action.Config.id,
                    this._hubName,
                    correlationValue,
                    this._organizationId.ToString(),
                    msg);

                LogEvent(string.Format("{0} is sent to hub", action.Name), LogLevel.Info);

                // Track
                _trackingStore.Enqueue(new TrackingMessage(msg.MessageBuffer, msg.ContentType)
                {
                    LastActivity = action.Config.id,
                    NextActivity = null,
                    Host = this._name,
                    Variables = msg.Variables,
                    OrganizationId = this._organizationId,
                    IntegrationId = msg.IntegrationId,
                    InterchangeId = msg.InterchangeId,
                    ItineraryId = msg.ItineraryId,
                    TimeStamp = DateTime.UtcNow,
                    State = TrackingMessageStatusCodes.FollowingCorrelation
                });

            }
            else
                throw new ApplicationException("Host is off-line and is unable to submit correlation message.");

        }
        private void ForwardMessage(IntegrationMessage message, MicroService action)
        {
            var itineraryHelper = new ItineraryHelper();
            // Get all child activities 
            List<Activity> activities = itineraryHelper.OrderActions(message.Itinerary);
            var successors = activities.First(a => a.Name == action.Config.id).Successors;

            foreach (var activity in successors)
            {
                var msg = message.Clone(); 
                itineraryHelper.SetStatus(msg.Itinerary, activity.Id, "COMPLETE");

                // Tracking outbound message
                _trackingStore.Enqueue(new TrackingMessage(msg.MessageBuffer, msg.ContentType)
                {
                    LastActivity = action.Config.id,
                    NextActivity = null,
                    Host = activity.Host,
                    Variables = msg.Variables,
                    OrganizationId = this._organizationId,
                    IntegrationId = msg.IntegrationId,
                    InterchangeId = msg.InterchangeId,
                    ItineraryId = msg.ItineraryId,
                    TimeStamp = DateTime.UtcNow,
                    State = TrackingMessageStatusCodes.Started
                });

                var nextService = _microServices.FirstOrDefault(s => ((MicroService)s).Config.id == action.Config.id &&
                                                            ((MicroService)s).ItineraryId == message.ItineraryId &&
                                                            ((MicroService)s).IntegrationId == message.IntegrationId);
                
                if (!nextService.IsEnabled())
                {
                    LogEvent(string.Format("{0} is DISABLED and sent to hub", activity.Name), LogLevel.Info);
                    _hubProxy.Invoke("sendMessage", activity.Name, msg);
                }
                if (activity.Host == _name) // Next activity is on the same host...
                {
                    LogEvent(string.Format("{0} is executed at host", activity.Name), LogLevel.Info);
                    ReceiveMessage(msg, activity.Name);
                }
                else if (_hubProxy != null) // Next activity is on a different host...
                {
                    LogEvent(string.Format("{0} is sent to hub", activity.Name), LogLevel.Info);
                    _hubProxy.Invoke("sendMessage", activity.Name, msg);
                }
            }
        }
        
        #endregion

        #region Events
        void _hub_Error(Exception obj)
        {
            LogEvent(string.Format("Error: {0}",obj.Message), LogLevel.Error);
            if (OnLogEvent != null)
            {
                OnLogEvent(obj.Message,  LogLevel.Error);
            }
        }

        void _hub_StateChanged(StateChange obj)
        {
            LogEvent(string.Format("{0} => {1}", obj.OldState, obj.NewState), LogLevel.Info);

            
            if (obj.NewState == ConnectionState.Disconnected) 
            {
                _stateTimer.Dispose();

                if (reconnectTrials <= maxReconnectTrials)
                {
                    reconnectTrials++;
                    Connect();
                }
                else
                {
                    LogEvent("Retry count has been exeeded and the host will remain in disconnected state.", LogLevel.Info);
                }
            }
            else if (obj.NewState == ConnectionState.Connected)
            {

                _stateTimer = new Timer(_callBack, _autoEvent, 1000, _checkForUpdatesIntervall * 1000);
                reconnectTrials = 0;
            }
        }

        void _hub_Reconnected()
        {
            LogEvent("Host is reconnected", LogLevel.Info);
        }

        void _hub_Reconnecting()
        {
            LogEvent("Host is reconnecting", LogLevel.Info);
        }

        void _hub_ConnectionSlow()
        {
            LogEvent("Warning - Connection is slow", LogLevel.Warning);
        }

        void _hub_Closed()
        {
            LogEvent("Connection is closed", LogLevel.Warning);
        }

        public enum LogLevel{Info, Warning, Error};
        
        public delegate void LogHandler(string message, LogLevel logLevel);
        
        public event LogHandler OnLogEvent;
        
        protected void LogEvent(string message, LogLevel logLevel)
        {
            Log(message);

            if (OnLogEvent != null)
            {
                OnLogEvent(message, logLevel);
            }
        }

        public void LogHostChannelEvent(string eventMessage)
        {
            LogEvent(eventMessage, LogLevel.Info);
        }

        public void CheckForUpdates(Object stateInfo)
        {
            if (_hasSignedIn)
            {
                try
                {
                    HttpWebRequest http = (HttpWebRequest)HttpWebRequest.Create(new Uri(string.Format("{0}/Home/AuditLog?organizationId={1}", this._hubUrl, this._organizationId)));
                    http.Timeout = 5000;
                    HttpWebResponse response = (HttpWebResponse)http.GetResponse();
                    using (StreamReader sr = new StreamReader(response.GetResponseStream()))
                    {
                        var json = sr.ReadToEnd();
                        dynamic auditlog = JsonConvert.DeserializeObject(json);
                        var timestamp = DateTime.Parse((string)auditlog.timestamp);
                        if (timestamp > this._lastUpdate)
                        {
                            // Call the hub for some updates!
                            LogEvent("CheckForUpdates", LogLevel.Info);
                        }
                    }
                }
                catch {
                    LogEvent("Unable to check for Updates", LogLevel.Info);
                }
            }
        }
        #endregion

        #region System.IDisposable Inteface
        public void Dispose()
        {
            this.Disconnect();
        }
        #endregion

        #region Private classes
        private class IntegrationItinerary
        {
            public IntegrationItinerary(Guid integrationId, dynamic itinerary)
            {
                this.IntegrationId = integrationId;
                this.Itinerary = itinerary;
            }
            public Guid IntegrationId { get; set; }
            public dynamic Itinerary { get; set; }
        }
        #endregion
    }
}
