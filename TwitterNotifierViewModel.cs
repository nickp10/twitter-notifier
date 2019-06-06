using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using NAudio.Wave;
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
		private string _settingsPath, _namesPath;
		private readonly string[] _urgentHandles;
		private readonly byte[] _normalNotification, _keywordNotification, _urgentNotification;
		private readonly IDictionary<string, string> _altNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly ISet<string> _hiddenNames = new HashSet<string>();

		#endregion

		#region Constructors

		public TwitterNotifierViewModel()
		{
			var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			var appDataBasePath = Path.Combine(appData, "twitter-notifier");
			var configBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration");
			_settingsPath = Path.Combine(appDataBasePath, "settings.json");
			_namesPath = Path.Combine(configBasePath, "twitterNames.csv");
			_normalNotification = File.ReadAllBytes(Path.Combine(configBasePath, "notification.wav"));
			_keywordNotification = File.ReadAllBytes(Path.Combine(configBasePath, "keywordNotification.wav"));
			_urgentNotification = File.ReadAllBytes(Path.Combine(configBasePath, "urgentNotification.wav"));
			_urgentHandles = File.ReadAllLines(Path.Combine(configBasePath, "urgentHandles.txt"));
			Directory.CreateDirectory(appDataBasePath);
			if (File.Exists(_settingsPath))
			{
				var contents = File.ReadAllText(_settingsPath);
				Settings = JsonConvert.DeserializeObject<TwitterSettings>(contents);
			}
			else
			{
				Settings = new TwitterSettings();
			}
			ThreadPool.QueueUserWorkItem(q =>
			{
				ReadAltNames();
				if (!string.IsNullOrEmpty(Settings.AuthKey) && !string.IsNullOrEmpty(Settings.AuthSecret))
				{
					var credentials = new TwitterCredentials(TWITTER_API_KEY, TWITTER_API_SECRET, Settings.AuthKey, Settings.AuthSecret);
					ShowHomeTimeline(credentials);
				}
				else
				{
					InitAuth();
				}
			});
		}

		#endregion

		#region Methods

		public void InitAuth()
		{
			IsLoginScreen = true;
			IsTweetScreen = false;
			var applicationCredentials = new ConsumerCredentials(TWITTER_API_KEY, TWITTER_API_SECRET);
			_authorizationContext = AuthFlow.InitAuthentication(applicationCredentials);
			AuthorizationURL = _authorizationContext.AuthorizationURL;
		}

		public void Logout()
		{
			Settings.AuthKey = null;
			Settings.AuthSecret = null;
			Tweets.Clear();
			InitAuth();
		}

		public void Authenticate()
		{
			IsNotAuthorizing = false;
			ThreadPool.QueueUserWorkItem(q =>
			{
				try
				{
					var credentials = AuthFlow.CreateCredentialsFromVerifierCode(AuthorizationCaptcha, _authorizationContext);
					AuthorizationCaptcha = null;
					if (Settings.RememberMe)
					{
						Settings.AuthKey = credentials.AccessToken;
						Settings.AuthSecret = credentials.AccessTokenSecret;
					}
					else
					{
						Settings.AuthKey = null;
						Settings.AuthSecret = null;
					}
					ShowHomeTimeline(credentials);
				}
				catch
				{
					IsNotAuthorizing = true;
					InitAuth();
					MessageBox.Show("Invalid credentials specified. Please try again.");
				}
			});
		}

		private bool Filter(ITweet tweet)
		{
			if (Settings.IgnoreRetweets && tweet.IsRetweet)
			{
				return false;
			}
			if (Settings.IgnoreReplyTos && !string.IsNullOrEmpty(tweet.InReplyToScreenName))
			{
				return false;
			}
			if (_hiddenNames.Contains(tweet.CreatedBy.ScreenName))
			{
				return false;
			}
			return true;
		}

		private bool IsUrgent(ITweet tweet)
		{
			return _urgentHandles.Any(h => string.Equals(h, tweet.CreatedBy.ScreenName, StringComparison.OrdinalIgnoreCase));
		}

		private bool TweetContainsKeyword(ITweet tweet)
		{
			foreach (var keyword in Settings.Keywords)
			{
				if (tweet.Text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}
			return false;
		}

		private void ShowHomeTimeline(ITwitterCredentials credentials)
		{
			try
			{
				Auth.SetCredentials(credentials);
				var timeline = Timeline.GetHomeTimeline();
				if (timeline != null)
				{
					Application.Current.Dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
					{
						foreach (var tweet in timeline)
						{
							if (Filter(tweet))
							{
								Tweets.Add(tweet);
							}
						}
						TweetsHTML = BuildHTML();
					}));
				}
				SubscribeToFilterStream(credentials);
			}
			finally
			{
				IsLoginScreen = false;
				IsTweetScreen = true;
				IsNotAuthorizing = true;
			}
		}

		private void SubscribeToFilterStream(ITwitterCredentials credentials)
		{
			var stream = Tweetinvi.Stream.CreateFilteredStream(credentials);
			var user = User.GetAuthenticatedUser(credentials);
			var following = User.GetFriendIds(user) ?? Enumerable.Empty<long>();
			foreach (var id in following)
			{
				stream.AddFollow(id);
			}
			stream.AddFollow(user);
			stream.MatchingTweetReceived += (s, e) =>
			{
				Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
				{
					NewTweet(e.Tweet);
				}));
			};
			stream.StartStreamMatchingAllConditionsAsync();
		}

		private void NewTweet(ITweet tweet)
		{
			if (Filter(tweet))
			{
				if (TweetContainsKeyword(tweet))
				{
					PlayKeywordNotification();
				}
				else if (IsUrgent(tweet))
				{
					PlayUrgentNotification();
				}
				else
				{
					PlayNormalNotification();
				}
				Tweets.Insert(0, tweet);
				TweetsHTML = BuildHTML();
			}
		}

		[DllImport("Winmm.dll")]
		public static extern bool PlaySound(byte[] data, IntPtr hMod, UInt32 dwFlags);

		public void PlayNormalNotification()
		{
			PlaySound(_normalNotification, Settings.Volume);
		}

		public void PlayKeywordNotification()
		{
			PlaySound(_keywordNotification, Settings.KeywordVolume);
		}

		public void PlayUrgentNotification()
		{
			PlaySound(_urgentNotification, Settings.UrgentVolume);
		}

		private void PlaySound(byte[] data, double volume)
		{
			using (var outputDevice = new WaveOutEvent())
			{
				outputDevice.Volume = (float)volume;
			}
			PlaySound(data, IntPtr.Zero, 1 | 4);
		}

		public void SaveSettings()
		{
			var contents = JsonConvert.SerializeObject(Settings);
			File.WriteAllText(_settingsPath, contents);
		}

		public string BuildHTML()
		{
			var builder = new StringBuilder();
			foreach (var tweet in Tweets)
			{
				builder.Append("<div style=\"background-color: #FFFFFF; padding: 5px; margin-top: 10px; margin-left: 5px; margin-right: 5px; margin-bottom: 10px;\">");
				builder.Append("<b>");
				builder.Append(FormatName(tweet.CreatedBy.ScreenName, tweet.CreatedBy.Name));
				builder.Append("</b> <a href=\"https://twitter.com/");
				builder.Append(tweet.CreatedBy.ScreenName);
				builder.Append("\" style=\"color: #657786\">@");
				builder.Append(tweet.CreatedBy.ScreenName);
				builder.Append("</a> - ");
				builder.Append(string.Format("{0:MM/dd/yy hh:mm:ss tt}", tweet.CreatedAt.ToLocalTime()));
				builder.Append("<br />");
				builder.Append(FormatText(tweet.FullText));
				builder.Append("</div>");
			}
			return builder.ToString();
		}

		public string FormatName(string screenName, string fallbackName)
		{
			string name;
			if (_altNames.TryGetValue(screenName, out name))
			{
				return name;
			}
			return fallbackName;
		}

		public void ReadAltNames()
		{
			foreach (var line in File.ReadAllLines(_namesPath))
			{
				var parts = line.Split(',');
				if (parts.Length >= 2)
				{
					var nameBuilder = new StringBuilder();
					nameBuilder.Append(parts[1]);
					if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
					{
						nameBuilder.Append(" (");
						nameBuilder.Append(parts[2]);
						nameBuilder.Append(")");
					}
					if (parts.Length >= 4 && string.Equals(parts[3], "n", StringComparison.OrdinalIgnoreCase))
					{
						_hiddenNames.Add(parts[0]);
					}
					for (int x = 4; parts.Length >= x + 2; x += 2)
					{
						if (!string.IsNullOrWhiteSpace(parts[x]) && !string.IsNullOrWhiteSpace(parts[x + 1]))
						{
							nameBuilder.Append(" <a href=\"");
							nameBuilder.Append(parts[x + 1]);
							nameBuilder.Append("\">");
							nameBuilder.Append(parts[x]);
							nameBuilder.Append("</a>");
						}
					}
					_altNames[parts[0]] = nameBuilder.ToString();
				}
			}
		}

		private void OnSettingValueChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "UrgentVolume")
			{
				PlayUrgentNotification();
			}
			else if (e.PropertyName == "KeywordVolume")
			{
				PlayKeywordNotification();
			}
			else if (e.PropertyName == "Volume")
			{
				PlayNormalNotification();
			}
			SaveSettings();
		}

		private void OnSettingValueCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			SaveSettings();
		}

		#endregion

		#region Format Methods

		private string FormatText(string text)
		{
			text = FormatAmpersands(text);
			text = FormatHyperlinks(text);
			text = FormatTags(text);
			return FormatLineBreaks(text);
		}

		private string FormatAmpersands(string text)
		{
			var pattern = @"(?xi)
							(
							  &						# Match '&'
							  (?!					# only if it's not followed by
							    (?:					# either
							      \#[0-9]+			# a '#' and a decimal value
							      |					# or
							      \#x[0-9a-fA-F]+	# a '#' and a hexadecimal value
							      |					# or
							      [a-zA-Z]\w*		# a letter followed by word characters
							    )
							    ;					# and a semicolon
							  )						# End of lookahead assertion
							)";
			var regex = new Regex(pattern, RegexOptions.IgnoreCase);
			return regex.Replace(text, "$1amp;");
		}

		private string FormatHyperlinks(string text)
		{
			return FormatURLs(FormatEmails(text));
		}

		private string FormatTags(string text)
		{
			/* Regex: (?xi)(<(?!(?:a|/a|img|embed)\b))
			 * Description: Change all '<' symbols not followed by 'a', '/a', 'img' or 'embed' into their 
			 * html equivalent.
			*/
			var pattern = @"(?xi)					
							(
							  <						# Match '<'					
							  (?!					# only if it's not followed by
							    (?:					# either
								  a					# 'a'
								  |					# or
								  /a				# '/a'
								  |					# or
								  img				# 'img'
								  |					# or
								  embed				# 'embed'
							    )
								\b					# a word boundary
							  )						# End of lookahead assertion		
							)";

			var regex = new Regex(pattern, RegexOptions.IgnoreCase);
			return regex.Replace(text, "&#60;");
		}

		private string FormatEmails(string text)
		{
			/* This pattern was taken from http://www.regular-expressions.info/email.html.
			 * 
			 * Two options mentioned in the article were used:
			 * 1. allow up to 6 characters in the top-level domain
			 * 2. do not allow multiple adjacent dots (e.g. john@aol...com)
			 * 
			 * Also the regex was modified slightly so that there must be white space (or the beginning of the string)
			 * before the email. This is to prevent the regex from wrapping the href of an existing link.
			 */
			var emailPattern = @"(\s|^)(mailto:)?([A-Z0-9._%+-]+@(?:[A-Z0-9-]+\.)+[A-Z]{2,6})\b";
			var regex = new Regex(emailPattern, RegexOptions.IgnoreCase);
			return regex.Replace(text, "$1<a href=\"mailto:$3\">$2$3</a>");
		}

		private string FormatURLs(string text)
		{
			/* This regex pattern was taken and modified from http://daringfireball.net/2010/07/improved_regex_for_matching_urls.
			 * The comments were left in place for ease in understanding.
			 * 
			 * Two changes were made to the original:
			 * 1. there must be whitespace (or the beginning of the string) before the URL to prevent
			 *    wrapping the href of an existing link
			 * 2. http, https, ftp, app, and file protocols are allowed
			 */
			var urlPattern = @"(?xi)
								(\s|^)											# EDIT: This was modified from \b
								(												# Capture 1: entire matched URL
								  (?:
									(?:https?://|ftp://|app:|file://)			# EDIT: This was modified to include http, https, ftp, app, and file
									|											#   or
									www\d{0,3}[.]								# ""www."", ""www1."", ""www2."" … ""www999.""
									|											#   or
									[a-z0-9.\-]+[.][a-z]{2,4}/					# looks like domain name followed by a slash
								  )
								  (?:											# One or more:
									[^\s()<>]+									# Run of non-space, non-()<>
									|											#   or
									\(([^\s()<>]+|(\([^\s()<>]+\)))*\)			# balanced parens, up to 2 levels
								  )+
								  (?:											# End with:
									\(([^\s()<>]+|(\([^\s()<>]+\)))*\)			# balanced parens, up to 2 levels
									|											#   or
									[^\s`!()\[\]{};:'"".,<>?«»“”‘’]				# not a space or one of these punct chars
								  )
								)";

			var regex = new Regex(urlPattern, RegexOptions.IgnoreCase);
			return regex.Replace(text, "$1<a href=\"$2\">$2</a>").Replace("href=\"www", "href=\"http://www");
		}

		private string FormatLineBreaks(string text)
		{
			return Regex.Replace(text, @"\r\n?|\n", "<br />"); ;
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

		private bool _isNotAuthorizing = true;
		public bool IsNotAuthorizing
		{
			get { return _isNotAuthorizing; }
			set
			{
				if (_isNotAuthorizing != value)
				{
					_isNotAuthorizing = value;
					OnPropertyChanged("IsNotAuthorizing");
				}
			}
		}

		private bool _isLoginScreen;
		public bool IsLoginScreen
		{
			get { return _isLoginScreen; }
			set
			{
				if (_isLoginScreen != value)
				{
					_isLoginScreen = value;
					OnPropertyChanged("IsLoginScreen");
				}
			}
		}

		private bool _isTweetScreen;
		public bool IsTweetScreen
		{
			get { return _isTweetScreen; }
			set
			{
				if (_isTweetScreen != value)
				{
					_isTweetScreen = value;
					OnPropertyChanged("IsTweetScreen");
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
					if (_settings != null)
					{
						_settings.PropertyChanged -= OnSettingValueChanged;
						_settings.Keywords.CollectionChanged -= OnSettingValueCollectionChanged;
					}
					_settings = value;
					if (_settings != null)
					{
						_settings.PropertyChanged += OnSettingValueChanged;
						_settings.Keywords.CollectionChanged += OnSettingValueCollectionChanged;
					}
					OnPropertyChanged("Settings");
				}
			}
		}

		private string _tweetsHTML;
		public string TweetsHTML
		{
			get { return _tweetsHTML; }
			set
			{
				if (_tweetsHTML != value)
				{
					_tweetsHTML = value;
					OnPropertyChanged("TweetsHTML");
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
