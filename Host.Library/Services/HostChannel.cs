using Host.Library.Model;
using Integrator.Hub.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Host.Library.Services
{
    public class HostChannel
    {
        public string Name { get; set; }
        public HostChannel(string name)
        {
            this.Name = name;
        }
      
        private List<IntegrationMessage> _messageQueue = new List<IntegrationMessage>();
        public async Task SubmitMessage(IntegrationMessage message, PersistOptions persistOptions, MicroService action)
        {
            switch (persistOptions)
            {
                case PersistOptions.None:
                    break;
                case PersistOptions.Optimistic:
                    Persist(message);
                    break;
                case PersistOptions.Pessimistic:
                    await Persist(message);
                    break;
                default:
                    break;
            }
            message.Sender = this.Name;
            ForwardMessage(message, action);
        }

        internal delegate void MessageSubmitted(IntegrationMessage message, MicroService action);
        
        internal event MessageSubmitted OnMessageSubmitted;

        internal delegate void LogHandler(string eventMessage);
        
        internal event LogHandler OnLogEvent;
        public void LogEvent(string eventMessage)
        {
            if (OnLogEvent != null)
            {
                OnLogEvent(eventMessage);
            }
        }

        internal delegate void TrackHandler(TrackingMessage trackingMessage);

        internal event TrackHandler OnTrackEvent;
        public void TrackEvent(TrackingMessage trackingMessage)
        {
            if (OnTrackEvent != null)
            {
                OnTrackEvent(trackingMessage);
            }
        }

        protected void ForwardMessage(IntegrationMessage message, MicroService action)
        {
            if (OnMessageSubmitted != null)
            {
                OnMessageSubmitted(message, action);
            }
        }
        private async Task Persist(IntegrationMessage message)
        {
            _messageQueue.Add(message);
        }
    }
}
