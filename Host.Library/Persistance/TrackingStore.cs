using Integrator.Hub.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Host.Library.Persistance
{
    public class TrackingStore
    {
        private static ConcurrentQueue<TrackingMessage> _store = null;
        private int _releaseEveryNumberOfSeconds;
        private AutoResetEvent _autoEvent;
        private TimerCallback _callBack;
        private Timer _stateTimer;

        public delegate void ReleaseHandler(List<TrackingMessage> trackingEvents);
        public event ReleaseHandler OnReleaseEvent;

        public TrackingStore(int releaseEveryNumberOfSeconds)
        {
            _store = new ConcurrentQueue<TrackingMessage>();
            _releaseEveryNumberOfSeconds = releaseEveryNumberOfSeconds;
            _autoEvent = new AutoResetEvent(false);

            // Create an inferred delegate that invokes methods for the timer.
            _callBack = this.Release;
            
            
        }

        public async Task Enqueue(TrackingMessage trackingMessage)
        {
            _store.Enqueue(trackingMessage);
        }
        public void Start()
        {
            _stateTimer = new Timer(_callBack, _autoEvent, 1000, _releaseEveryNumberOfSeconds * 1000);
        }
        public void Stop()
        {
            _stateTimer.Dispose();
        }
        public void Release(Object stateInfo)
        {
            if (_store.Count > 0 && OnReleaseEvent != null)
            {
                OnReleaseEvent(_store.ToList());
                _store = new ConcurrentQueue<TrackingMessage>();
            }
        }

    }
}
