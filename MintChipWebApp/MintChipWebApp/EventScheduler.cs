using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace MintChipWebApp
{
    #region EventScheduler

    /// <summary>Provides an accurate mechanism to schedule callback functions in an efficient manner.</summary>
    public class EventScheduler : IDisposable
    {
        #region Instance variables

        /// <summary>value needs to be a list because more than one item could be scheduled for the exact same time.</summary>
        private SortedDictionary<DateTime, List<object>> scheduleTable;
        private DateTime nextScheduledItem; // keep track of when the next item is to be scheduled such that it can be calculated when the next time to wake up is
        private object lockObj;
        private bool isDisposed;
        /// <summary>This will be set to false in Dispose() such that the internal thread can complete to clean up resources</summary>
        private bool isAlive;

        #endregion

        #region Instance

        /// <summary>For reasons of efficiency of resources, this class is a singleton</summary>
        public static EventScheduler Instance
        {
            get { return Nested.instance; }
        }

        class Nested
        {
            // Explicit static constructor to tell C# compiler
            // not to mark type as beforefieldinit
            static Nested() { }

            // we only want one instance. Either the Attorney version or the Accounting version
            internal static EventScheduler instance = new EventScheduler();
        }// class Nested

        #endregion

        #region Constructor

        private EventScheduler()
        {
            // keep items in a sorted structure. It is debatable which class to use, but a SortedDictionary has been chosen
            // see: http://msdn.microsoft.com/en-us/library/5z658b67.aspx
            // SortedDictionary is O(log n) for insertion and removal, which is better than SortedList which is O(n)
            scheduleTable = null;   // will be created by Run()

            nextScheduledItem = DateTime.MaxValue;
            lockObj = new object();
            isDisposed = false;
            isAlive = true;

            Thread t = new Thread(Run);
            t.Start();

            // wait until the thread starts so we know it is ready for instructions
            lock (lockObj)
            {
                if (scheduleTable == null)
                    Monitor.Wait(lockObj);
            }
        }

        #endregion

        #region Destructor

        // this destructor exists such that the scheduler thread can terminate and clean up properly
        ~EventScheduler()
        {
            Dispose();
        }

        #endregion

        #region Run

        private void Run()
        {
            lock (lockObj)
            {
                scheduleTable = new SortedDictionary<DateTime, List<object>>();

                // signal in case the thread that accessed the scheduler for the first time is waiting
                Monitor.PulseAll(lockObj);

                while (isAlive)
                {
                    // determine when the next item to be scheduled is. Either the list is empty, or we will use the first item (which works because it is sorted)
                    nextScheduledItem = DateTime.MaxValue;

                    if (scheduleTable.Count > 0)
                    {
                        nextScheduledItem = scheduleTable.Keys.First();
                        DateTime now = Now();

                        if (nextScheduledItem <= now)
                        {
                            List<object> list = scheduleTable[nextScheduledItem];

                            foreach (object item in list)
                            {
                                ThreadPool.QueueUserWorkItem(ExecuteEvent, new object[] { nextScheduledItem, item });
                            }
                            list.Clear();
                            scheduleTable.Remove(nextScheduledItem);
                            // will go back to the top of the loop (and possibly) process the next item
                        }
                        else
                        {
                            // the longest wait possible is int.MaxValue milliseconds. Don't let this crash. If the time to wait is longer than this period, just sleep for int.MaxValue milliseconds and the loop will come around and sleep again
                            TimeSpan timeSpan = nextScheduledItem.Subtract(Now());

                            if (timeSpan.TotalMilliseconds > int.MaxValue)
                                timeSpan = new TimeSpan(0, 0, 0, 0, int.MaxValue - 1);  // subtract 1 just to be safe

                            if (timeSpan.TotalMilliseconds > 0)
                                Monitor.Wait(lockObj, timeSpan);    // wake up when the next item is scheduled
                        }
                    }
                    else
                        Monitor.Wait(lockObj); // will be woken up when an item is to be scheduled
                }
            }
        }

        private void ExecuteEvent(object state)
        {
            object[] stateArray = state as object[];

            // state[0] is the time the event was supposed to be scheduled
            // state[1] is the two item array of { callback, callbackInfo } (i.e. the function to call and its parameters)

            object[] objArray = stateArray[1] as object[];

            ScheduledEventCallback callback = objArray[0] as ScheduledEventCallback;
            ScheduledEventCallbackInfo callbackInfo = objArray[1] as ScheduledEventCallbackInfo;
           
            System.Diagnostics.Debug.WriteLine(string.Format("At {0}, Event '{1}' scheduled which was supposed to be run at {2}.", DateTime.Now, (state as object[])[1].ToString(), (state as object[])[0].ToString()));

            if (callback == null)
                return; // null callback used in the TestHarness, do nothing

            callback.Invoke(callbackInfo);
        }

        #endregion

        #region Methods

        /// <summary>Schedule an event to occur. The callback will be invoked with the provided parameters</summary>
        /// <returns>A unique identifier such that it can be cancelled later</returns>
        public Guid ScheduleEvent(DateTime scheduleTime, ScheduledEventCallback callback, ScheduledEventCallbackInfo callbackInfo)// where T : ScheduledEventCallbackInfo
        {
            PerformDisposedCheck();

            lock (lockObj)
            {
                List<object> list = null;

                if (this.scheduleTable.ContainsKey(scheduleTime))
                    list = this.scheduleTable[scheduleTime];
                else
                {
                    list = new List<object>();
                    this.scheduleTable[scheduleTime] = list;
                }

                Guid guid = Guid.NewGuid();

                list.Add(new object[] {callback, callbackInfo, guid});

                if (scheduleTime < nextScheduledItem)
                {
                    // wake up the scheduler to update the schedule and possibly notify
                    Monitor.PulseAll(lockObj);
                }

                return guid;
            }
        }

        /// <summary>
        /// Removes a previously scheduled event at the given time with the given identifier. Not guaranteed to run in O(1) time, depends on the number of items scheduled at the same time.
        /// Will not remove the event if it was scheduled at a different time than provided in the scheduledTime parameter (i.e. if something is scheduled for 1:00 PM and CancelEvent is called with a time of 2:00 PM, it will not be cancelled even if the Guid matches. This is done to improve performance of this method).
        /// </summary>
        /// <param name="scheduledTime"></param>
        /// <param name="identifier"></param>
        public void CancelEvent(DateTime scheduledTime, Guid identifier)
        {
            lock (lockObj)
            {
                if (this.scheduleTable.ContainsKey(scheduledTime))
                {
                    List<object> list = this.scheduleTable[scheduledTime];

                    for (int index = 0; index < list.Count; index++)
                    {
                        object[] objArray = list[index] as object[];

                        Guid guid = (Guid)objArray[2];

                        if (guid == identifier)
                        {
                            list.RemoveAt(index);

                            if (list.Count == 0)
                            {
                                // remove entry from dictionary as well
                                this.scheduleTable.Remove(scheduledTime);
                            }

                            return; // there will be only one item with this Guid
                        }
                    }
                }
            }
        }

        /// <summary>Check to make sure this hasn't been disposed yet. Events cannot be scheduled after the scheduler is disposed</summary>
        private void PerformDisposedCheck()
        {
            bool throwException = false;

            lock (lockObj)
            {
                if (this.isDisposed)
                    throwException = true;
            }

            if (throwException)
                throw new Exception("Cannot perform operation on disposed EventScheduler.");
        }

        private DateTime Now()
        {
            return DateTime.Now;
        }

        #endregion

        #region Types

        //public delegate void ScheduledEventCallback<T>(T callbackInfo) where T : ScheduledEventCallbackInfo<T>;
        public delegate void ScheduledEventCallback(ScheduledEventCallbackInfo callbackInfo);

        /// <summary>Base class without any information for the scheduled callback. Equivalent of EventArgs.Empty</summary>
        public class ScheduledEventCallbackInfo
        {
            public ScheduledEventCallbackInfo()
            {
            }
        }

        /// <summary>Simple class to encapsulate information needed in the callback for when the Event is raised from the scheduler.</summary>
        public class ScheduledEventCallbackInfo<T> : ScheduledEventCallbackInfo
        {
            #region Instance variables

            protected T info;

            #endregion

            #region Contructors

            public ScheduledEventCallbackInfo()
            {
                info = default(T);
            }

            public ScheduledEventCallbackInfo(T info)
            {
                this.info = info;
            }

            #endregion

            #region Properties

            /// <summary>Simple property that includes any information the callback needs to process the event properly. Information is set by the caller and used by the caller in the callback, the scheduler only stores the information and never does anything with it.</summary>
            public T CallbackInfo
            {
                get { return this.info; }
                set { this.info = value; }
            }

            #endregion
        }

        #endregion

        #region IDisposable

        /// <summary>Stops the scheduler from raising any further events, clears the internal list of events and cleans up any resources such as Threads that invoke the scheduled events.
        /// The scheduler will no longer be functional after disposing</summary>
        public void Dispose()
        {
            lock (lockObj)
            {
                if (isDisposed)
                    return;

                isDisposed = true;
                isAlive = false;

                Monitor.PulseAll(lockObj);  // this will wake up the scheduler thread, and since isAlive is now false, it will terminate
            }
        }

        #endregion
    }

    #endregion
}