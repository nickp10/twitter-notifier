using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
							TweetsHTML = BuildHTML();
						}));
						IsAuthorizing = false;
						IsMonitoringTweets = true;
					};
					stream.TweetCreatedByAnyone += (s, e) =>
					{
						Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
						{
							Tweets.Insert(0, e.Tweet);
							TweetsHTML = BuildHTML();
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

		public string BuildHTML()
		{
			var builder = new StringBuilder();
			foreach (var tweet in Tweets)
			{
				if (builder.Length != 0)
				{
					builder.Append("<br /><br />");
				}
				builder.Append("<b>");
				builder.Append(tweet.CreatedBy.Name);
				builder.Append("</b>");
				builder.Append(" - ");
				builder.Append(string.Format("{0:MM/dd/yy hh:mm:ss tt}", tweet.CreatedAt));
				builder.Append("<br />");
				builder.Append(FormatText(tweet.FullText));
			}
			return builder.ToString();
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
