using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Media;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Tweetinvi;
using Tweetinvi.Models;

namespace TwitterNotifier
{
	public class TwitterNotifierViewModel : INotifyPropertyChanged
	{
		#region Data Members

		public const string TWITTER_API_KEY = "QAqEo4of92uePdh511aL3hLKP";
		public const string TWITTER_API_SECRET = "nqRY6bpihJiVvwNEz4xfITJf2QAAszoZsDftwzz4kAyJePPh24";
		private IAuthenticationContext _authorizationContext;

		#endregion

		#region Constructors

		public TwitterNotifierViewModel()
		{
			InitAuth();
		}

		#endregion

		#region Methods

		public void InitAuth()
		{
			var applicationCredentials = new ConsumerCredentials(TWITTER_API_KEY, TWITTER_API_SECRET);
			_authorizationContext = AuthFlow.InitAuthentication(applicationCredentials);
			AuthorizationURL = _authorizationContext.AuthorizationURL;
		}

		public void Authenticate()
		{
			ThreadPool.QueueUserWorkItem(q =>
			{
				try
				{
					var credentials = AuthFlow.CreateCredentialsFromVerifierCode(AuthorizationCaptcha, _authorizationContext);
					var stream = Stream.CreateUserStream(credentials);
					stream.StreamStarted += (s, e) =>
					{
						IsAuthorizing = false;
						IsMonitoringTweets = true;
					};
					stream.TweetCreatedByAnyone += (s, e) =>
					{
						Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
						{
							Tweets.Add(e.Tweet);
							var player = new SoundPlayer(TwitterNotifier.Properties.Resources.notification);
							player.PlaySync();
						}));
					};
					stream.StartStream();
				}
				catch
				{
					InitAuth();
					MessageBox.Show("Invalid credentials specified. Please try again.");
				}
			});
		}

		#endregion

		#region Properties

		private string _authorizationCaptcha;
		public string AuthorizationCaptcha
		{
			get { return _authorizationCaptcha; }
			set
			{
				if (_authorizationCaptcha != value)
				{
					_authorizationCaptcha = value;
					OnPropertyChanged("AuthorizationCaptcha");
				}
			}
		}

		private string _authorizationURL;
		public string AuthorizationURL
		{
			get { return _authorizationURL; }
			set
			{
				if (_authorizationURL != value)
				{
					_authorizationURL = value;
					OnPropertyChanged("AuthorizationURL");
				}
			}
		}

		private bool _isAuthorizing = true;
		public bool IsAuthorizing
		{
			get { return _isAuthorizing; }
			set
			{
				if (_isAuthorizing != value)
				{
					_isAuthorizing = value;
					OnPropertyChanged("IsAuthorizing");
				}
			}
		}

		private bool _isMonitoringTweets;
		public bool IsMonitoringTweets
		{
			get { return _isMonitoringTweets; }
			set
			{
				if (_isMonitoringTweets != value)
				{
					_isMonitoringTweets = value;
					OnPropertyChanged("IsMonitoringTweets");
				}
			}
		}

		private readonly ObservableCollection<ITweet> _tweets = new ObservableCollection<ITweet>();
		public ObservableCollection<ITweet> Tweets
		{
			get { return _tweets; }
		}

		#endregion

		#region INotifyPropertyChanged Members

		public event PropertyChangedEventHandler PropertyChanged;
		private void OnPropertyChanged(string propertyName)
		{
			var handler = PropertyChanged;
			if (handler != null)
			{
				handler(this, new PropertyChangedEventArgs(propertyName));
			}
		}

		#endregion
	}
}
