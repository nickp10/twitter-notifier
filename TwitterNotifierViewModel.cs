using System;
using System.Collections;
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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NAudio.Wave;
using Newtonsoft.Json;
using NLog;
using Tweetinvi;
using Tweetinvi.Core.Helpers;
using Tweetinvi.Models;
using Tweetinvi.Streaming;

namespace TwitterNotifier
{
	public class TwitterNotifierViewModel : INotifyPropertyChanged
	{
		#region Data Members

		public const string TWITTER_API_KEY = "QAqEo4of92uePdh511aL3hLKP";
		public const string TWITTER_API_SECRET = "nqRY6bpihJiVvwNEz4xfITJf2QAAszoZsDftwzz4kAyJePPh24";
		private TwitterClient _applicationClient;
		private IAuthenticationRequest _authenticationRequest;
		private IFilteredStream _currentStream;
		private string _settingsPath, _namesPath;
		private System.Timers.Timer _refreshTimer;
		private readonly string[] _urgentHandles;
		private readonly byte[] _normalNotification, _keywordNotification, _urgentNotification;
		private readonly IDictionary<string, string> _altNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly ISet<string> _hiddenNames = new HashSet<string>();
		private readonly Logger _logger = LogManager.GetCurrentClassLogger();

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
			ThreadPool.QueueUserWorkItem(async q =>
			{
				ReadAltNames(out var errorMsgs);
				ErrorMsgs = errorMsgs;
				await ShowInitialScreen();
			});
		}

		#endregion

		#region Properties

		private readonly IEnumerable _refreshIntervalValues = new[]
		{
			new { Display = "1 Minute", Value = TimeSpan.FromMinutes(1).TotalMilliseconds },
			new { Display = "30 Minutes", Value = TimeSpan.FromMinutes(30).TotalMilliseconds },
			new { Display = "1 Hour", Value = TimeSpan.FromHours(1).TotalMilliseconds },
			new { Display = "4 Hours", Value = TimeSpan.FromHours(4).TotalMilliseconds },
		};
		public IEnumerable RefreshIntervalValues
		{
			get { return _refreshIntervalValues; }
		}

		public readonly IEnumerable<string> _themeValues = new[] { "Dark", "Light" };

		public IEnumerable<string> ThemeValues
		{
			get { return _themeValues; }
		}

		#endregion

		#region Methods

