using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Utils
{
    public class SmartObservableCollection<T> : ObservableCollection<T>
    {
        private volatile bool _isObserving = true;

        private bool IsObserving
        {
            get { return _isObserving; }
            set { _isObserving = value; }
        }

        public SmartObservableCollection()
        {

        }

        public SmartObservableCollection(IEnumerable<T> source) : base(source)
        {

        }

        public void AddRange(IEnumerable<T> range)
        {
            if (range == null)
                return;

            // get out if no new items
            var enumerable = range as T[] ?? range.ToArray();
            if (!enumerable.Any()) return;

            // prepare data for firing the events
            var newStartingIndex = Count;
            var newItems = new List<T>();
            newItems.AddRange(enumerable);

            // add the items, making sure no events are fired
            IsObserving = false;
            try
            {
                foreach (var item in enumerable)
                {
                    Add(item);
                }
            }
            finally
            {
                IsObserving = true;
            }

            // fire the events
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            if (newItems.Count == 1)
            {
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, newItems[0], newStartingIndex));
            }
            else
            {
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }

        }

        public void ReplaceRange(IEnumerable<T> range)
        {
            var enumerable = range?.ToArray() ?? Array.Empty<T>();

            IsObserving = false;
            try
            {
                Clear();
                foreach (var item in enumerable)
                {
                    Add(item);
                }
            }
            finally
            {
                IsObserving = true;
            }

            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!IsObserving) return;
            var dispatcher = ResourceLocator.DispatcherAdapter;
            if (dispatcher != null)
            {
                dispatcher.Run(() => base.OnCollectionChanged(e));
            }
            else
            {
                base.OnCollectionChanged(e);
            }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (!IsObserving) return;
            var dispatcher = ResourceLocator.DispatcherAdapter;
            if (dispatcher != null)
            {
                dispatcher.Run(() => base.OnPropertyChanged(e));
            }
            else
            {
                base.OnPropertyChanged(e);
            }
        }
    }
}