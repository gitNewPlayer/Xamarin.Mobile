using System;
using System.Threading.Tasks;
using Android.Locations;
using System.Threading;
using System.Collections.Generic;
using Android.App;
using Android.OS;
using System.Linq;

namespace Xamarin.Geolocation
{
	public class Geolocator
	{
		public Geolocator (LocationManager manager)
		{
			if (manager == null)
				throw new ArgumentNullException ("manager");

			this.manager = manager;
			this.providers = manager.GetProviders (enabledOnly: false);
		}

		public event EventHandler<PositionErrorEventArgs> PositionError;
		public event EventHandler<PositionEventArgs> PositionChanged;

		public bool IsListening
		{
			get { return this.listener != null; }
		}

		public double DesiredAccuracy
		{
			get;
			set;
		}

		public bool SupportsHeading
		{
			get
			{
				return false;
//				if (this.headingProvider == null || !this.manager.IsProviderEnabled (this.headingProvider))
//				{
//					Criteria c = new Criteria { BearingRequired = true };
//					string providerName = this.manager.GetBestProvider (c, enabledOnly: false);
//
//					LocationProvider provider = this.manager.GetProvider (providerName);
//
//					if (provider.SupportsBearing())
//					{
//						this.headingProvider = providerName;
//						return true;
//					}
//					else
//					{
//						this.headingProvider = null;
//						return false;
//					}
//				}
//				else
//					return true;
			}
		}

		public bool IsGeolocationAvailable
		{
			get { return this.providers.Count > 0; }
		}
		
		public bool IsGeolocationEnabled
		{
			get { return this.providers.Where (p => this.manager.IsProviderEnabled (p)).Any(); }
		}

		public Task<Position> GetCurrentPosition (CancellationToken cancelToken)
		{
			return GetCurrentPosition (0, cancelToken);
		}
		
		public Task<Position> GetCurrentPosition (int timeout)
		{
			return GetCurrentPosition (timeout, CancellationToken.None);
		}
		
		public Task<Position> GetCurrentPosition (int timeout, CancellationToken cancelToken)
		{
			if (timeout <= 0 && timeout != Timeout.Infinite)
				throw new ArgumentOutOfRangeException ("timeout", "timeout must be greater than or equal to 0");
			
			var tcs = new TaskCompletionSource<Position>();

			if (!IsListening)
			{
				GeolocationSingleListener singleListener = null;
				singleListener = new GeolocationSingleListener ((float)DesiredAccuracy, timeout,
					() =>
				{
					for (int i = 0; i < this.providers.Count; ++i)
						this.manager.RemoveUpdates (singleListener);
				});
				
				if (cancelToken != CancellationToken.None)
				{
					cancelToken.Register (() =>
					{
						singleListener.Cancel();
						
						for (int i = 0; i < this.providers.Count; ++i)
							this.manager.RemoveUpdates (singleListener);
					}, true);
				}
				
				try
				{
					int enabled = 0;
					for (int i = 0; i < this.providers.Count; ++i)
					{
						if (this.manager.IsProviderEnabled (this.providers[i]))
							enabled++;
						
						this.manager.RequestLocationUpdates (this.providers[i], 0, 0, singleListener, Looper.MyLooper() ?? Looper.MainLooper);
					}
					
					if (enabled == 0)
					{
						for (int i = 0; i < this.providers.Count; ++i)
							this.manager.RemoveUpdates (singleListener);
						
						tcs.SetCanceled();
						return tcs.Task;
					}
				}
				catch (Java.Lang.SecurityException ex)
				{
					tcs.SetCanceled();
					return tcs.Task;
				}

				return singleListener.Task;
			}
			else
			{
				
				lock (this.positionSync)
				{
					if (this.lastPosition == null)
					{
						if (cancelToken != CancellationToken.None)
						{
							cancelToken.Register (() =>
							{
								tcs.TrySetCanceled();
							});
						}

						EventHandler<PositionEventArgs> gotPosition = null;
						gotPosition = (s, e) =>
						{
							tcs.TrySetResult (e.Position);
							PositionChanged -= gotPosition;
						};

						PositionChanged += gotPosition;
					}
					else
					{
						tcs.SetResult (this.lastPosition);
					}
				}
			}

			return tcs.Task;
		}

		public void StartListening (int minTime, double minDistance)
		{
			if (minTime < 0)
				throw new ArgumentOutOfRangeException ("minTime");
			if (minDistance < 0)
				throw new ArgumentOutOfRangeException ("minDistance");
			if (IsListening)
				throw new InvalidOperationException ("This geolocation is already listening");

			this.listener = new GeolocationContinuousListener (this.manager, TimeSpan.FromMilliseconds (minTime), this.providers);
			this.listener.PositionChanged += OnListenerPositionChanged;
			this.listener.PositionError += OnListenerPositionError;

			for (int i = 0; i < this.providers.Count; ++i)
				this.manager.RequestLocationUpdates (providers[i], minTime, (float)minDistance, listener, Looper.MyLooper() ?? Looper.MainLooper);
		}

		public void StopListening()
		{
			if (this.listener == null)
				return;

			this.listener.PositionChanged -= OnListenerPositionChanged;
			this.listener.PositionError -= OnListenerPositionError;

			for (int i = 0; i < this.providers.Count; ++i)
				this.manager.RemoveUpdates (this.listener);

			this.listener = null;
		}

		private readonly IList<string> providers;
		private readonly LocationManager manager;
		private string headingProvider;

		private GeolocationContinuousListener listener;

		private readonly object positionSync = new object();
		private Position lastPosition;

		private void OnListenerPositionChanged (object sender, PositionEventArgs e)
		{
			if (!IsListening) // ignore anything that might come in afterwards
				return;

			lock (this.positionSync)
			{
				this.lastPosition = e.Position;

				var changed = PositionChanged;
				if (changed != null)
					changed (this, e);
			}
		}
		
		private void OnListenerPositionError (object sender, PositionErrorEventArgs e)
		{
			StopListening();

			var error = PositionError;
			if (error != null)
				error (this, e);
		}
	}
}