		private string Encode(string plainText)
		{
			try
			{
				var plainTextBytes = Encoding.UTF8.GetBytes(plainText);
				return Convert.ToBase64String(plainTextBytes);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private string Decode(string base64EncodedText)
		{
			try
			{
				var base64EncodedBytes = Convert.FromBase64String(base64EncodedText);
				return Encoding.UTF8.GetString(base64EncodedBytes);
			}
			catch (Exception)
			{
				return null;
			}
		}

		private DateTimeOffset? GetExpiration(string key)
		{
			if (!string.IsNullOrWhiteSpace(key))
			{
				var decoded = Decode(key);
				if (!string.IsNullOrWhiteSpace(decoded) && decoded.Length > 24)
				{
					if (long.TryParse(decoded.Substring(24), out var millis))
					{
						return DateTimeOffset.FromUnixTimeMilliseconds(millis);
					}
				}
			}
			return null;
		}

		private bool IsKeyValid(string key)
		{
			var expiration = GetExpiration(key);
			return expiration != null && expiration.Value >= DateTimeOffset.Now;
		}

		private string BuildAppKey()
		{
			return Encode(Guid.NewGuid().ToString().Substring(0, 24) + DateTimeOffset.Now.AddDays(14).ToUnixTimeMilliseconds());
		}

		public async Task ValidateAppKey()
		{
			var key = EnteredAppKey;
			if (IsKeyValid(key))
			{
				Settings.AppKey = key;
				await ShowInitialScreen();
			}
			else
			{
				MessageBox.Show("An invalid application key was entered");
			}
		}

		public void GenerateKey()
		{
			var key = BuildAppKey();
			Clipboard.SetText(key);
			MessageBox.Show("An application key has been copied to your clipboard. It is valid for 2 weeks. The generated application key is:\n\n" + key);
		}

		private async Task ShowInitialScreen()
		{
#if REQ_KEY
			var appKey = Settings.AppKey;
			if (!IsKeyValid(appKey))
			{
				IsAppKeyScreen = true;
				IsLoginScreen = false;
				IsTweetScreen = false;
			}
			else
#endif
			if (!string.IsNullOrEmpty(Settings.AuthKey) && !string.IsNullOrEmpty(Settings.AuthSecret))
			{
				var credentials = new TwitterCredentials(TWITTER_API_KEY, TWITTER_API_SECRET, Settings.AuthKey, Settings.AuthSecret);
				_applicationClient = new TwitterClient(credentials);
				await ShowHomeTimeline();
			}
			else
			{
				await InitAuth();
			}
		}

		public async Task InitAuth()
		{
			IsAppKeyScreen = false;
			IsLoginScreen = true;
			IsTweetScreen = false;
			var applicationCredentials = new TwitterCredentials(TWITTER_API_KEY, TWITTER_API_SECRET);
			_applicationClient = new TwitterClient(applicationCredentials);
			_authenticationRequest = await _applicationClient.Auth.RequestAuthenticationUrlAsync();
			AuthorizationURL = _authenticationRequest.AuthorizationURL;
		}

		public async Task Logout()
		{
			Settings.AuthKey = null;
			Settings.AuthSecret = null;
			Tweets.Clear();
			await InitAuth();
		}

		public void Authenticate()
		{
			if (string.IsNullOrWhiteSpace(AuthorizationCaptcha))
			{
				MessageBox.Show("Please specify the PIN from the Twitter login before proceeding");
				return;
			}
			IsNotAuthorizing = false;
			ThreadPool.QueueUserWorkItem(async q =>
			{
				try
				{
					var credentials = await _applicationClient.Auth.RequestCredentialsFromVerifierCodeAsync(AuthorizationCaptcha, _authenticationRequest);
					_applicationClient = new TwitterClient(credentials);
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
					await ShowHomeTimeline();
				}
				catch (Exception e)
				{
					_logger.Error(e);
					IsNotAuthorizing = true;
					await InitAuth();
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

		private async Task ShowHomeTimeline()
		{
			try
			{
				var timeline = await _applicationClient.Timelines.GetHomeTimelineAsync();
				if (timeline != null)
				{
					var dispatcher = Application.Current?.Dispatcher;
					if (dispatcher != null)
					{
						dispatcher.Invoke(DispatcherPriority.Normal, new Action(() =>
						{
							Tweets.Clear();
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
				}
				_currentStream = await SubscribeToFilterStream();
				_refreshTimer = new System.Timers.Timer(Settings.RefreshInterval)
				{
					AutoReset = false
				};
				_refreshTimer.Elapsed += OnRefreshTimerElapsed;
				_refreshTimer.Start();
			}
			finally
			{
				IsAppKeyScreen = false;
				IsLoginScreen = false;
				IsTweetScreen = true;
				IsNotAuthorizing = true;
			}
		}

		private void OnRefreshTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			_refreshTimer.Dispose();
			if (_currentStream != null)
			{
				_currentStream.Stop();
			}
			ThreadPool.QueueUserWorkItem(async q =>
			{
				var credentials = _applicationClient.Credentials;
				_applicationClient = new TwitterClient(credentials);
				await ShowHomeTimeline();
			});
		}

		private async Task<IFilteredStream> SubscribeToFilterStream()
		{
			var stream = _applicationClient.Streams.CreateFilteredStream();
			var user = await _applicationClient.Users.GetAuthenticatedUserAsync();
			var following = await _applicationClient.Users.GetFriendIdsAsync(user);
			following = following ?? new long[0];
			foreach (var id in following)
			{
				stream.AddFollow(id);
			}
			stream.AddFollow(user);
			stream.MatchingTweetReceived += (s, e) =>
			{
				var dispatcher = Application.Current?.Dispatcher;
				if (dispatcher != null)
				{
					dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
					{
						NewTweet(e.Tweet);
					}));
				}
			};
			var t = stream.StartMatchingAllConditionsAsync();
			return stream;
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
				WriteTweetFile(tweet);
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

		private int NextFileNumber(string directory)
		{
			int max = 0;
			foreach (var filename in Directory.GetFiles(directory))
			{
				var name = Path.GetFileNameWithoutExtension(filename);
				if (int.TryParse(name, out var nameInt))
				{
					max = Math.Max(nameInt, max);
				}
			}
			return max + 1;
		}

		private void WriteTweetFile(ITweet tweet)
		{
			var directory = Settings.OutputDirectory;
			if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
			{
				var number = NextFileNumber(directory);
				var path = Path.Combine(directory, number + ".txt");
				var builder = new StringBuilder();
				builder.Append(FormatName(tweet.CreatedBy.ScreenName, tweet.CreatedBy.Name));
				builder.Append(" @");
				builder.Append(tweet.CreatedBy.ScreenName);
				builder.Append(" - ");
				builder.Append(string.Format("{0:MM/dd/yy hh:mm:ss tt}", tweet.CreatedAt.ToLocalTime()));
				builder.AppendLine();
				builder.Append(new HttpUtility().HtmlDecode(tweet.FullText));
				File.WriteAllText(path, builder.ToString());
			}
		}

		public string BuildHTML()
		{
			var builder = new StringBuilder();
			foreach (var tweet in Tweets)
			{
				var bg = Settings.Theme == "Dark" ? "#222222" : "#FFFFFF";
				var fg = Settings.Theme == "Dark" ? "#FFFFFF" : "#000000";
				builder.Append("<div style=\"background-color: ");
				builder.Append(bg);
				builder.Append("; color: ");
				builder.Append(fg);
				builder.Append("; padding: 5px; margin-top: 10px; margin-left: 5px; margin-right: 5px; margin-bottom: 10px;\">");
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

		public void ReadAltNames(out IEnumerable<string> errors)
		{
			var errorMsgs = new List<string>();
			if (!File.Exists(_namesPath))
			{
				errorMsgs.Add("Settings file does not exist: " + _namesPath);
				errors = errorMsgs;
				return;
			}
			var skipHeader = true;
			foreach (var line in File.ReadAllLines(_namesPath))
			{
				if (!string.IsNullOrWhiteSpace(line))
				{
					if (skipHeader)
					{
						skipHeader = false;
						continue;
					}

					// Screen Name, Alternate Name 1, Alternate Name 2, Is Visible (Y/N), Hyperlink Display 1, Hyperlink URL 1, Hyperlink Display 2, Hyperlink URL 2, Hyperlink etc.
					var parts = line.Split(',');
					if (parts.Length >= 2)
					{
						var screenName = parts[0];
						if (!string.IsNullOrWhiteSpace(screenName))
						{
							var nameBuilder = new StringBuilder();
							var alternateName = parts[1];
							if (!string.IsNullOrWhiteSpace(alternateName))
							{
								nameBuilder.Append(alternateName);
							}
							if (parts.Length >= 3)
							{
								var additionalAlternateName = parts[2];
								if (!string.IsNullOrWhiteSpace(additionalAlternateName))
								{
									if (nameBuilder.Length > 0)
									{
										nameBuilder.Append(" (");
										nameBuilder.Append(additionalAlternateName);
										nameBuilder.Append(")");
									}
									else
									{
										nameBuilder.Append(additionalAlternateName);
									}
								}
							}
							if (parts.Length >= 4)
							{
								var isVisible = parts[3];
								if (!string.IsNullOrWhiteSpace(isVisible) && string.Equals(isVisible, "n", StringComparison.OrdinalIgnoreCase))
								{
									_hiddenNames.Add(screenName);
								}
							}
							for (var x = 4; parts.Length >= x + 2; x += 2)
							{
								var hyperlinkDisplay = parts[x];
								var hyperlinkURL = parts[x + 1];
								if (!string.IsNullOrWhiteSpace(hyperlinkDisplay) && !string.IsNullOrWhiteSpace(hyperlinkURL))
								{
									nameBuilder.Append(" <a href=\"");
									nameBuilder.Append(hyperlinkURL);
									nameBuilder.Append("\">");
									nameBuilder.Append(hyperlinkDisplay);
									nameBuilder.Append("</a>");
								}
							}
							if (nameBuilder.Length > 0)
							{
								_altNames[screenName] = nameBuilder.ToString();
							}
						}
						else
						{
							errorMsgs.Add("Screen name must be specified for this line: " + line);
						}
					}
					else
					{
						errorMsgs.Add("Not enough data entered for line: " + line);
					}
				}
			}
			errors = errorMsgs;
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
			else if (e.PropertyName == "Theme")
			{
				TweetsHTML = BuildHTML();
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

		public bool CanGenerateKey
		{
#if REQ_KEY
			get { return false; }
#else
			get { return true; }
#endif
		}

		private string _enteredAppKey;
		public string EnteredAppKey
		{
			get { return _enteredAppKey; }
			set
			{
				if (_enteredAppKey != value)
				{
					_enteredAppKey = value;
					OnPropertyChanged("EnteredAppKey");
				}
			}
		}

		private IEnumerable<string> _errorMsgs;
		public IEnumerable<string> ErrorMsgs
		{
			get { return _errorMsgs; }
			set
			{
				if (_errorMsgs != value)
				{
					_errorMsgs = value;
					OnPropertyChanged(nameof(ErrorMsgs));
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

		private bool _isAppKeyScreen;
		public bool IsAppKeyScreen
		{
			get { return _isAppKeyScreen; }
			set
			{
				if (_isAppKeyScreen != value)
				{
					_isAppKeyScreen = value;
					OnPropertyChanged("IsAppKeyScreen");
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
