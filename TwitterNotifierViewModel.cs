using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Media;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json;
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
		private string _notifierBasePath, _notificationPath, _settingsPath;

		#endregion

		#region Constructors

		public TwitterNotifierViewModel()
		{
			ThreadPool.QueueUserWorkItem(q =>
			{
				var appdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				_notifierBasePath = Path.Combine(appdata, "twitter-notifier");
				_notificationPath = Path.Combine(_notifierBasePath, "notification.mp3");
				_settingsPath = Path.Combine(_notifierBasePath, "settings.json");
				Directory.CreateDirectory(_notifierBasePath);
				if (File.Exists(_settingsPath))
				{
					var contents = File.ReadAllText(_settingsPath);
					Settings = JsonConvert.DeserializeObject<TwitterSettings>(contents);
				}
				else
				{
					Settings = new TwitterSettings();
				}
				if (!File.Exists(_notificationPath))
				{
					using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/notification.mp3")).Stream)
					{
						using (var file = File.Create(_notificationPath))
						{
							stream.CopyTo(file);
						}
					}
				}
				InitAuth();
			});
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
					var stream = Tweetinvi.Stream.CreateUserStream(credentials);
					stream.StreamStarted += (s, e) =>
					{
						Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
						{
							Auth.SetCredentials(credentials);
							foreach (var tweet in Timeline.GetHomeTimeline())
							{
								Tweets.Add(tweet);
							}
						}));
						IsAuthorizing = false;
						IsMonitoringTweets = true;
					};
					stream.TweetCreatedByAnyone += (s, e) =>
					{
						Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
						{
							Tweets.Insert(0, e.Tweet);
							PlaySound();
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

		public void PlaySound()
		{
			Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
			{
				var player = new MediaPlayer();
				player.MediaOpened += (s, e) =>
				{
					player.Volume = Settings.Volume;
					player.Play();
				};
				player.MediaEnded += (s, e) =>
				{
					player.Close();
				};
				player.Open(new Uri(_notificationPath));
			}));
		}

		public void SaveSettings()
		{
			var contents = JsonConvert.SerializeObject(Settings);
			File.WriteAllText(_settingsPath, contents);
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

		private TwitterSettings _settings;
		public TwitterSettings Settings
		{
			get { return _settings; }
			set
			{
				if (_settings != value)
				{
					_settings = value;
					OnPropertyChanged("Settings");
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
